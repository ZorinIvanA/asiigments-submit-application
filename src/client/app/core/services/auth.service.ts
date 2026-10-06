/**
 * AuthService — сервис домена Auth для страниц (C-101, контракт IF-101,
 * FR-4.1/FR-4.2): вход, регистрация, выход, текущий пользователь и поток
 * восстановления пароля поверх MockApiClient.call. Наружу мок-слой
 * импортируют только сервисы core (FR-002) — страницы работают только
 * с этим сервисом.
 *
 * Гарантии IF-101:
 *  - register/login при успехе атомарно ставят сессию (мок) и обновляют
 *    кэш currentUser (здесь);
 *  - logout очищает сессию (мок) и кэш currentUser;
 *  - loadMe при отсутствии/битой сессии очищает её и возвращает null;
 *  - isAuthenticated() — синхронно по наличию mock.session.userId (ADR-105);
 *  - recovery-методы ведут recovery.flow.v1 (RecoveryFlowStore, IF-102):
 *    email — после запроса кода, resetToken — после подтверждения; успешный
 *    сброс очищает поток, терминальная ветка токена («Ссылка…» 400) удаляет
 *    только resetToken, email сохраняется.
 */
import { Injectable, inject, signal } from '@angular/core';

import { SessionStore } from '../../mock/auth/session-store';
import {
  AUTH_ERRORS,
  ConfirmRecoveryResult,
  LoginParams,
  RegisterParams,
} from '../../mock/auth/handlers';
import { MockApiClient } from '../../mock/mock-api-client';
import { isApiError } from '../../shared/api-error';
import { MeDto } from '../../shared/models';
import { RecoveryFlowStore } from './recovery-flow-store';

/** Типы контракта IF-101, потребляемые страницами auth (C-108/C-109/C-110). */
export type { ConfirmRecoveryResult, LoginParams, MeDto, RegisterParams };

/**
 * Тексты отказов IF-101 (единый источник — мок-обработчики домена Auth):
 * страницы распознают терминальные ветки сравнением с ними, не дублируя
 * литералы (ревью CR-001 T-111).
 */
export { AUTH_ERRORS };

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly client = inject(MockApiClient);
  private readonly session = inject(SessionStore);
  private readonly recoveryFlow = inject(RecoveryFlowStore);

  /** Приватный writable-кэш текущего пользователя; мутируют только методы сервиса. */
  private readonly currentUserState = signal<MeDto | null>(null);

  /**
   * Кэш текущего пользователя; null = не загружен/неавторизован (IF-101).
   * Наружу отдаётся только для чтения — asReadonly() поверх приватного
   * writable-сигнала (ревью CR-002 T-101).
   */
  readonly currentUser = this.currentUserState.asReadonly();

  /** Синхронная проверка сессии по наличию ключа mock.session.userId. */
  isAuthenticated(): boolean {
    return this.session.hasUser();
  }

  /** Вход (US-1): успех → сессия (мок) + кэш currentUser; отказ — ApiError. */
  async login(params: LoginParams): Promise<MeDto> {
    const me = await this.client.call<MeDto>('auth.login', params);
    this.currentUserState.set(me);
    return me;
  }

  /** Регистрация студента (US-15): успех → автоматический вход (§4.1). */
  async register(params: RegisterParams): Promise<MeDto> {
    const me = await this.client.call<MeDto>('auth.register', params);
    this.currentUserState.set(me);
    return me;
  }

  /** Выход (§4.1): сессия очищается моком, кэш — здесь. */
  async logout(): Promise<void> {
    await this.client.call('auth.logout', null);
    this.currentUserState.set(null);
  }

  /**
   * Загружает текущего пользователя (однократная догрузка после F5,
   * ADR-105). Нет сессии — null без вызова; битая сессия (401) — очищается,
   * кэш сбрасывается, null (IF-101).
   */
  async loadMe(): Promise<MeDto | null> {
    if (!this.session.hasUser()) {
      this.currentUserState.set(null);
      return null;
    }
    try {
      const me = await this.client.call<MeDto>('auth.me', null);
      this.currentUserState.set(me);
      return me;
    } catch (error: unknown) {
      if (isApiError(error) && error.status === 401) {
        this.session.clear();
        this.currentUserState.set(null);
        return null;
      }
      throw error;
    }
  }

  /** Шаг 1 потока (US-2): всегда 200; email запоминается для шага кода. */
  async requestRecoveryCode(email: string): Promise<void> {
    await this.client.call('auth.recovery.request', { email });
    this.recoveryFlow.setEmail(email);
  }

  /** Шаг 2 потока (US-3): resetToken запоминается для шага смены пароля. */
  async confirmRecoveryCode(email: string, code: string): Promise<ConfirmRecoveryResult> {
    const result = await this.client.call<ConfirmRecoveryResult>('auth.recovery.confirm', {
      email,
      code,
    });
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
      await this.client.call('auth.reset-password', { resetToken, password, confirmPassword });
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
}
