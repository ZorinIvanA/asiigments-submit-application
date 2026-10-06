/**
 * Unit-тесты NotificationService (IF-009, FR-021): выбор режима по
 * брейкпоинту 768px, маршрутизация якоря formId → форма-инициатор,
 * fallback 'header' при неизвестном formId, автозакрытие 5000 мс
 * (fakeAsync), досрочное закрытие, допустимые тексты успеха.
 */
import { BreakpointObserver } from '@angular/cdk/layout';
import { fakeAsync, TestBed, tick } from '@angular/core/testing';

import { MockBreakpointObserver } from '../../../testing/mock-breakpoint-observer';
import { NotificationService } from './notification-service';

describe('NotificationService', () => {
  let breakpoints: MockBreakpointObserver;
  let service: NotificationService;

  function setup(initialMobile: boolean): void {
    breakpoints = new MockBreakpointObserver();
    TestBed.configureTestingModule({
      providers: [{ provide: BreakpointObserver, useValue: breakpoints }],
    });
    service = TestBed.inject(NotificationService);
    breakpoints.simulate(initialMobile);
  }

  afterEach(() => {
    // Гасим возможный незавершённый таймер автозакрытия, чтобы он не
    // срабатывал после конца теста.
    service.dismissMobile();
  });

  it('первоначально уведомлений нет, режим десктопный', () => {
    setup(false);
    expect(service.desktopMessage()).toBeNull();
    expect(service.mobileMessage()).toBeNull();
    expect(service.isMobile()).toBe(false);
  });

  describe('выбор режима по брейкпоинту (>=768px Toast, <768px inline)', () => {
    it('на десктопе ошибка публикуется только в десктоп-канал', () => {
      setup(false);
      service.notifyError('Лабораторная с таким номером уже есть в семестре', {
        formId: 'lab-form',
      });
      expect(service.desktopMessage()).toEqual({
        severity: 'error',
        text: 'Лабораторная с таким номером уже есть в семестре',
      });
      expect(service.mobileMessage()).toBeNull();
    });

    it('на мобильном ошибка публикуется только в мобильный канал', () => {
      setup(true);
      service.notifyError('Неверный логин или пароль');
      expect(service.mobileMessage()).toEqual({
        severity: 'error',
        text: 'Неверный логин или пароль',
        formId: null,
      });
      expect(service.desktopMessage()).toBeNull();
    });

    it('режим переключается сменой ширины окна', () => {
      setup(true);
      breakpoints.simulate(false);
      service.notifyError('Ошибка А');
      expect(service.mobileMessage()).toBeNull();
      expect(service.desktopMessage()?.text).toBe('Ошибка А');

      breakpoints.simulate(true);
      service.notifyError('Ошибка Б');
      expect(service.desktopMessage()).toBeNull();
      expect(service.mobileMessage()?.text).toBe('Ошибка Б');
    });

    it('смена режима гасит показанное мобильное уведомление', () => {
      setup(true);
      service.notifyError('Ошибка');
      breakpoints.simulate(false);
      expect(service.mobileMessage()).toBeNull();
    });
  });

  describe('маршрутизация якоря formId (AR-017: две формы /profile)', () => {
    it('зарегистрированный formId маршрутизируется под форму-инициатор', () => {
      setup(true);
      service.registerAnchor('profile-form');
      service.registerAnchor('password-form');
      service.notifyError('Email уже занят', { formId: 'profile-form' });
      expect(service.mobileMessage()).toEqual({
        severity: 'error',
        text: 'Email уже занят',
        formId: 'profile-form',
      });

      service.notifyError('Неверный текущий пароль', { formId: 'password-form' });
      expect(service.mobileMessage()?.formId).toBe('password-form');
    });

    it('незарегистрированный formId резервно уходит под шапку (IF-009)', () => {
      setup(true);
      service.notifyError('Ошибка', { formId: 'ghost-form' });
      expect(service.mobileMessage()?.formId).toBeNull();
    });

    it('явный якорь header адресуется под шапку', () => {
      setup(true);
      service.notifyError('Ошибка', 'header');
      expect(service.mobileMessage()?.formId).toBeNull();
    });

    it('после снятия якоря с формы formId снова неизвестен', () => {
      setup(true);
      service.registerAnchor('profile-form');
      service.unregisterAnchor('profile-form');
      service.notifyError('Ошибка', { formId: 'profile-form' });
      expect(service.mobileMessage()?.formId).toBeNull();
    });
  });

  describe('успех: только три допустимых текста (FR-021)', () => {
    it('каждый из трёх текстов публикуется дословно с severity success', () => {
      setup(true);
      for (const text of ['Сохранено', 'Удалено', 'Пароль изменён'] as const) {
        breakpoints.simulate(false);
        service.notifySuccess(text);
        expect(service.desktopMessage()).toEqual({ severity: 'success', text });

        // Смена режима гасит предыдущее уведомление — можно проверять канал заново.
        breakpoints.simulate(true);
        service.notifySuccess(text);
        expect(service.mobileMessage()).toEqual({
          severity: 'success',
          text,
          formId: null,
        });
      }
    });
  });

  describe('автозакрытие 5000 мс в обоих режимах (fakeAsync)', () => {
    it('мобильное уведомление живёт ровно до 5000 мс', fakeAsync(() => {
      setup(true);
      service.notifyError('Ошибка');
      tick(4999);
      expect(service.mobileMessage()).not.toBeNull();
      tick(1);
      expect(service.mobileMessage()).toBeNull();
    }));

    it('десктопное уведомление снимается сервисом через 5000 мс', fakeAsync(() => {
      setup(false);
      service.notifySuccess('Сохранено');
      tick(5000);
      expect(service.desktopMessage()).toBeNull();
    }));

    it('новое уведомление перезапускает таймер автозакрытия', fakeAsync(() => {
      setup(true);
      service.notifyError('Первая');
      tick(4000);
      service.notifyError('Вторая');
      tick(4000);
      expect(service.mobileMessage()?.text).toBe('Вторая');
      tick(1000);
      expect(service.mobileMessage()).toBeNull();
    }));

    it('закрытие крестиком гасит уведомление досрочно и отменяет таймер', fakeAsync(() => {
      setup(true);
      service.notifyError('Ошибка');
      service.dismissMobile();
      expect(service.mobileMessage()).toBeNull();
      tick(10_000);
      expect(service.mobileMessage()).toBeNull();
    }));

    it('после закрытия можно показать новое уведомление', fakeAsync(() => {
      setup(true);
      service.notifyError('Первая');
      service.dismissMobile();
      service.notifyError('Вторая');
      expect(service.mobileMessage()?.text).toBe('Вторая');
      tick(5000);
    }));
  });
});
