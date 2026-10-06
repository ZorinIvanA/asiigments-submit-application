/**
 * Юнит-тесты ResetPasswordPage (T-111, SCR-005):
 *  - тексты макета дословно: «Новый пароль», «Шаг 3 из 3», блок
 *    требований, «Новый пароль»/«Повторите пароль», «Отмена»/
 *    «Сменить пароль»; ссылки «Запросить код заново» изначально нет;
 *  - клиентская валидация: passwordRules (все правила словаря) +
 *    passwordMatch («Пароли не совпадают») + requiredTrim — без запроса
 *    к моку;
 *  - AC step3-success: пароль изменён, store полностью очищен (IF-102,
 *    сервис), редирект /login без автологина;
 *  - AC step3-terminal: 400 «Ссылка восстановления недействительна или
 *    истекла» — баннер дословно, экран остаётся, появляется ссылка
 *    «Запросить код заново» → /recovery, resetToken удалён из store,
 *    email сохранён (IF-101/IF-102), автоматического перехода нет;
 *  - повторная отправка погашенного токена — тот же 400, тот же баннер;
 *  - 400 «Данные заполнены неверно» (полевой) — баннер без терминальной
 *    ссылки, токен в store сохраняется;
 *  - «Отмена» — полный сброс store и /login; mobile-якорь 'reset-password-form'.
 *
 * Транспортная граница — MockApiClient.call (spy): AuthService и
 * RecoveryFlowStore реальные, поэтому терминальная ветка проверяется
 * сквозь реальное поведение сервиса (гашение токена, IF-101).
 */
import { BreakpointObserver } from '@angular/cdk/layout';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { RecoveryFlowStore } from '../../../../core/services/recovery-flow-store';
import { MockApiClient } from '../../../../mock/mock-api-client';
import { NotificationService } from '../../../../shared/notifications/notification-service';
import { MockBreakpointObserver } from '../../../../../testing/mock-breakpoint-observer';
import { ResetPasswordPage } from './reset-password-page';

