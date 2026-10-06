/**
 * Точка входа приложения: бутстрап корневого компонента AppRoot
 * с конфигурацией appConfig (маршруты, русская локаль, тема PrimeNG).
 *
 * Ошибки бутстрапа пробрасываются наружу, а не пишутся в консоль самим
 * кодом приложения: чистота консоли браузера — требование NFR-006.
 */
import { bootstrapApplication } from '@angular/platform-browser';

import { appConfig } from './app/core/config/app.config';
import { AppRoot } from './app/layout/app-root/app-root';

bootstrapApplication(AppRoot, appConfig).catch((err: unknown) => {
  throw err;
});
