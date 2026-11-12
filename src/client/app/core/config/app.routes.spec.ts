/**
 * Юнит-тесты композиции маршрутов приложения (C-117, T-119) — структурная
 * спека анонимной консоли преподавателя: все экраны фич смонтированы под
 * оболочкой AppShell с компонентами и title фич, корень '' и wildcard '**'
 * редиректят на /works, /my-submissions — lazy loadComponent; smoke
 * Router-навигации: '/' открывает список работ, '/profile' — профиль,
 * неизвестный URL уводит на /works (без ошибок консоли). Guards и
 * гостевое семейство auth удалены вместе с аутентификацией.
 *
 * Навигационный smoke идёт с двойниками доменных сервисов: активированные
 * страницы не выполняют HTTP доменов.
 */
import { BreakpointObserver } from '@angular/cdk/layout';
import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Route, Router, Routes, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { MockBreakpointObserver } from '../../../testing/mock-breakpoint-observer';
import { ACCESS_ROUTES, AccessPage } from '../../features/access/access.routes';
import {
  GroupPage,
  GROUPS_ROUTES,
  GroupsPage,
} from '../../features/groups/groups.routes';
import { MY_SUBMISSIONS_ROUTES } from '../../features/my-submissions/my-submissions.routes';
import { MySubmissionsPage } from '../../features/my-submissions/pages/my-submissions-page';
import {
  ProfilePage,
  PROFILE_ROUTES,
} from '../../features/profile/profile.routes';
import {
  SubmissionsPage,
  SUBMISSIONS_ROUTES,
} from '../../features/submissions/submissions.routes';
import { WORKS_ROUTES } from '../../features/works/works.routes';
import { LabFormPage } from '../../features/works/pages/lab-form-page/lab-form-page';
import { WorksPage } from '../../features/works/pages/works-page/works-page';
import { AppShell } from '../../layout/app-shell/app-shell';
import { LABS_PAGE_SIZE, LabsService } from '../services/labs.service';
import { ProfileService } from '../services/profile.service';
import { SubmissionsService } from '../services/submissions.service';
import { routes } from './app.routes';

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
      (node.route.component !== undefined ||
        node.route.loadComponent !== undefined),
  );
  expect(nodes.length).withContext(`ровно один лист ${path}`).toBe(1);
  return nodes[0].route;
}

class LabsServiceStub {
  getList = jasmine.createSpy<LabsService['getList']>('getList');
  getSemesters = jasmine.createSpy<LabsService['getSemesters']>('getSemesters');
}

class SubmissionsServiceStub {
  getMy = jasmine.createSpy<SubmissionsService['getMy']>('getMy');
}

class ProfileServiceStub {
  get = jasmine.createSpy<ProfileService['get']>('get').and.resolveTo({
    login: 'teacher',
    email: 'teacher@example.com',
    fullName: 'Сидоров Семён Семёнович',
    role: 'teacher',
    groupName: null,
  });
}

