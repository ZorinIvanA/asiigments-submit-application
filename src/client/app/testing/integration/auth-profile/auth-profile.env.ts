/**
 * Окружение интеграционных тестов батча auth-profile (batch1: FR-4.1/4.2/4.7,
 * IF-108): реальное приложение (app.routes + все страницы) на реальном
 * HTTP-ядре — HttpClient + authInterceptor (withCredentials, дедуплицируемый
 * refresh, нормализация отказов в ApiError) — с программируемым
 * HttpTestingController-бэкендом (FR-026). Реальные AuthService/
 * RecoveryFlowStore/Notification/guards; признак сессии — ТОЛЬКО память
 * AuthService (FR-092), шаги потока восстановления — recovery.flow.v1 в
 * sessionStorage (IF-102).
 *
 * Отличия от продакшн-бутстрапа (и почему):
 *  - HTTP-бэкенд — HttpTestingController вместо сети: каждый вызов API
 *    программируется сценарием через respond(), факт перехвата попадает в
 *    журнал env.requests; серверное поведение (роли, лимиты, TTL) — зона
 *    бэкенд-доменов, здесь задается ответами сценариев;
 *  - холодный старт воспроизводит прод-инициализацию сессии: initSession →
 *    GET /auth/me → session (200 с MeDto) | null (401 + refresh 401 →
 *    анонимный старт);
 *  - BreakpointObserver — MockBreakpointObserver (без медиазапросов);
 *  - LabsService/SubmissionsService — пустые заглушки: их страницы (/works,
 *    /my-submissions) — цели редиректов ролей, вне зоны батча (паттерн
 *    app.routes.spec.ts); все auth/profile-домены — РЕАЛЬНЫЕ;
 *  - анимации — noop.
 *
 * Файл компилируется и проектом приложения (tsconfig.app.json включает все
 * не-spec ts), поэтому здесь нет обращений к jasmine/expect — ассерты живут
 * в *.spec.ts, хелперы сообщают об ошибках окружения обычными Error.
 *
 * Тайминги: задержек бэкенда нет — settle() дренирует микрозадачи
 * (промисы flush → интерцептор → сервис → страница) макротаском, а
 * waitForUrl() дожидается фактической навигации по URL (ленивые
 * loadComponent-чанки — асинхронные). Флак-контроль: показанный баннер
 * уведомления держит таймер автозакрытия (5000 мс), на котором «зависнет»
 * whenStable навигаций — после КАЖДОГО чтения уведомления вызывайте
 * `notifications.dismissMobile()` (гасит таймер и баннер).
 */
import { BreakpointObserver } from '@angular/cdk/layout';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { authInterceptor } from '../../../core/auth-interceptor';
import { API_BASE_URL } from '../../../core/api-base-url';
import { routes } from '../../../core/config/app.routes';
import { AuthService } from '../../../core/services/auth.service';
import { LabsService } from '../../../core/services/labs.service';
import { RecoveryFlowStore } from '../../../core/services/recovery-flow-store';
import { SubmissionsService } from '../../../core/services/submissions.service';
import { MeDto, ProfileDto } from '../../../shared/models';
import { NotificationService } from '../../../shared/notifications/notification-service';
import { MockBreakpointObserver } from '../../../../testing/mock-breakpoint-observer';

/** Демо-учётки сценариев (значения ответов определяют сценарии). */
export const DEMO_LOGINS = ['student01', 'teacher'] as const;

/** Логин демо-учётки. */
export type DemoLogin = (typeof DEMO_LOGINS)[number];

/** Ответы GET /auth/me и POST /auth/login|register для демо-учёток. */
export const ME_FIXTURES: Record<DemoLogin, MeDto> = {
  student01: {
    login: 'student01',
    fullName: 'Иванов Иван Иванович 01',
    role: 'student',
    groupName: 'ИК-221',
  },
  teacher: {
    login: 'teacher',
    fullName: 'Сидоров Семён Семёнович',
    role: 'teacher',
    groupName: null,
  },
};

/** Ответы GET/PUT /me/profile для демо-учёток. */
export const PROFILE_FIXTURES: Record<DemoLogin, ProfileDto> = {
  student01: {
    login: 'student01',
    email: 'student01@example.com',
    fullName: 'Иванов Иван Иванович 01',
    role: 'student',
    groupName: 'ИК-221',
  },
  teacher: {
    login: 'teacher',
    email: 'teacher@example.com',
    fullName: 'Сидоров Семён Семёнович',
    role: 'teacher',
    groupName: null,
  },
};

