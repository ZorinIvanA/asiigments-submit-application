/**
 * AuthService — сервис домена Auth для страниц (C-014, контракты IF-007 и
 * IF-014, FR-091/FR-092): вход, регистрация, выход, текущий пользователь и
 * поток восстановления пароля поверх HttpClient. Базовый префикс — токен
 * API_BASE_URL (ADR-009); запросы идут с withCredentials: true через
 * authInterceptor (C-013), HTTP-отказы нормализуются им в ApiError
 * {status, body: {message, errors?}} (http-errors.ts) — страницы работают
 * только с этим сервисом и прежней формой отказов.
 *
 * Гарантии IF-014/FR-092:
 *  - признак сессии ТОЛЬКО в памяти: isAuthenticated() синхронно отражает
 *    currentUser; localStorage не читается и не пишется (localStorage-ключ
 *    сессии прошлой, моковой реализации удалён из обращения — FR-092);
 *  - login/register при успехе (2xx) обновляют кэш currentUser из MeDto и
 *    синхронно публикуют SessionLifecycle.markSessionRestored() (сброс
 *    фиксатора неудачного refresh — ADR-019);
 *  - logout (POST /auth/logout) сбрасывает кэш после 2xx; сброс по событию
 *    sessionExpired$ выполняется подпиской без единого HTTP-вызова
 *    (никаких POST /auth/logout из сервиса — ADR-019);
 *  - loadMe — GET /auth/me: 200 → кэш; 401 → аноним (null) без
 *    необработанных исключений; initSession() для provideAppInitializer
 *    вызывает loadMe и завершает фазу инициализации markInitDone() при
 *    любом исходе (401/сеть — анонимный старт, редирект определяют guards);
 *  - recovery-методы ведут recovery.flow.v1 (RecoveryFlowStore, IF-102):
 *    email — после запроса кода, resetToken — после подтверждения; успешный
 *    сброс очищает поток, терминальная ветка токена («Ссылка…» 400) удаляет
 *    только resetToken, email сохраняется.
 */
import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { API_BASE_URL } from '../api-base-url';
import { SessionLifecycle } from '../session-lifecycle';
import { isApiError } from '../../shared/api-error';
import {
  AUTH_ERRORS,
  ConfirmRecoveryResult,
  LoginParams,
  MeDto,
  RegisterParams,
} from '../../shared/models';
import { RecoveryFlowStore } from './recovery-flow-store';

/** Типы контракта IF-007, потребляемые страницами auth (C-108/C-109/C-110). */
export type { ConfirmRecoveryResult, LoginParams, MeDto, RegisterParams };

/**
 * Тексты отказов IF-007 (единый источник — shared/models, FR-090):
 * страницы распознают терминальные ветки сравнением с ними, не дублируя
 * литералы (ревью CR-001 T-111).
 */
