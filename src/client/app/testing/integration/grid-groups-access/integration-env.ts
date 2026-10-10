/**
 * Тестовая среда интеграционных спеков батча 3 «Группы · Доступ · Ведомость ·
 * Сдачи студента · Оболочка» (переработка на HttpTestingController, FR-026):
 * реальные страницы фич, реальные core-сервисы и реальное HTTP-ядро
 * приложения (HttpClient + authInterceptor: withCredentials, дедуплицируемый
 * refresh, нормализация отказов в ApiError) над программируемым
 * HttpTestingController-бэкендом — GridBackendStub (детерминированный сид
 * зоны, состояние REST-доменов в памяти). Прежний демонстрационный мок-слой
 * зоной не используется (удалён по FR-026); признак сессии — ТОЛЬКО память
 * AuthService (FR-092).
 *
 * Сессия:
 *  - serverSession(login)  — серверная личность бэкенда без HTTP (уровень
 *    компонентных спеков: роли и /me/submissions определяются по ней);
 *  - loginAs(login)        — то же + настоящая загрузка профиля (GET /auth/me
 *    → кэш AuthService): fakeAsync-спеки (uses tick);
 *  - await loginAsync(login) — то же для RouterTestingHarness-спеков с
 *    реальными таймерами.
 *
 * Тайминги: задержек бэкенда нет — «волна» settle() отвечает все
 * перехваченные на данный момент запросы (сериальные цепочки — следующими
 * волнами) и продвигает виртуальное время (микрозадачи + таймеры дебаунса
 * 300 мс и сокрытия диалогов PrimeNG). Для асинхронных маршрутизаторных
 * спеков — pump()/waitFor() с реальными макротасками.
 *
 *  - settle(env, fixture, waves) — волны ответов + CD (fakeAsync);
 *  - flushNgModel / waitConfirmClosed / drainNotifications — точечный дренаж;
 *  - qsIn/qsAllIn/buttonByText/openSelect/pickOption/selectLabelOf/
 *    selectModelValueOf — DOM-помощники прежней зоны (оверлеи PrimeNG ищутся
 *    и в документе).
 */
import { tick, ComponentFixture, TestBed } from '@angular/core/testing';
import { BreakpointObserver } from '@angular/cdk/layout';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { EnvironmentProviders, Provider, Type } from '@angular/core';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { By } from '@angular/platform-browser';
import { Select } from 'primeng/select';

import { API_BASE_URL } from '../../../core/api-base-url';
import { authInterceptor } from '../../../core/auth-interceptor';
import { routes } from '../../../core/config/app.routes';
import { AuthService } from '../../../core/services/auth.service';
import { NotificationService } from '../../../shared/notifications/notification-service';
import {
  NOTIFICATION_AUTO_CLOSE_MS,
} from '../../../shared/notifications/notification-model';
import { MockBreakpointObserver } from '../../../../testing/mock-breakpoint-observer';
import { GridBackendSeed, GridBackendStub } from './grid-backend-stub';

/** Опции сборки окружения. */
export interface GridEnvOptions {
  /** Дополнительные провайдеры (provideRouter и т.п.). */
  readonly providers?: Array<Provider | EnvironmentProviders>;
  /** Сид бэкенда; по умолчанию — интеграционный сид зоны. */
  readonly seed?: GridBackendSeed;
}

/** Собранное окружение одного интеграционного сценария. */
export class GridAccessEnv {
  /** Программируемый HTTP-бэкенд зоны: состояние REST-доменов. */
  readonly backend: GridBackendStub;

  /** HttpTestingController — контролируемый стоп сценария (verify). */
  readonly httpMock: HttpTestingController;

  /** Базовый префикс API (TestBed.inject(API_BASE_URL), конвенция ADR-014). */
  readonly apiBase: string;

  readonly auth: AuthService;
  readonly notifications: NotificationService;

  /** Маршрутизаторное окружение (RouterTestingHarness), если создано. */
  routingHarness: RouterTestingHarness | null = null;

  private constructor(
    readonly breakpoints: MockBreakpointObserver,
    seed?: GridBackendSeed,
  ) {
    this.backend = new GridBackendStub(seed);
    this.httpMock = TestBed.inject(HttpTestingController);
    this.apiBase = TestBed.inject(API_BASE_URL);
    this.auth = TestBed.inject(AuthService);
    this.notifications = TestBed.inject(NotificationService);
  }

