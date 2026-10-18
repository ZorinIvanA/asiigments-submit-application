/**
 * RecoveryFlowStore — хранилище потока восстановления пароля (C-101,
 * контракт IF-102, ADR-106): ключ sessionStorage recovery.flow.v1
 * (FR-003). Переживает F5 в пределах вкладки; шаги /recovery/code и
 * /reset-password проверяют hasEmail()/hasResetToken() синхронно
 * (recoveryStepGuard).
 *
 * Стор принадлежит домену Auth: email ставит AuthService после успешного
 * запроса кода, resetToken — после успешного подтверждения кода; очистка —
 * при завершении потока (успешный сброс), отмене (выход на /login) и в
 * терминальной ветке токена (удаляется только resetToken, email
 * сохраняется — IF-102).
 */
import { Injectable, computed, signal } from '@angular/core';

import { STORAGE_KEYS } from '../../shared/models';

/** Содержимое ключа recovery.flow.v1. */
interface RecoveryFlowState {
  email: string | null;
  resetToken: string | null;
}

const EMPTY_FLOW: RecoveryFlowState = { email: null, resetToken: null };

/** Защитное чтение ключа: мусор/битый JSON → пустое состояние. */
function readFlow(): RecoveryFlowState {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEYS.recoveryFlow);
    if (raw === null) {
      return { ...EMPTY_FLOW };
    }
    const parsed: unknown = JSON.parse(raw);
    if (typeof parsed !== 'object' || parsed === null) {
      return { ...EMPTY_FLOW };
    }
    const record = parsed as Record<string, unknown>;
    return {
      email: typeof record['email'] === 'string' ? record['email'] : null,
      resetToken: typeof record['resetToken'] === 'string' ? record['resetToken'] : null,
    };
  } catch {
    return { ...EMPTY_FLOW };
  }
}

@Injectable({ providedIn: 'root' })
export class RecoveryFlowStore {
  private readonly state = signal<RecoveryFlowState>(readFlow());

  /** Email, для которого запрошен код восстановления. */
  readonly email = computed(() => this.state().email);

  /** Токен сброса, полученный подтверждением кода. */
  readonly resetToken = computed(() => this.state().resetToken);

  /** Запоминает email шага «Восстановление: email». */
  setEmail(email: string): void {
    this.write({ ...this.state(), email });
  }

  /** Запоминает resetToken шага «Восстановление: код». */
  setResetToken(token: string): void {
    this.write({ ...this.state(), resetToken: token });
  }

  /** Предикат для recoveryStepGuard шага /recovery/code. */
  hasEmail(): boolean {
    const email = this.email();
    return email !== null && email !== '';
  }

  /** Предикат для recoveryStepGuard шага /reset-password. */
  hasResetToken(): boolean {
    const resetToken = this.resetToken();
    return resetToken !== null && resetToken !== '';
  }

  /** Полная очистка потока: успешный сброс пароля или отмена. */
  clear(): void {
    this.write({ ...EMPTY_FLOW });
  }

  /** Терминальная ветка токена: удаляется только resetToken, email остаётся. */
  clearResetToken(): void {
    this.write({ ...this.state(), resetToken: null });
  }

  /** Единственная точка записи: сигнал + sessionStorage согласованно. */
  private write(state: RecoveryFlowState): void {
    this.state.set(state);
    sessionStorage.setItem(STORAGE_KEYS.recoveryFlow, JSON.stringify(state));
  }
}
