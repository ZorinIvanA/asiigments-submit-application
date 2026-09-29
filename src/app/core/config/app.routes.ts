/**
 * Маршруты приложения — дерево навигации §7 спеки (C-117, T-119, аменда 4
 * к ADR-110): единственное место композиции маршрутов фич и подключения
 * guards (IF-108).
 *
 * Правила композиции (аменда 4):
 *  - файлы фич экспортируют чистые Routes БЕЗ guards и route data — здесь
 *    они монтируются как есть (компоненты и title фич сохраняются);
 *  - сквозные guards — на componentless-обёртках: guestGuard на гостевом
 *    семействе auth, authGuard на оболочке AppShell;
 *  - точечные guards — немутирующим хелпером withGuards, создающим КОПИИ
 *    объектов маршрутов с дописанным canActivate: recoveryStepGuard на
 *    шагах восстановления, roleGuard на страницах ролей;
 *  - корневые '' и '**' — homeGuard (сессия → домашний маршрут роли
 *    ROLE_HOME, иначе /login); /403 — внутри children оболочки без
 *    roleGuard (туда ведёт redirect roleGuard, IF-108).
 */
import { CanActivateFn, Routes } from '@angular/router';

import { ACCESS_ROUTES } from '../../features/access/access.routes';
import { AUTH_ROUTES } from '../../features/auth/auth.routes';
import { GROUPS_ROUTES } from '../../features/groups/groups.routes';
import { MY_SUBMISSIONS_ROUTES } from '../../features/my-submissions/my-submissions.routes';
import { PROFILE_ROUTES } from '../../features/profile/profile.routes';
import { SUBMISSIONS_ROUTES } from '../../features/submissions/submissions.routes';
import { WORKS_ROUTES } from '../../features/works/works.routes';
import { AppShell } from '../../layout/app-shell/app-shell';
import { GuestShell } from '../../layout/guest-shell/guest-shell';
import { Page403 } from '../../layout/page-403/page-403';
import {
  authGuard,
  guestGuard,
  homeGuard,
  recoveryStepGuard,
  roleGuard,
} from '../navigation/guards';

/**
 * Немутирующий хелпер (аменда 4): возвращает НОВЫЙ массив с копиями
 * объектов маршрутов; на копии, чей path удовлетворяет pathMatcher,
 * дописывается canActivate (уже имеющиеся guard'ы сохраняются). Исходные
 * массивы и объекты фич не изменяются.
 */
function withGuards(
  routes: Routes,
  pathMatcher: (path: string) => boolean,
  guard: CanActivateFn,
): Routes {
  return routes.map((route) =>
    pathMatcher(route.path ?? '')
      ? { ...route, canActivate: [...(route.canActivate ?? []), guard] }
      : { ...route },
  );
}

/** Пути семейства works: 'works', 'works/new', 'works/:id/edit'. */
function isWorksPath(path: string): boolean {
  return path === 'works' || path.startsWith('works/');
}

export const routes: Routes = [
  // Корень '' → homeGuard: сессия → домашний маршрут роли, иначе /login
  // (guard всегда возвращает UrlTree — маршрут никуда не рендерится).
  { path: '', pathMatch: 'full', canActivate: [homeGuard], children: [] },

  // Гостевое семейство auth (§7 «все»): сквозной guestGuard на обёртке;
  // внутри — гостевая оболочка GuestShell (§4.8, VS-006, фикс VBUG-004):
  // топбар с вкладками «Вход», «Регистрация», «Восстановление пароля»
  // виден только гостю, сами страницы фичи не меняются; шаги восстановления
  // дополнительно защищены точечными recoveryStepGuard: /recovery/code
  // требует store.email, /reset-password — store.resetToken (IF-108,
  // ADR-106); прямые deep-link на пустом потоке → /recovery.
  {
    path: '',
    canActivate: [guestGuard],
    children: [
      {
        path: '',
        component: GuestShell,
        children: withGuards(
          withGuards(
            AUTH_ROUTES,
            (path) => path === 'recovery/code',
            recoveryStepGuard('code'),
          ),
          (path) => path === 'reset-password',
          recoveryStepGuard('reset'),
        ),
      },
    ],
  },

  // Оболочка авторизованной части (C-109): сквозной authGuard — без сессии
  // /login; страницы фич с точечными roleGuard по §7; /profile — любая
  // роль; /403 — без roleGuard (доступна любой роли, IF-108).
  {
    path: '',
    component: AppShell,
    canActivate: [authGuard],
    children: [
      // Преподаватель: works, works/new, works/:id/edit (US-5..8).
      ...withGuards(WORKS_ROUTES, isWorksPath, roleGuard('teacher')),
      // Преподаватель: ведомость /submissions (US-10).
      ...withGuards(
        SUBMISSIONS_ROUTES,
        (path) => path === 'submissions',
        roleGuard('teacher'),
      ),
      // Студент: сдачи /my-submissions (US-11); путь объявляет обёртка,
      // маршрут '' фичи монтируется внутрь (roleGuard('student') на копии).
      {
        path: 'my-submissions',
        children: withGuards(
          MY_SUBMISSIONS_ROUTES,
          (path) => path === '',
          roleGuard('student'),
        ),
      },
      // Преподаватель: группы /groups и карточка /groups/:id (US-16);
      // относительные '' и ':id' фичи монтируются под обёрткой 'groups'.
      {
        path: 'groups',
        children: withGuards(
          GROUPS_ROUTES,
          (path) => path === '' || path === ':id',
          roleGuard('teacher'),
        ),
      },
      // Преподаватель: выдача доступа /access (US-17).
      ...withGuards(
        ACCESS_ROUTES,
        (path) => path === 'access',
        roleGuard('teacher'),
      ),
      // Профиль — любая авторизованная роль (§4.7/US-18): без roleGuard.
      ...PROFILE_ROUTES,
      // 403 «Доступ запрещён» — без roleGuard: сюда ведёт redirect
      // roleGuard чужой роли (IF-108).
      { path: '403', component: Page403 },
    ],
  },

  // Wildcard '**' → homeGuard: неизвестный URL ведёт по той же схеме
  // (сессия → домашний маршрут роли, иначе /login).
  { path: '**', canActivate: [homeGuard], children: [] },
];
