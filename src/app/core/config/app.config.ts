/**
 * Конфигурация приложения (appConfig) — единый набор провайдеров бутстрапа:
 * маршруты, русская локаль, зона детекции изменений, асинхронные анимации
 * и тема PrimeNG Aura с кастомизацией design-токенов по макетам
 * (AppThemePreset, ADR-108).
 *
 * Относится к core/config по NFR-003 («сервисы и конфиг»).
 */
import { registerLocaleData } from '@angular/common';
import localeRu from '@angular/common/locales/ru';
import {
  ApplicationConfig,
  LOCALE_ID,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  provideZoneChangeDetection,
} from '@angular/core';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter } from '@angular/router';

import { definePreset } from '@primeuix/themes';
import Aura from '@primeuix/themes/aura';
import { providePrimeNG } from 'primeng/config';

import { setupMockLayer } from '../../mock';
import { routes } from './app.routes';
import { PRIME_NG_RU } from './primeng-ru';

// Русская локаль (FR-001, NFR-004): данные локали регистрируются один раз
// при импорте конфигурации, чтобы быть доступными везде, где используется
// appConfig, включая unit-тесты.
registerLocaleData(localeRu);

/**
 * Пресет темы по макетам (T-109, ADR-108, design/mockups/*.html): build
 * от Aura через definePreset — кастомизируются только токены макетов,
 * остальные значения темы остаются базовыми:
 *  - primary — индиго макетов #4F46E5 (500) с hover #4338CA (600);
 *  - поля и кнопки 44px: paddingY 9px при строке текста 24px и рамке 1px
 *    (формулы высоты полей и кнопок Aura — из formField-токенов);
 *  - радиус полей 6px (кнопки наследуют тот же токен);
 *  - фон страницы #F1F5F9 (slate-100 макетов) — токен surface.ground
 *    светлой схемы, который используют body (styles.scss) и страницы;
 *  - радиус карточек 8px — токен компонента card.
 */
const AppThemePreset = definePreset(Aura, {
  semantic: {
    primary: {
      50: '#EEF2FF',
      100: '#E0E7FF',
      200: '#C7D2FE',
      300: '#A5B4FC',
      400: '#818CF8',
      500: '#4F46E5',
      600: '#4338CA',
      700: '#3730A3',
      800: '#312E81',
      900: '#1E1B4B',
      // 950 — реальный тон темнее 900 (ревью CR-001 T-109): шкала макетов
      // обрывается на 900, ступень достроена продолжением индиго, а не
      // дубликатом 900; в светлой схеме макетов ступень не используется.
      950: '#151332',
    },
    formField: {
      paddingX: '12px',
      paddingY: '9px',
      borderRadius: '6px',
    },
    colorScheme: {
      light: {
        surface: {
          ground: '#F1F5F9',
        },
      },
    },
  },
  components: {
    card: {
      root: {
        borderRadius: '8px',
      },
    },
  },
});

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    // Анимации (T-109, VBUG-001/VBUG-002): без провайдера анимаций PrimeNG-
    // оверлеи (Toast, ConfirmDialog, Dialog, Select) не работают в живом
    // приложении — при регистрации synthetic-слушателей (например,
    // @toastAnimation.start) Angular выдаёт NG05105 и рендер ограничивается
    // маской. provideAnimationsAsync подключает анимационный рендерер,
    // не загружая модуль анимаций до первого использования (меньше
    // стартового бандла); в unit-тестах перекрывается provideNoopAnimations.
    provideAnimationsAsync(),
    { provide: LOCALE_ID, useValue: 'ru' },
    providePrimeNG({
      ripple: true,
      theme: {
        preset: AppThemePreset,
        options: {
          // Тёмная тема спекой и макетами не предусмотрена: фиксируем
          // светлую тему детерминированно, без автопереключения по ОС.
          darkModeSelector: false,
        },
      },
      translation: PRIME_NG_RU,
    }),
    // Мок-слой (FR-002, IF-110, ADR-110): сид и обработчики всех доменов
    // регистрируются один раз при бутстрапе, до первого обращения любой
    // страницы к сервисам core. Повторная регистрация заменяет обработчики.
    provideAppInitializer(() => setupMockLayer()),
  ],
};
