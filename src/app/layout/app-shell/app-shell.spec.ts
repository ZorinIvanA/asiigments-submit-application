/**
 * Unit-тесты оболочки AppShell (C-109, IF-111/IF-109, T-109): наборы вкладок
 * по ролям (5 у преподавателя / 2 у студента — подписи и ссылки дословно
 * IF-111), активная вкладка routerLinkActive, «Выйти» → AuthService.logout
 * и редирект /login, ФИО в шапке, --app-header-height:56px и хост мобильного
 * баннера <app-header-notification/> сразу после шапки (AC
 * header-notification-host: <768px баннер показан под шапкой).
 *
 * AuthService замещается стабом с настоящим сигналом currentUser (mock-
 * состояние роли), BreakpointObserver — MockBreakpointObserver (<768px без
 * реальных медиазапросов). CSS-медиаветку <768px (скрытие ФИО, скролл
 * вкладок) вёрстки ChromeHeadless с viewport 800px не активирует — мобильное
 * поведение JS-калитки проверяется через MockBreakpointObserver, вёрстка —
 * визуальным прогоном по макету.
 */
import { BreakpointObserver } from '@angular/cdk/layout';
import { Component, WritableSignal, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { AuthService } from '../../core/services/auth.service';
import { MeDto } from '../../core/services/auth.service';
import { NotificationService } from '../../shared/notifications/notification-service';
import { MockBreakpointObserver } from '../../../testing/mock-breakpoint-observer';
import { AppShell } from './app-shell';

/** Минимальный маршрут-заглушка для завершённых навигаций в тестах. */
@Component({ template: '' })
class EmptyPage {}

const TEACHER_ME: MeDto = {
  login: 'teacher',
  fullName: 'Сидоров Семён Семёнович',
  role: 'teacher',
  groupName: null,
};

const STUDENT_ME: MeDto = {
  login: 'student01',
  fullName: 'Иванов Иван Иванович 01',
  role: 'student',
  groupName: 'ИК-221',
};

describe('AppShell — оболочка (IF-111, T-109)', () => {
  let breakpoints: MockBreakpointObserver;
  let currentUser: WritableSignal<MeDto | null>;
  let logout: jasmine.Spy<() => Promise<void>>;
  let fixture: ComponentFixture<AppShell>;

  beforeEach(async () => {
    breakpoints = new MockBreakpointObserver();
    currentUser = signal(null);
    logout = jasmine.createSpy('logout').and.resolveTo(undefined);
    await TestBed.configureTestingModule({
      imports: [AppShell],
      providers: [
        { provide: BreakpointObserver, useValue: breakpoints },
        { provide: AuthService, useValue: { currentUser, logout } },
        provideRouter([
          { path: '', pathMatch: 'full', redirectTo: 'works' },
          { path: 'login', component: EmptyPage },
          { path: 'works', component: EmptyPage },
          { path: 'submissions', component: EmptyPage },
          { path: 'my-submissions', component: EmptyPage },
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

  it('teacher: ровно 5 вкладок с подписями и ссылками по IF-111', () => {
    currentUser.set(TEACHER_ME);
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

  it('student: ровно 2 вкладки «Сдача работ» и «Профиль» по IF-111', () => {
    currentUser.set(STUDENT_ME);
    fixture.detectChanges();

    expect(tabs().map((tab) => tab.textContent!.trim())).toEqual([
      'Сдача работ',
      'Профиль',
    ]);
    expect(tabs().map((tab) => tab.getAttribute('href'))).toEqual([
      '/my-submissions',
      '/profile',
    ]);
  });

  it('без пользователя в кэше вкладки не рендерятся (пустой набор)', () => {
    fixture.detectChanges();

    expect(tabs().length).toBe(0);
  });

  it('активная вкладка подсвечивается routerLinkActive и помечена aria-current', async () => {
    currentUser.set(TEACHER_ME);
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

  it('ФИО пользователя показано в шапке (AuthService.currentUser)', () => {
    currentUser.set(STUDENT_ME);
    fixture.detectChanges();

    const who = fixture.nativeElement.querySelector('.app-topbar__who')!;
    expect(who.textContent!.trim()).toBe('Иванов Иван Иванович 01');
  });

  it('«Выйти»: вызывает AuthService.logout и выполняет навигацию на /login', async () => {
    currentUser.set(TEACHER_ME);
    fixture.detectChanges();

    (
      fixture.nativeElement.querySelector(
        '.app-topbar__logout',
      ) as HTMLButtonElement
    ).click();
    await fixture.whenStable();

    expect(logout).toHaveBeenCalledTimes(1);
    expect(TestBed.inject(Router).url).withContext('редирект на вход').toBe('/login');
  });

  it('объявляет --app-header-height: 56px, высота шапки 56px', () => {
    currentUser.set(TEACHER_ME);
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
    currentUser.set(TEACHER_ME);
    document.body.appendChild(fixture.nativeElement);
    breakpoints.simulate(true);
    fixture.detectChanges();

    const notifications = TestBed.inject(NotificationService);
    notifications.notifyError('Ошибка загрузки данных');
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const bannerHost = host.querySelector('app-header-notification');
    expect(bannerHost).not.toBeNull();
    expect(
      bannerHost!.querySelector('app-notification-banner'),
    ).withContext('баннер смонтирован').not.toBeNull();
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
    currentUser.set(TEACHER_ME);
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
