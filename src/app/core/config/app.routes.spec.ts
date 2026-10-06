/**
 * Юнит-тесты композиции маршрутов приложения (C-117, T-119, аменда 4 к
 * ADR-110, контракт IF-108): таблица §7 (13 строк — 14 путей с компонентами
 * и title фич), сквозные guards на componentless-обёртках (homeGuard на ''
 * и '**', guestGuard на гостевом семействе, authGuard на оболочке),
 * гостевая оболочка GuestShell с вкладками §4.8 внутри guest-обёртки
 * (VS-006, фикс VBUG-004), точечные guards немутирующим хелпером (копии
 * маршрутов, исходные массивы фич не мутируются), /403 и /profile без
 * roleGuard; smoke Router-навигации через RouterTestingHarness: гость на
 * /works и на deep-link teacher-страницы → /login без ошибок консоли,
 * студент на /works → /403, корни '' и '**' → homeGuard, шаги
 * восстановления без store → /recovery.
 *
 * Guards работают против реального AuthService (мок-слой auth на
 * фиксированных часах, задержка 500 мс реальными таймерами); страницы,
 * активируемые при редиректах, получают заглушки сервисов доменов.
 */
import { BreakpointObserver } from '@angular/cdk/layout';
import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Route, Router, Routes, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { registerAuthHandlers } from '../../mock/auth/handlers';
import { SessionStore } from '../../mock/auth/session-store';
import { MockApiClient } from '../../mock/mock-api-client';
import {
  MockDbData,
  configureMockDbSeed,
  emptyMockDbData,
  resetMockDbSeed,
} from '../../mock/mock-db';
import { MockBreakpointObserver } from '../../../testing/mock-breakpoint-observer';
import { ACCESS_ROUTES, AccessPage } from '../../features/access/access.routes';
import { AUTH_ROUTES } from '../../features/auth/auth.routes';
import { LoginPage } from '../../features/auth/pages/login-page/login-page';
import { RecoveryCodePage } from '../../features/auth/pages/recovery-code-page/recovery-code-page';
import { RecoveryPage } from '../../features/auth/pages/recovery-page/recovery-page';
import { RegisterPage } from '../../features/auth/pages/register-page/register-page';
import { ResetPasswordPage } from '../../features/auth/pages/reset-password-page/reset-password-page';
import { GroupPage, GROUPS_ROUTES, GroupsPage } from '../../features/groups/groups.routes';
import { MY_SUBMISSIONS_ROUTES } from '../../features/my-submissions/my-submissions.routes';
import { MySubmissionsPage } from '../../features/my-submissions/pages/my-submissions-page';
import { ProfilePage, PROFILE_ROUTES } from '../../features/profile/profile.routes';
import { SubmissionsPage, SUBMISSIONS_ROUTES } from '../../features/submissions/submissions.routes';
import { WORKS_ROUTES } from '../../features/works/works.routes';
import { LabFormPage } from '../../features/works/pages/lab-form-page/lab-form-page';
import { WorksPage } from '../../features/works/pages/works-page/works-page';
import { AppShell } from '../../layout/app-shell/app-shell';
import { GuestShell } from '../../layout/guest-shell/guest-shell';
import { Page403 } from '../../layout/page-403/page-403';
import { LABS_PAGE_SIZE, LabsService } from '../services/labs.service';
import { ProfileService } from '../services/profile.service';
import { SubmissionsService } from '../services/submissions.service';
import { authGuard, guestGuard, homeGuard } from '../navigation/guards';
import { routes } from './app.routes';

const T0 = 1_758_240_000_000;

const TEACHER_ID = 'aaaaaaaa-1111-4111-8111-111111111111';
const STUDENT_ID = 'bbbbbbbb-2222-4222-8222-222222222222';

interface RouteNode {
  /** Накопленный путь с ведущим '/' (корень '' — без слэша). */
  path: string;
  route: Route;
}

/** Плоское дерево: все узлы конфигурации с накопленными путями. */
function flatten(list: Routes, prefix = ''): RouteNode[] {
  return list.flatMap((route): RouteNode[] => {
    const path = route.path === '' ? prefix : `${prefix}/${route.path}`;
    return [{ path, route }, ...flatten(route.children ?? [], path)];
  });
}

