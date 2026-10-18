/**
 * Юнит-тесты RecoveryPage (T-111, SCR-003, AC step1-success):
 *  - тексты макета дословно: «Восстановление пароля», «Шаг 1 из 3»,
 *    «Email», «Отмена», «Отправить код»;
 *  - клиентская валидация email (тексты словаря ERROR_TEXTS: «Заполните
 *    поле», «Введите корректный email», граница 254 символа) без запроса
 *    к бэкенду;
 *  - успех и незарегистрированный email одинаково ведут на шаг 2
 *    (запрос всегда 200, §9): email в RecoveryFlowStore (IF-102),
 *    навигация /recovery/code;
 *  - 429 — баннер «Слишком много попыток. Повторите позже» (desktop —
 *    тост, mobile — inline-баннер под формой с якорем 'recovery-form',
 *    IF-109), редиректа нет;
 *  - «Отмена» очищает store (IF-102) и возвращает на /login;
 *  - трим значения перед DTO; блокировка кнопок на время запроса.
 *
 * Транспортная граница — программируемый HttpTestingController (FR-026):
 * страница, AuthService и RecoveryFlowStore реальные вместе с production
 * цепочкой HttpClient + authInterceptor (нормализация отказов в ApiError),
 * поэтому гарантия IF-101/IF-102 «email запоминается после запроса кода»
 * проверяется вместе со страницей. Ожидаемые URL строятся из
 * TestBed.inject(API_BASE_URL) (конвенция ADR-014). Режим показа
 * уведомлений переключается MockBreakpointObserver (FR-022, UI-двойник —
 * не мок-слой).
 */
import { BreakpointObserver } from '@angular/cdk/layout';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { authInterceptor } from '../../../../core/auth-interceptor';
import { API_BASE_URL } from '../../../../core/api-base-url';
import { RecoveryFlowStore } from '../../../../core/services/recovery-flow-store';
import { NotificationService } from '../../../../shared/notifications/notification-service';
import { MockBreakpointObserver } from '../../../../../testing/mock-breakpoint-observer';
import { RecoveryPage } from './recovery-page';

