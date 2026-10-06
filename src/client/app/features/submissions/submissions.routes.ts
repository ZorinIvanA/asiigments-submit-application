/**
 * Маршруты фичи «Сдача работ» (C-112, ADR-110): ведомость преподавателя
 * /submissions. app.routes.ts не правится — композицию всех маршрутов и
 * guards (роль teacher) выполняет задача навигации (T-119). Компонент
 * реэкспортируется наружу, чтобы композиция маршрутов не импортировала
 * внутренние файлы страницы.
 */
import { Routes } from '@angular/router';

import { SubmissionsPage } from './pages/submissions-page';

export { SubmissionsPage };

/** Ведомость преподавателя /submissions (SCR-009, FR-4.4, US-10). */
export const SUBMISSIONS_ROUTES: Routes = [
  {
    path: 'submissions',
    component: SubmissionsPage,
    title: 'Сдача работ',
  },
];