/** Лист дерева по пути (узел с component/loadComponent) — ровно один. */
function leaf(path: string): Route {
  const nodes = flatten(routes).filter(
    (node) =>
      node.path === path &&
      (node.route.component !== undefined || node.route.loadComponent !== undefined),
  );
  expect(nodes.length).withContext(`ровно один лист ${path}`).toBe(1);
  return nodes[0].route;
}

function makeSeed(): MockDbData {
  return {
    ...emptyMockDbData(),
    users: [
      {
        id: TEACHER_ID,
        login: 'teacher',
        email: 'teacher@example.com',
        fullName: 'Сидоров Семён Семёнович',
        role: 'teacher',
        groupId: null,
        password: 'teacher123!',
      },
      {
        id: STUDENT_ID,
        login: 'student01',
        email: 'student01@example.com',
        fullName: 'Иванов Иван Иванович 01',
        role: 'student',
        groupId: null,
        password: 'student123!',
      },
    ],
  };
}

class LabsServiceStub {
  getList = jasmine.createSpy<LabsService['getList']>('getList');
  getSemesters = jasmine.createSpy<LabsService['getSemesters']>('getSemesters');
}

class SubmissionsServiceStub {
  getMy = jasmine.createSpy<SubmissionsService['getMy']>('getMy');
}

class ProfileServiceStub {
  get = jasmine.createSpy<ProfileService['get']>('get');
}