describe('RecoveryPage — шаг 1 восстановления пароля (SCR-003, T-111)', () => {
  const EMAIL = 'student01@example.com';
  const TOO_MANY = 'Слишком много попыток. Повторите позже';

  let breakpoints: MockBreakpointObserver;
  let fixture: ComponentFixture<RecoveryPage>;
  let httpMock: HttpTestingController;
  let apiBase: string;
  let flow: RecoveryFlowStore;
  let notifications: NotificationService;
  let router: Router;

  beforeEach(async () => {
    sessionStorage.clear();
    localStorage.clear();
    breakpoints = new MockBreakpointObserver();
    await TestBed.configureTestingModule({
      imports: [RecoveryPage],
      providers: [
        { provide: BreakpointObserver, useValue: breakpoints },
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    apiBase = TestBed.inject(API_BASE_URL);
    flow = TestBed.inject(RecoveryFlowStore);
    notifications = TestBed.inject(NotificationService);
    router = TestBed.inject(Router);
    spyOn(router, 'navigateByUrl');

    fixture = TestBed.createComponent(RecoveryPage);
    fixture.detectChanges();
  });

  afterEach(() => {
    // Ни одного незакрытого/лишнего запроса (валидационные ветки — ни одного).
    httpMock.verify();
    notifications.dismissMobile();
    sessionStorage.clear();
    localStorage.clear();
  });

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function emailInput(): HTMLInputElement {
    return root().querySelector<HTMLInputElement>('#email-input')!;
  }

  function submitButton(): HTMLButtonElement {
    return root().querySelector<HTMLButtonElement>('button[type="submit"]')!;
  }

  function cancelButton(): HTMLButtonElement {
    return root().querySelector<HTMLButtonElement>('button[type="button"]')!;
  }

  function fieldError(): string | null {
    return root().querySelector('.field__error')?.textContent?.trim() ?? null;
  }

  function fill(email: string): void {
    emailInput().value = email;
    emailInput().dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function submit(): void {
    submitButton().click();
    fixture.detectChanges();
  }

  /**
   * Сабмит заполненной формы с программируемым ответом бэкенда:
   * POST /auth/recovery/request перехватывается и завершается заданным
   * статусом/телом, затем микрозадачи (промисы сервиса и страницы)
   * дренируются макротаском и выполняется CD.
   */
  async function submitAndFlush(status: number, body: unknown = null): Promise<void> {
    fill(EMAIL);
    submit();
    const request = httpMock.expectOne(`${apiBase}/auth/recovery/request`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ email: emailInput().value.trim() });
    request.flush(body as object | null, {
      status,
      statusText: status === 200 ? 'OK' : 'Error',
    });
    await new Promise<void>((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }

  it('тексты макета дословно: заголовок, «Шаг 1 из 3», «Email», «Отмена», «Отправить код»', () => {
    expect(root().querySelector('h1')!.textContent!.trim()).toBe('Восстановление пароля');
    expect(root().querySelector('.auth-card__step')!.textContent!.trim()).toBe('Шаг 1 из 3');
    expect(root().querySelector('label')!.textContent!.trim()).toBe('Email');
    expect(submitButton().textContent).toContain('Отправить код');
    expect(cancelButton().textContent).toContain('Отмена');
  });

  it('пустой email: «Заполните поле», баннер валидации, запрос к бэкенду не выполняется', async () => {
    submit();
    await new Promise<void>((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    expect(fieldError()).toBe('Заполните поле');
    expect(notifications.desktopMessage()!.text).toBe('Данные заполнены неверно');
    expect(httpMock.match(`${apiBase}/auth/recovery/request`).length)
      .withContext('запрос не отправлен')
      .toBe(0);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('некорректный формат («abc»): «Введите корректный email», запроса нет', async () => {
    fill('abc');
    submit();
    await new Promise<void>((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    expect(fieldError()).toBe('Введите корректный email');
    expect(httpMock.match(`${apiBase}/auth/recovery/request`).length).toBe(0);
  });

  it('граница длины email: 255 символов — ошибка словаря, 254 — валидно', () => {
    fill('a'.repeat(250) + '@b.ru'); // 255 символов
    submit();
    fixture.detectChanges();
    expect(fieldError()).toBe('Email — не более 254 символов');

    fill('a'.repeat(249) + '@b.ru'); // ровно 254 символа
    fixture.detectChanges();
    expect(fieldError()).toBeNull();
  });

  it('AC step1-success: успех и незарегистрированный email одинаково — email в store, переход /recovery/code', async () => {
    // Оба исхода дают один и тот же ответ 200 (§9): поведение страницы
    // идентично; различие «существует ли email» — зона бэкенда (домен Auth API).
    await submitAndFlush(200);

    expect(flow.email()).toBe(EMAIL);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/recovery/code');
  });

  it('значение триммится перед DTO (IF-010)', async () => {
    fill('  a@b.ru  ');
    submit();
    const request = httpMock.expectOne(`${apiBase}/auth/recovery/request`);
    expect(request.request.body).toEqual({ email: 'a@b.ru' });
    request.flush(null, { status: 200, statusText: 'OK' });
    await new Promise<void>((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    expect(router.navigateByUrl).toHaveBeenCalledWith('/recovery/code');
  });

  it('AC 429: баннер «Слишком много попыток. Повторите позже», экран остаётся, редиректа нет', async () => {
    await submitAndFlush(429, { message: TOO_MANY });

    expect(notifications.desktopMessage()!.text).toBe(TOO_MANY);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
    expect(root().querySelector('h1')!.textContent).toContain('Восстановление пароля');
  });

  it('429 на мобильной ширине: inline-баннер под формой с якорем recovery-form (IF-109)', async () => {
    breakpoints.simulate(true);
    await submitAndFlush(429, { message: TOO_MANY });

    const message = notifications.mobileMessage();
    expect(message).not.toBeNull();
    expect(message!.text).toBe(TOO_MANY);
    expect(message!.formId).toBe('recovery-form');

    const form = root().querySelector('form')!;
    const anchor = root().querySelector('app-notification-anchor')!;
    expect(form.contains(anchor)).withContext('якорь смонтирован под формой').toBeTrue();
    const banner = anchor.querySelector('app-notification-banner');
    expect(banner).not.toBeNull();
    expect(banner!.textContent).toContain(TOO_MANY);
  });

  it('«Отмена» очищает поток восстановления (IF-102) и возвращает на /login', async () => {
    flow.setEmail(EMAIL);
    cancelButton().click();
    await new Promise<void>((resolve) => setTimeout(resolve, 0));

    expect(flow.email()).toBeNull();
    expect(flow.hasEmail()).toBeFalse();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/login');
  });

  it('на время запроса кнопки заблокированы, повторный сабмит игнорируется (FR-025)', async () => {
    fill(EMAIL);
    submit();
    fixture.detectChanges();

    // Ответ ещё не программируется: запрос «висит» — состояние busy.
    expect(submitButton().disabled).withContext('сабмит заблокирован').toBeTrue();
    expect(submitButton().className)
      .withContext('loading-класс p-button')
      .toContain('p-button-loading');
    expect(cancelButton().disabled).withContext('отмена заблокирована').toBeTrue();

    submit(); // повторный клик — игнорируется
    // match() ПОТРЕБЛЯЕТ найденные запросы (удаляет из открытых), поэтому
    // «ровно один» проверяется здесь и ответ уходит через полученный запрос.
    const pending = httpMock.match(`${apiBase}/auth/recovery/request`);
    expect(pending.length).withContext('ровно один запрос в полёте').toBe(1);

    pending[0].flush(null, { status: 200, statusText: 'OK' });
    await new Promise<void>((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/recovery/code');
  });
});
