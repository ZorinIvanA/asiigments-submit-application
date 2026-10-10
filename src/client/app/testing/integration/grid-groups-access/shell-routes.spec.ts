/**
 * Интеграционные спеки оболочки и навигации (батч 3, FR-4.8, IF-108/IF-111):
 * полное дерево маршрутов приложения (app.routes.ts) через RouterTestingHarness
 * с настоящими guards, AuthService и core-сервисами на реальном HTTP-ядре
 * (HttpClient + authInterceptor) поверх программируемого
 * HttpTestingController-бэкенда GridBackendStub с сидом зоны (реальные
 * таймеры: макротаски pump()).
 *
 * Сценарии (automation: automated): TS-370 (вкладки преподавателя — ровно по
 * IF-111), TS-371 (вкладки студента — ровно две), TS-372 (чужая роль → /403;
 * сервер тоже отклоняет 403), TS-373 (гость → /login со всех защищённых
 * маршрутов), TS-375 (выход: сессия в памяти очищена, назад не попасть,
 * повтор безопасен), TS-376 (deep-link всех 13 маршрутов §7, wildcard и
 * активная вкладка; активированная страница проверяется по DOM-селектору —
 * CR-005: navigateByUrl для shell-вложенных маршрутов возвращает AppShell).
 *
 * TS-378 отозван оркестратором на стадии scenarios (несовместимые ожидания:
 * битая сессия внутри активной оболочки недостижима при кэше профиля в
 * guards) — теста для него нет.
 */
import { Router, provideRouter } from '@angular/router';

import { NotificationService } from '../../../shared/notifications/notification-service';
import { RecoveryFlowStore } from '../../../core/services/recovery-flow-store';
import { StudentsService } from '../../../core/services/students.service';
import { SubmissionsService } from '../../../core/services/submissions.service';
import { routes } from '../../../core/config/app.routes';
import { GridAccessEnv } from './integration-env';

