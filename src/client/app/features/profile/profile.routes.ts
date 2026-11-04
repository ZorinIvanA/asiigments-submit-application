/**
 * Маршруты фичи «Профиль» (C-116, §4.7/US-18): страница /profile.
 * Подключение к дереву приложения (внутри оболочки) — конфигурация
 * app.routes.ts: файлы фич переэкспортируют свои компоненты и маршруты.
 */
import { Routes } from '@angular/router';

import { ProfilePage } from './pages/profile-page/profile-page';

/** Переэкспорт страницы фичи (сборка app.routes.ts). */
export { ProfilePage };

/** Маршруты фичи: /profile — потомок оболочки приложения. */
export const PROFILE_ROUTES: Routes = [
  {
    path: 'profile',
    component: ProfilePage,
    title: 'Профиль',
  },
];
