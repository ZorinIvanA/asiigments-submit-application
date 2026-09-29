/**
 * Маршруты фичи «Группы» (C-114, ADR-110): фича экспортирует свои Route[]
 * в собственном файле и НЕ правит core/config/app.routes.ts — компоновку
 * всех маршрутов, guards и оболочку выполняет единая задача T-119,
 * монтирующая эти маршруты под путём 'groups' (/groups и /groups/:id
 * из §7). Компоненты страниц переэкспортированы для импорта из файлов фич.
 */
import { Routes } from '@angular/router';

import { GroupPage } from './pages/group-page';
import { GroupsPage } from './pages/groups-page';

export { GroupPage, GroupsPage };

export const GROUPS_ROUTES: Routes = [
  {
    path: '',
    component: GroupsPage,
    title: 'Группы',
  },
  {
    path: ':id',
    component: GroupPage,
    title: 'Карточка группы',
  },
];
