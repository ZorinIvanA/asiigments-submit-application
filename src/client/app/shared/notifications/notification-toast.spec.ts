/**
 * Unit-тесты NotificationToast (FR-021, десктоп >=768px): PrimeNG Toast
 * справа вверху, перенос текста дословно и severity из desktopMessage
 * сервиса, отсутствие показа в мобильном режиме.
 *
 * Тесты асинхронные: эффект переноса уведомления в Toast исполняется
 * при дренаже зоны (await fixture.whenStable()), а не синхронным
 * TestBed.tick() поверх запланированного тика — иначе ApplicationRef
 * падает с NG0101 (рекурсивный tick).
 */
import { BreakpointObserver } from '@angular/cdk/layout';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';

import { MockBreakpointObserver } from '../../../testing/mock-breakpoint-observer';
import { NotificationService } from './notification-service';
import { NotificationToast } from './notification-toast';

describe('NotificationToast', () => {
  let breakpoints: MockBreakpointObserver;
  let notifications: NotificationService;
  let fixture: ComponentFixture<NotificationToast>;

  beforeEach(() => {
    breakpoints = new MockBreakpointObserver();
    TestBed.configureTestingModule({
      providers: [
        { provide: BreakpointObserver, useValue: breakpoints },
        provideNoopAnimations(),
      ],
    });
    notifications = TestBed.inject(NotificationService);
    fixture = TestBed.createComponent(NotificationToast);
    fixture.detectChanges();
  });

  afterEach(async () => {
    notifications.dismissMobile();
    // Дренаж запланированных задач зоны до уничтожения фикстуры, чтобы
    // таймеры автозакрытия Toast не срабатывали в чужих тестах.
    await fixture.whenStable();
    fixture.destroy();
  });

  it('ошибка на десктопе показывает Toast справа вверху с дословным текстом', async () => {
    breakpoints.simulate(false);

    notifications.notifyError('Лабораторная с таким номером уже есть в семестре');
    await fixture.whenStable();
    fixture.detectChanges();

    const toast = fixture.nativeElement.querySelector('.p-toast');
    expect(toast).not.toBeNull();
    expect(toast!.classList).toContain('p-toast-top-right');
    expect(toast!.textContent).toContain('Лабораторная с таким номером уже есть в семестре');
    expect(toast!.querySelector('.p-toast-message-error')).not.toBeNull();
  });

  it('успех на десктопе показывает зелёный Toast с дословным текстом', async () => {
    breakpoints.simulate(false);

    notifications.notifySuccess('Пароль изменён');
    await fixture.whenStable();
    fixture.detectChanges();

    const toast = fixture.nativeElement.querySelector('.p-toast')!;
    expect(toast.textContent).toContain('Пароль изменён');
    expect(toast.querySelector('.p-toast-message-success')).not.toBeNull();
  });

  it('в мобильном режиме (<768px) Toast не наполняется', async () => {
    breakpoints.simulate(true);

    notifications.notifyError('Ошибка');
    await fixture.whenStable();
    fixture.detectChanges();

    const toast = fixture.nativeElement.querySelector('.p-toast');
    expect(toast?.textContent ?? '').not.toContain('Ошибка');
  });
});
