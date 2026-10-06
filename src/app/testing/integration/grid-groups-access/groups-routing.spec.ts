/**
 * Интеграционные спеки маршрутизации раздела «Группы» (батч 3, FR-4.5):
 * полное дерево маршрутов приложения (app.routes.ts) с реальной оболочкой,
 * настоящими guards, core-сервисами и мок-слоем поверх сида seedFixtures —
 * RouterTestingHarness с реальными таймерами (задержка мок-вызовов 500 мс).
 *
 * Сценарии (automation: automated): TS-330 (список групп: счётчики, порядок,
 * клик по названию ведёт на карточку), TS-338 (карточка несуществующей
 * группы — баннер «Группа не найдена» и редирект на /groups), TS-339 (гонки
 * навигации между карточками: последний запрос выигрывает, данные ИК-221 не
 * мелькают).
 */
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { BreakpointObserver } from '@angular/cdk/layout';

import { MockBreakpointObserver } from '../../../../testing/mock-breakpoint-observer';
import { NotificationService } from '../../../shared/notifications/notification-service';
import { routes } from '../../../core/config/app.routes';
import {
  groupIdOf,
  hydrateSeed,
  installMockLayer,
  loginAs,
  resetZoneEnvAfterSpec,
} from './integration-env';

/** Таймаут спеков с настоящими таймерами мока (500 мс на вызов). */
const SPEC_TIMEOUT_MS = 30000;

describe('Groups — маршрутизация на реальном дереве маршрутов (батч 3, FR-4.5)', () => {
  let harness: RouterTestingHarness;
  let notifications: NotificationService;
  let ik221: string;
  let ik222: string;
  let savedTimeout: number;

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
    ik221 = groupIdOf(db, 'ИК-221');
    ik222 = groupIdOf(db, 'ИК-222');
    loginAs(db, 'teacher');

    harness = await RouterTestingHarness.create();
  });

  afterEach(() => {
    // Гасим возможный показ мобильного баннера (таймер автозакрытия —
    // реальный; между тестами TestBed пересоздаёт корневой сервис).
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

  /** Дожидается параллельные загрузки активированной страницы и перерисовки. */
  async function settle(): Promise<void> {
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
  }

  /** Поллит условие с реальными таймерами (навигации после кликов). */
  async function waitFor(condition: () => boolean, what: string): Promise<void> {
    const deadline = Date.now() + SPEC_TIMEOUT_MS / 2;
    while (Date.now() < deadline) {
      if (condition()) {
        return;
      }
      await new Promise((resolve) => setTimeout(resolve, 25));
      harness.fixture.detectChanges();
    }
    throw new Error(`groups-routing.spec: не дождались — ${what}`);
  }

  it('TS-330: список групп — колонки Название/Студентов/Действия, порядок name↑ (25/5/0), название — ссылка, клик ведёт на карточку /groups/:id', async () => {
    await harness.navigateByUrl('/groups');
    await settle();

    expect(harness.routeNativeElement?.querySelector('h1')?.textContent?.trim()).toBe('Группы');
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
  });

  it('TS-338: карточка несуществующей группы — баннер «Группа не найдена», редирект на /groups', async () => {
    await harness.navigateByUrl('/groups/00000000-0000-4000-8000-ffffffffffff');
    await waitFor(() => router().url === '/groups', 'редирект на /groups');

    // Баннер фиксируется СРАЗУ после редиректа (CR-003): settle()/whenStable
    // дожидается 5000-мс таймера автозакрытия — уведомление исчезает до проверки.
    expect(notifications.desktopMessage()).toEqual({
      severity: 'error',
      text: 'Группа не найдена',
    });

    await settle();
    expect(router().url).toBe('/groups');
    expect(root().querySelector('app-groups-page')).not.toBeNull();
    expect(console.error).not.toHaveBeenCalled();
  });

  it('TS-339: гонки навигации между карточками — показан состав последнего клика (ИК-222), данные ИК-221 не мелькают', async () => {
    await harness.navigateByUrl('/groups');
    await settle();

    // Две почти одновременные навигации: вторая определяет итоговый экран.
    const first = harness.navigateByUrl(`/groups/${ik221}`);
    const second = harness.navigateByUrl(`/groups/${ik222}`);
    await Promise.allSettled([first, second]);
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
    expect(
      names.some((name) => name.startsWith('Иванов Иван Иванович 0')),
    )
      .withContext('данные ИК-221 не мелькают')
      .toBeFalse();
    expect(root().querySelector('.group-page__range')?.textContent?.trim()).toBe(
      'Показать записи с 1 по 5 из 5',
    );
    expect(console.error).not.toHaveBeenCalled();
  });
});