/** Таймаут спеков с реальными таймерами (макротаски pump/waitFor). */
const SPEC_TIMEOUT_MS = 30000;

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
  let env: GridAccessEnv;
  let notifications: NotificationService;
  let savedTimeout: number;

  let ik221: string;
  let lab1: string;

  beforeEach(async () => {
    savedTimeout = jasmine.DEFAULT_TIMEOUT_INTERVAL;
    jasmine.DEFAULT_TIMEOUT_INTERVAL = SPEC_TIMEOUT_MS;

    spyOn(console, 'error');

    env = GridAccessEnv.setup({ providers: [provideRouter(routes)] });
    notifications = env.notifications;
    ik221 = env.backend.groupIdByName('ИК-221');
    lab1 = env.backend.labId(1, 1);

    // Харнес создаётся гостем (начальный '/' → homeGuard → /login, без HTTP);
    // сессия поднимается в спеках настоящей загрузкой /auth/me.
    await env.attachRoutingHarness();
  });

  afterEach(() => {
    notifications.dismissMobile();
    env.stop();
    jasmine.DEFAULT_TIMEOUT_INTERVAL = savedTimeout;
  });

  function root(): HTMLElement {
    return env.routingFixture!.nativeElement as HTMLElement;
  }

  function router(): Router {
    return env.router;
  }

  /** Дожидается загрузку активированной страницы (волны ответов + CD). */
  async function settle(): Promise<void> {
    await env.pump();
    env.routingFixture!.detectChanges();
  }

  /** Поллит условие с реальными таймерами (клики/история браузера). */
  function waitFor(condition: () => boolean, what: string): Promise<void> {
    return env.waitFor(condition, what, SPEC_TIMEOUT_MS / 2);
  }

  /** Подписи и ссылки вкладок топбара. */
  function tabs(): Array<[string, string | null]> {
    return Array.from(root().querySelectorAll<HTMLAnchorElement>('a.app-topbar__tab')).map(
      (tab) => [tab.textContent?.trim() ?? '', tab.getAttribute('href')],
    );
  }

  it('TS-370: вкладки преподавателя — ровно 5 по IF-111, бренд, ФИО, кнопка «Выйти»', async () => {
    await env.loginAsync('teacher');
    await env.routingHarness!.navigateByUrl('/submissions');
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
  }, SPEC_TIMEOUT_MS);

  it('TS-371: вкладки студента — ровно две, вкладок преподавателя нет, ФИО отображается', async () => {
    await env.loginAsync('student01');
    await env.routingHarness!.navigateByUrl('/my-submissions');
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
  }, SPEC_TIMEOUT_MS);

  it('TS-372: студент — пять чужих маршрутов → /403 «Доступ запрещён» с кнопкой на /my-submissions; прямые вызовы students.getList и submissions.getGrid → ApiError 403', async () => {
    await env.loginAsync('student01');

    for (const path of ['/works', '/submissions', '/groups', `/groups/${ik221}`, '/access']) {
      await env.routingHarness!.navigateByUrl(path);
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
    const students = env.inject(StudentsService);
    const submissions = env.inject(SubmissionsService);
    const rejectedStudents = expectAsync(students.getList({ page: 1 })).toBeRejectedWith({
      status: 403,
      body: { message: 'Доступ запрещён' },
    });
    const rejectedGrid = expectAsync(
      submissions.getGrid({ groupId: ik221, semester: 1, page: 1 }),
    ).toBeRejectedWith({
      status: 403,
      body: { message: 'Доступ запрещён' },
    });
    await env.pump();
    await Promise.all([rejectedStudents, rejectedGrid]);
    expect(console.error).not.toHaveBeenCalled();
  }, SPEC_TIMEOUT_MS);

  it('TS-373: гость — каждый защищённый маршрут уводит на /login, ошибок в консоли нет', async () => {
    for (const path of [
      '/works',
      '/submissions',
      '/my-submissions',
      '/groups',
      '/access',
      '/profile',
    ]) {
      await env.routingHarness!.navigateByUrl(path);
      await settle();
      expect(router().url).withContext(`гость на ${path}`).toBe('/login');
      expect(root().querySelector('.app-topbar'))
        .withContext(`оболочка не рендерится для гостя (${path})`)
        .toBeNull();
    }
    expect(console.error).not.toHaveBeenCalled();
  }, SPEC_TIMEOUT_MS);

  it('TS-375: выход — сессия в памяти очищена, редирект /login; на /submissions после выхода не попасть (guard); повторный «Выйти» безопасен', async () => {
    await env.loginAsync('teacher');
    await env.routingHarness!.navigateByUrl('/submissions');
    await settle();
    expect(env.auth.isAuthenticated()).withContext('сессия активна (память)').toBeTrue();
    expect(localStorage.length)
      .withContext('сессия не пишется в localStorage (FR-092: признак только в памяти)')
      .toBe(0);

    // «Выйти»: POST /auth/logout → сброс кэша → редирект /login.
    root().querySelector<HTMLButtonElement>('.app-topbar__logout')!.click();
    await waitFor(() => router().url === '/login', 'редирект /login после выхода');
    await settle();

    expect(env.auth.isAuthenticated()).withContext('кэш сессии сброшен').toBeFalse();
    expect(localStorage.length).withContext('сессия по-прежнему не пишется').toBe(0);
    expect(root().querySelector('.app-topbar')).withContext('оболочки нет на /login').toBeNull();

    // «Назад не попасть»: повторный заход на защищённый маршрут после выхода —
    // authGuard решает по пустому кэшу и возвращает /login (без «зомби»).
    // (Браузерный history.back() в общем окне karma не воспроизводим —
    // история страницы разделена между спеками файла.)
    await env.routingHarness!.navigateByUrl('/submissions');
    await env.pump();
    expect(router().url).withContext('guard вернул на /login').toBe('/login');
    expect(root().querySelector('.app-topbar')).withContext('зомби-оболочки нет').toBeNull();

    // Повторный «Выйти» без сессии: logout идемпотентен (действие кнопки —
    // AuthService.logout, на /login кнопки нет — вызываем то же действие).
    // Прямой сервисный вызов: POST программируется волной pump (как в loginAsync).
    const secondLogout = env.auth.logout();
    await env.pump();
    await secondLogout;
    expect(env.auth.isAuthenticated()).withContext('сессия по-прежнему отсутствует').toBeFalse();
    expect(router().url).toBe('/login');
    expect(console.error).not.toHaveBeenCalled();
  }, SPEC_TIMEOUT_MS);

  it('TS-376: deep-link всех 13 маршрутов §7 и wildcard рендерит свой экран; вкладка-родитель активна на /submissions, /groups/:id, /access', async () => {
    // Гость: 5 auth-маршрутов (шаги восстановления — с заполненным потоком).
    const recovery = env.inject(RecoveryFlowStore);
    recovery.setEmail('student31@example.com');
    recovery.setResetToken('reset-token');
    // Активированную страницу проверяем по DOM (CR-005): navigateByUrl для
    // shell-вложенных маршрутов возвращает AppShell, а не страницу.
    const pageMounted = (selector: string): boolean =>
      root().querySelector(selector) !== null;

    await env.routingHarness!.navigateByUrl('/login');
    expect(pageMounted('app-login-page')).toBeTrue();
    await env.routingHarness!.navigateByUrl('/register');
    expect(pageMounted('app-register-page')).toBeTrue();
    await env.routingHarness!.navigateByUrl('/recovery');
    expect(pageMounted('app-recovery-page')).toBeTrue();
    await env.routingHarness!.navigateByUrl('/recovery/code');
    expect(pageMounted('app-recovery-code-page')).toBeTrue();
    await env.routingHarness!.navigateByUrl('/reset-password');
    expect(pageMounted('app-reset-password-page')).toBeTrue();
    expect(router().url).withContext('глубокий линк не переадресован').toBe('/reset-password');

    // Студент: /my-submissions.
    await env.loginAsync('student01');
    await env.routingHarness!.navigateByUrl('/my-submissions');
    await settle();
    expect(pageMounted('app-my-submissions-page')).toBeTrue();

    // Преподаватель: works×3, submissions, groups×2, access, profile.
    await env.loginAsync('teacher');
    await env.routingHarness!.navigateByUrl('/works');
    await settle();
    expect(pageMounted('app-works-page')).toBeTrue();
    await env.routingHarness!.navigateByUrl('/works/new');
    await settle();
    expect(pageMounted('app-lab-form-page')).toBeTrue();
    expect(router().url).toBe('/works/new');
    await env.routingHarness!.navigateByUrl(`/works/${lab1}/edit`);
    await settle();
    expect(pageMounted('app-lab-form-page')).toBeTrue();
    expect(router().url).toBe(`/works/${lab1}/edit`);
    await env.routingHarness!.navigateByUrl('/submissions');
    await settle();
    expect(pageMounted('app-submissions-page')).toBeTrue();
    await env.routingHarness!.navigateByUrl('/groups');
    await settle();
    expect(pageMounted('app-groups-page')).toBeTrue();
    await env.routingHarness!.navigateByUrl(`/groups/${ik221}`);
    await settle();
    expect(pageMounted('app-group-page')).toBeTrue();
    await env.routingHarness!.navigateByUrl('/access');
    await settle();
    expect(pageMounted('app-access-page')).toBeTrue();
    await env.routingHarness!.navigateByUrl('/profile');
    await settle();
    expect(pageMounted('app-profile-page')).toBeTrue();

    // Wildcard: несуществующий URL → домашний маршрут роли (homeGuard '**').
    await env.routingHarness!.navigateByUrl('/nonexistent');
    await settle();
    expect(pageMounted('app-works-page')).toBeTrue();
    expect(router().url).toBe('/works');

    // Вкладка-родитель активна на deep-link дочерних маршрутов.
    const activeTab = (): string =>
      root().querySelector('a.app-topbar__tab--active')?.textContent?.trim() ?? '';
    await env.routingHarness!.navigateByUrl('/submissions');
    await settle();
    expect(activeTab()).toBe('Сдача работ');
    await env.routingHarness!.navigateByUrl(`/groups/${ik221}`);
    await settle();
    expect(activeTab()).toBe('Группы');
    await env.routingHarness!.navigateByUrl('/access');
    await settle();
    expect(activeTab()).toBe('Доступ');

    expect(console.error).not.toHaveBeenCalled();
  }, SPEC_TIMEOUT_MS);
});
