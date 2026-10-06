/**
 * Юнит-тесты кастомизации design-токенов темы (T-109, ADR-108, макеты
 * design/mockups/*.html): primary индиго макетов #4F46E5 (hover #4338CA),
 * поля и кнопки 44px (paddingY 9px при строке текста 24px и рамке 1px),
 * радиус полей 6px, карточек 8px, фон страницы #F1F5F9 — токен
 * surface.ground светлой схемы, который используют global-стили
 * (styles.scss: фон body) и страницы. Тёмная схема остаётся отключённой.
 *
 * Утверждения делаются по пресету, зарегистрированному в PrimeNG-сервисе
 * (theme() — тот же объект, что передан providePrimeNG в appConfig).
 */
import { TestBed } from '@angular/core/testing';

import { PrimeNG } from 'primeng/config';

import { appConfig } from './app.config';

/** Пресет — произвольное дерево токенов; типизация theme() в PrimeNG — any. */
function themePreset(): any {
  return TestBed.inject(PrimeNG).theme()?.preset;
}

describe('design-токены темы по макетам (T-109, ADR-108)', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: appConfig.providers });
  });

  it('primary — индиго макетов: #4F46E5 основной и #4338CA hover', () => {
    const primary = themePreset().semantic.primary;
    expect(primary[500]).toBe('#4F46E5');
    expect(primary[600]).toBe('#4338CA');
  });

  it('поля и кнопки 44px: paddingY 9px (24px строка + 2×9px + 2×1px рамка)', () => {
    const formField = themePreset().semantic.formField;
    expect(formField.paddingY).toBe('9px');
    expect(formField.paddingX).toBe('12px');
  });

  it('радиус полей ввода 6px', () => {
    expect(themePreset().semantic.formField.borderRadius).toBe('6px');
  });

  it('радиус карточек 8px (токен компонента card)', () => {
    expect(themePreset().components.card.root.borderRadius).toBe('8px');
  });

  it('фон страницы #F1F5F9 — токен surface.ground светлой схемы', () => {
    const surface = themePreset().semantic.colorScheme.light.surface;
    expect(surface.ground).toBe('#F1F5F9');
  });

  it('тёмная схема по-прежнему отключена (darkModeSelector: false)', () => {
    const theme = TestBed.inject(PrimeNG).theme() as
      | { options?: { darkModeSelector?: unknown } }
      | undefined;
    expect(theme?.options?.darkModeSelector).toBe(false);
  });
});
