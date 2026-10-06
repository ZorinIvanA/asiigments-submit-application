/**
 * Юнит-тесты RecoveryPage (T-111, SCR-003, AC step1-success):
 *  - тексты макета дословно: «Восстановление пароля», «Шаг 1 из 3»,
 *    «Email», «Отмена», «Отправить код»;
 *  - клиентская валидация email (тексты словаря ERROR_TEXTS: «Заполните
 *    поле», «Введите корректный email», граница 254 символа) без запроса
 *    к моку;
 *  - успех и незарегистрированный email одинаково ведут на шаг 2
 *    (запрос всегда 200, §9): email в RecoveryFlowStore (IF-102),
 *    навигация /recovery/code;
 *  - 429 — баннер «Слишком много попыток. Повторите позже» (desktop —
 *    тост, mobile — inline-баннер под формой с якорем 'recovery-form',
 *    IF-109), редиректа нет;
 *  - «Отмена» очищает store (IF-102) и возвращает на /login;
 *  - трим значения перед DTO; блокировка кнопок на время запроса.
 *
 * Транспортная граница — MockApiClient.call (spy): AuthService и
 * RecoveryFlowStore реальные, поэтому гарантия IF-101/IF-102 «email
 * запоминается после запроса кода» проверяется вместе со страницей.
 * Режим показа уведомлений переключается MockBreakpointObserver (FR-022).
 */
import { BreakpointObserver } from '@angular/cdk/layout';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { RecoveryFlowStore } from '../../../../core/services/recovery-flow-store';
import { MockApiClient } from '../../../../mock/mock-api-client';
import { NotificationService } from '../../../../shared/notifications/notification-service';
import { MockBreakpointObserver } from '../../../../../testing/mock-breakpoint-observer';
import { RecoveryPage } from './recovery-page';

describe('RecoveryPage — шаг 1 восстановления пароля (SCR-003, T-111)', () => {
  const EMAIL = 'student01@example.com';
  const TOO_MANY = 'Слишком много попыток. Повторите позже';
  const INVALID_DATA = 'Данные заполнены неверно';

  let breakpoints: MockBreakpointObserver;
  let fixture: ComponentFixture<RecoveryPage>;
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
      imports: [RecoveryPage],
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

    fixture = TestBed.createComponent(RecoveryPage);
    fixture.detectChanges();
  });

  afterEach(() => {
    notifications.dismissMobile();
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

  it('тексты макета дословно: заголовок, «Шаг 1 из 3», «Email», «Отмена», «Отправить код»', () => {
    expect(root().querySelector('h1')!.textContent!.trim()).toBe('Восстановление пароля');
    expect(root().querySelector('.auth-card__step')!.textContent!.trim()).toBe('Шаг 1 из 3');
    expect(root().querySelector('label')!.textContent!.trim()).toBe('Email');
    expect(submitButton().textContent).toContain('Отправить код');
    expect(cancelButton().textContent).toContain('Отмена');
  });

  it('пустой email: «Заполните поле», баннер валидации, запрос к моку не выполняется', fakeAsync(() => {
    submit();
    tick();

    expect(fieldError()).toBe('Заполните поле');
    expect(notifications.desktopMessage()!.text).toBe(INVALID_DATA);
    expect(client.call).not.toHaveBeenCalled();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  }));

  it('некорректный формат («abc»): «Введите корректный email», запроса нет', fakeAsync(() => {
    fill('abc');
    submit();
    tick();

    expect(fieldError()).toBe('Введите корректный email');
    expect(client.call).not.toHaveBeenCalled();
  }));

  it('граница длины email: 255 символов — ошибка словаря, 254 — валидно', fakeAsync(() => {
    fill('a'.repeat(250) + '@b.ru'); // 255 символов
    submit();
    tick();
    expect(fieldError()).toBe('Email — не более 254 символов');

    fill('a'.repeat(249) + '@b.ru'); // ровно 254 символа
    expect(fieldError()).toBeNull();
  }));

  it('AC step1-success: успех и незарегистрированный email одинаково — email в store, переход /recovery/code', fakeAsync(() => {
    // Оба исхода дают один и тот же ответ 200 (§9): поведение страницы
    // идентично; различие «существует ли email» — зона мока (домен T-101).
    client.call.and.resolveTo(null);
    fill(EMAIL);
    submit();
    tick();

    expect(client.call).toHaveBeenCalledWith('auth.recovery.request', { email: EMAIL });
    expect(flow.email()).toBe(EMAIL);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/recovery/code');
  }));

  it('значение триммится перед DTO (IF-010)', fakeAsync(() => {
    client.call.and.resolveTo(null);
    fill('  a@b.ru  ');
    submit();
    tick();

    expect(client.call).toHaveBeenCalledWith('auth.recovery.request', { email: 'a@b.ru' });
  }));

  it('AC 429: баннер «Слишком много попыток. Повторите позже», экран остаётся, редиректа нет', fakeAsync(() => {
    client.call.and.rejectWith({ status: 429, body: { message: TOO_MANY } });
    fill(EMAIL);
    submit();
    tick();

    expect(notifications.desktopMessage()!.text).toBe(TOO_MANY);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
    expect(root().querySelector('h1')!.textContent).toContain('Восстановление пароля');
  }));

  it('429 на мобильной ширине: inline-баннер под формой с якорем recovery-form (IF-109)', fakeAsync(() => {
    breakpoints.simulate(true);
    client.call.and.rejectWith({ status: 429, body: { message: TOO_MANY } });
    fill(EMAIL);
    submit();
    tick();
    fixture.detectChanges();

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
  }));

  it('«Отмена» очищает поток восстановления (IF-102) и возвращает на /login', fakeAsync(() => {
    flow.setEmail(EMAIL);
    cancelButton().click();
    tick();

    expect(flow.email()).toBeNull();
    expect(flow.hasEmail()).toBeFalse();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/login');
  }));

  it('на время запроса кнопки заблокированы, повторный сабмит игнорируется (FR-025)', fakeAsync(() => {
    let resolveRequest!: () => void;
    client.call.and.returnValue(
      new Promise<null>((resolve) => (resolveRequest = () => resolve(null))),
    );
    fill(EMAIL);
    submit();

    expect(submitButton().disabled).withContext('сабмит заблокирован').toBeTrue();
    expect(submitButton().className).withContext('loading-класс p-button').toContain('p-button-loading');
    expect(cancelButton().disabled).withContext('отмена заблокирована').toBeTrue();

    submit(); // повторный клик — игнорируется
    expect(client.call).toHaveBeenCalledTimes(1);

    resolveRequest();
    tick();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/recovery/code');
  }));
});
