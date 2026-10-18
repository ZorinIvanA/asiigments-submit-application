/**
 * Маршруты фичи works (ADR-110: каждая фича экспортирует свои Route[],
 * app.routes.ts компонует единый задачей T-119).
 *
 * Т-113 (этот файл создан формой): /works/new и /works/:id/edit — один
 * компонент LabFormPage на оба маршрута, режим определяется параметром id.
 * Запись списка /works (страница WorksPage) принадлежит T-112 и добавляется
 * в этот же массив: перед правкой перечитать файл, свои записи не трогать.
 * Guards (authGuard/roleGuard teacher) подключает T-119 на уровне
 * приложения — файл фичи их не дублирует.
 */
import { Routes } from '@angular/router';

import { LabFormPage } from './pages/lab-form-page/lab-form-page';
import { WorksPage } from './pages/works-page/works-page';

export const WORKS_ROUTES: Routes = [
  // T-112: список лабораторных — SCR-007.
  {
    path: 'works',
    component: WorksPage,
    title: 'Лабораторные работы',
  },
  {
    path: 'works/new',
    component: LabFormPage,
    title: 'Новая лабораторная работа',
  },
  {
    path: 'works/:id/edit',
    component: LabFormPage,
    title: 'Редактирование лабораторной работы',
  },
];
