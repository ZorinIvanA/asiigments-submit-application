/**
 * Маршруты фичи auth (C-110, ADR-110): фича экспортирует свой набор
 * маршрутов локально; композицию в app.routes.ts и подключение guards
 * (guestGuard на /login и /register, recoveryStepGuard('code'|'reset')
 * на шагах восстановления, IF-108) выполняет единственная задача T-119 —
 * здесь guards не подключаются.
 */
import { Routes } from '@angular/router';

import { LoginPage } from './pages/login-page/login-page';
import { RecoveryCodePage } from './pages/recovery-code-page/recovery-code-page';
import { RecoveryPage } from './pages/recovery-page/recovery-page';
import { RegisterPage } from './pages/register-page/register-page';
import { ResetPasswordPage } from './pages/reset-password-page/reset-password-page';

export const AUTH_ROUTES: Routes = [
  // title — по образцу записей T-111 (ревью CR-003 T-110): каждый маршрут
  // фичи задаёт заголовок вкладки браузера.
  { path: 'login', component: LoginPage, title: 'Вход' },
  { path: 'register', component: RegisterPage, title: 'Регистрация' },
  // Восстановление пароля — шаги 1..3 (SCR-003/004/005, IF-102);
  // доступность шагов по store подключается задачей T-119 (IF-108).
  { path: 'recovery', component: RecoveryPage, title: 'Восстановление пароля' },
  { path: 'recovery/code', component: RecoveryCodePage, title: 'Восстановление пароля — код' },
  { path: 'reset-password', component: ResetPasswordPage, title: 'Новый пароль' },
];
