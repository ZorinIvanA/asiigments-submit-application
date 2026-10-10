/**
 * Интеграционный стенд батча «works + notifications» (FR-4.3/FR-4.9,
 * сценарии TS-201..TS-228): реальные страницы works, реальный Router
 * (маршруты фичи), реальный NotificationService со всеми хостами IF-109 и
 * реальные сервисы core (LabsService/AuthService) поверх HttpClient —
 * HTTP-ядро приложения (authInterceptor: withCredentials, нормализация
 * отказов в ApiError) на программируемом HttpTestingController-бэкенде
 * (FR-026, зона без мок-слоя).
 *
 * Отличие от unit-спеков соседей: тесты проходят ЧЕРЕЗ границы компонентов
 * (страница → LabsService → HttpTestingController → состояние бэкенда и
 * NotificationService → toast/якоря); серверное состояние — детерминированный
 * сид WorksBackendStub (23 работы: семестр 1 №1–20, семестр 2 №1–3),
 * пересоздаваемый на каждый сценарий.
 *
 * Инфраструктура — только собственные двойники зоны и HttpTestingController
 * (изоляция зоны, FR-026): BreakpointObserverStub и WorksBackendStub живут
 * рядом; общее интеграционное окружение прежних батчей (файл вне зоны,
 * его потребитель удаляется задачей T-023 — ISS-002) и мок-слой
 * не используются.
 *
 *  - сессия teacher поднимается настоящим auth.login (POST /auth/login,
 *    teacher/teacher123!, ADR-107) — как в демонстрации; признак сессии —
 *    только память AuthService (FR-092);
 *  - режим уведомлений (>=768px desktop / <768px mobile) — BreakpointObserverStub;
 *  - тайминги: задержек бэкенда нет — «волна» flush() отвечает все
 *    перехваченные на данный момент запросы; сериальные цепочки страницы
 *    (например remove → getList → getSemesters) обслуживаются следующими
 *    волнами — детерминированный fakeAsync/tick без искусственных задержек.
 */
import { BreakpointObserver } from '@angular/cdk/layout';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, DebugElement } from '@angular/core';
import { ComponentFixture, TestBed, flushMicrotasks, tick } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Router, RouterOutlet, provideRouter } from '@angular/router';

import { API_BASE_URL } from '../../../core/api-base-url';
import { authInterceptor } from '../../../core/auth-interceptor';
import { AuthService } from '../../../core/services/auth.service';
import { LabsService } from '../../../core/services/labs.service';
import { WORKS_ROUTES } from '../../../features/works/works.routes';
import { HeaderNotification } from '../../../shared/notifications/header-notification';
import { NOTIFICATION_AUTO_CLOSE_MS } from '../../../shared/notifications/notification-model';
import { NotificationService } from '../../../shared/notifications/notification-service';
import { NotificationToast } from '../../../shared/notifications/notification-toast';
import { BreakpointObserverStub } from './breakpoint-observer-stub';
import { WorksBackendSnapshot, WorksBackendStub } from './works-backend-stub';

/**
 * Хост-оболочка — сокращённый аналог app-root + app-shell (IF-109/C-109):
 * глобальный хост десктоп-тостов объявлен один раз, мобильный хост «под
 * шапкой» стоит сразу после шапки и перед контентом, страницы — в outlet.
 */
@Component({
  selector: 'app-works-integration-host',
  template: `
    <app-notification-toast />
    <header class="integration-topbar">Сдача лабораторных</header>
    <app-header-notification />
    <router-outlet />
  `,
  imports: [RouterOutlet, NotificationToast, HeaderNotification],
})
export class WorksIntegrationHost {}

/**
 * Интеграционный стенд. Все методы с задержками (loginTeacher/navigate/
 * flush/uiSettle) обязаны вызываться внутри fakeAsync — они используют tick.
 */
export class WorksIntegrationHarness {
  readonly breakpoints = new BreakpointObserverStub();

  /** Программируемый HTTP-бэкенд зоны: состояние REST-домена works. */
  readonly backend = new WorksBackendStub();

  /** HttpTestingController — контролируемый стоп сценария (verify). */
  readonly httpMock: HttpTestingController;

  private readonly apiBase: string;

  private fixture!: ComponentFixture<WorksIntegrationHost>;
  private nativeElement!: HTMLElement;

  readonly labs: LabsService;
  readonly auth: AuthService;
  readonly notifications: NotificationService;
  readonly router: Router;

