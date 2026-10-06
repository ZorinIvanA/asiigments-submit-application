/**
 * Unit-тесты RegisterPage (T-110, SCR-002, AC register-*): клиентская
 * валидация — баннер «Данные заполнены неверно» + тексты полей из
 * словаря ERROR_TEXTS, сервис не вызывался; отказы мока 409 (оба) и 429 —
 * дословно с якорем 'register-form'; успех — автологин (DTO триммится)
 * и редирект /my-submissions без уведомления об успехе; состояние loading
 * кнопки; подсказки макета и ссылка «У меня уже есть аккаунт».
 *
 * AuthService подменяется spy-объектом: правила и тексты мока покрыты
 * тестами домена (T-101). Режим уведомлений — MockBreakpointObserver.
 */
import { BreakpointObserver } from '@angular/cdk/layout';
import {
  ComponentFixture,
  TestBed,
  fakeAsync,
  tick,
} from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { AuthService, MeDto } from '../../../../core/services/auth.service';
import { MockBreakpointObserver } from '../../../../../testing/mock-breakpoint-observer';
import { NotificationService } from '../../../../shared/notifications/notification-service';
import { RegisterPage } from './register-page';

const STUDENT: MeDto = {
  login: 'newstudent1',
  fullName: 'Новиков Никита Николаевич',
  role: 'student',
  groupName: null,
};

