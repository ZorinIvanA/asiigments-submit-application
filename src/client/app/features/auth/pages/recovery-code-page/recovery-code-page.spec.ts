/**
 * Юнит-тесты RecoveryCodePage (T-111, SCR-004):
 *  - тексты макета дословно: «Восстановление пароля», «Шаг 2 из 3»,
 *    «Код восстановления», подсказки, «Отмена»/«Ввести код»,
 *    «Переотправить код»;
 *  - валидатор кода code6 (пусто / «12a456» / «1234567» — тексты словаря,
 *    запроса к бэкенду нет);
 *  - AC step2-wrong-code: 400 «Код восстановления не подходит» — баннер
 *    дословно, экран /recovery/code остаётся активным (ISS-110/AR-011);
 *  - успех: resetToken в store (IF-102, сервис), переход /reset-password;
 *  - AC step2-resend-after-annul: «Переотправить код» повторно вызывает
 *    request по сохранённому email, экран не покидается, email сохранён
 *    (в т.ч. после отказа подтверждения — 400);
 *  - 429 переотправки — баннер, экран остаётся;
 *  - «Отмена» — полный сброс store и /login; busy-блокировка.
 *
 * Транспортная граница — программируемый HttpTestingController (FR-026):
 * страница, AuthService и RecoveryFlowStore реальные вместе с production
 * цепочкой HttpClient + authInterceptor — гарантии IF-101/IF-102
 * (resetToken после подтверждения) проверяются вместе со страницей.
 * Ожидаемые URL строятся из TestBed.inject(API_BASE_URL) (ADR-014).
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
import { RecoveryCodePage } from './recovery-code-page';

describe('RecoveryCodePage — шаг 2 восстановления пароля (SCR-004, T-111)', () => {
  const EMAIL = 'student01@example.com';
  const CODE = '123456';
  const CODE_REJECTED = 'Код восстановления не подходит';
  const TOO_MANY = 'Слишком много попыток. Повторите позже';

  let breakpoints: MockBreakpointObserver;
  let fixture: ComponentFixture<RecoveryCodePage>;
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
      imports: [RecoveryCodePage],
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

    fixture = TestBed.createComponent(RecoveryCodePage);
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

  function codeInput(): HTMLInputElement {
    return root().querySelector<HTMLInputElement>('#code-input')!;
  }

  function submitButton(): HTMLButtonElement {
    return root().querySelector<HTMLButtonElement>('button[type="submit"]')!;
  }

  function cancelButton(): HTMLButtonElement {
    return root().querySelector<HTMLButtonElement>('button[type="button"]')!;
  }

  function resendButton(): HTMLButtonElement {
    return root().querySelector<HTMLButtonElement>('.auth-card__link-button')!;
  }

  function fieldError(): string | null {
    return root().querySelector('.field__error')?.textContent?.trim() ?? null;
  }

  function fill(code: string): void {
    codeInput().value = code;
    codeInput().dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function submit(): void {
    submitButton().click();
    fixture.detectChanges();
  }

  /** Дренаж микрозадач (промисы flush → сервис → страница) + CD. */
  async function settle(): Promise<void> {
    await new Promise<void>((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }

  /** Сабмит заполненного кода с программируемым ответом POST /auth/recovery/confirm. */
  async function submitAndFlushConfirm(status: number, body: unknown): Promise<void> {
    fill(CODE);
    submit();
    const request = httpMock.expectOne(`${apiBase}/auth/recovery/confirm`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ email: EMAIL, code: codeInput().value.trim() });
    request.flush(body as object | null, {
      status,
      statusText: status === 200 ? 'OK' : 'Error',
    });
    await settle();
  }

  /** Переотправка с программируемым ответом POST /auth/recovery/request. */
  async function resendAndFlush(status: number, body: unknown = null): Promise<void> {
    resendButton().click();
    const request = httpMock.expectOne(`${apiBase}/auth/recovery/request`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ email: EMAIL });
    request.flush(body as object | null, {
      status,
      statusText: status === 200 ? 'OK' : 'Error',
    });
    await settle();
  }

  it('тексты макета дословно: заголовок, «Шаг 2 из 3», «Код восстановления», подсказки, кнопки, «Переотправить код»', () => {
    expect(root().querySelector('h1')!.textContent!.trim()).toBe('Восстановление пароля');
    expect(root().querySelector('.auth-card__step')!.textContent!.trim()).toBe('Шаг 2 из 3');
    expect(root().querySelector('label')!.textContent!.trim()).toBe('Код восстановления');
    const hints = Array.from(root().querySelectorAll('.field__hint')).map((h) => h.textContent!.trim());
    expect(hints[0]).toBe('Код из 6 цифр');
    expect(hints[1]).toBe('В демо-режиме код отправляется в консоль браузера (F12) — [mock-email]');
    expect(submitButton().textContent).toContain('Ввести код');
    expect(cancelButton().textContent).toContain('Отмена');
    expect(resendButton().textContent!.trim()).toBe('Переотправить код');
  });

  it('валидация кода: пусто — «Заполните поле», «12a456» и «1234567» — «Код должен состоять из 6 цифр», запроса нет', async () => {
    submit();
    await settle();
    expect(fieldError()).toBe('Заполните поле');

    fill('12a456');
    submit();
    await settle();
    expect(fieldError()).toBe('Код должен состоять из 6 цифр');

    fill('1234567');
    submit();
    await settle();
    expect(fieldError()).toBe('Код должен состоять из 6 цифр');

    expect(notifications.desktopMessage()!.text).toBe('Данные заполнены неверно');
    expect(httpMock.match(`${apiBase}/auth/recovery/confirm`).length)
      .withContext('confirm не отправлен')
      .toBe(0);
  });

  it('AC step2-wrong-code: 400 «Код восстановления не подходит» — баннер дословно, экран остаётся активным', async () => {
    flow.setEmail(EMAIL);
    await submitAndFlushConfirm(400, { message: CODE_REJECTED });

    expect(notifications.desktopMessage()!.text).toBe(CODE_REJECTED);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
    // Экран остаётся: форма и «Переотправить код» доступны, email в store.
    expect(codeInput()).not.toBeNull();
    expect(resendButton().disabled).toBeFalse();
    expect(flow.email()).toBe(EMAIL);
    expect(flow.hasResetToken()).toBeFalse();
  });

  it('успех: resetToken в store (IF-102), переход /reset-password', async () => {
    flow.setEmail(EMAIL);
    await submitAndFlushConfirm(200, { resetToken: 'reset-token-1' });

    expect(flow.resetToken()).toBe('reset-token-1');
    expect(flow.email()).toBe(EMAIL);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/reset-password');
  });

  it('AC step2-resend-after-annul: «Переотправить код» запрашивает новый код, email сохранён, экран не покидается', async () => {
    flow.setEmail(EMAIL);
    // Предварительно: подтверждение отклонено (код неверный или аннулирован
    // 5-й попыткой на бэкенде — единый 400, IF-101).
    await submitAndFlushConfirm(400, { message: CODE_REJECTED });
    expect(router.navigateByUrl).not.toHaveBeenCalled();

    // Переотправка: новый запрос кода по сохранённому email, экран остаётся.
    await resendAndFlush(200);

    expect(flow.email()).toBe(EMAIL);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
    expect(root().querySelector('h1')!.textContent).toContain('Восстановление пароля');
  });

  it('429 переотправки: баннер «Слишком много попыток. Повторите позже», экран остаётся', async () => {
    flow.setEmail(EMAIL);
    await resendAndFlush(429, { message: TOO_MANY });

    expect(notifications.desktopMessage()!.text).toBe(TOO_MANY);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
    expect(flow.email()).toBe(EMAIL);
  });

  it('«Отмена» очищает поток восстановления (IF-102) и возвращает на /login', async () => {
    flow.setEmail(EMAIL);
    flow.setResetToken('reset-token-1');
    cancelButton().click();
    await settle();

    expect(flow.email()).toBeNull();
    expect(flow.resetToken()).toBeNull();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/login');
  });

  it('на время запроса кнопки заблокированы, «Переотправить код» не выполняется параллельно сабмиту (FR-025)', async () => {
    flow.setEmail(EMAIL);
    fill(CODE);
    submit();
    fixture.detectChanges();

    // Ответ не программируется: запрос подтверждения «висит» — busy.
    expect(submitButton().disabled).toBeTrue();
    expect(resendButton().disabled).withContext('переотправка заблокирована').toBeTrue();

    resendButton().click(); // клик по disabled-кнопке — ничего не делает
    expect(httpMock.match(`${apiBase}/auth/recovery/request`).length)
      .withContext('параллельного request нет')
      .toBe(0);
    // match() ПОТРЕБЛЯЕТ найденные запросы (удаляет из открытых), поэтому
    // «ровно одно подтверждение» проверяется здесь и ответ уходит через него.
    const pending = httpMock.match(`${apiBase}/auth/recovery/confirm`);
    expect(pending.length).withContext('подтверждение в полёте — ровно одно').toBe(1);

    pending[0].flush({ resetToken: 'reset-token-1' }, { status: 200, statusText: 'OK' });
    await settle();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/reset-password');
  });
});
