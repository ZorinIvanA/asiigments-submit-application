/**
 * Навигационные guards (C-108, контракт IF-108, FR-4.8, ADR-105):
 * функциональные CanActivateFn поверх AuthService/RecoveryFlowStore.
 *
 * Схема защиты (IF-108):
 *  - наличие сессии проверяется синхронно по ключу localStorage
 *    (isAuthenticated, ADR-105) — переходы между вкладками мгновенны;
 *  - при первом входе после F5 роль доожидается однократным
 *    AuthService.loadMe() (кэш currentUser заполняется, дальше синхронно);
 *  - битая сессия (me 401) очищается внутри loadMe → редирект /login;
 *  - returnUrl не используется — после входа редирект по роли.
 *
 * Редиректы возвращаются как UrlTree; guards не выполняют побочных
 * вызовов доменов и не знают о маршрутах фич, кроме фиксированных
 * /login, /403, /recovery и домашних маршрутов ролей (ROLE_HOME).
 */
import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';

import { UserRole } from '../../shared/models';
// MeDto — через реэкспорт AuthService (IF-101): core не импортирует мок-слой
// напрямую, минуя сервисы (ревью CR-001 T-108).
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
 * Текущий пользователь для решения guard'а: без сессии — null (синхронно);
 * сессия есть, кэш пуст (первый вход после F5) — однократная догрузка
 * loadMe(); битая сессия (401) очищается внутри loadMe → null.
 */
async function resolveCurrentUser(auth: AuthService): Promise<MeDto | null> {
  if (!auth.isAuthenticated()) {
    return null;
  }
  return auth.currentUser() ?? (await auth.loadMe());
}

/**
 * Авторизованный доступ: нет сессии ИЛИ битая сессия → /login,
 * иначе навигация разрешена.
 */
export const authGuard: CanActivateFn = async () => {
  const router = inject(Router);
  const me = await resolveCurrentUser(inject(AuthService));
  return me !== null ? true : toLogin(router);
};

/**
 * Доступ по роли (after authGuard): нет/битая сессия → /login;
 * роль вне списка → /403; иначе навигация разрешена.
 */
export function roleGuard(...roles: UserRole[]): CanActivateFn {
  return async () => {
    const router = inject(Router);
    const me = await resolveCurrentUser(inject(AuthService));
    if (me === null) {
      return toLogin(router);
    }
    return roles.includes(me.role) ? true : router.parseUrl('/403');
  };
}

/**
 * Страницы входа/регистрации для гостя: есть сессия (с валидной ролью) →
 * домашний маршрут роли; гость (и битая сессия, очищенная loadMe) → вход.
 */
export const guestGuard: CanActivateFn = async () => {
  const router = inject(Router);
  const me = await resolveCurrentUser(inject(AuthService));
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
export const homeGuard: CanActivateFn = async () => {
  const router = inject(Router);
  const me = await resolveCurrentUser(inject(AuthService));
  return me !== null ? toHome(router, me.role) : toLogin(router);
};
