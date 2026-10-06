/**
 * Юнит-тесты RecoveryCodePage (T-111, SCR-004):
 *  - тексты макета дословно: «Восстановление пароля», «Шаг 2 из 3»,
 *    «Код восстановления», подсказки, «Отмена»/«Ввести код»,
 *    «Переотправить код»;
 *  - валидатор кода code6 (пусто / «12a456» / «1234567» — тексты словаря,
 *    запроса к моку нет);
 *  - AC step2-wrong-code: 400 «Код восстановления не подходит» — баннер
 *    дословно, экран /recovery/code остаётся активным (ISS-110/AR-011);
 *  - успех: resetToken в store (IF-102, сервис), переход /reset-password;
 *  - AC step2-resend-after-annul: «Переотправить код» повторно вызывает
 *    request по сохранённому email, экран не покидается, email сохранён
 *    (в т.ч. после аннулирования кода — 400 у подтверждения);
 *  - 429 переотправки — баннер, экран остаётся;
 *  - «Отмена» — полный сброс store и /login; busy-блокировка.
 *
 * Транспортная граница — MockApiClient.call (spy): AuthService и
 * RecoveryFlowStore реальные — гарантии IF-101/IF-102 (resetToken после
 * подтверждения) проверяются вместе со страницей.
 */
import { BreakpointObserver } from '@angular/cdk/layout';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { RecoveryFlowStore } from '../../../../core/services/recovery-flow-store';
import { MockApiClient } from '../../../../mock/mock-api-client';
import { NotificationService } from '../../../../shared/notifications/notification-service';
import { MockBreakpointObserver } from '../../../../../testing/mock-breakpoint-observer';
import { RecoveryCodePage } from './recovery-code-page';