  /**
   * Собирает окружение (beforeEach): свежий инжектор, реальное HTTP-ядро
   * (интерцептор + тестовый бэкенд), заглушка брейкпоинтов, noop-анимации.
   */
  static setup(opts: GridEnvOptions = {}): GridAccessEnv {
    localStorage.clear();
    sessionStorage.clear();
    const breakpoints = new MockBreakpointObserver();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: BreakpointObserver, useValue: breakpoints },
        provideNoopAnimations(),
        ...(opts.providers ?? []),
      ],
    });
    return new GridAccessEnv(breakpoints, opts.seed);
  }

  /** Монтирует компонент страницы и выполняет первый проход CD. */
  mount<T>(component: Type<T>): ComponentFixture<T> {
    const fixture = TestBed.createComponent(component);
    fixture.detectChanges();
    return fixture;
  }

  /** Инъекция сервиса из TestBed-инжектора (сворачивание шума в спеках). */
  inject<T>(token: Type<T>): T {
    return TestBed.inject(token);
  }

  /** Продвижение виртуального времени (fakeAsync) — шов для дебаунса. */
  tick(ms: number): void {
    tick(ms);
  }

  // ---------- сессия ----------

  /** Серверная личность бэкенда без HTTP (уровень компонентных спеков). */
  serverSession(login: string | null): void {
    this.backend.sessionLogin = login;
  }

  /**
   * Сессия с настоящей загрузкой профиля (GET /auth/me → кэш AuthService):
   * fakeAsync-спеки — использует tick().
   */
  loginAs(login: string): void {
    this.serverSession(login);
    const pending = this.auth.loadMe();
    this.respondAll();
    tick(0);
    void pending;
  }

  /** То же для асинхронных RouterTestingHarness-спеков (реальные таймеры). */
  async loginAsync(login: string): Promise<void> {
    this.serverSession(login);
    const pending = this.auth.loadMe();
    await this.pump();
    await pending;
  }

  // ---------- волны ответов ----------

  /** Отвечает все перехваченные к текущему моменту запросы по состоянию бэкенда. */
  respondAll(): void {
    for (const captured of this.httpMock.match(() => true)) {
      const response = this.backend.handle(captured.request, this.apiBase);
      captured.flush(response.body, {
        status: response.status,
        statusText: response.statusText,
      });
    }
  }

  /**
   * Волны ответов (fakeAsync): каждая волна — продвижение виртуального
   * времени (микрозадачи + таймеры: дебаунс 300 мс, сокрытие диалогов
   * PrimeNG), ответ всех появившихся запросов, дренаж промисов и CD.
   * Сериальные цепочки (ответ → новый запрос) обслуживаются следующей
   * волной. В конце — три раунда «микрозадачи + CD»: внутренние состояния
   * p-select/p-datepicker ([ngModel] → writeValue) доезжают с запаздыванием.
   */
  settle(fixture: ComponentFixture<unknown>, waves = 1): void {
    for (let wave = 0; wave < waves; wave += 1) {
      tick(600);
      this.respondAll();
      tick(0);
      fixture.detectChanges();
    }
    for (let round = 0; round < 3; round += 1) {
      tick(0);
      fixture.detectChanges();
    }
  }

  /**
   * Волны ответов для ПРЯМЫХ сервисных вызовов спека (без фикстуры страницы,
   * в том числе до монтирования или после destroy): каждая волна отвечает
   * появившиеся запросы и дренирует микрозадачи промисов (fakeAsync).
   */
  settleRequests(waves = 1): void {
    for (let wave = 0; wave < waves; wave += 1) {
      this.respondAll();
      tick(0);
    }
  }

  /** Один проход микрозадач NgModel + CD (без волн и таймингов). */
  flushNgModel(fixture: ComponentFixture<unknown>): void {
    tick(0);
    fixture.detectChanges();
  }

  /** Гасит таймер автозакрытия уведомления (5000 мс, IF-009) — для fakeAsync. */
  drainNotifications(): void {
    tick(NOTIFICATION_AUTO_CLOSE_MS + 1);
  }

  /**
   * Дожидается закрытия confirm-оверлея (CR-009): оверлей анимируется и
   * исчезает через несколько циклов CD — одного detectChanges мало.
   */
  waitConfirmClosed(
    fixture: ComponentFixture<unknown>,
    selector = '.p-confirmdialog',
  ): void {
    for (let attempt = 0; attempt < 20; attempt++) {
      tick(50);
      fixture.detectChanges();
      if (fixture.nativeElement.querySelector(selector) === null) {
        return;
      }
    }
    throw new Error('integration-env: confirm-оверлей не закрылся');
  }

  // ---------- маршрутизаторные спеки (реальные таймеры) ----------

  /** Создаёт RouterTestingHarness на полном дереве маршрутов приложения. */
  async attachRoutingHarness(initialUrl?: string): Promise<RouterTestingHarness> {
    this.routingHarness = await RouterTestingHarness.create(initialUrl);
    return this.routingHarness;
  }

  get routingFixture(): ComponentFixture<unknown> | null {
    return this.routingHarness?.fixture ?? null;
  }

  get router(): Router {
    return TestBed.inject(Router);
  }

  /**
   * Дренаж асинхронного сценария: волна ответов + макротаск-хопы (промисы
   * навигаций/сервисов) + CD маршрутизаторной фикстуры.
   */
  async pump(hops = 4): Promise<void> {
    for (let hop = 0; hop < hops; hop += 1) {
      this.respondAll();
      await new Promise<void>((resolve) => setTimeout(resolve, 0));
      this.routingFixture?.detectChanges();
    }
    this.respondAll();
  }

  /**
   * Поллит условие с реальными таймерами (навигации после кликов, popstate).
   * Между попытками — волна ответов: появившиеся запросы страниц честно
   * обслуживаются бэкендом.
   */
  async waitFor(condition: () => boolean, what: string, timeoutMs = 15000): Promise<void> {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
      await this.pump(1);
      if (condition()) {
        return;
      }
    }
    throw new Error(`integration-env: не дождались — ${what}`);
  }

  // ---------- стоп сценария ----------

  /**
   * Завершение сценария (afterEach): гасим баннер/таймер уведомлений,
   * отвечаем остаточные запросы (хвосты сериальных цепочек уничтоженных
   * страниц) и проверяем, что каждый выполненный вызов был обслужен
   * бэкендом (verify), затем очищаем storage.
   */
  stop(): void {
    this.notifications.dismissMobile();
    this.respondAll();
    this.httpMock.verify();
    localStorage.clear();
    sessionStorage.clear();
  }
}

