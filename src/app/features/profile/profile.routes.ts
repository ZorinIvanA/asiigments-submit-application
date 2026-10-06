/**
 * Маршруты фичи «Профиль» (C-116): страница /profile доступна любой
 * авторизованной роли (§4.7/US-18). Подключение к дереву приложения
 * (внутри оболочки, guards) — задача T-119, единственная правка
 * app.routes.ts: файлы фич переэкспортируют свои компоненты и маршруты.
 */
import { Routes } from '@angular/router';

import { ProfilePage } from './pages/profile-page/profile-page';

/** Переэкспорт страницы фичи для T-119 (сборка app.routes.ts). */
export { ProfilePage };

/** Маршруты фичи: /profile — потомок оболочки приложения. */
export const PROFILE_ROUTES: Routes = [
  {
    path: 'profile',
    component: ProfilePage,
    title: 'Профиль',
  },
];