/** Демо-пароли (значение тела login — проверяет сценарий). */
export const DEMO_PASSWORDS: Record<DemoLogin, string> = {
  student01: 'student123!',
  teacher: 'teacher123!',
};

/** Тексты статусов для flush (диагностика в отчётах HttpTestingController). */
const STATUS_TEXT: Record<number, string> = {
  200: 'OK',
  204: 'No Content',
  400: 'Bad Request',
  401: 'Unauthorized',
  403: 'Forbidden',
  404: 'Not Found',
  409: 'Conflict',
  429: 'Too Many Requests',
};

/** Журнальная запись о перехваченном и отвеченном API-вызове. */
export interface RecordedRequest {
  readonly method: string;
  readonly path: string;
  /** Тело запроса на границе HTTP (для утверждений сценариев). */
  readonly body: unknown;
}

/** Программируемый ответ бэкенда для respond(). */
export interface RespondOptions {
  /** HTTP-статус ответа; по умолчанию 200. */
  readonly status?: number;
  /** Тело ответа; по умолчанию null (в т.ч. для 204). */
  readonly body?: unknown;
}

/** Собранное окружение одного интеграционного сценария. */
export interface AuthProfileEnv {
  /** Базовый префикс API (TestBed.inject(API_BASE_URL), конвенция ADR-014). */
  readonly apiBase: string;
  /** HttpTestingController: контролируется стопом сценария (verify). */
  readonly httpMock: HttpTestingController;
  /** Журнал отвеченных API-вызовов в порядке поступления. */
  readonly requests: RecordedRequest[];
  readonly breakpoints: MockBreakpointObserver;
  readonly harness: RouterTestingHarness;
  readonly router: Router;
  readonly auth: AuthService;
  readonly flow: RecoveryFlowStore;
  readonly notifications: NotificationService;
}

/** Опции старта окружения. */
export interface StartOptions {
  /** Начальная навигация (RouterTestingHarness выполняет её внутри create). */
  readonly initialUrl?: string;
  /**
   * Сессия холодного старта: логин демо-учётки (GET /auth/me → 200 с MeDto)
   * или null — аноним (401 от me + неудачный refresh, как у гостя без cookie).
   * По умолчанию null.
   */
  readonly session?: DemoLogin | null;
  /** Сохранить storage между инстансами приложения (аналог F5). */
  readonly keepStorage?: boolean;
}

/**
 * Запускает окружение сценария: fresh injector, HttpTestingController,
 * инициализация сессии как в проде, затем начальная навигация.
 */
export async function startAuthProfileEnv(
  opts: StartOptions = {},
): Promise<AuthProfileEnv> {
  if (!opts.keepStorage) {
    clearStorages();
  }
  const breakpoints = new MockBreakpointObserver();

  TestBed.configureTestingModule({
    providers: [
      provideRouter(routes),
      provideNoopAnimations(),
      provideHttpClient(withInterceptors([authInterceptor])),
      provideHttpClientTesting(),
      { provide: BreakpointObserver, useValue: breakpoints },
      { provide: LabsService, useValue: new EmptyLabsStub() },
      { provide: SubmissionsService, useValue: new EmptySubmissionsStub() },
    ],
  });

  const apiBase = TestBed.inject(API_BASE_URL);
  const httpMock = TestBed.inject(HttpTestingController);
  const auth = TestBed.inject(AuthService);
  const requests: RecordedRequest[] = [];
  const env: AuthProfileEnv = {
    apiBase,
    httpMock,
    requests,
    breakpoints,
    // Заполняется после create (harness/router); объявлены ниже по контракту.
    harness: undefined as unknown as RouterTestingHarness,
    router: undefined as unknown as Router,
    auth,
    flow: TestBed.inject(RecoveryFlowStore),
    notifications: TestBed.inject(NotificationService),
  };

  // Холодный старт как в проде (provideAppInitializer → initSession):
  // GET /auth/me программируется опцией session (журналяется наравне
  // с respond() — счётчики вызовов сценариям доступны единым образом).
  const init = auth.initSession();
  const meRequest = httpMock.expectOne(`${apiBase}/auth/me`);
  requests.push({ method: 'GET', path: '/auth/me', body: meRequest.request.body });
  if (opts.session) {
    meRequest.flush(ME_FIXTURES[opts.session]);
  } else {
    meRequest.flush(
      { message: 'Не авторизован' },
      { status: 401, statusText: STATUS_TEXT[401] },
    );
    const refreshRequest = httpMock.expectOne(`${apiBase}/auth/refresh`);
    requests.push({
      method: 'POST',
      path: '/auth/refresh',
      body: refreshRequest.request.body,
    });
    refreshRequest.flush(
      { message: 'Не авторизован' },
      { status: 401, statusText: STATUS_TEXT[401] },
    );
  }
  await init;

  const harness = await RouterTestingHarness.create(opts.initialUrl);
  return {
    ...env,
    harness,
    router: TestBed.inject(Router),
  };
}

