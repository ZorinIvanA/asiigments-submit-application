/**
 * Маршруты фичи «Сдача работ» студента (C-113, SCR-010): экран
 * /my-submissions. Подключение к app.routes выполняется задачей навигации
 * (C-108) — сам файл app.routes.ts фича не изменяет; guards (роль student,
 * homeGuard) — зона C-108, здесь не дублируются.
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