describe('app.routes — структура дерева консоли преподавателя (T-119)', () => {
  it('каждый путь §7 (за вычетом auth-семейства) существует с компонентом фичи', async () => {
    const table: Array<{
      path: string;
      component?: Type<unknown>;
      lazy?: boolean;
    }> = [
      { path: '/works', component: WorksPage },
      { path: '/works/new', component: LabFormPage },
      { path: '/works/:id/edit', component: LabFormPage },
      { path: '/submissions', component: SubmissionsPage },
      { path: '/my-submissions', lazy: true },
      { path: '/groups', component: GroupsPage },
      { path: '/groups/:id', component: GroupPage },
      { path: '/access', component: AccessPage },
      { path: '/profile', component: ProfilePage },
    ];

    for (const row of table) {
      const route = leaf(row.path);
      if (row.lazy) {
        expect(route.loadComponent)
          .withContext(`${row.path}: loadComponent`)
          .toBeDefined();
        expect(await route.loadComponent!())
          .withContext(`${row.path}: класс страницы`)
          .toBe(MySubmissionsPage);
      } else {
        expect(route.component)
          .withContext(`${row.path}: компонент`)
          .toBe(row.component);
      }
    }
  });

  it('title фич сохранены дословно (ревью CR-003 T-110)', () => {
    const titles: Array<[string, string | undefined]> = [
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

  it('корень и wildcard «**» — редирект на /works (домашний экран консоли)', () => {
    const nodes = flatten(routes);
    const roots = nodes.filter(
      (node) =>
        (node.path === '' || node.path === '/**') &&
        node.route.redirectTo !== undefined,
    );
    expect(roots.map((node) => node.path))
      .withContext('редирект-узлы')
      .toEqual(['', '/**']);
    for (const root of roots) {
      expect(root.route.redirectTo)
        .withContext(`redirect ${root.path}`)
        .toBe('works');
      expect(root.route.component)
        .withContext(`${root.path} — componentless`)
        .toBeUndefined();
    }
  });

  it('все маршруты фич — внутри оболочки AppShell, canActivate в дереве нет', () => {
    const nodes = flatten(routes);
    const shell = nodes.find(
      (node) => node.route.component === AppShell,
    )!.route;
    expect(shell.path).toBe('');
    expect(shell.canActivate).withContext('guards удалены').toBeUndefined();

    // Все листья фич — потомки оболочки (в дереве ровно один AppShell).
    const shellNodes = nodes.filter(
      (node) => node.route.component === AppShell,
    );
    expect(shellNodes.length).withContext('ровно одна оболочка').toBe(1);
    for (const featurePath of [
      '/works',
      '/works/new',
      '/works/:id/edit',
      '/submissions',
      '/my-submissions',
      '/groups',
      '/groups/:id',
      '/access',
      '/profile',
    ]) {
      expect(leaf(featurePath))
        .withContext(`${featurePath} — лист дерева`)
        .toBeDefined();
    }

    // Ни один узел дерева не несёт canActivate (guards удалены целиком).
    for (const node of nodes) {
      expect(node.route.canActivate)
        .withContext(`canActivate ${node.path}`)
        .toBeUndefined();
    }
  });

  it('обёртки my-submissions/groups — componentless-узлы без собственных страниц', () => {
    const nodes = flatten(routes);
    const wrappers = nodes.filter(
      (node) =>
        (node.path === '/my-submissions' || node.path === '/groups') &&
        node.route.component === undefined &&
        node.route.loadComponent === undefined,
    );
    expect(wrappers.length).withContext('по одной обёртке на путь').toBe(2);
  });

  it('массивы фич не мутируются при композиции (маршруты монтируются как есть)', () => {
    // В файлах фич guards нет и после композиции не появились.
    const originals: Array<[string, Routes]> = [
      ['WORKS_ROUTES', WORKS_ROUTES],
      ['SUBMISSIONS_ROUTES', SUBMISSIONS_ROUTES],
      ['MY_SUBMISSIONS_ROUTES', MY_SUBMISSIONS_ROUTES],
      ['GROUPS_ROUTES', GROUPS_ROUTES],
      ['ACCESS_ROUTES', ACCESS_ROUTES],
      ['PROFILE_ROUTES', PROFILE_ROUTES],
    ];
    for (const [name, list] of originals) {
      for (const route of list) {
        expect(route.canActivate)
          .withContext(`${name}: ${route.path} без canActivate`)
          .toBeUndefined();
      }
    }
  });
});

describe('app.routes — smoke-навигация по скомпонованному дереву', () => {
  let harness: RouterTestingHarness;
  let labs: LabsServiceStub;

  beforeEach(async () => {
    spyOn(console, 'error');

    labs = new LabsServiceStub();
    labs.getList.and.resolveTo({
      items: [],
      total: 0,
      page: 1,
      pageSize: LABS_PAGE_SIZE,
    });
    labs.getSemesters.and.resolveTo([]);

    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        provideNoopAnimations(),
        { provide: BreakpointObserver, useValue: new MockBreakpointObserver() },
        // Двойники доменных сервисов: активированные страницы не выполняют
        // HTTP доменов — smoke проверяет только маршрутизацию.
        { provide: LabsService, useValue: labs },
        { provide: SubmissionsService, useValue: new SubmissionsServiceStub() },
        { provide: ProfileService, useValue: new ProfileServiceStub() },
      ],
    });
    harness = await RouterTestingHarness.create();
  });

  function currentUrl(): string {
    return TestBed.inject(Router).url;
  }

  /** Дожидание микрозадач активированной страницы и CD. */
  async function settle(): Promise<void> {
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
  }

  it('корень сайта → /works, works-страница активирована внутри оболочки', async () => {
    await harness.navigateByUrl('/');
    expect(currentUrl()).toBe('/works');
    await settle();
    expect(
      harness.routeNativeElement?.querySelector('h1')?.textContent,
    ).toContain('Лабораторные работы');
    expect(labs.getList).toHaveBeenCalled();
    expect(console.error).not.toHaveBeenCalled();
  });

  it('unknown URL → /works (wildcard-редирект), ошибок в консоли нет', async () => {
    await harness.navigateByUrl('/nonexistent/page');
    expect(currentUrl()).toBe('/works');
    expect(console.error).not.toHaveBeenCalled();
  });

  it('/profile активирован без guards, страница профиля в DOM', async () => {
    await harness.navigateByUrl('/profile');
    expect(currentUrl()).toBe('/profile');
    await settle();
    expect(harness.fixture.nativeElement.querySelector('app-profile-page'))
      .withContext('страница профиля активирована')
      .not.toBeNull();
    expect(console.error).not.toHaveBeenCalled();
  });
});
