/**
 * SessionStore — доступ к ключу сессии mock.session.userId (C-101, FR-003,
 * ADR-105): единственный владелец ключа — домен Auth. Значение — uuid
 * пользователя из мок-БД либо отсутствие ключа = неавторизован; строка
 * хранится в localStorage как есть (без JSON-обёртки).
 *
 * Состояния в памяти нет — все операции читают/пишут localStorage напрямую,
 * поэтому любые экземпляры (сервисы core, замыкание registerAuthHandlers,
 * пересоздание после F5) видят одно и то же; проверка isAuthenticated()
 * в guards синхронна именно по наличию ключа (ADR-105).
 */
import { Injectable } from '@angular/core';

import { STORAGE_KEYS } from '../../shared/models';

@Injectable({ providedIn: 'root' })
export class SessionStore {
  /** uuid пользователя сессии либо null = сессии нет. */
  getUserId(): string | null {
    return localStorage.getItem(STORAGE_KEYS.session);
  }

  /** Ставит сессию (успешный register/login). */
  setUserId(userId: string): void {
    localStorage.setItem(STORAGE_KEYS.session, userId);
  }

  /** Очищает сессию (logout, битая сессия при loadMe). */
  clear(): void {
    localStorage.removeItem(STORAGE_KEYS.session);
  }

  /** Синхронный признак «пользователь в сессии» — по наличию ключа. */
  hasUser(): boolean {
    return this.getUserId() !== null;
  }
}
