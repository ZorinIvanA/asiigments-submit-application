/**
 * Маршруты приложения — дерево навигации §7 спеки (C-117, T-119): единственное
 * место композиции маршрутов фич (анонимная консоль преподавателя — guards
 * и гостевое семейство auth удалены вместе с аутентификацией).
 *
 * Правила композиции:
 *  - файлы фич экспортируют чистые Routes — здесь они монтируются как есть
 *    (компоненты и title фич сохраняются), всё под оболочкой AppShell;
 *  - корневые '' и '**' — редирект на список работ /works (домашний экран
 *    консоли преподавателя).
 */
import { Routes } from '@angular/router';

import { ACCESS_ROUTES } from '../../features/access/access.routes';
import { GROUPS_ROUTES } from '../../features/groups/groups.routes';
import { MY_SUBMISSIONS_ROUTES } from '../../features/my-submissions/my-submissions.routes';
import { PROFILE_ROUTES } from '../../features/profile/profile.routes';
import { SUBMISSIONS_ROUTES } from '../../features/submissions/submissions.routes';
import { WORKS_ROUTES } from '../../features/works/works.routes';
import { AppShell } from '../../layout/app-shell/app-shell';

export const routes: Routes = [
  // Корень '' → список работ (домашний экран консоли преподавателя).
  { path: '', pathMatch: 'full', redirectTo: 'works' },

  // Оболочка приложения (C-109): все экраны консоли преподавателя.
  {
    path: '',
    component: AppShell,
    children: [
      // Работы: /works, /works/new, /works/:id/edit (US-5..8).
      ...WORKS_ROUTES,
      // Ведомость /submissions (US-10).
      ...SUBMISSIONS_ROUTES,
      // Сдачи студента /my-submissions (US-11); путь объявляет обёртка,
      // маршрут '' фичи монтируется внутрь (экран в ветке студента спеки,
      // в консоли преподавателя показывает пустое состояние «без группы»).
      { path: 'my-submissions', children: MY_SUBMISSIONS_ROUTES },
      // Группы /groups и карточка /groups/:id (US-16); относительные '' и
      // ':id' фичи монтируются под обёрткой 'groups'.
      { path: 'groups', children: GROUPS_ROUTES },
      // Выдача доступа /access (US-17).
      ...ACCESS_ROUTES,
      // Профиль (§4.7/US-18).
      ...PROFILE_ROUTES,
    ],
  },

  // Wildcard '**' → список работ (неизвестный URL ведёт на домашний экран).
  { path: '**', redirectTo: 'works' },
];