// ---------- DOM-помощники (прошлая зона батча 3) ----------

/** Поиск по фикстуре с фолбэком в документ — оверлеи PrimeNG вне фикстуры. */
export function qsIn(fixture: ComponentFixture<unknown>, selector: string): HTMLElement | null {
  const root = fixture.nativeElement as HTMLElement;
  return root.querySelector(selector) ?? (document.querySelector(selector) as HTMLElement | null);
}

/** То же для querySelectorAll. */
export function qsAllIn(fixture: ComponentFixture<unknown>, selector: string): HTMLElement[] {
  const root = fixture.nativeElement as HTMLElement;
  const inFixture = Array.from(root.querySelectorAll<HTMLElement>(selector));
  return inFixture.length > 0
    ? inFixture
    : Array.from(document.querySelectorAll<HTMLElement>(selector));
}

/** Кнопка по точному тексту — в фикстуре или в документе (оверлеи). */
export function buttonByText(fixture: ComponentFixture<unknown>, label: string): HTMLButtonElement {
  const found = qsAllIn(fixture, 'button').find(
    (candidate) => candidate.textContent?.trim() === label,
  );
  if (found === undefined) {
    throw new Error(`integration-env: не найдена кнопка «${label}»`);
  }
  return found as HTMLButtonElement;
}

/** Открывает оверлей p-select (клик, как пользователь). */
export function openSelect(fixture: ComponentFixture<unknown>, select: Element): void {
  (select as HTMLElement).click();
  fixture.detectChanges();
}

/** Выбирает опцию с подписью label в открытом оверлее p-select. */
export function pickOption(fixture: ComponentFixture<unknown>, label: string): void {
  const option = qsAllIn(fixture, 'li.p-select-option').find(
    (candidate) => candidate.textContent?.trim() === label,
  );
  if (option === undefined) {
    throw new Error(`integration-env: в открытом селекторе нет опции «${label}»`);
  }
  option.click();
  fixture.detectChanges();
}

/** Подпись выбранной опции p-select. */
export function selectLabelOf(select: Element): string {
  return select.querySelector('.p-select-label')?.textContent?.trim() ?? '';
}

/** Значение модели p-select в строке таблицы (аменда 6: groupId из DTO). */
export function selectModelValueOf(
  fixture: ComponentFixture<unknown>,
  row: HTMLElement,
): unknown {
  const rowDe = fixture.debugElement
    .queryAll(By.css('tbody tr'))
    .find((candidate) => candidate.nativeElement === row);
  if (rowDe === undefined) {
    throw new Error('integration-env: строка не найдена в debugElement');
  }
  const selectDe = rowDe.query(By.directive(Select));
  if (selectDe === null) {
    throw new Error('integration-env: в строке нет p-select');
  }
  return (selectDe.componentInstance as unknown as { modelValue(): unknown }).modelValue();
}
