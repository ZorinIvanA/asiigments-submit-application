/**
 * Конфигурация приложения (appConfig) — единый набор провайдеров бутстрапа:
 * маршруты, русская локаль, зона детекции изменений и тема PrimeNG Aura.
 *
 * Относится к core/config по NFR-003 («сервисы и конфиг»).
 */
import { registerLocaleData } from '@angular/common';
import localeRu from '@angular/common/locales/ru';
import {
  ApplicationConfig,
  LOCALE_ID,
  provideBrowserGlobalErrorListeners,
  provideZoneChangeDetection,
} from '@angular/core';
import { provideRouter } from '@angular/router';

import Aura from '@primeuix/themes/aura';
import { providePrimeNG } from 'primeng/config';

import { routes } from './app.routes';
import { PRIME_NG_RU } from './primeng-ru';

// Русская локаль (FR-001, NFR-004): данные локали регистрируются один раз
// при импорте конфигурации, чтобы быть доступными везде, где используется
// appConfig, включая unit-тесты.
registerLocaleData(localeRu);

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    { provide: LOCALE_ID, useValue: 'ru' },
    providePrimeNG({
      ripple: true,
      theme: {
        preset: Aura,
        options: {
          // Тёмная тема спекой и макетами не предусмотрена: фиксируем
          // светлую тему детерминированно, без автопереключения по ОС.
          darkModeSelector: false,
        },
      },
      translation: PRIME_NG_RU,
    }),
  ],
};
