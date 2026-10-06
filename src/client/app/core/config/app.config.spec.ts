/**
 * Юнит-тесты конфигурации приложения: русская локаль, тема PrimeNG Aura,
 * русские переводы PrimeNG (требования FR-001/NFR-004 на уровне каркаса)
 * и провайдер анимаций для PrimeNG-оверлеев (VBUG-001/VBUG-002, NG05105).
 */
import { formatDate } from '@angular/common';
import { ANIMATION_MODULE_TYPE, Component, LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';

import Aura from '@primeuix/themes/aura';
import { MessageService } from 'primeng/api';
import { PrimeNG } from 'primeng/config';
import { Toast } from 'primeng/toast';

import { appConfig } from './app.config';
import { PRIME_NG_RU } from './primeng-ru';

/**
 * Хост с <p-toast/> — тот же PrimeNG-оверлей, чьи synthetic-слушатели
 * (@toastAnimation.start) вызывали NG05105 без провайдера анимаций.
 */
@Component({
  imports: [Toast],
  providers: [MessageService],
  template: '<p-toast />',
})
class ToastHost {}

describe('appConfig — конфигурация каркаса', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: appConfig.providers });
  });

  it('провайдер LOCALE_ID установлен в ru', () => {
    expect(TestBed.inject(LOCALE_ID)).toBe('ru');
  });

  it('данные локали ru зарегистрированы через registerLocaleData', () => {
    // Без registerLocaleData(localeRu) форматирование с 'ru' бросает
    // ошибку «Missing locale data for the locale ru».
    expect(formatDate(new Date(2026, 0, 15), 'dd.MM.yyyy', 'ru')).toBe(
      '15.01.2026',
    );
  });

  it('PrimeNG инициализирован с пресетом на базе Aura (кастомизация ADR-108)', () => {
    const primeNg = TestBed.inject(PrimeNG);
    // T-109: пресет строится definePreset(Aura, …) — дерево секций Aura
    // сохранено (semantic/components), заменой темы оно не становится.
    const preset = primeNg.theme()?.preset as
      | {
          primitive?: Record<string, unknown>;
          semantic?: Record<string, unknown>;
          components?: Record<string, unknown>;
        }
      | undefined;
    expect(preset).withContext('кастомный пресет определён').toBeTruthy();
    expect(preset!.semantic).withContext('секция semantic из Aura').toBeDefined();
    expect(preset!.components).withContext('секция components из Aura').toBeDefined();
    // definePreset не трогает primitive-секцию: она обязана совпасть с базой
    // Aura (усиление ассерта — ревью CR-002 T-109).
    expect(preset!.primitive)
      .withContext('primitive-секция без изменений от базы Aura')
      .toEqual(Aura.primitive as Record<string, unknown>);
  });

  it('PrimeNG инициализирован с русскими переводами', () => {
    const primeNg = TestBed.inject(PrimeNG);
    expect(primeNg.translation).toEqual(PRIME_NG_RU);
  });

  it('кнопки подтверждающих диалогов — «Yes»/«No» (исключение NFR-004)', () => {
    const primeNg = TestBed.inject(PrimeNG);
    expect(primeNg.translation.accept).toBe('Yes');
    expect(primeNg.translation.reject).toBe('No');
  });
});

describe('appConfig — провайдер анимаций (VBUG-001/VBUG-002, NG05105)', () => {
  it('provideAnimationsAsync включён: ANIMATION_MODULE_TYPE — BrowserAnimations', () => {
    TestBed.configureTestingModule({ providers: appConfig.providers });
    // Без провайдера анимаций токен не задан вовсе — именно это приводило
    // к NG05105 и неработающим оверлеям (Toast, ConfirmDialog, Select).
    expect(TestBed.inject(ANIMATION_MODULE_TYPE)).toBe('BrowserAnimations');
  });

  it('рендер p-toast (@toastAnimation.start) не бросает NG05105', () => {
    TestBed.configureTestingModule({ providers: appConfig.providers });
    const fixture = TestBed.createComponent(ToastHost);
    // Регистрация synthetic-слушателя @toastAnimation.start обычным
    // DOM-рендерером бросает NG05105; с provideAnimationsAsync рендер
    // проходит без ошибок.
    expect(() => fixture.detectChanges()).not.toThrow();
    expect(fixture.nativeElement.querySelector('.p-toast'))
      .withContext('контейнер тостов в DOM')
      .not.toBeNull();
  });

  it('паттерн unit-тестов [...appConfig.providers, provideNoopAnimations()] перекрывает анимации', () => {
    // Тесты TestBed по кодовой базе добавляют provideNoopAnimations ПОСЛЕ
    // appConfig.providers: последний провайдер побеждает, noop-анимации
    // сохраняются (детерминизм юнит-тестов не ломается).
    TestBed.configureTestingModule({
      providers: [...appConfig.providers, provideNoopAnimations()],
    });
    expect(TestBed.inject(ANIMATION_MODULE_TYPE)).toBe('NoopAnimations');
  });
});
