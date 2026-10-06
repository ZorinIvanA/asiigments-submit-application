/**
 * Окружение интеграционных тестов батча auth-profile (batch1: FR-4.1/4.2/4.7,
 * IF-108, TS-001..TS-040): реальное приложение (app.routes + все страницы),
 * реальный мок-слой (setupMockLayer — все шесть доменов, как в проде) на
 * свежем MockApiClient, реальные AuthService/RecoveryFlowStore/Notification.
 *
 * Отличия от продакшн-бутстрапа (и почему):
 *  - собственный экземпляр MockApiClient — свежий после resetDemoEnv
 *    (состояние в памяти клиента переживает localStorage.clear());
 *  - часы обработчиков auth управляемые (`clock.now`) — детерминизм лимитов
 *    частоты и TTL кода/токена (now — параметр registerAuthHandlers);
 *  - BreakpointObserver — MockBreakpointObserver (без медиазапросов);
 *  - LabsService/SubmissionsService — пустые заглушки: их страницы (/works,
 *    /my-submissions) — цели редиректов ролей, вне зоны батча (паттерн
 *    app.routes.spec.ts); все auth/profile-домены — РЕАЛЬНЫЕ.
 *
 * Файл компилируется и проектом приложения (tsconfig.app.json включает все
 * не-spec ts), поэтому здесь нет обращений к jasmine/expect — ассерты живут
 * в *.spec.ts, хелперы сообщают об ошибках окружения обычными Error.
 *
 * Тайминги мока — реальные таймеры (задержка MockApiClient 500 мс на вызов,
 * управляемая задержка мока отклонена оркестратором): `flushMock(n)` ждёт
 * n мок-вызовов. Правило флак-контроля: показанный баннер уведомления
 * держит таймер автозакрытия (5000 мс), на котором «зависнет» внутренний
 * whenStable навигаций — после КАЖДОГО чтения уведомления вызывайте
 * `notifications.dismissMobile()` (гасит таймер и баннер).
 */
import { BreakpointObserver } from '@angular/cdk/layout';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { routes } from '../../../core/config/app.routes';
import { AuthService } from '../../../core/services/auth.service';
import { LabsService } from '../../../core/services/labs.service';
import { RecoveryFlowStore } from '../../../core/services/recovery-flow-store';
import { SubmissionsService } from '../../../core/services/submissions.service';
import { setupMockLayer } from '../../../mock';
import { registerAuthHandlers } from '../../../mock/auth/handlers';
import { MockApiClient } from '../../../mock/mock-api-client';
import { MockDbData } from '../../../mock/mock-db';
import { MOCK_DELAY_MS, STORAGE_KEYS } from '../../../shared/models';
import { NotificationService } from '../../../shared/notifications/notification-service';
import { MockBreakpointObserver } from '../../../../testing/mock-breakpoint-observer';
import { resetDemoEnv, resetDemoEnvAfterSpec } from '../../integration-env';

/** Фиксированный момент «сейчас» начала сценария (детерминизм часов auth). */
export const T0 = 1_758_240_000_000;

/** Собранное окружение одного интеграционного сценария. */
export interface AuthProfileEnv {
  /** Свежий мок-клиент со всеми доменными обработчиками (реестр IF-110). */
  readonly client: MockApiClient;
  /** Управляемые часы обработчиков auth (лимиты/TTL). */
  readonly clock: { now: number };
  readonly breakpoints: MockBreakpointObserver;
  readonly harness: RouterTestingHarness;
  readonly router: Router;
  readonly auth: AuthService;
  readonly flow: RecoveryFlowStore;
  readonly notifications: NotificationService;
}

/**
 * Запускает окружение сценария. `initialUrl` — начальная навигация
 * (RouterTestingHarness выполняет её внутри create); без него навигацию
 * выполняет тест — так сессию (`loginAs`) можно установить ДО первого
 * перехода.
 */
export async function startAuthProfileEnv(
  opts: { initialUrl?: string; keepStorage?: boolean } = {},
): Promise<AuthProfileEnv> {
  if (!opts.keepStorage) {
    resetDemoEnv();
  }
  const clock = { now: T0 };
  const client = new MockApiClient();
  setupMockLayer(client); // все домены — как в проде (IF-110)
  // Часы auth — управляемые: повторная регистрация заменяет обработчики.
  registerAuthHandlers(client, () => clock.now);
  const breakpoints = new MockBreakpointObserver();

  TestBed.configureTestingModule({
    providers: [
      provideRouter(routes),
      provideNoopAnimations(),
      { provide: MockApiClient, useValue: client },
      { provide: BreakpointObserver, useValue: breakpoints },
      { provide: LabsService, useValue: new EmptyLabsStub() },
      { provide: SubmissionsService, useValue: new EmptySubmissionsStub() },
    ],
  });

  const harness = await RouterTestingHarness.create(opts.initialUrl);
  return {
    client,
    clock,
    breakpoints,
    harness,
    router: TestBed.inject(Router),
    auth: TestBed.inject(AuthService),
    flow: TestBed.inject(RecoveryFlowStore),
    notifications: TestBed.inject(NotificationService),
  };
}

/** Перезапуск приложения «как F5»: новый инжектор, storage — по opts. */
export async function restartApp(
  env: AuthProfileEnv,
  opts: { keepStorage?: boolean } = {},
): Promise<AuthProfileEnv> {
  env.notifications.dismissMobile();
  TestBed.resetTestingModule();
  return startAuthProfileEnv(opts);
}

