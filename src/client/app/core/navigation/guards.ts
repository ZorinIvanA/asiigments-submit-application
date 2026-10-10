/**
 * Навигационные guards (C-014, контракт IF-108, FR-4.8, FR-092):
 * функциональные CanActivateFn поверх AuthService/RecoveryFlowStore.
 *
 * Схема защиты (IF-108/FR-092):
 *  - сессия существует ТОЛЬКО в памяти AuthService: isAuthenticated()
 *    синхронно отражает currentUser; localStorage не читается и не пишется
 *    (localStorage-ключ сессии прошлой, моковой реализации удалён из
 *    обращения — FR-092);
 *  - кэш currentUser наполняет инициализация сессии (AuthService.initSession
 *    из provideAppInitializer: GET /auth/me → 200) ДО первого решения
 *    guard'а, поэтому guard'и синхронны и HTTP-вызовов не выполняют;
 *  - аноним (пустой кэш: 401 от /auth/me, logout или событие
 *    sessionExpired$) → редирект /login;
 *  - returnUrl не используется — после входа редирект по роли.
 *
 * Редиректы возвращаются как UrlTree; guards не выполняют побочных
 * вызовов доменов и не знают о маршрутах фич, кроме фиксированных
 * /login, /403, /recovery и домашних маршрутов ролей (ROLE_HOME).
 */
import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';

import { UserRole } from '../../shared/models';
// MeDto — через реэкспорт AuthService (IF-007): единый словарь типов
// домена Auth для guard'ов и страниц.
import { AuthService, MeDto } from '../services/auth.service';
import { RecoveryFlowStore } from '../services/recovery-flow-store';

/** Домашний маршрут по роли (IF-108/IF-111): после входа и для guest. */
export const ROLE_HOME: Record<UserRole, string> = {
  teacher: '/works',
  student: '/my-submissions',
};

/** UrlTree /login — единая точка редиректа неавторизованных. */
function toLogin(router: Router): UrlTree {
  return router.parseUrl('/login');
}

/** UrlTree домашнего маршрута роли (ROLE_HOME). */
function toHome(router: Router, role: UserRole): UrlTree {
  return router.parseUrl(ROLE_HOME[role]);
}

/**
 * Текущий пользователь для решения guard'а: кэш AuthService (сессия только
 * в памяти — FR-092). Пустой кэш = аноним: loadMe здесь не вызывается —
 * холодный старт обслуживает initSession (provideAppInitializer).
 */
function resolveCurrentUser(auth: AuthService): MeDto | null {
  return auth.isAuthenticated() ? auth.currentUser() : null;
}

/**
 * Авторизованный доступ: нет сессии → /login, иначе навигация разрешена.
 */
export const authGuard: CanActivateFn = () => {
  const router = inject(Router);
  const me = resolveCurrentUser(inject(AuthService));
  return me !== null ? true : toLogin(router);
};

/**
 * Доступ по роли (after authGuard): нет сессии → /login;
 * роль вне списка → /403; иначе навигация разрешена.
 */
export function roleGuard(...roles: UserRole[]): CanActivateFn {
  return () => {
    const router = inject(Router);
    const me = resolveCurrentUser(inject(AuthService));
    if (me === null) {
      return toLogin(router);
    }
    return roles.includes(me.role) ? true : router.parseUrl('/403');
  };
}

/**
 * Страницы входа/регистрации для гостя: есть сессия (с валидной ролью) →
 * домашний маршрут роли; гость → вход.
 */
export const guestGuard: CanActivateFn = () => {
  const router = inject(Router);
  const me = resolveCurrentUser(inject(AuthService));
  return me !== null ? toHome(router, me.role) : true;
};

/**
 * Шаги потока восстановления (ADR-106): шаг 'code' (/recovery/code)
 * требует store.email, шаг 'reset' (/reset-password) — store.resetToken;
 * прямые deep-link на пустом потоке → /recovery (безопасный рестарт).
 */
export function recoveryStepGuard(step: 'code' | 'reset'): CanActivateFn {
  return () => {
    const router = inject(Router);
    const flow = inject(RecoveryFlowStore);
    const allowed = step === 'code' ? flow.hasEmail() : flow.hasResetToken();
    return allowed ? true : router.parseUrl('/recovery');
  };
}

/**
 * Корень '' и wildcard '**': есть сессия → домашний маршрут роли,
 * иначе → /login (вход с последующим редиректом по роли).
 */
export const homeGuard: CanActivateFn = () => {
  const router = inject(Router);
  const me = resolveCurrentUser(inject(AuthService));
  return me !== null ? toHome(router, me.role) : toLogin(router);
};