export { AUTH_ERRORS };

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly apiBase = inject(API_BASE_URL);
  private readonly lifecycle = inject(SessionLifecycle);
  private readonly recoveryFlow = inject(RecoveryFlowStore);

  /** Приватный writable-кэш текущего пользователя; мутируют только методы сервиса. */
  private readonly currentUserState = signal<MeDto | null>(null);

  /**
   * Кэш текущего пользователя; null = не загружен/неавторизован (IF-014).
   * Наружу отдаётся только для чтения — asReadonly() поверх приватного
   * writable-сигнала (ревью CR-002 T-101).
   */
  readonly currentUser = this.currentUserState.asReadonly();

  constructor() {
    // Сброс сессии по каналу недействительной сессии (IF-014, ADR-019):
    // публикатор — только интерцептор после неудачного refresh; сброс —
    // в памяти, без единого HTTP-вызова (никаких POST /auth/logout).
    // Подписка живёт столько же, сколько root-сервис (время жизни
    // приложения) — паттерн RefreshCoordinator интерцептора.
    this.lifecycle.sessionExpired$.subscribe(() => {
      this.resetSession();
    });
  }

  /** Синхронная проверка сессии — строго по кэшу currentUser (FR-092). */
  isAuthenticated(): boolean {
    return this.currentUser() !== null;
  }

  /**
   * Инициализация сессии для provideAppInitializer (FR-092): loadMe → 200 —
   * currentUser известен до первого решения guard'а; 401/сеть — анонимный
   * старт без необработанных исключений; в любом случае фаза инициализации
   * завершается markInitDone() (до его вызова интерцептор не навигирует —
   * ADR-016, редирект определяют guards).
   */
  async initSession(): Promise<void> {
    try {
      await this.loadMe();
    } catch {
      // Сеть/5xx — начинаем анонимом: guards решают редирект, повторная
      // догрузка возможна последующими вызовами loadMe().
    } finally {
      this.lifecycle.markInitDone();
    }
  }

  /** Вход (US-1): POST /auth/login → кэш currentUser + markSessionRestored. */
  async login(params: LoginParams): Promise<MeDto> {
    const me = await this.establishSession(`${this.apiBase}/auth/login`, params);
    return me;
  }

  /** Регистрация студента (US-15): успех → автоматический вход (§4.1). */
  async register(params: RegisterParams): Promise<MeDto> {
    const me = await this.establishSession(`${this.apiBase}/auth/register`, params);
    return me;
  }

  /** Выход (§4.1): POST /auth/logout (204) → кэш сброшен здесь. */
  async logout(): Promise<void> {
    await this.post(`${this.apiBase}/auth/logout`, null);
    this.resetSession();
  }

  /**
   * Загружает текущего пользователя (GET /auth/me — холодный старт и
   * догрузка). 200 → кэш и MeDto; 401 («Не авторизован»; после исчерпанной
   * попытки тихого refresh интерцептора) → аноним: кэш сброшен, null без
   * необработанных исключений (FR-092); прочие отказы пробрасываются
   * страницам в форме ApiError.
   */
  async loadMe(): Promise<MeDto | null> {
    try {
      const me = await this.get<MeDto>(`${this.apiBase}/auth/me`);
      this.currentUserState.set(me);
      return me;
    } catch (error: unknown) {
      if (isApiError(error) && error.status === 401) {
        this.resetSession();
        return null;
      }
      throw error;
    }
  }

  /** Шаг 1 потока (US-2): всегда 200; email запоминается для шага кода. */
  async requestRecoveryCode(email: string): Promise<void> {
    await this.post(`${this.apiBase}/auth/recovery/request`, { email });
    this.recoveryFlow.setEmail(email);
  }

  /** Шаг 2 потока (US-3): resetToken запоминается для шага смены пароля. */
  async confirmRecoveryCode(email: string, code: string): Promise<ConfirmRecoveryResult> {
    const result = await this.post<ConfirmRecoveryResult>(
      `${this.apiBase}/auth/recovery/confirm`,
      { email, code },
    );
    this.recoveryFlow.setResetToken(result.resetToken);
    return result;
  }

  /**
   * Шаг 3 потока (US-4): успех — поток очищается полностью; терминальная
   * ветка токена (400 «Ссылка…») — удаляется только resetToken, email
   * сохраняется (IF-102); отказ пробрасывается странице.
   */
  async resetPassword(
    resetToken: string,
    password: string,
    confirmPassword: string,
  ): Promise<void> {
    try {
      await this.post(`${this.apiBase}/auth/reset-password`, {
        resetToken,
        password,
        confirmPassword,
      });
      this.recoveryFlow.clear();
    } catch (error: unknown) {
      if (
        isApiError(error) &&
        error.status === 400 &&
        error.body.message === AUTH_ERRORS.resetLinkInvalid
      ) {
        this.recoveryFlow.clearResetToken();
      }
      throw error;
    }
  }

  /**
   * Общая ветка установления сессии (login/register): 2xx + MeDto → кэш
   * обновлён и markSessionRestored() опубликован СИНХРОННО до разрешения
   * промиса (подписчики sessionRestored$ видят уже заполненный кэш) —
   * сброс фиксатора неудачного refresh (IF-014, ADR-019).
   */
  private async establishSession(url: string, body: unknown): Promise<MeDto> {
    const me = await this.post<MeDto>(url, body);
    this.currentUserState.set(me);
    this.lifecycle.markSessionRestored();
    return me;
  }

  /** GET c базовым префиксом; withCredentials выставляет интерцептор. */
  private get<T>(url: string): Promise<T> {
    return firstValueFrom(this.http.get<T>(url));
  }

  /** POST c базовым префиксом и JSON-телом (null — тело отсутствует). */
  private post<T>(url: string, body: unknown): Promise<T> {
    return firstValueFrom(this.http.post<T>(url, body));
  }

  /** Единственная точка сброса сессии: кэш currentUser — null (FR-092). */
  private resetSession(): void {
    this.currentUserState.set(null);
  }
}
