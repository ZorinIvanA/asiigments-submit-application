/**
 * Unit-тесты оболочки AppShell (C-109, IF-111/IF-109, T-109): статичный
 * набор вкладок преподавателя (5 — подписи и ссылки дословно IF-111),
 * активная вкладка routerLinkActive, --app-header-height:56px и хост
 * мобильного баннера <app-header-notification/> сразу после шапки (AC
 * header-notification-host: <768px баннер показан под шапкой).
 *
 * BreakpointObserver замещается MockBreakpointObserver (<768px без реальных
 * медиазапросов). CSS-медиаветку <768px (скролл вкладок) вёрстка
 * ChromeHeadless с viewport 800px не активирует — мобильное поведение
 * JS-калитки проверяется через MockBreakpointObserver, вёрстка — визуальным
 * прогоном по макету.
 */
import { BreakpointObserver } from '@angular/cdk/layout';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { NotificationService } from '../../shared/notifications/notification-service';
import { MockBreakpointObserver } from '../../../testing/mock-breakpoint-observer';
import { AppShell } from './app-shell';

/** Минимальный маршрут-заглушка для завершённых навигаций в тестах. */
@Component({ template: '' })
class EmptyPage {}

describe('AppShell — оболочка консоли преподавателя (IF-111, T-109)', () => {
  let breakpoints: MockBreakpointObserver;
  let fixture: ComponentFixture<AppShell>;

  beforeEach(async () => {
    breakpoints = new MockBreakpointObserver();
    await TestBed.configureTestingModule({
      imports: [AppShell],
      providers: [
        { provide: BreakpointObserver, useValue: breakpoints },
        provideRouter([
          { path: '', pathMatch: 'full', redirectTo: 'works' },
          { path: 'works', component: EmptyPage },
          { path: 'submissions', component: EmptyPage },
          { path: 'groups', component: EmptyPage },
          { path: 'access', component: EmptyPage },
          { path: 'profile', component: EmptyPage },
          { path: '**', redirectTo: 'works' },
        ]),
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(AppShell);
  });

  afterEach(() => {
    TestBed.inject(NotificationService).dismissMobile();
    (fixture.nativeElement as HTMLElement).remove();
    fixture.destroy();
  });

  /** Ссылки-вкладки топбара в порядке рендера. */
  function tabs(): HTMLAnchorElement[] {
    return Array.from(
      fixture.nativeElement.querySelectorAll(
        'a.app-topbar__tab',
      ) as NodeListOf<HTMLAnchorElement>,
    );
  }

  it('ровно 5 вкладок преподавателя с подписями и ссылками по IF-111', () => {
    fixture.detectChanges();

    expect(tabs().map((tab) => tab.textContent!.trim())).toEqual([
      'Работы',
      'Сдача работ',
      'Группы',
      'Доступ',
      'Профиль',
    ]);
    expect(tabs().map((tab) => tab.getAttribute('href'))).toEqual([
      '/works',
      '/submissions',
      '/groups',
      '/access',
      '/profile',
    ]);
  });

  it('активная вкладка подсвечивается routerLinkActive и помечена aria-current', async () => {
    fixture.detectChanges();

    await TestBed.inject(Router).navigate(['/works']);
    fixture.detectChanges();

    const active = tabs().filter((tab) =>
      tab.classList.contains('app-topbar__tab--active'),
    );
    expect(active.length).withContext('активна ровно одна вкладка').toBe(1);
    expect(active[0].getAttribute('href')).toBe('/works');
    expect(active[0].getAttribute('aria-current')).toBe('page');
  });

  it('кнопки «Выйти» и блока пользователя в шапке нет (анонимная консоль)', () => {
    fixture.detectChanges();

    expect(
      fixture.nativeElement.querySelector('.app-topbar__logout'),
    ).toBeNull();
    expect(fixture.nativeElement.querySelector('.app-topbar__who')).toBeNull();
  });

  it('объявляет --app-header-height: 56px, высота шапки 56px', () => {
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(
      getComputedStyle(host).getPropertyValue('--app-header-height').trim(),
    ).toBe('56px');
    const header = host.querySelector<HTMLElement>('.app-topbar')!;
    expect(getComputedStyle(header).height).toBe('56px');
  });

  it('<768px: ошибка без якоря формы показана баннером под шапкой (IF-109)', () => {
    document.body.appendChild(fixture.nativeElement);
    breakpoints.simulate(true);
    fixture.detectChanges();

    const notifications = TestBed.inject(NotificationService);
    notifications.notifyError('Ошибка загрузки данных');
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const bannerHost = host.querySelector('app-header-notification');
    expect(bannerHost).not.toBeNull();
    expect(bannerHost!.querySelector('app-notification-banner'))
      .withContext('баннер смонтирован')
      .not.toBeNull();
    expect(bannerHost!.textContent).toContain('Ошибка загрузки данных');

    // Смещение баннера — высота шапки оболочки (переменная наследуется хостом).
    expect(getComputedStyle(bannerHost as HTMLElement).top).toBe('56px');

    // Хост баннера расположен сразу после шапки в порядке документа.
    const header = host.querySelector('.app-topbar')!;
    expect(
      header.compareDocumentPosition(bannerHost!) &
        Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
  });

  it('<768px: баннер скрыт на десктопе (>=768px) — калитка уведомлений', () => {
    fixture.detectChanges();

    const notifications = TestBed.inject(NotificationService);
    breakpoints.simulate(false);
    notifications.notifyError('Ошибка загрузки данных');
    fixture.detectChanges();

    const bannerHost =
      (fixture.nativeElement as HTMLElement).querySelector(
        'app-header-notification',
      ) ?? null;
    expect(bannerHost!.querySelector('app-notification-banner')).toBeNull();
  });
});
