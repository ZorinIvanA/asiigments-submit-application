/**
 * Unit-тесты HeaderNotification (FR-021/AR-013): фиксированный под шапкой
 * мобильный баннер для экранов без формы и резервного якоря 'header';
 * закрытие крестиком, скрытие на десктопе, зелёный вариант успеха.
 */
import { BreakpointObserver } from '@angular/cdk/layout';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { MockBreakpointObserver } from '../../../testing/mock-breakpoint-observer';
import { HeaderNotification } from './header-notification';
import { NotificationService } from './notification-service';

describe('HeaderNotification', () => {
  let breakpoints: MockBreakpointObserver;
  let notifications: NotificationService;
  let fixture: ComponentFixture<HeaderNotification>;

  beforeEach(() => {
    breakpoints = new MockBreakpointObserver();
    TestBed.configureTestingModule({
      providers: [{ provide: BreakpointObserver, useValue: breakpoints }],
    });
    notifications = TestBed.inject(NotificationService);
    fixture = TestBed.createComponent(HeaderNotification);
    // Хост крепится в document: позиционирование fixed разрешается
    // браузером только для элемента в дереве документа.
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
  });

  afterEach(() => {
    notifications.dismissMobile();
    document.body.removeChild(fixture.nativeElement);
    fixture.destroy();
  });

  it('показывает мобильную ошибку без якоря фиксированно под шапкой', () => {
    breakpoints.simulate(true);

    notifications.notifyError('Слишком много попыток входа');
    fixture.detectChanges();

    const banner = fixture.nativeElement.querySelector('app-notification-banner');
    expect(banner).not.toBeNull();
    expect(banner!.textContent).toContain('Слишком много попыток входа');
    expect(getComputedStyle(fixture.nativeElement).position).toBe('fixed');
  });

  it('показывает мобильный успех (зелёный, якоря у успеха нет)', () => {
    breakpoints.simulate(true);

    notifications.notifySuccess('Сохранено');
    fixture.detectChanges();

    const banner = fixture.nativeElement.querySelector('.app-notification-banner')!;
    expect(banner.textContent).toContain('Сохранено');
    expect(banner.classList).toContain('app-notification-banner--success');
  });

  it('показывает ошибку с неизвестным formId (fallback IF-009)', () => {
    breakpoints.simulate(true);

    notifications.notifyError('Ошибка', { formId: 'ghost-form' });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Ошибка');
  });

  it('не показывается для уведомления, адресованного форме', () => {
    breakpoints.simulate(true);
    notifications.registerAnchor('profile-form');

    notifications.notifyError('Email уже занят', { formId: 'profile-form' });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('app-notification-banner')).toBeNull();
  });

  it('на десктопе (>=768px) баннер под шапкой не показывается', () => {
    breakpoints.simulate(false);

    notifications.notifyError('Ошибка');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('app-notification-banner')).toBeNull();
  });

  it('клик по крестику закрывает баннер', () => {
    breakpoints.simulate(true);

    notifications.notifyError('Ошибка');
    fixture.detectChanges();

    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('.app-notification-banner__close')!
      .click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('app-notification-banner')).toBeNull();
    expect(notifications.mobileMessage()).toBeNull();
  });
});
