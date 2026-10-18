/**
 * Unit-тесты якоря формы NotificationAnchor (FR-021/AR-017): мобильный
 * inline-баннер показывается под формой-инициатором (на /profile — под
 * одной из двух форм, чьё действие вызвало уведомление), закрытие
 * крестиком, автозакрытие через 5000 мс (fakeAsync), скрытие на десктопе,
 * снятие регистрации formId при уничтожении якоря.
 */
import { BreakpointObserver } from '@angular/cdk/layout';
import { Component } from '@angular/core';
import { fakeAsync, TestBed, tick } from '@angular/core/testing';

import { MockBreakpointObserver } from '../../../testing/mock-breakpoint-observer';
import { NotificationService } from './notification-service';
import { NotificationAnchor } from './notification-anchor';

/** Хост с двумя формами — раскладка экрана /profile (AR-017). */
@Component({
  imports: [NotificationAnchor],
  template: `
    <section data-test="profile-section">
      <app-notification-anchor formId="profile-form" />
    </section>
    <section data-test="password-section">
      <app-notification-anchor formId="password-form" />
    </section>
  `,
})
class TwoFormsHost {}

describe('NotificationAnchor', () => {
  let breakpoints: MockBreakpointObserver;
  let notifications: NotificationService;

  beforeEach(() => {
    breakpoints = new MockBreakpointObserver();
    TestBed.configureTestingModule({
      providers: [{ provide: BreakpointObserver, useValue: breakpoints }],
    });
    notifications = TestBed.inject(NotificationService);
  });

  afterEach(() => {
    // Гасим возможный незавершённый таймер автозакрытия текущего сервиса.
    notifications.dismissMobile();
  });

  it('регистрирует formId: уведомление маршрутизируется под форму-инициатор', () => {
    breakpoints.simulate(true);
    const fixture = TestBed.createComponent(TwoFormsHost);
    fixture.detectChanges();

    notifications.notifyError('Email уже занят', { formId: 'profile-form' });
    fixture.detectChanges();

    const profile = fixture.nativeElement.querySelector(
      '[data-test="profile-section"] app-notification-banner',
    );
    const password = fixture.nativeElement.querySelector(
      '[data-test="password-section"] app-notification-banner',
    );
    expect(profile).withContext('баннер под формой профиля').not.toBeNull();
    expect(profile!.textContent).toContain('Email уже занят');
    expect(password).withContext('под чужой формой баннера нет').toBeNull();
  });

  it('ошибка формы смены пароля показывается под формой смены пароля (AR-017)', () => {
    breakpoints.simulate(true);
    const fixture = TestBed.createComponent(TwoFormsHost);
    fixture.detectChanges();

    notifications.notifyError('Неверный текущий пароль', { formId: 'password-form' });
    fixture.detectChanges();

    const profile = fixture.nativeElement.querySelector(
      '[data-test="profile-section"] app-notification-banner',
    );
    const password = fixture.nativeElement.querySelector(
      '[data-test="password-section"] app-notification-banner',
    );
    expect(password).not.toBeNull();
    expect(password!.textContent).toContain('Неверный текущий пароль');
    expect(profile).toBeNull();
  });

  it('замена уведомления переносит баннер между формами', () => {
    breakpoints.simulate(true);
    const fixture = TestBed.createComponent(TwoFormsHost);
    fixture.detectChanges();

    notifications.notifyError('Первое', { formId: 'profile-form' });
    fixture.detectChanges();
    notifications.notifyError('Второе', { formId: 'password-form' });
    fixture.detectChanges();

    expect(
      fixture.nativeElement.querySelector('[data-test="profile-section"] app-notification-banner'),
    ).toBeNull();
    const movedBanner = fixture.nativeElement.querySelector(
      '[data-test="password-section"] app-notification-banner',
    );
    expect(movedBanner.textContent).toContain('Второе');
  });

  it('успех (якорь header) не показывается под формами', () => {
    breakpoints.simulate(true);
    const fixture = TestBed.createComponent(TwoFormsHost);
    fixture.detectChanges();

    notifications.notifySuccess('Пароль изменён');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('app-notification-banner')).toBeNull();
  });

  it('на десктопе (>=768px) под формой баннера нет', () => {
    breakpoints.simulate(false);
    const fixture = TestBed.createComponent(TwoFormsHost);
    fixture.detectChanges();

    notifications.notifyError('Email уже занят', { formId: 'profile-form' });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('app-notification-banner')).toBeNull();
  });

  it('клик по крестику закрывает баннер', () => {
    breakpoints.simulate(true);
    const fixture = TestBed.createComponent(TwoFormsHost);
    fixture.detectChanges();

    notifications.notifyError('Ошибка', { formId: 'profile-form' });
    fixture.detectChanges();

    fixture.nativeElement
      .querySelector('[data-test="profile-section"] .app-notification-banner__close')!
      .click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('app-notification-banner')).toBeNull();
    expect(notifications.mobileMessage()).toBeNull();
  });

  it('автозакрытие: через 5000 мс без ручного закрытия баннер скрыт', fakeAsync(() => {
    breakpoints.simulate(true);
    const fixture = TestBed.createComponent(TwoFormsHost);
    fixture.detectChanges();

    notifications.notifyError('Ошибка', { formId: 'profile-form' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-notification-banner')).not.toBeNull();

    tick(5000);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('app-notification-banner')).toBeNull();
  }));

  it('уничтожение якоря снимает регистрацию formId (fallback под шапку)', () => {
    breakpoints.simulate(true);
    const fixture = TestBed.createComponent(TwoFormsHost);
    fixture.detectChanges();
    fixture.destroy();

    notifications.notifyError('Ошибка', { formId: 'profile-form' });
    expect(notifications.mobileMessage()?.formId).toBeNull();
  });
});