  private constructor() {
    TestBed.configureTestingModule({
      imports: [WorksIntegrationHost],
      providers: [
        // Производственная цепочка HTTP: interceptors + тестовый бэкенд
        // (паттерн spec-файлов сервисов core).
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: BreakpointObserver, useValue: this.breakpoints },
        // Маршруты фичи works без guards: авторизация сессией через
        // auth.login, guards — зона app.routes (вне scope батча).
        provideRouter(WORKS_ROUTES),
        provideNoopAnimations(),
      ],
    });
    this.apiBase = TestBed.inject(API_BASE_URL);
    this.httpMock = TestBed.inject(HttpTestingController);
    this.labs = TestBed.inject(LabsService);
    this.auth = TestBed.inject(AuthService);
    this.notifications = TestBed.inject(NotificationService);
    this.router = TestBed.inject(Router);
  }

  /** Создаёт стенд с чистым состоянием бэкенда (beforeEach). */
  static setup(): WorksIntegrationHarness {
    return new WorksIntegrationHarness();
  }

  /** Сброс после спеки (afterEach): очистка хранилищ браузера. */
  static restoreSeed(): void {
    localStorage.clear();
    sessionStorage.clear();
  }

  /** Монтирует оболочку в document (позиционирование и диалоги — в дереве). */
  attach(): void {
    this.fixture = TestBed.createComponent(WorksIntegrationHost);
    this.nativeElement = this.fixture.nativeElement as HTMLElement;
    document.body.appendChild(this.nativeElement);
    this.fixture.detectChanges();
  }

  /**
   * Снимает оболочку с документа и уничтожает (afterEach). Хвостовые
   * запросы серийных цепочек получают ответы, затем контроль: ни один
   * перехваченный запрос не остался без ответа (verify).
   */
  detach(): void {
    if (this.fixture) {
      document.body.removeChild(this.nativeElement);
      this.fixture.destroy();
    }
    this.respondPendingRequests();
    this.httpMock.verify();
  }

  /** Корневой элемент оболочки. */
  get native(): HTMLElement {
    return this.nativeElement;
  }

  /** DebugElement-корень — для доступа к директивам (например Tooltip). */
  get debugElement(): DebugElement {
    return this.fixture.debugElement;
  }

  /** Вход teacher/teacher123! настоящим auth.login (одна волна ответов). */
  loginTeacher(): void {
    void this.auth.login({ login: 'teacher', password: 'teacher123!' });
    this.flush();
  }

  /**
   * Навигация средствами реального Router. Дренаж: (1) микрозадачи роутера
   * монтируют страницу; (2) CD назначает required-входы смонтированным
   * компонентам (защита от NG0950 при последующей смене маршрута);
   * (3) волна ответов завершает стартовые загрузки страницы
   * (getList/getSemesters/getById); (4) финальный сеттлмент.
   */
  navigate(url: string): void {
    void this.router.navigateByUrl(url).catch(() => undefined);
    tick();
    this.detectChanges(1);
    this.flush();
  }

  /**
   * Завершает `waves` волн ответов бэкенда: волна отвечает все
   * перехваченные к этому моменту запросы (вызовы, стартовавшие
   * одновременно, завершаются одной волной); запросы сериальных цепочек
   * (например перезагрузка после remove) — следующими волнами. CD между
   * волнами назначает входы страницам, смонтированным навигацией внутри
   * волны (редиректы форм). В конце — сеттлмент.
   */
  flush(waves = 1): void {
    for (let wave = 0; wave < waves; wave += 1) {
      this.respondPendingRequests();
      tick();
      this.detectChanges(1);
    }
    tick();
    this.settle();
  }

  /** Дренаж микрозадач + CD — открытие/закрытие диалогов и оверлеев. */
  uiSettle(): void {
    tick();
    this.settle();
  }

  /**
   * Завершение жизненного цикла диалога PrimeNG: скрытие планирует
   * отложенный (~10 мс) таймер из CD, следующей за кликом. Порядок обязателен:
   * CD (старт скрытия) → короткое продвижение времени (выгорание таймера,
   * безопасно меньше сериальной волны) → микрозадачи → CD.
   */
  dialogSettle(): void {
    this.detectChanges(1);
    tick(16);
    flushMicrotasks();
    this.detectChanges(2);
  }

  /**
   * Сеттлмент состояния по закону «CD → микрозадачи → CD»: первый CD
   * применяет готовое состояние и запускает отложенные обновления
   * (CVA writeValue компонентов PrimeNG → сигналы), flushMicrotasks их
   * созревает, второй CD рендерит (хост-привязки data-p-checked, метка
   * селекта, строки таблицы).
   */
  private settle(): void {
    this.detectChanges(1);
    flushMicrotasks();
    this.detectChanges(2);
  }

  /** Прогон(ы) change detection фикстуры. */
  private detectChanges(passes = 1): void {
    for (let pass = 0; pass < passes; pass += 1) {
      this.fixture.detectChanges();
    }
  }

  /**
   * Финализация сценария: отвечает остаточные запросы сериальных цепочек
   * (например getSemesters после перезагрузки списка), выгорает очередь
   * таймеров fakeAsync-зоны — автозакрытие уведомлений
   * (NOTIFICATION_AUTO_CLOSE_MS) и life-таймеры PrimeNG Toast — и отвечает
   * запросы, стартовавшие при выгорании. Зона обязана завершиться с пустой
   * очередью, иначе zone.js бросает «N timer(s) still in the queue».
   * Вызывается в конце спеков, порождающих уведомления, ПОСЛЕ основных
   * ассертов.
   */
  finalize(): void {
    this.respondPendingRequests();
    tick(NOTIFICATION_AUTO_CLOSE_MS);
    this.respondPendingRequests();
    this.fixture.detectChanges();
  }

  /**
   * Число перехваченных, но ещё не отвеченных HTTP-запросов. Осторожно:
   * match() у HttpTestingController снимает запросы с контроля (verify),
   * поэтому метод предназначен только для ассертов «ожидается 0 запросов»
   * (например read-only чекбоксы не порождают HTTP).
   */
  pendingHttpRequests(): number {
    return this.httpMock.match(() => true).length;
  }

  /** Отвечает все перехваченные запросы по состоянию бэкенда. */
  private respondPendingRequests(): void {
    for (const request of this.httpMock.match(() => true)) {
      const response = this.backend.handle(request.request, this.apiBase);
      request.flush(response.body, {
        status: response.status,
        statusText: response.statusText,
      });
    }
  }

  // ---------- состояние бэкенда (чтение сценариями) ----------

  /** Снимок серверного состояния бэкенда (пользователи/работы/сдачи). */
  readDb(): WorksBackendSnapshot {
    return this.backend.snapshot();
  }

  /** uuid сид-работы по паре (семестр, номер). */
  labId(semester: number, number: number): string {
    return this.backend.labId(semester, number);
  }

  /** Удаление работы «из другой вкладки» — мимо UI, прямая мутация. */
  removeLabDirect(id: string): void {
    this.backend.removeLabDirect(id);
  }

  // ---------- DOM: список /works ----------

  /** Строки таблицы списка (включая строку пустого состояния). */
  rows(): HTMLTableRowElement[] {
    return Array.from(this.nativeElement.querySelectorAll<HTMLTableRowElement>('tbody tr'));
  }

  /** Текстовые ячейки строки (обрезанные). */
  rowCells(row: HTMLTableRowElement): string[] {
    return Array.from(row.querySelectorAll('td')).map((td) => (td.textContent ?? '').trim());
  }

  /** Строка списка по номеру работы (первая ячейка). */
  rowByNumber(number: number): HTMLTableRowElement {
    const row = this.rows().find((candidate) => this.rowCells(candidate)[0] === String(number));
    if (row === undefined) {
      throw new Error(`works-integration-harness: строка работы №${number} не найдена в таблице`);
    }
    return row;
  }

  /**
   * Заголовки колонок таблицы — только текст подписи, без индикатора
   * сортировки ▲/▼ (span.works-page__sort внутри th).
   */
  headerTitles(): string[] {
    return Array.from(this.nativeElement.querySelectorAll('th')).map(
      (th) => (th.firstChild?.textContent ?? '').trim(),
    );
  }

  /** Заголовок-кнопка сортировки по подписи колонки. */
  sortHeader(title: string): HTMLTableCellElement {
    const header = Array.from(this.nativeElement.querySelectorAll('th')).find((th) =>
      (th.firstChild?.textContent ?? '').trim().startsWith(title),
    );
    if (header === undefined) {
      throw new Error(`works-integration-harness: заголовок «${title}» не найден`);
    }
    return header;
  }

  /** Подпись пагинации «Показать записи с X по Y из Z». */
  reportText(): string {
    return this.nativeElement.querySelector('.works-page__report')?.textContent ?? '';
  }

  /** Открывает панель селектора семестра (works и lab-form — один p-select). */
  openSemesterSelect(): void {
    const select = this.nativeElement.querySelector('p-select');
    if (select === null) {
      throw new Error('works-integration-harness: селектор семестра (p-select) не найден');
    }
    (select as HTMLElement).click();
    this.uiSettle();
  }

  /** Подписи открытых опций селектора (по документу — оверлей PrimeNG). */
  semesterOptionLabels(): string[] {
    return Array.from(document.querySelectorAll('.p-select-option')).map((option) =>
      (option.textContent ?? '').trim(),
    );
  }

  /** Выбор опции селектора по подписи (панель должна быть открыта). */
  chooseSemesterOption(label: string): void {
    const option = Array.from(document.querySelectorAll<HTMLElement>('.p-select-option')).find(
      (candidate) => (candidate.textContent ?? '').trim() === label,
    );
    if (option === undefined) {
      throw new Error(`works-integration-harness: открытая опция «${label}» не найдена`);
    }
    option.click();
    this.uiSettle();
  }

  /** Закрывает панель селектора без выбора (например после осмотра опций). */
  closeSemesterSelect(): void {
    (this.nativeElement.querySelector('p-select') as HTMLElement | null)?.click();
    this.uiSettle();
  }

  /** Текущая подпись закрытого селектора семестра. */
  semesterSelectLabel(): string {
    return this.nativeElement.querySelector('.p-select-label')?.textContent?.trim() ?? '';
  }

  /** Переход пагинатором на страницу (1-based). */
  gotoPage(page: number): void {
    const links = this.nativeElement.querySelectorAll<HTMLButtonElement>('.p-paginator-page');
    if (links.length < page) {
      throw new Error(`works-integration-harness: страницы ${page} нет в пагинаторе (кнопок ${links.length})`);
    }
    links[page - 1]!.click();
    this.uiSettle();
  }

  /** Открывает диалог удаления для строки (клик 🗑). */
  openRemoveDialog(row: HTMLTableRowElement): void {
    const trash = row.querySelector<HTMLButtonElement>('button[aria-label="Удалить"]');
    if (trash === null) {
      throw new Error('works-integration-harness: кнопка «Удалить» не найдена в строке');
    }
    trash.click();
    this.uiSettle();
  }

  /** Текст открытого диалога подтверждения («Yes»/«No» — primeng-ru, ADR-111). */
  dialogMessage(): string {
    return document.body.querySelector('.p-confirmdialog-message')?.textContent?.trim() ?? '';
  }

  /** Кнопка диалога: accept — «Yes», reject — «No». */
  dialogButton(kind: 'accept' | 'reject'): HTMLButtonElement {
    const button = document.body.querySelector<HTMLButtonElement>(
      `button.p-confirmdialog-${kind}-button`,
    );
    if (button === null) {
      throw new Error(`works-integration-harness: кнопка «${kind}» диалога не найдена`);
    }
    return button;
  }

  // ---------- DOM: форма лабораторной ----------

  /** Имитация ввода пользователя (событие input для форм Angular). */
  setInput(selector: string, value: string): void {
    const input = this.nativeElement.querySelector<HTMLInputElement>(selector);
    if (input === null) {
      throw new Error(`works-integration-harness: поле ${selector} не найдено`);
    }
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true }));
  }

  /** Значение поля. */
  inputValue(selector: string): string {
    return this.nativeElement.querySelector<HTMLInputElement>(selector)?.value ?? '';
  }

  /** Текст ошибки поля формы (подсветка): null — поле не подсвечено. */
  fieldErrorText(inputId: string): string | null {
    const field = this.nativeElement
      .querySelector(`#${inputId}`)
      ?.closest('.lab-form__field');
    return field?.querySelector('.lab-form__error')?.textContent?.trim() ?? null;
  }

  /** Кнопка формы по подписи («Сохранить», «Назад»). */
  buttonByLabel(label: string): HTMLButtonElement {
    const buttons = Array.from(
      this.nativeElement.querySelectorAll<HTMLButtonElement>('button'),
    );
    const button = buttons.find((candidate) => (candidate.textContent ?? '').trim() === label);
    if (button === undefined) {
      throw new Error(`works-integration-harness: кнопка «${label}» не найдена`);
    }
    return button;
  }

  /** Кнопка-цепочка «добавить или изменить ссылку». */
  chainButton(): HTMLButtonElement {
    const button = this.nativeElement.querySelector<HTMLButtonElement>(
      'button[aria-label="Ссылка на задание"]',
    );
    if (button === null) {
      throw new Error('works-integration-harness: кнопка-цепочка не найдена');
    }
    return button;
  }

  // ---------- DOM: уведомления (IF-109) ----------

  /** Тексты desktop-тостов (хост app-notification-toast, >=768px). */
  toastTexts(): string[] {
    return Array.from(document.querySelectorAll('.p-toast-summary')).map((element) =>
      (element.textContent ?? '').trim(),
    );
  }

  /** Текст последнего тоста (тосты копятся до life 5000 мс PrimeNG). */
  lastToastText(): string | null {
    const summaries = document.querySelectorAll('.p-toast-summary');
    return summaries.length === 0
      ? null
      : (summaries[summaries.length - 1]!.textContent ?? '').trim();
  }

  /** Тексты мобильных баннеров «под шапкой» (хост app-header-notification). */
  headerBannerTexts(): string[] {
    return Array.from(
      document.querySelectorAll('app-header-notification .app-notification-banner__text'),
    ).map((element) => (element.textContent ?? '').trim());
  }

  /** Тексты мобильных баннеров под формами (якоря app-notification-anchor). */
  formAnchorBannerTexts(): string[] {
    return Array.from(
      document.querySelectorAll('app-notification-anchor .app-notification-banner__text'),
    ).map((element) => (element.textContent ?? '').trim());
  }
}