describe('RecoveryCodePage — шаг 2 восстановления пароля (SCR-004, T-111)', () => {
  const EMAIL = 'student01@example.com';
  const CODE_REJECTED = 'Код восстановления не подходит';
  const TOO_MANY = 'Слишком много попыток. Повторите позже';
  const INVALID_DATA = 'Данные заполнены неверно';

  let breakpoints: MockBreakpointObserver;
  let fixture: ComponentFixture<RecoveryCodePage>;
  let client: jasmine.SpyObj<MockApiClient>;
  let flow: RecoveryFlowStore;
  let notifications: NotificationService;
  let router: Router;

  beforeEach(async () => {
    sessionStorage.clear();
    localStorage.clear();
    breakpoints = new MockBreakpointObserver();
    // Транспорт (MockApiClient.call) — spy-объект в DI: страница,
    // AuthService и RecoveryFlowStore реальные, поэтому эффекты
    // IF-101/IF-102 в store проверяются вместе со страницей.
    client = jasmine.createSpyObj<MockApiClient>('MockApiClient', ['call']);
    await TestBed.configureTestingModule({
      imports: [RecoveryCodePage],
      providers: [
        { provide: BreakpointObserver, useValue: breakpoints },
        { provide: MockApiClient, useValue: client },
        provideRouter([]),
      ],
    }).compileComponents();

    flow = TestBed.inject(RecoveryFlowStore);
    notifications = TestBed.inject(NotificationService);
    router = TestBed.inject(Router);
    spyOn(router, 'navigateByUrl');

    fixture = TestBed.createComponent(RecoveryCodePage);
    fixture.detectChanges();
  });

  afterEach(() => {
    notifications.dismissMobile();
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

  it('валидация кода: пусто — «Заполните поле», «12a456» и «1234567» — «Код должен состоять из 6 цифр», запроса нет', fakeAsync(() => {
    submit();
    tick();
    expect(fieldError()).toBe('Заполните поле');

    fill('12a456');
    submit();
    tick();
    expect(fieldError()).toBe('Код должен состоять из 6 цифр');

    fill('1234567');
    submit();
    tick();
    expect(fieldError()).toBe('Код должен состоять из 6 цифр');

    expect(notifications.desktopMessage()!.text).toBe(INVALID_DATA);
    expect(client.call).not.toHaveBeenCalled();
  }));

  it('AC step2-wrong-code: 400 «Код восстановления не подходит» — баннер дословно, экран остаётся активным', fakeAsync(() => {
    flow.setEmail(EMAIL);
    client.call.and.rejectWith({ status: 400, body: { message: CODE_REJECTED } });
    fill('000000');
    submit();
    tick();
    fixture.detectChanges();

    expect(client.call).toHaveBeenCalledWith('auth.recovery.confirm', {
      email: EMAIL,
      code: '000000',
    });
    expect(notifications.desktopMessage()!.text).toBe(CODE_REJECTED);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
    // Экран остаётся: форма и «Переотправить код» доступны, email в store.
    expect(codeInput()).not.toBeNull();
    expect(resendButton().disabled).toBeFalse();
    expect(flow.email()).toBe(EMAIL);
    expect(flow.hasResetToken()).toBeFalse();
  }));

  it('успех: resetToken в store (IF-102), переход /reset-password', fakeAsync(() => {
    flow.setEmail(EMAIL);
    client.call.and.resolveTo({ resetToken: 'reset-token-1' });
    fill('123456');
    submit();
    tick();

    expect(client.call).toHaveBeenCalledWith('auth.recovery.confirm', {
      email: EMAIL,
      code: '123456',
    });
    expect(flow.resetToken()).toBe('reset-token-1');
    expect(flow.email()).toBe(EMAIL);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/reset-password');
  }));

  it('AC step2-resend-after-annul: «Переотправить код» запрашивает новый код, email сохранён, экран не покидается', fakeAsync(() => {
    flow.setEmail(EMAIL);
    // Предварительно: подтверждение отклонено (код неверный или аннулирован
    // 5-й попыткой — единый 400, IF-101).
    client.call.and.rejectWith({ status: 400, body: { message: CODE_REJECTED } });
    fill('000000');
    submit();
    tick();
    fixture.detectChanges();
    expect(router.navigateByUrl).not.toHaveBeenCalled();

    // Переотправка: новый запрос кода по сохранённому email, экран остаётся.
    client.call.calls.reset();
    client.call.and.resolveTo(null);
    resendButton().click();
    tick();

    expect(client.call).toHaveBeenCalledWith('auth.recovery.request', { email: EMAIL });
    expect(flow.email()).toBe(EMAIL);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
    expect(root().querySelector('h1')!.textContent).toContain('Восстановление пароля');
  }));

  it('429 переотправки: баннер «Слишком много попыток. Повторите позже», экран остаётся', fakeAsync(() => {
    flow.setEmail(EMAIL);
    client.call.and.rejectWith({ status: 429, body: { message: TOO_MANY } });
    resendButton().click();
    tick();

    expect(client.call).toHaveBeenCalledWith('auth.recovery.request', { email: EMAIL });
    expect(notifications.desktopMessage()!.text).toBe(TOO_MANY);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
    expect(flow.email()).toBe(EMAIL);
  }));

  it('«Отмена» очищает поток восстановления (IF-102) и возвращает на /login', fakeAsync(() => {
    flow.setEmail(EMAIL);
    flow.setResetToken('reset-token-1');
    cancelButton().click();
    tick();

    expect(flow.email()).toBeNull();
    expect(flow.resetToken()).toBeNull();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/login');
  }));

  it('на время запроса кнопки заблокированы, «Переотправить код» не выполняется параллельно сабмиту (FR-025)', fakeAsync(() => {
    flow.setEmail(EMAIL);
    let resolveConfirm!: (result: { resetToken: string }) => void;
    client.call.and.returnValue(
      new Promise<{ resetToken: string }>((resolve) => (resolveConfirm = resolve)),
    );
    fill('123456');
    submit();

    expect(submitButton().disabled).toBeTrue();
    expect(resendButton().disabled).withContext('переотправка заблокирована').toBeTrue();

    resendButton().click(); // клик по disabled-кнопке — ничего не делает
    expect(client.call).toHaveBeenCalledTimes(1);

    resolveConfirm({ resetToken: 'reset-token-1' });
    tick();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/reset-password');
  }));
});
