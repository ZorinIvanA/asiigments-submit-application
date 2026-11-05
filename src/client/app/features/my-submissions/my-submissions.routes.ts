/**
 * Маршруты фичи «Сдача работ» (C-113, SCR-010): экран /my-submissions
 * (в анонимной консоли преподавателя — состояние «без группы»).
 * Подключение к дереву выполняет конфигурация app.routes.ts — сам файл
 * app.routes.ts фича не изменяет.
 */
import { Routes } from '@angular/router';

export const MY_SUBMISSIONS_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./pages/my-submissions-page').then((m) => m.MySubmissionsPage),
    title: 'Сдача работ',
  },
];