describe('RegisterPage — регистрация (SCR-002, T-110)', () => {
  let breakpoints: MockBreakpointObserver;
  let auth: jasmine.SpyObj<AuthService>;
  let notifications: NotificationService;
  let router: Router;
  let fixture: ComponentFixture<RegisterPage>;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    breakpoints = new MockBreakpointObserver();
    auth = jasmine.createSpyObj<AuthService>('AuthService', ['register']);
    TestBed.configureTestingModule({
      imports: [RegisterPage],
      providers: [
        { provide: BreakpointObserver, useValue: breakpoints },
        { provide: AuthService, useValue: auth },
        provideRouter([]),
      ],
    });
    notifications = TestBed.inject(NotificationService);
    router = TestBed.inject(Router);
    spyOn(router, 'navigateByUrl');
    fixture = TestBed.createComponent(RegisterPage);
    fixture.detectChanges();
  });

  afterEach(() => {
    notifications.dismissMobile();
  });

  function submitButton(): HTMLButtonElement {
    return (
      fixture.nativeElement as HTMLElement
    ).querySelector<HTMLButtonElement>('button[type="submit"]')!;
  }

  function fill(
    fullName: string,
    login: string,
    email: string,
    password: string,
    repeatPassword: string,
  ): void {
    const values: Array<[string, string]> = [
      ['#full-name-input', fullName],
      ['#login-input', login],
      ['#email-input', email],
      ['#password-input', password],
      ['#repeat-password-input', repeatPassword],
    ];
    const root = fixture.nativeElement as HTMLElement;
    for (const [selector, value] of values) {
      const input = root.querySelector<HTMLInputElement>(selector)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
    fixture.detectChanges();
  }

  function fillValid(): void {
    fill(
      'Новиков Никита Николаевич',
      'newstudent1',
      'newstudent1@example.com',
      'Password1!',
      'Password1!',
    );
  }

  function submit(): void {
    submitButton().click();
    fixture.detectChanges();
  }

  function errorTexts(): string[] {
    const root = fixture.nativeElement as HTMLElement;
    return Array.from(root.querySelectorAll<HTMLElement>('.field__error')).map(
      (element) => element.textContent!.trim(),
    );
  }

  it('AC register-client-validation: баннер «Данные заполнены неверно», тексты полей, сервис не вызывался', () => {
    auth.register.and.resolveTo(STUDENT); // вызова быть не должно
    fill(
      'Новиков Никита Николаевич',
      'newstudent1',
      'newstudent1@example.com',
      'abcdefgh!',
      'другой1!',
    );

    submit();

    const message = notifications.desktopMessage();
    expect(message).not.toBeNull();
    expect(message!.text).toBe('Данные заполнены неверно');

    const texts = errorTexts();
    expect(texts).toContain('Пароль должен содержать хотя бы одну цифру');
    expect(texts).toContain('Пароли не совпадают');
    expect(
      fixture.nativeElement.querySelectorAll('.field--invalid').length,
    ).toBeGreaterThan(0);
    expect(auth.register).not.toHaveBeenCalled();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('до первой попытки отправки тексты полей не показываются', () => {
    fill('', '', '', '', '');
    expect(errorTexts()).toEqual([]);
    expect(notifications.desktopMessage()).toBeNull();
  });

  it('тексты полей — по словарю ERROR_TEXTS для каждого правила', () => {
    const cases: Array<[string, string, string, string, string, string]> = [
      ['', 'login', 'a@b.ru', 'Password1!', 'Password1!', 'Заполните поле'], // пустое ФИО
      [
        'Ф'.repeat(201),
        'login',
        'a@b.ru',
        'Password1!',
        'Password1!',
        'ФИО — от 1 до 200 символов',
      ],
      [
        'ФИО',
        'логин с пробелом',
        'a@b.ru',
        'Password1!',
        'Password1!',
        'Логин может содержать только латинские буквы, цифры, точку, дефис и подчёркивание',
      ],
      [
        'ФИО',
        'l'.repeat(101),
        'a@b.ru',
        'Password1!',
        'Password1!',
        'Логин — от 1 до 100 символов',
      ],
      [
        'ФИО',
        'login',
        'не email',
        'Password1!',
        'Password1!',
        'Введите корректный email',
      ],
      [
        'ФИО',
        'login',
        `a@${'b'.repeat(250)}.ru`,
        'Password1!',
        'Password1!',
        'Email — не более 254 символов',
      ],
      [
        'ФИО',
        'login',
        'a@b.ru',
        'a1!',
        'a1!',
        'Пароль должен содержать не менее 8 символов',
      ],
      [
        'ФИО',
        'login',
        'a@b.ru',
        '12345678!',
        '12345678!',
        'Пароль должен содержать хотя бы одну букву',
      ],
      [
        'ФИО',
        'login',
        'a@b.ru',
        'abcdefgh1',
        'abcdefgh1',
        'Пароль должен содержать хотя бы один специальный знак',
      ],
      ['ФИО', 'login', 'a@b.ru', 'Password1!', '', 'Заполните поле'], // пустой повтор
    ];

    for (const [fullName, login, email, password, repeat, expected] of cases) {
      notifications.dismissMobile();
      fill(fullName, login, email, password, repeat);
      submit();
      expect(auth.register).not.toHaveBeenCalled();
      expect(errorTexts())
        .withContext(`${expected} (${login || 'пусто'})`)
        .toContain(expected);
    }
  });

  it('AC register-409-login: баннер «Пользователь с таким логином уже существует»', fakeAsync(() => {
    auth.register.and.callFake(() =>
      Promise.reject({
        status: 409,
        body: { message: 'Пользователь с таким логином уже существует' },
      }),
    );
    fillValid();
    submit();
    tick();

    const message = notifications.desktopMessage();
    expect(message).not.toBeNull();
    expect(message!.text).toBe('Пользователь с таким логином уже существует');
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  }));

  it('409 email: баннер «Пользователь с таким email уже существует»', fakeAsync(() => {
    auth.register.and.callFake(() =>
      Promise.reject({
        status: 409,
        body: { message: 'Пользователь с таким email уже существует' },
      }),
    );
    fillValid();
    submit();
    tick();

    expect(notifications.desktopMessage()!.text).toBe(
      'Пользователь с таким email уже существует',
    );
  }));

  it('429 на мобильной ширине: «Слишком много попыток. Повторите позже» с якорем register-form', fakeAsync(() => {
    breakpoints.simulate(true);
    auth.register.and.callFake(() =>
      Promise.reject({
        status: 429,
        body: { message: 'Слишком много попыток. Повторите позже' },
      }),
    );
    fillValid();
    submit();
    tick();
    fixture.detectChanges();

    const message = notifications.mobileMessage();
    expect(message).not.toBeNull();
    expect(message!.text).toBe('Слишком много попыток. Повторите позже');
    expect(message!.formId).toBe('register-form');
    const anchor = fixture.nativeElement.querySelector(
      'app-notification-anchor',
    )!;
    expect(fixture.nativeElement.querySelector('form')!.contains(anchor))
      .withContext('якорь смонтирован под формой')
      .toBeTrue();
    expect(
      anchor.querySelector('app-notification-banner')!.textContent,
    ).toContain('Слишком много попыток. Повторите позже');
  }));

  it('успех: автологин (DTO триммится), редирект /my-submissions, без уведомления об успехе', fakeAsync(() => {
    auth.register.and.resolveTo(STUDENT);
    fill(
      '  Новиков Никита Николаевич  ',
      '  newstudent1  ',
      '  newstudent1@example.com  ',
      'Password1!',
      'Password1!',
    );
    submit();
    tick();

    expect(auth.register).toHaveBeenCalledWith({
      fullName: 'Новиков Никита Николаевич',
      login: 'newstudent1',
      email: 'newstudent1@example.com',
      password: 'Password1!',
      repeatPassword: 'Password1!',
    });
    expect(router.navigateByUrl).toHaveBeenCalledWith('/my-submissions');
    expect(notifications.desktopMessage())
      .withContext('успеха-уведомления нет')
      .toBeNull();
    expect(notifications.mobileMessage()).toBeNull();
  }));

  it('кнопка в состоянии loading и заблокирована, пока запрос выполняется', fakeAsync(() => {
    let resolveRegister!: (me: MeDto) => void;
    auth.register.and.returnValue(
      new Promise<MeDto>((resolve) => (resolveRegister = resolve)),
    );
    fillValid();
    submit();

    expect(submitButton().className).toContain('p-button-loading');
    expect(submitButton().disabled).toBeTrue();

    // Повторная отправка, пока запрос в полёте, игнорируется (FR-025).
    fixture.componentInstance.submit();
    expect(auth.register).toHaveBeenCalledTimes(1);

    resolveRegister(STUDENT);
    tick();
    fixture.detectChanges();

    expect(submitButton().className).not.toContain('p-button-loading');
    expect(router.navigateByUrl).toHaveBeenCalledWith('/my-submissions');
  }));

  it('тексты и ссылки макета дословно: kicker, «Регистрация», подсказки, «У меня уже есть аккаунт» → /login', () => {
    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('Сдача лабораторных');
    expect(root.querySelector('h1')!.textContent!.trim()).toBe('Регистрация');
    const labels = Array.from(root.querySelectorAll('label')).map((l) =>
      l.textContent!.trim(),
    );
    expect(labels).toEqual([
      'ФИО',
      'Логин',
      'Email',
      'Пароль',
      'Повторите пароль',
    ]);
    expect(submitButton().textContent).toContain('Зарегистрироваться');

    // Подсказки макета (требования к паролю выведены на экран, FR-010).
    expect(root.textContent).toContain('От 1 до 200 символов');
    expect(root.textContent).toContain(
      'От 1 до 100 символов, не меняется после регистрации',
    );
    expect(root.textContent).toContain(
      'Не менее 8 символов; минимум одна цифра, одна буква и один спецзнак; пароли должны совпадать',
    );

    // routerLink привязывает attr.href: после detectChanges href содержит путь.
    const anchor = Array.from(root.querySelectorAll('a')).find(
      (a) => a.textContent!.trim() === 'У меня уже есть аккаунт',
    );
    expect(anchor)
      .withContext('ссылка «У меня уже есть аккаунт»')
      .toBeDefined();
    expect(anchor!.getAttribute('href') ?? '').toContain('/login');
  });
});