describe('app.routes — таблица §7 и композиция guards (T-119, аменда 4)', () => {
  it('каждый из 13 путей §7 (14 физических путей) существует с компонентом фичи', async () => {
    const table: Array<{
      path: string;
      component?: Type<unknown>;
      lazy?: boolean;
    }> = [
      { path: '/login', component: LoginPage },
      { path: '/register', component: RegisterPage },
      { path: '/recovery', component: RecoveryPage },
      { path: '/recovery/code', component: RecoveryCodePage },
      { path: '/reset-password', component: ResetPasswordPage },
      { path: '/my-submissions', lazy: true },
      { path: '/works', component: WorksPage },
      { path: '/works/new', component: LabFormPage },
      { path: '/works/:id/edit', component: LabFormPage },
      { path: '/submissions', component: SubmissionsPage },
      { path: '/groups', component: GroupsPage },
      { path: '/groups/:id', component: GroupPage },
      { path: '/access', component: AccessPage },
      { path: '/profile', component: ProfilePage },
    ];

    for (const row of table) {
      const route = leaf(row.path);
      if (row.lazy) {
        expect(route.loadComponent).withContext(`${row.path}: loadComponent`).toBeDefined();
        expect(await route.loadComponent!()).withContext(`${row.path}: класс страницы`).toBe(
          MySubmissionsPage,
        );
      } else {
        expect(route.component).withContext(`${row.path}: компонент`).toBe(row.component);
      }
    }

    // Страница 403 — внутри children оболочки (C-109).
    expect(leaf('/403').component).toBe(Page403);
  });

  it('title фич сохранены дословно, включая /login и /register (ревью CR-003 T-110)', () => {
    const titles: Array<[string, string | undefined]> = [
      ['/login', 'Вход'],
      ['/register', 'Регистрация'],
      ['/recovery', 'Восстановление пароля'],
      ['/recovery/code', 'Восстановление пароля — код'],
      ['/reset-password', 'Новый пароль'],
      ['/my-submissions', 'Сдача работ'],
      ['/works', 'Лабораторные работы'],
      ['/works/new', 'Новая лабораторная работа'],
      ['/works/:id/edit', 'Редактирование лабораторной работы'],
      ['/submissions', 'Сдача работ'],
      ['/groups', 'Группы'],
      ['/groups/:id', 'Карточка группы'],
      ['/access', 'Доступ'],
      ['/profile', 'Профиль'],
    ];
    for (const [path, title] of titles) {
      expect(leaf(path).title).withContext(`title ${path}`).toBe(title);
    }
  });

  it('сквозные guards на обёртках: homeGuard на корне и wildcard, guestGuard, authGuard', () => {
    const nodes = flatten(routes);

    // Корневые '' и '**' — ровно homeGuard (IF-108).
    const homeRoots = nodes.filter((node) => node.route.canActivate?.includes(homeGuard));
    expect(homeRoots.map((node) => node.path)).withContext('пути homeGuard').toEqual(['', '/**']);
    for (const root of homeRoots) {
      expect(root.route.canActivate?.length).withContext(`guards ${root.path}`).toBe(1);
      expect(root.route.component).withContext(`${root.path} — componentless`).toBeUndefined();
    }

    // Гостевая обёртка: componentless, ровно guestGuard, внутри — гостевая
    // оболочка GuestShell (VBUG-004: топбар с вкладками §4.8).
    const guestWrapper = nodes.find(
      (node) =>
        node.path === '' &&
        node.route.children?.some((child) => child.component === GuestShell),
    )!.route;
    expect(guestWrapper.component).toBeUndefined();
    expect(guestWrapper.canActivate?.length).toBe(1);
    expect(guestWrapper.canActivate![0]).toBe(guestGuard);

    // Гостевая оболочка: GuestShell без собственных guards, дети —
    // AUTH_ROUTES (гость видит топбар с вкладками §4.8 на всех auth-путях).
    const guestShellNodes = nodes.filter(
      (node) => node.route.component === GuestShell && node.path === '',
    );
    expect(guestShellNodes.length).withContext('ровно одна GuestShell-обёртка').toBe(1);
    expect(guestShellNodes[0].route.canActivate).withContext('GuestShell без guards')
      .toBeUndefined();
    expect(guestShellNodes[0].route.children?.some((child) => child.path === 'login')).toBeTrue();

    // Оболочка: AppShell, ровно authGuard.
    const shell = nodes.find((node) => node.route.component === AppShell)!.route;
    expect(shell.path).toBe('');
    expect(shell.canActivate?.length).toBe(1);
    expect(shell.canActivate![0]).toBe(authGuard);
  });

  it('точечные guards — на КОПИЯХ маршрутов: исходные массивы фич не мутированы', () => {
    // Аменда 4: в файлах фич guards нет — после композиции их там не появилось.
    const originals: Array<[string, Routes]> = [
      ['AUTH_ROUTES', AUTH_ROUTES],
      ['WORKS_ROUTES', WORKS_ROUTES],
      ['SUBMISSIONS_ROUTES', SUBMISSIONS_ROUTES],
      ['MY_SUBMISSIONS_ROUTES', MY_SUBMISSIONS_ROUTES],
      ['GROUPS_ROUTES', GROUPS_ROUTES],
      ['ACCESS_ROUTES', ACCESS_ROUTES],
      ['PROFILE_ROUTES', PROFILE_ROUTES],
    ];
    for (const [name, list] of originals) {
      for (const route of list) {
        expect(route.canActivate).withContext(`${name}: ${route.path} без canActivate`).toBeUndefined();
      }
    }

    // Скомпонованные листья — другие объекты (копии) с ровно одним guard'ом.
    const guarded: Array<[string, number]> = [
      ['/login', 0],
      ['/recovery', 0],
      ['/recovery/code', 1],
      ['/reset-password', 1],
      ['/works', 1],
      ['/works/new', 1],
      ['/works/:id/edit', 1],
      ['/submissions', 1],
      ['/my-submissions', 1],
      ['/groups', 1],
      ['/groups/:id', 1],
      ['/access', 1],
      ['/profile', 0],
      ['/403', 0],
    ];
    for (const [path, guardCount] of guarded) {
      expect(leaf(path).canActivate?.length ?? 0).withContext(`canActivate ${path}`).toBe(guardCount);
    }
    expect(leaf('/works')).withContext('копия, а не исходный объект').not.toBe(WORKS_ROUTES[0]);
    expect(leaf('/recovery/code'))
      .withContext('копия, а не исходный объект')
      .not.toBe(AUTH_ROUTES[3]);
  });

  it('обёртки my-submissions/groups без собственных guards (roleGuard — на копиях внутри)', () => {
    const nodes = flatten(routes);
    // Обёртка — componentless-узел (лист с component/loadComponent — фича).
    const wrappers = nodes.filter(
      (node) =>
        (node.path === '/my-submissions' || node.path === '/groups') &&
        node.route.component === undefined &&
        node.route.loadComponent === undefined,
    );
    expect(wrappers.length).withContext('по одной обёртке на путь').toBe(2);
    for (const wrapper of wrappers) {
      expect(wrapper.route.canActivate).withContext(`обёртка ${wrapper.path}`).toBeUndefined();
    }
  });
});

