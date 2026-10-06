/**
 * Интеграционные спеки оболочки и навигации (батч 3, FR-4.8, IF-108/IF-111):
 * полное дерево маршрутов приложения (app.routes.ts) через RouterTestingHarness
 * с реальными guards, AuthService, core-сервисами и мок-слоем поверх сида
 * seedFixtures (реальные таймеры: задержка мок-вызовов 500 мс).
 *
 * Сценарии (automation: automated): TS-370 (вкладки преподавателя — ровно по
 * IF-111), TS-371 (вкладки студента — ровно две), TS-372 (чужая роль → /403;
 * сервер тоже отклоняет 403), TS-373 (гость → /login со всех защищённых
 * маршрутов), TS-375 (выход: сессия очищена, назад не попасть, повтор
 * безопасен), TS-376 (deep-link всех 13 маршрутов §7, wildcard и активная
 * вкладка; активированная страница проверяется по DOM-селектору — CR-005:
 * navigateByUrl для shell-вложенных маршрутов возвращает AppShell).
 *
 * TS-378 отозван оркестратором на стадии scenarios (несовместимые ожидания:
 * битая сессия внутри активной оболочки недостижима при кэше профиля в
 * guards) — теста для него нет.
 */
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Location } from '@angular/common';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { BreakpointObserver } from '@angular/cdk/layout';

import { MockBreakpointObserver } from '../../../../testing/mock-breakpoint-observer';
import { NotificationService } from '../../../shared/notifications/notification-service';
import { AuthService } from '../../../core/services/auth.service';
import { RecoveryFlowStore } from '../../../core/services/recovery-flow-store';
import { StudentsService } from '../../../core/services/students.service';
import { SubmissionsService } from '../../../core/services/submissions.service';
import { routes } from '../../../core/config/app.routes';
import {
  groupIdOf,
  hydrateSeed,
  installMockLayer,
  labIdOf,
  loginAs,
  resetZoneEnvAfterSpec,
  sessionKeyRaw,
  switchSession,
  userIdOf,
} from './integration-env';

/** Таймаут спеков с настоящими таймерами мока (500 мс на вызов). */
const SPEC_TIMEOUT_MS = 60000;

/** Ожидаемый состав вкладок по IF-111 (§4.8). */
const TEACHER_TABS: ReadonlyArray<readonly [string, string]> = [
  ['Работы', '/works'],
  ['Сдача работ', '/submissions'],
  ['Группы', '/groups'],
  ['Доступ', '/access'],
  ['Профиль', '/profile'],
];
const STUDENT_TABS: ReadonlyArray<readonly [string, string]> = [
  ['Сдача работ', '/my-submissions'],
  ['Профиль', '/profile'],
];