/** Завершение сценария: гасим баннер/таймер уведомлений и storage/сид. */
export function stopAuthProfileEnv(env: AuthProfileEnv): void {
  env.notifications.dismissMobile();
  resetDemoEnvAfterSpec();
}

/**
 * Ожидание завершения n последовательных мок-вызовов (+ CD). Запас +500 мс
 * поверх задержки мока покрывает планировщик таймеров Karma, навигацию и
 * ленивые loadComponent (ревью CR-012: +150 мс флакало на цепочках
 * «вызов → редирект → инициализация страницы»).
 */
export async function flushMock(
  env: AuthProfileEnv,
  calls = 1,
): Promise<void> {
  await new Promise((resolve) =>
    setTimeout(resolve, MOCK_DELAY_MS * calls + 500),
  );
  env.harness.fixture.detectChanges();
}

/**
 * Обязательный элемент по селектору (ревью CR-013): отсутствие элемента —
 * падение с читаемой ошибкой и текущим URL, а не «Cannot read properties
 * of null» на spot-ассерте.
 */
export function required<K extends HTMLElement = HTMLElement>(
  env: AuthProfileEnv,
  selector: string,
): K {
  const element = root(env).querySelector<K>(selector);
  if (element === null) {
    throw new Error(
      `required: элемент «${selector}» не найден на ${env.router.url}`,
    );
  }
  return element;
}

/** Корневой элемент отрисованного приложения. */
export function root(env: AuthProfileEnv): HTMLElement {
  return env.harness.fixture.nativeElement;
}

/** Ввод значения в поле (input event + CD), как печать пользователя. */
export function setInput(
  env: AuthProfileEnv,
  selector: string,
  value: string,
): void {
  const input = root(env).querySelector<HTMLInputElement>(selector);
  if (input === null) {
    throw new Error(`setInput: поле ${selector} не найдено на странице`);
  }
  input.value = value;
  input.dispatchEvent(new Event('input'));
  env.harness.fixture.detectChanges();
}

/** Кнопка отправки формы (button[type=submit]). */
export function submitButton(env: AuthProfileEnv): HTMLButtonElement {
  const button = root(env).querySelector<HTMLButtonElement>(
    'button[type="submit"]',
  );
  if (button === null) {
    throw new Error('submitButton: кнопка button[type=submit] не найдена');
  }
  return button;
}

/** Кнопка по дословной подписи («Ввести код», «Переотправить код», …). */
export function buttonByLabel(
  env: AuthProfileEnv,
  label: string,
): HTMLButtonElement {
  const button = Array.from(
    root(env).querySelectorAll<HTMLButtonElement>('button'),
  ).find((b) => (b.textContent ?? '').trim() === label);
  if (button === undefined) {
    throw new Error(`buttonByLabel: кнопка «${label}» не найдена`);
  }
  return button;
}

/** Ссылка по дословному тексту («Запросить код заново», …). */
export function linkByLabel(
  env: AuthProfileEnv,
  label: string,
): HTMLAnchorElement {
  const anchor = Array.from(
    root(env).querySelectorAll<HTMLAnchorElement>('a'),
  ).find((a) => (a.textContent ?? '').trim() === label);
  if (anchor === undefined) {
    throw new Error(`linkByLabel: ссылка «${label}» не найдена`);
  }
  return anchor;
}

/** Текст полевой ошибки у поля (div.field__error соседнего .field). */
export function fieldErrorText(
  env: AuthProfileEnv,
  inputSelector: string,
): string | null {
  const input = root(env).querySelector(inputSelector);
  if (input === null) {
    return null;
  }
  const error = input
    .closest('.field')
    ?.querySelector<HTMLElement>('.field__error');
  return error === undefined || error === null
    ? null
    : error.textContent!.trim();
}

/** Ссылки-вкладки топбара в порядке рендера (IF-111). */
export function shellTabs(env: AuthProfileEnv): HTMLAnchorElement[] {
  return Array.from(
    root(env).querySelectorAll<HTMLAnchorElement>('a.app-topbar__tab'),
  );
}

/** Значение ключа сессии mock.session.userId. */
export function sessionUserId(): string | null {
  return localStorage.getItem(STORAGE_KEYS.session);
}

/** Снимок mock.db.v1 из localStorage. */
export function mockDbData(): MockDbData {
  const raw = localStorage.getItem(STORAGE_KEYS.mockDb);
  if (raw === null) {
    throw new Error('mockDbData: ключ mock.db.v1 отсутствует в localStorage');
  }
  return JSON.parse(raw) as MockDbData;
}

/**
 * Заглушки страниц-целей редиректов ролей (/works, /my-submissions) —
 * пустые данные без мок-вызовов; домены этих страниц — зона других батчей.
 */
class EmptyLabsStub {
  async getList(): Promise<{
    items: unknown[];
    total: number;
    page: number;
    pageSize: number;
  }> {
    return { items: [], total: 0, page: 1, pageSize: 10 };
  }

  async getSemesters(): Promise<number[]> {
    return [];
  }
}

class EmptySubmissionsStub {
  async getMy(_semester: number): Promise<{
    hasGroup: boolean;
    labs: unknown[];
    submissions: unknown[];
  }> {
    return { hasGroup: true, labs: [], submissions: [] };
  }
}
