/**
 * Интеграционные спеки маршрутизации раздела «Группы» (батч 3, FR-4.5):
 * полное дерево маршрутов приложения (app.routes.ts) с реальной оболочкой,
 * настоящими guards и core-сервисами на реальном HTTP-ядре
 * (HttpClient + authInterceptor) поверх программируемого
 * HttpTestingController-бэкенда GridBackendStub с сидом зоны —
 * RouterTestingHarness с реальными таймерами (макротаски pump()).
 *
 * Сценарии (automation: automated): TS-330 (список групп: счётчики, порядок,
 * клик по названию ведёт на карточку), TS-338 (карточка несуществующей
 * группы — баннер «Группа не найдена» и редирект на /groups), TS-339 (гонки
 * навигации между карточками: последний запрос выигрывает, данные ИК-221 не
 * мелькают).
 */
import { Router, provideRouter } from '@angular/router';

import { NotificationService } from '../../../shared/notifications/notification-service';
import { routes } from '../../../core/config/app.routes';
import { GridAccessEnv } from './integration-env';

/** Таймаут спеков с реальными таймерами (макротаски pump/waitFor). */
const SPEC_TIMEOUT_MS = 20000;

describe('Groups — маршрутизация на реальном дереве маршрутов (батч 3, FR-4.5)', () => {
  let env: GridAccessEnv;
  let notifications: NotificationService;
  let ik221: string;
  let ik222: string;
  let savedTimeout: number;

  beforeEach(async () => {
    savedTimeout = jasmine.DEFAULT_TIMEOUT_INTERVAL;
    jasmine.DEFAULT_TIMEOUT_INTERVAL = SPEC_TIMEOUT_MS;

    spyOn(console, 'error');

    env = GridAccessEnv.setup({ providers: [provideRouter(routes)] });
    notifications = env.notifications;
    ik221 = env.backend.groupIdByName('ИК-221');
    ik222 = env.backend.groupIdByName('ИК-222');

    // Харнес создаётся гостем (начальный '/' → homeGuard → /login, без HTTP);
    // сессия поднимается в спеках настоящей загрузкой /auth/me.
    await env.attachRoutingHarness();
  });

  afterEach(() => {
    // Гасим возможный показ мобильного баннера (таймер автозакрытия —
    // реальный; между тестами TestBed пересоздаёт корневой сервис).
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

  /** Дожидается параллельные загрузки активированной страницы и перерисовку. */
  async function settle(): Promise<void> {
    await env.pump();
    env.routingFixture!.detectChanges();
  }

  /** Поллит условие с реальными таймерами (навигации после кликов). */
  function waitFor(condition: () => boolean, what: string): Promise<void> {
    return env.waitFor(condition, what, SPEC_TIMEOUT_MS / 2);
  }

  it('TS-330: список групп — колонки Название/Студентов/Действия, порядок name↑ (25/5/0), название — ссылка, клик ведёт на карточку /groups/:id', async () => {
    await env.loginAsync('teacher');
    await env.routingHarness!.navigateByUrl('/groups');
    await settle();

    expect(env.routingHarness!.routeNativeElement?.querySelector('h1')?.textContent?.trim()).toBe('Группы');
    const headers = Array.from(root().querySelectorAll('thead th')).map((th) =>
      th.textContent?.trim(),
    );
    expect(headers).toEqual(['Название', 'Студентов', 'Действия']);

    const rows = Array.from(root().querySelectorAll('tbody tr'));
    expect(rows.length).toBe(3);
    const names = rows.map((row) => row.querySelector('td')?.textContent?.trim());
    const counts = rows.map((row) => row.querySelectorAll('td')[1]?.textContent?.trim() ?? '');
    expect(names).toEqual(['ИК-221', 'ИК-222', 'ИК-223']);
    expect(counts).toEqual(['25', '5', '0']);

    // Название — ссылка; клик ведёт на карточку этой группы.
    const link = rows[1]!.querySelector<HTMLAnchorElement>('a');
    expect(link?.getAttribute('href')).toBe(`/groups/${ik222}`);
    link!.click();
    await waitFor(() => router().url === `/groups/${ik222}`, 'переход на карточку ИК-222');
    await settle();

    expect(root().querySelector('h1')?.textContent?.trim()).toBe('ИК-222');
    expect(root().querySelector('.group-page__count')?.textContent?.trim()).toBe('Студентов: 5');
    expect(console.error).not.toHaveBeenCalled();
  }, SPEC_TIMEOUT_MS);

  it('TS-338: карточка несуществующей группы — баннер «Группа не найдена», редирект на /groups', async () => {
    await env.loginAsync('teacher');
    await env.routingHarness!.navigateByUrl('/groups/00000000-0000-4000-8000-ffffffffffff');
    await waitFor(() => router().url === '/groups', 'редирект на /groups');

    // Баннер фиксируется СРАЗУ после редиректа (CR-003): pump/settle дожидаются
    // микрозадач, но не 5000-мс таймера автозакрытия — уведомление живо.
    expect(notifications.desktopMessage()).toEqual({
      severity: 'error',
      text: 'Группа не найдена',
    });

    await settle();
    expect(router().url).toBe('/groups');
    expect(root().querySelector('app-groups-page')).not.toBeNull();
    expect(console.error).not.toHaveBeenCalled();
  }, SPEC_TIMEOUT_MS);

  it('TS-339: гонки навигации между карточками — показан состав последнего клика (ИК-222), данные ИК-221 не мелькают', async () => {
    await env.loginAsync('teacher');
    await env.routingHarness!.navigateByUrl('/groups');
    await settle();

    // Две почти одновременные навигации: вторая определяет итоговый экран.
    const first = env.routingHarness!.navigateByUrl(`/groups/${ik221}`);
    const second = env.routingHarness!.navigateByUrl(`/groups/${ik222}`);
    await Promise.allSettled([first, second]);
    await env.pump();
    // Ждём не только URL: данные карточки ещё в полёте после смены маршрута
    // (CR-004) — поллим заголовок и счётчик последнего клика.
    await waitFor(
      () =>
        router().url === `/groups/${ik222}` &&
        root().querySelector('h1')?.textContent?.trim() === 'ИК-222' &&
        root().querySelector('.group-page__count')?.textContent?.trim() === 'Студентов: 5',
      'карточка ИК-222 с заголовком и счётчиком',
    );
    await settle();

    // Заголовок, счётчик и таблица согласованы между собой и с ИК-222.
    expect(root().querySelector('h1')?.textContent?.trim()).toBe('ИК-222');
    expect(root().querySelector('.group-page__count')?.textContent?.trim()).toBe('Студентов: 5');
    const names = Array.from(root().querySelectorAll('tbody tr td:first-child')).map((cell) =>
      cell.textContent?.trim() ?? '',
    );
    expect(names).toEqual([
      'Иванов Иван Иванович 26',
      'Иванов Иван Иванович 27',
      'Иванов Иван Иванович 28',
      'Иванов Иван Иванович 29',
      'Иванов Иван Иванович 30',
    ]);
    expect(names.some((name) => name.startsWith('Иванов Иван Иванович 0')))
      .withContext('данные ИК-221 не мелькают')
      .toBeFalse();
    expect(root().querySelector('.group-page__range')?.textContent?.trim()).toBe(
      'Показать записи с 1 по 5 из 5',
    );
    expect(console.error).not.toHaveBeenCalled();
  }, SPEC_TIMEOUT_MS);
});