describe('ResetPasswordPage — шаг 3 восстановления пароля (SCR-005, T-111)', () => {
  const EMAIL = 'student01@example.com';
  const RESET_TOKEN = 'reset-token-1';
  const PASSWORD = 'Newpass#1';
  const LINK_INVALID = 'Ссылка восстановления недействительна или истекла';
  const INVALID_DATA = 'Данные заполнены неверно';

  let breakpoints: MockBreakpointObserver;
  let fixture: ComponentFixture<ResetPasswordPage>;
  let client: jasmine.SpyObj<MockApiClient>;
  let flow: RecoveryFlowStore;
  let notifications: NotificationService;
  let router: Router;

  beforeEach(async () => {
    sessionStorage.clear();
    localStorage.clear();
    breakpoints = new MockBreakpointObserver();
    // Транспорт (MockApiClient.call) — spy-объект в DI: страница,
    // AuthService и RecoveryFlowStore реальные, поэтому терминальная ветка
    // токена проверяется сквозь реальное поведение сервиса (IF-101/IF-102).
    client = jasmine.createSpyObj<MockApiClient>('MockApiClient', ['call']);
    await TestBed.configureTestingModule({
      imports: [ResetPasswordPage],
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

    fixture = TestBed.createComponent(ResetPasswordPage);
    fixture.detectChanges();
  });

  afterEach(() => {
    notifications.dismissMobile();
  });

  /** Предусловие шага 3: в store есть email шага 2 и resetToken шага 3. */
  function seedFlow(): void {
    flow.setEmail(EMAIL);
    flow.setResetToken(RESET_TOKEN);
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function passwordInput(): HTMLInputElement {
    return root().querySelector<HTMLInputElement>('#password-input')!;
  }

  function repeatInput(): HTMLInputElement {
    return root().querySelector<HTMLInputElement>('#repeat-password-input')!;
  }

  function submitButton(): HTMLButtonElement {
    return root().querySelector<HTMLButtonElement>('button[type="submit"]')!;
  }

  function cancelButton(): HTMLButtonElement {
    return root().querySelector<HTMLButtonElement>('button[type="button"]')!;
  }

  function restartLink(): HTMLAnchorElement | null {
    const links = Array.from(root().querySelectorAll('a')) as HTMLAnchorElement[];
    return links.find((a) => a.textContent!.trim() === 'Запросить код заново') ?? null;
  }

  function errors(): string[] {
    return Array.from(root().querySelectorAll('.field__error')).map((e) =>
      e.textContent!.trim(),
    );
  }

  function fill(password: string, repeat: string): void {
    passwordInput().value = password;
    passwordInput().dispatchEvent(new Event('input'));
    repeatInput().value = repeat;
    repeatInput().dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function submit(): void {
    submitButton().click();
    fixture.detectChanges();
  }

  it('тексты макета дословно: заголовок, «Шаг 3 из 3», требования, поля, кнопки; ссылки «Запросить код заново» нет', () => {
    expect(root().querySelector('h1')!.textContent!.trim()).toBe('Новый пароль');
    expect(root().querySelector('.auth-card__step')!.textContent!.trim()).toBe('Шаг 3 из 3');
    const requirements = root().querySelector('.auth-card__requirements')!.textContent!;
    expect(requirements).toContain('Требования к паролю');
    expect(requirements).toContain('не менее 8 символов; минимум одна цифра;');
    expect(requirements).toContain('минимум одна буква; минимум один спецзнак;');
    expect(requirements).toContain('пароли должны совпадать');
    const labels = Array.from(root().querySelectorAll('label')).map((l) => l.textContent!.trim());
    expect(labels).toEqual(['Новый пароль', 'Повторите пароль']);
    expect(submitButton().textContent).toContain('Сменить пароль');
    expect(cancelButton().textContent).toContain('Отмена');
    expect(restartLink()).withContext('до ошибки токена ссылки нет').toBeNull();
  });

  it('валидация пароля: короче 8 — «не менее 8 символов», без спецзнака — «хотя бы один специальный знак»; запроса нет', fakeAsync(() => {
    seedFlow();

    fill('Ab1!', 'Ab1!');
    submit();
    tick();
    expect(errors()).toContain('Пароль должен содержать не менее 8 символов');

    fill('abcdefgh1', 'abcdefgh1');
    submit();
    tick();
    expect(errors()).toContain('Пароль должен содержать хотя бы один специальный знак');

    expect(client.call).not.toHaveBeenCalled();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  }));

  it('валидация пары: несовпадение — «Пароли не совпадают» у повтора, пустой повтор — «Заполните поле»; запроса нет', fakeAsync(() => {
    seedFlow();

    fill(PASSWORD, 'Newpass#2');
    submit();
    tick();
    expect(errors()).toContain('Пароли не совпадают');

    fill(PASSWORD, '');
    submit();
    tick();
    expect(errors()).toContain('Заполните поле');

    expect(client.call).not.toHaveBeenCalled();
  }));

  it('AC step3-success: пароль изменён, store полностью очищен, редирект /login (без автологина)', fakeAsync(() => {
    seedFlow();
    client.call.and.resolveTo(null);
    fill(PASSWORD, PASSWORD);
    submit();
    tick();

    expect(client.call).toHaveBeenCalledWith('auth.reset-password', {
      resetToken: RESET_TOKEN,
      password: PASSWORD,
      confirmPassword: PASSWORD,
    });
    // Поток очищен целиком (IF-102): и токен, и email шага 2.
    expect(flow.resetToken()).toBeNull();
    expect(flow.email()).toBeNull();
    expect(flow.hasEmail()).toBeFalse();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/login');
  }));

  it('AC step3-terminal: баннер «Ссылка…», экран остаётся, «Запросить код заново» → /recovery, токен удалён, email сохранён', fakeAsync(() => {
    seedFlow();
    client.call.and.rejectWith({ status: 400, body: { message: LINK_INVALID } });
    fill(PASSWORD, PASSWORD);
    submit();
    tick();
    fixture.detectChanges();

    expect(client.call).toHaveBeenCalledWith('auth.reset-password', {
      resetToken: RESET_TOKEN,
      password: PASSWORD,
      confirmPassword: PASSWORD,
    });
    expect(notifications.desktopMessage()!.text).toBe(LINK_INVALID);
    expect(router.navigateByUrl).not.toHaveBeenCalled();

    // Токен удалён из store сервисом (IF-101), email сохранён.
    expect(flow.resetToken()).toBeNull();
    expect(flow.email()).toBe(EMAIL);

    // Ссылка продолжения потока появилась и ведёт на шаг 1.
    const link = restartLink();
    expect(link).not.toBeNull();
    expect(link!.getAttribute('href')).toBe('/recovery');
  }));

  it('повторная отправка погашенного токена: тот же 400 и тот же баннер, экран остаётся', fakeAsync(() => {
    seedFlow();
    client.call.and.rejectWith({ status: 400, body: { message: LINK_INVALID } });
    fill(PASSWORD, PASSWORD);
    submit();
    tick();
    fixture.detectChanges();
    expect(flow.resetToken()).toBeNull();

    // Пользователь снова жмёт «Сменить пароль»: токен уже удалён из store
    // терминальной веткой (IF-102), страница отправляет пустое значение —
    // мок отвечает тем же 400 «Ссылка…» (повторная отправка погашенного
    // токена), экран остаётся, ссылка на месте.
    client.call.calls.reset();
    client.call.and.rejectWith({ status: 400, body: { message: LINK_INVALID } });
    submit();
    tick();
    fixture.detectChanges();

    expect(client.call).toHaveBeenCalledWith('auth.reset-password', {
      resetToken: '',
      password: PASSWORD,
      confirmPassword: PASSWORD,
    });
    expect(notifications.desktopMessage()!.text).toBe(LINK_INVALID);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
    expect(restartLink()).not.toBeNull();
  }));

  it('полевой 400 «Данные заполнены неверно»: баннер, ссылки нет, токен остаётся в store', fakeAsync(() => {
    seedFlow();
    client.call.and.rejectWith({
      status: 400,
      body: { message: INVALID_DATA, errors: { password: ['Пароль должен содержать не менее 8 символов'] } },
    });
    fill(PASSWORD, PASSWORD);
    submit();
    tick();
    fixture.detectChanges();

    expect(notifications.desktopMessage()!.text).toBe(INVALID_DATA);
    expect(restartLink()).withContext('терминальная ссылка не показывается').toBeNull();
    expect(flow.resetToken()).withContext('токен не гасится (IF-101)').toBe(RESET_TOKEN);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  }));

  it('терминальный баннер на мобильной ширине: inline-баннер под формой с якорем reset-password-form (IF-109)', fakeAsync(() => {
    breakpoints.simulate(true);
    seedFlow();
    client.call.and.rejectWith({ status: 400, body: { message: LINK_INVALID } });
    fill(PASSWORD, PASSWORD);
    submit();
    tick();
    fixture.detectChanges();

    const message = notifications.mobileMessage();
    expect(message).not.toBeNull();
    expect(message!.text).toBe(LINK_INVALID);
    expect(message!.formId).toBe('reset-password-form');

    const form = root().querySelector('form')!;
    const anchor = root().querySelector('app-notification-anchor')!;
    expect(form.contains(anchor)).withContext('якорь смонтирован под формой').toBeTrue();
    const banner = anchor.querySelector('app-notification-banner');
    expect(banner).not.toBeNull();
    expect(banner!.textContent).toContain(LINK_INVALID);
  }));

  it('«Отмена» очищает поток восстановления (IF-102) и возвращает на /login', fakeAsync(() => {
    seedFlow();
    fill(PASSWORD, PASSWORD);
    cancelButton().click();
    tick();

    expect(flow.email()).toBeNull();
    expect(flow.resetToken()).toBeNull();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/login');
  }));
});