/**
 * Перезапуск приложения «как F5»: новый инжектор (сессия в памяти НЕ
 * переживает рестарт — восстанавливается ответом /auth/me), storage —
 * по opts.
 */
export async function restartApp(
  env: AuthProfileEnv,
  opts: { keepStorage?: boolean; session?: DemoLogin | null } = {},
): Promise<AuthProfileEnv> {
  env.notifications.dismissMobile();
  TestBed.resetTestingModule();
  return startAuthProfileEnv({
    keepStorage: opts.keepStorage,
    session: opts.session ?? null,
  });
}

/**
 * Завершение сценария: гасим баннер/таймер уведомлений и проверяем, что
 * каждый выполненный API-вызов был запрограммирован сценарием (verify) —
 * незапрограммированные вызовы означают дыру в программировании бэкенда.
 */
export function stopAuthProfileEnv(env: AuthProfileEnv): void {
  env.notifications.dismissMobile();
  env.httpMock.verify();
  clearStorages();
}

/** Очистка браузерных хранилищ (recovery.flow.v1 — sessionStorage, IF-102). */
export function clearStorages(): void {
  localStorage.clear();
  sessionStorage.clear();
}

/**
 * Программирует ответ ожидаемого API-вызова: запрос к `method path` должен
 * быть уже отправлен приложением (вызывается ПОСЛЕ триггера действия),
 * фиксируется в журнале env.requests.
 */
export function respond(
  env: AuthProfileEnv,
  method: string,
  path: string,
  options: RespondOptions = {},
): void {
  const status = options.status ?? 200;
  const request = env.httpMock.expectOne({
    method,
    url: `${env.apiBase}${path}`,
  });
  env.requests.push({ method, path, body: request.request.body });
  request.flush(options.body ?? null, {
    status,
    statusText: STATUS_TEXT[status] ?? '',
  });
}

/** Полный URL API-вызова (конвенция ADR-014: от TestBed-префикса). */
export function apiUrl(env: AuthProfileEnv, path: string): string {
  return `${env.apiBase}${path}`;
}

/** Число отвеченных вызовов `method path` в журнале сценария. */
export function requestsOf(
  env: AuthProfileEnv,
  method: string,
  path: string,
): number {
  return env.requests.filter((r) => r.method === method && r.path === path)
    .length;
}

/**
 * Дренаж микрозадач (цепочки промисов flush → интерцептор → сервис →
 * страница) и CD. Не ждёт таймеры (баннер автозакрытия не мешает) и
 * ленивые чанки — для навигаций используйте waitForUrl.
 */
export async function settle(env: AuthProfileEnv, hops = 2): Promise<void> {
  for (let i = 0; i < hops; i++) {
    await new Promise<void>((resolve) => setTimeout(resolve, 0));
  }
  env.harness.fixture.detectChanges();
}

/**
 * Ожидание фактической навигации на URL (ленивые loadComponent-чанки
 * асинхронны): макротаск-хол до совпадения router.url, с таймаутом.
 */
export async function waitForUrl(
  env: AuthProfileEnv,
  url: string,
  timeoutMs = 5000,
): Promise<void> {
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    await new Promise<void>((resolve) => setTimeout(resolve, 0));
    env.harness.fixture.detectChanges();
    if (env.router.url === url) {
      return;
    }
    if (Date.now() > deadline) {
      throw new Error(
        `waitForUrl: «${url}» не открыт (текущий ${env.router.url})`,
      );
    }
  }
}

/**
 * Существующая сессия без UI-входа (аналог loginAs прошлой версии —
 * через реальную загрузку /auth/me, программируемую ответом 200):
 * кэш currentUser заполнен, guard'ы видят сессию.
 */
export async function loginAs(env: AuthProfileEnv, login: DemoLogin): Promise<void> {
  const pending = env.auth.loadMe();
  respond(env, 'GET', '/auth/me', { body: ME_FIXTURES[login] });
  await pending;
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

/**
 * Заглушки страниц-целей редиректов ролей (/works, /my-submissions) —
 * пустые данные без HTTP-вызовов; домены этих страниц — зона других батчей.
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
