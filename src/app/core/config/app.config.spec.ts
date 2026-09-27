/**
 * Юнит-тесты конфигурации приложения: русская локаль, тема PrimeNG Aura
 * и русские переводы PrimeNG (требования FR-001/NFR-004 на уровне каркаса).
 */
import { formatDate } from '@angular/common';
import { LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import Aura from '@primeuix/themes/aura';
import { PrimeNG } from 'primeng/config';

import { appConfig } from './app.config';
import { PRIME_NG_RU } from './primeng-ru';

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

  it('PrimeNG инициализирован с темой Aura', () => {
    const primeNg = TestBed.inject(PrimeNG);
    expect(primeNg.theme()?.preset).toBe(Aura);
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