describe('app.routes — навигация по скомпонованному дереву (IF-108)', () => {
  let harness: RouterTestingHarness;
  let labs: LabsServiceStub;
  let submissions: SubmissionsServiceStub;
  let profile: ProfileServiceStub;

  beforeEach(async () => {
    localStorage.clear();
    sessionStorage.clear();
    spyOn(console, 'error');
    configureMockDbSeed(() => makeSeed());

    const client = new MockApiClient();
    registerAuthHandlers(client, () => T0);

    labs = new LabsServiceStub();
    labs.getList.and.resolveTo({ items: [], total: 0, page: 1, pageSize: LABS_PAGE_SIZE });
    labs.getSemesters.and.resolveTo([]);
    submissions = new SubmissionsServiceStub();
    submissions.getMy.and.resolveTo({ hasGroup: false, labs: [], submissions: [] });
    profile = new ProfileServiceStub();
    profile.get.and.resolveTo({
      login: 'student01',
      email: 'student01@example.com',
      fullName: 'Иванов Иван Иванович 01',
      role: 'student',
      groupName: 'ИК-221',
    });

    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        provideNoopAnimations(),
        { provide: MockApiClient, useValue: client },
        { provide: BreakpointObserver, useValue: new MockBreakpointObserver() },
        { provide: LabsService, useValue: labs },
        { provide: SubmissionsService, useValue: submissions },
        { provide: ProfileService, useValue: profile },
      ],
    });
    harness = await RouterTestingHarness.create();
  });

  afterEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    resetMockDbSeed();
  });

  function setSession(userId: string): void {
    TestBed.inject(SessionStore).setUserId(userId);
  }

  function currentUrl(): string {
    return TestBed.inject(Router).url;
  }

  /** Дожидание микрозадач активированной страницы и CD. */
  async function settle(): Promise<void> {
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
  }

  it('гость: /works → /login (authGuard), гостевая оболочка активирована', async () => {
    const shell = await harness.navigateByUrl('/works');
    expect(currentUrl()).toBe('/login');
    expect(shell).toBeInstanceOf(GuestShell);
    expect(
      harness.fixture.nativeElement.querySelector('app-login-page'),
    ).withContext('страница входа активирована внутри гостевой оболочки').not.toBeNull();
    expect(console.error).not.toHaveBeenCalled();
  });

  it('гость на /login: три вкладки шапки §4.8 (VS-006, фикс VBUG-004)', async () => {
    await harness.navigateByUrl('/login');
    const native = harness.fixture.nativeElement;
    const tabs = Array.from(
      native.querySelectorAll('a.guest-topbar__tab') as NodeListOf<HTMLAnchorElement>,
    );

    // Состав и ссылки — дословно §4.8 («Неавторизованный: Вход,
    // Регистрация, Восстановление пароля»).
    expect(tabs.map((tab) => tab.textContent!.trim())).toEqual([
      'Вход',
      'Регистрация',
      'Восстановление пароля',
    ]);
    expect(tabs.map((tab) => tab.getAttribute('href'))).toEqual([
      '/login',
      '/register',
      '/recovery',
    ]);

    // Активная вкладка — текущий гостевой маршрут.
    const active = tabs.filter((tab) =>
      tab.classList.contains('guest-topbar__tab--active'),
    );
    expect(active.length).withContext('активна ровно одна вкладка').toBe(1);
    expect(active[0].getAttribute('href')).toBe('/login');
    expect(active[0].getAttribute('aria-current')).toBe('page');

    // Вкладки видны на всех гостевых маршрутах (без перезагрузки оболочки).
    await harness.navigateByUrl('/register');
    expect(native.querySelectorAll('a.guest-topbar__tab').length).toBe(3);
    await harness.navigateByUrl('/recovery');
    expect(native.querySelectorAll('a.guest-topbar__tab').length).toBe(3);
    expect(console.error).not.toHaveBeenCalled();
  });

  it('гость: корень сайта → /login (homeGuard)', async () => {
    await harness.navigateByUrl('/');
    expect(currentUrl()).toBe('/login');
    expect(console.error).not.toHaveBeenCalled();
  });

  it('deep-link teacher-страницы: гость на /works/abc/edit → /login без ошибок консоли', async () => {
    await harness.navigateByUrl('/works/abc/edit');
    expect(currentUrl()).toBe('/login');
    expect(console.error).not.toHaveBeenCalled();
  });

  it('гость: /recovery/code без store → /recovery (recoveryStepGuard code)', async () => {
    const shell = await harness.navigateByUrl('/recovery/code');
    expect(currentUrl()).toBe('/recovery');
    expect(shell).toBeInstanceOf(GuestShell);
    expect(
      harness.fixture.nativeElement.querySelector('app-recovery-page'),
    ).withContext('страница восстановления активирована внутри оболочки').not.toBeNull();
    expect(console.error).not.toHaveBeenCalled();
  });

  it('гость: /reset-password без store → /recovery (recoveryStepGuard reset)', async () => {
    await harness.navigateByUrl('/reset-password');
    expect(currentUrl()).toBe('/recovery');
    expect(console.error).not.toHaveBeenCalled();
  });

  it('гость: неизвестный URL → /login (wildcard → homeGuard)', async () => {
    await harness.navigateByUrl('/nonexistent/page');
    expect(currentUrl()).toBe('/login');
    expect(console.error).not.toHaveBeenCalled();
  });

  it('teacher с сессией: корень сайта → /works (homeGuard → ROLE_HOME), works-страница активирована', async () => {
    setSession(TEACHER_ID);
    await harness.navigateByUrl('/');
    expect(currentUrl()).toBe('/works');
    await settle();
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toContain(
      'Лабораторные работы',
    );
    expect(labs.getList).toHaveBeenCalled();
    expect(console.error).not.toHaveBeenCalled();
  });

  it('AC guest-blocked: teacher с сессией на /login → /works (ROLE_HOME.teacher)', async () => {
    setSession(TEACHER_ID);
    await harness.navigateByUrl('/login');
    expect(currentUrl()).toBe('/works');
    expect(console.error).not.toHaveBeenCalled();
  });

  it('AC guest-blocked: student с сессией на /login → /my-submissions (ROLE_HOME.student)', async () => {
    setSession(STUDENT_ID);
    await harness.navigateByUrl('/login');
    expect(currentUrl()).toBe('/my-submissions');
    // Страница — внутри outlet оболочки: проверяется селектором хоста.
    expect(harness.fixture.nativeElement.querySelector('app-my-submissions-page'))
      .withContext('страница студента активирована (lazy loadComponent)')
      .not.toBeNull();
    expect(console.error).not.toHaveBeenCalled();
  });

  it('student: /works → /403 (roleGuard teacher), страница 403 доступна без roleGuard', async () => {
    setSession(STUDENT_ID);
    await harness.navigateByUrl('/works');
    expect(currentUrl()).toBe('/403');
    expect(harness.fixture.nativeElement.querySelector('app-page-403'))
      .withContext('страница 403 активирована внутри оболочки')
      .not.toBeNull();
    expect(harness.fixture.nativeElement.textContent).toContain('Доступ запрещён');
    expect(console.error).not.toHaveBeenCalled();
  });

  it('student: /profile доступен — любая роль (без roleGuard)', async () => {
    setSession(STUDENT_ID);
    await harness.navigateByUrl('/profile');
    expect(currentUrl()).toBe('/profile');
    expect(harness.fixture.nativeElement.querySelector('app-profile-page'))
      .withContext('страница профиля активирована')
      .not.toBeNull();
    expect(console.error).not.toHaveBeenCalled();
  });

  it('teacher с сессией: /recovery/code → /works (guestGuard срабатывает раньше recoveryStepGuard)', async () => {
    // Сессия преподавателя на гостевом маршруте: guestGuard → /works,
    // recoveryStepGuard не вызывается (обёртка сработала раньше).
    setSession(TEACHER_ID);
    await harness.navigateByUrl('/recovery/code');
    expect(currentUrl()).toBe('/works');
    expect(console.error).not.toHaveBeenCalled();
  });
});