describe('Оболочка и навигация на реальном дереве маршрутов (батч 3, FR-4.8)', () => {
  let harness: RouterTestingHarness;
  let notifications: NotificationService;
  let savedTimeout: number;

  let teacherId: string;
  let student01: string;
  let ik221: string;
  let lab1: string;

  beforeEach(async () => {
    savedTimeout = jasmine.DEFAULT_TIMEOUT_INTERVAL;
    jasmine.DEFAULT_TIMEOUT_INTERVAL = SPEC_TIMEOUT_MS;

    localStorage.clear();
    sessionStorage.clear();
    spyOn(console, 'error');

    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        provideNoopAnimations(),
        { provide: BreakpointObserver, useValue: new MockBreakpointObserver() },
      ],
    });
    notifications = TestBed.inject(NotificationService);
    installMockLayer();

    const db = hydrateSeed();
    teacherId = userIdOf(db, 'teacher');
    student01 = userIdOf(db, 'student01');
    ik221 = groupIdOf(db, 'ИК-221');
    lab1 = labIdOf(db, 1, 1);

    harness = await RouterTestingHarness.create();
  });

  afterEach(() => {
    notifications.dismissMobile();
    resetZoneEnvAfterSpec();
    jasmine.DEFAULT_TIMEOUT_INTERVAL = savedTimeout;
  });

  function root(): HTMLElement {
    return harness.fixture.nativeElement as HTMLElement;
  }

  function router(): Router {
    return TestBed.inject(Router);
  }

  /** Дожидается загрузки активированной страницы (реальные 500 мс/вызов). */
  async function settle(): Promise<void> {
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
  }

  /** Поллит условие с реальными таймерами (клики/история браузера). */
  async function waitFor(condition: () => boolean, what: string): Promise<void> {
    const deadline = Date.now() + SPEC_TIMEOUT_MS / 2;
    while (Date.now() < deadline) {
      if (condition()) {
        return;
      }
      await new Promise((resolve) => setTimeout(resolve, 25));
      harness.fixture.detectChanges();
    }
    throw new Error(`shell-routes.spec: не дождались — ${what}`);
  }

  /** Подписи и ссылки вкладок топбара. */
  function tabs(): Array<[string, string | null]> {
    return Array.from(root().querySelectorAll<HTMLAnchorElement>('a.app-topbar__tab')).map(
      (tab) => [tab.textContent?.trim() ?? '', tab.getAttribute('href')],
    );
  }

  it('TS-370: вкладки преподавателя — ровно 5 по IF-111, бренд, ФИО, кнопка «Выйти»', async () => {
    loginAs(hydrateSeed(), 'teacher');
    await harness.navigateByUrl('/submissions');
    await settle();

    expect(tabs()).toEqual(TEACHER_TABS.map(([label, path]) => [label, path]));
    expect(root().querySelector('.app-topbar__brand')?.textContent?.trim()).toBe(
      'Сдача лабораторных',
    );
    expect(root().querySelector('.app-topbar__who')?.textContent?.trim()).toBe(
      'Сидоров Семён Семёнович',
    );
    expect(root().querySelector('.app-topbar__logout')?.textContent?.trim()).toBe('Выйти');
    expect(console.error).not.toHaveBeenCalled();
  });

  it('TS-371: вкладки студента — ровно две, вкладок преподавателя нет, ФИО отображается', async () => {
    loginAs(hydrateSeed(), 'student01');
    await harness.navigateByUrl('/my-submissions');
    await settle();

    expect(tabs()).toEqual(STUDENT_TABS.map(([label, path]) => [label, path]));
    const labels = tabs().map(([label]) => label);
    expect(labels).not.toContain('Работы');
    expect(labels).not.toContain('Группы');
    expect(labels).not.toContain('Доступ');
    expect(root().querySelector('.app-topbar__who')?.textContent?.trim()).toBe(
      'Иванов Иван Иванович 01',
    );
    expect(console.error).not.toHaveBeenCalled();
  });

  it('TS-372: студент — пять чужих маршрутов → /403 «Доступ запрещён» с кнопкой на /my-submissions; прямые вызовы students.getList и submissions.getGrid → ApiError 403', async () => {
    loginAs(hydrateSeed(), 'student01');

    for (const path of ['/works', '/submissions', '/groups', `/groups/${ik221}`, '/access']) {
      await harness.navigateByUrl(path);
      await settle();
      expect(router().url).withContext(`чужой маршрут ${path}`).toBe('/403');
      expect(root().querySelector('.page-403__title')?.textContent?.trim()).toBe(
        'Доступ запрещён',
      );
    }

    // Кнопка ведёт на домашний маршрут студента.
    root().querySelector<HTMLButtonElement>('.page-403__home')!.click();
    await waitFor(() => router().url === '/my-submissions', 'возврат на /my-submissions');

    // Проверка прав на «сервере»: домены отклоняют студенческую сессию.
    const students = TestBed.inject(StudentsService);
    const submissions = TestBed.inject(SubmissionsService);
    await expectAsync(students.getList({ page: 1 })).toBeRejectedWith({
      status: 403,
      body: { message: 'Доступ запрещён' },
    });
    await expectAsync(submissions.getGrid({ groupId: ik221, semester: 1, page: 1 })).toBeRejectedWith({
      status: 403,
      body: { message: 'Доступ запрещён' },
    });
    expect(console.error).not.toHaveBeenCalled();
  });

  it('TS-373: гость — каждый защищённый маршрут уводит на /login, ошибок в консоли нет', async () => {
    for (const path of [
      '/works',
      '/submissions',
      '/my-submissions',
      '/groups',
      '/access',
      '/profile',
    ]) {
      await harness.navigateByUrl(path);
      await settle();
      expect(router().url).withContext(`гость на ${path}`).toBe('/login');
      expect(root().querySelector('.app-topbar'))
        .withContext(`оболочка не рендерится для гостя (${path})`)
        .toBeNull();
    }
    expect(console.error).not.toHaveBeenCalled();
  });

  it('TS-375: выход — сессия очищена, редирект /login; назад на /submissions не попасть; повторный «Выйти» без сессии безопасен', async () => {
    loginAs(hydrateSeed(), 'teacher');
    await harness.navigateByUrl('/submissions');
    await settle();
    expect(sessionKeyRaw()).withContext('сессия активна').not.toBeNull();

    // «Выйти»: logout (мок) → редирект /login.
    root().querySelector<HTMLButtonElement>('.app-topbar__logout')!.click();
    await waitFor(() => router().url === '/login', 'редирект /login после выхода');
    await settle();

    expect(sessionKeyRaw()).withContext('ключ mock.session.userId удалён').toBeNull();
    expect(root().querySelector('.app-topbar')).withContext('оболочки нет на /login').toBeNull();

    // History-back на /submissions: guard возвращает на /login (без «зомби»).
    TestBed.inject(Location).back();
    // Даём popstate сработать, затем дожидаемся финального /login после guard.
    await new Promise((resolve) => setTimeout(resolve, 100));
    await waitFor(() => router().url === '/login', 'history-back завершается /login');
    await settle();
    await new Promise((resolve) => setTimeout(resolve, 200));
    harness.fixture.detectChanges();
    expect(router().url).toBe('/login');
    expect(root().querySelector('.app-topbar')).withContext('зомби-оболочки нет').toBeNull();

    // Повторный «Выйти» без сессии: logout идемпотентен (действие кнопки —
    // AuthService.logout, на /login кнопки нет — вызываем то же действие).
    await TestBed.inject(AuthService).logout();
    expect(sessionKeyRaw()).withContext('сессия по-прежнему отсутствует').toBeNull();
    expect(router().url).toBe('/login');
    expect(console.error).not.toHaveBeenCalled();
  });

  it('TS-376: deep-link всех 13 маршрутов §7 и wildcard рендерит свой экран; вкладка-родитель активна на /submissions, /groups/:id, /access', async () => {
    // Гость: 5 auth-маршрутов (шаги восстановления — с заполненным потоком).
    const recovery = TestBed.inject(RecoveryFlowStore);
    recovery.setEmail('student31@example.com');
    recovery.setResetToken('reset-token');
    // Активированную страницу проверяем по DOM (CR-005): navigateByUrl для
    // shell-вложенных маршрутов возвращает AppShell, а не страницу.
    const pageMounted = (selector: string): boolean =>
      root().querySelector(selector) !== null;

    await harness.navigateByUrl('/login');
    expect(pageMounted('app-login-page')).toBeTrue();
    await harness.navigateByUrl('/register');
    expect(pageMounted('app-register-page')).toBeTrue();
    await harness.navigateByUrl('/recovery');
    expect(pageMounted('app-recovery-page')).toBeTrue();
    await harness.navigateByUrl('/recovery/code');
    expect(pageMounted('app-recovery-code-page')).toBeTrue();
    await harness.navigateByUrl('/reset-password');
    expect(pageMounted('app-reset-password-page')).toBeTrue();
    expect(router().url).withContext('глубокий линк не переадресован').toBe('/reset-password');

    // Студент: /my-submissions.
    switchSession(student01);
    await TestBed.inject(AuthService).loadMe();
    await harness.navigateByUrl('/my-submissions');
    expect(pageMounted('app-my-submissions-page')).toBeTrue();

    // Преподаватель: works×3, submissions, groups×2, access, profile.
    switchSession(teacherId);
    await TestBed.inject(AuthService).loadMe();
    await harness.navigateByUrl('/works');
    expect(pageMounted('app-works-page')).toBeTrue();
    await harness.navigateByUrl('/works/new');
    expect(pageMounted('app-lab-form-page')).toBeTrue();
    expect(router().url).toBe('/works/new');
    await harness.navigateByUrl(`/works/${lab1}/edit`);
    expect(pageMounted('app-lab-form-page')).toBeTrue();
    expect(router().url).toBe(`/works/${lab1}/edit`);
    await harness.navigateByUrl('/submissions');
    expect(pageMounted('app-submissions-page')).toBeTrue();
    await harness.navigateByUrl('/groups');
    expect(pageMounted('app-groups-page')).toBeTrue();
    await harness.navigateByUrl(`/groups/${ik221}`);
    expect(pageMounted('app-group-page')).toBeTrue();
    await harness.navigateByUrl('/access');
    expect(pageMounted('app-access-page')).toBeTrue();
    await harness.navigateByUrl('/profile');
    expect(pageMounted('app-profile-page')).toBeTrue();

    // Wildcard: несуществующий URL → домашний маршрут роли (homeGuard '**').
    await harness.navigateByUrl('/nonexistent');
    expect(pageMounted('app-works-page')).toBeTrue();
    expect(router().url).toBe('/works');

    // Вкладка-родитель активна на deep-link дочерних маршрутов.
    const activeTab = (): string =>
      root().querySelector('a.app-topbar__tab--active')?.textContent?.trim() ?? '';
    await harness.navigateByUrl('/submissions');
    await settle();
    expect(activeTab()).toBe('Сдача работ');
    await harness.navigateByUrl(`/groups/${ik221}`);
    await settle();
    expect(activeTab()).toBe('Группы');
    await harness.navigateByUrl('/access');
    await settle();
    expect(activeTab()).toBe('Доступ');

    expect(console.error).not.toHaveBeenCalled();
  });
});
