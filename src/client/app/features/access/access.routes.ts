/**
 * Маршруты фичи «Доступ» (C-115, ADR-110): раздел целиком объявляется в
 * собственном файле фичи, app.routes.ts не правится — композицию всех
 * маршрутов и оболочку выполняет конфигурация app.routes.ts. Компонент
 * реэкспортируется наружу, чтобы композиция маршрутов не импортировала
 * внутренние файлы страницы.
 */
import { Routes } from '@angular/router';

import { AccessPage } from './pages/access-page/access-page';

export { AccessPage };

export const ACCESS_ROUTES: Routes = [
  {
    path: 'access',
    component: AccessPage,
    title: 'Доступ',
  },
];
