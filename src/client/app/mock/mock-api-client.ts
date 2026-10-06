/**
 * MockApiClient — конвейер мок-вызова (C-002, контракт IF-001, FR-002/FR-003,
 * ADR-003): реестр обработчиков + задержка MOCK_DELAY_MS + трансляция
 * MockValidationError → ApiError.
 *
 * call<T>(method, params) находит обработчик в таблице, выполняет его
 * синхронно над собственным экземпляром MockDb и задерживает завершение
 * (resolve и reject) до момента «не ранее MOCK_DELAY_MS от начала вызова»;
 * фактическая длительность каждого вызова ∈ [500; 800] мс (NFR-001):
 * обработчик синхронный и в памяти, поэтому задержка почти ровно 500 мс.
 *
 * Обработчики — чистые функции над MockDb (ADR-003), не имеют доступа к
 * клиенту; наружу мок-слой импортируют только шесть сервисов core (FR-002),
 * которые и вызывают call. Реестр заполняет агрегатор мок-слоя через
 * register(...) — доменные обработчики подключаются задачами
 * T-006/T-012/T-015/T-018/T-021.
 */
import { Injectable } from '@angular/core';

import { ApiError, MOCK_DELAY_MS } from '../shared/models';
import { MockDb } from './mock-db';
import { MockValidationError } from './mock-error';

/**
 * Имя мок-метода — строка из реестра (конвенция именования: домен и
 * операция, например 'auth.login', 'labs.create').
 */
export type MockMethodId = string;

/** Обработчик мок-метода: чистая синхронная функция над MockDb (ADR-003). */
export type MockHandler<P = unknown, R = unknown> = (db: MockDb, params: P) => R;

type RegisteredHandler = MockHandler<unknown, unknown>;

/** Задержка до MOCK_DELAY_MS от начала вызова (уже учтённое время — минус). */
function remainingDelayMs(startedAt: number): number {
  return Math.max(0, MOCK_DELAY_MS - (Date.now() - startedAt));
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/** Трансляция сигнала отказа в транспортную форму ApiError (IF-001). */
function toApiError(error: MockValidationError): ApiError {
  const body: ApiError['body'] = error.errors
    ? { message: error.message, errors: error.errors }
    : { message: error.message };
  return { status: error.status, body };
}

@Injectable({ providedIn: 'root' })
export class MockApiClient {
  private readonly handlers = new Map<MockMethodId, RegisteredHandler>();
  private readonly db = new MockDb();

  /** Регистрирует обработчик метода (ADR-003); повторная регистрация заменяет. */
  register<P, R>(method: MockMethodId, handler: MockHandler<P, R>): this {
    this.handlers.set(method, handler as unknown as RegisteredHandler);
    return this;
  }

  /**
   * Мок-вызов: задержка ∈ [500; 800] мс от начала до resolve/reject;
   * успех — тело по контракту метода; отказ — ApiError {status,
   * body: {message, errors?}} из MockValidationError обработчика.
   *
   * Не зарегистрированный метод — ошибка сборки мок-слоя (не ответ
   * «бэкенда»): отказ без задержки, с обычным Error; прочие исключения
   * обработчиков (не MockValidationError) пробрасываются как есть — это
   * дефекты кода, а не эмулируемые HTTP-коды.
   */
  async call<T>(method: MockMethodId, params: unknown): Promise<T> {
    const handler = this.handlers.get(method);
    if (handler === undefined) {
      throw new Error(`MockApiClient: обработчик метода «${method}» не зарегистрирован`);
    }
    const startedAt = Date.now();
    try {
      const result = handler(this.db, params) as T;
      await sleep(remainingDelayMs(startedAt));
      return result;
    } catch (error) {
      await sleep(remainingDelayMs(startedAt));
      throw error instanceof MockValidationError ? toApiError(error) : error;
    }
  }
}
