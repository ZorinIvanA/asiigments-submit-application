/**
 * Unit-тесты LoginPage (T-110, SCR-001, AC login-*): блокировка «Войти»
 * при пустом логине ИЛИ пароле (и на значении из одних пробелов); отказы
 * 401/429 — уведомление с текстом контракта IF-101 дословно и якорем
 * 'login-form' (mobile — inline-баннер под формой, MockBreakpointObserver),
 * редиректа нет; успех — редирект по роли (ROLE_HOME: teacher → /works,
 * student → /my-submissions); состояние loading кнопки; трим значений
 * перед DTO; ссылки и тексты макета дословно.
 *
 * AuthService подменяется spy-объектом: конвейер входа и тексты мока
 * покрыты тестами домена (T-101) — здесь проверяется поведение страницы.
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
import { LoginPage } from './login-page';

const TEACHER: MeDto = {
  login: 'teacher',
  fullName: 'Сидоров Семён Семёнович',
  role: 'teacher',
  groupName: null,
};

const STUDENT: MeDto = {
  login: 'student01',
  fullName: 'Иванов Иван Иванович 01',
  role: 'student',
  groupName: 'ИК-221',
};

describe('LoginPage — вход (SCR-001, T-110)', () => {
  let breakpoints: MockBreakpointObserver;
  let auth: jasmine.SpyObj<AuthService>;
  let notifications: NotificationService;
  let router: Router;
  let fixture: ComponentFixture<LoginPage>;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    breakpoints = new MockBreakpointObserver();
    auth = jasmine.createSpyObj<AuthService>('AuthService', ['login']);
    TestBed.configureTestingModule({
      imports: [LoginPage],
      providers: [
        { provide: BreakpointObserver, useValue: breakpoints },
        { provide: AuthService, useValue: auth },
        provideRouter([]),
      ],
    });
    notifications = TestBed.inject(NotificationService);
    router = TestBed.inject(Router);
    spyOn(router, 'navigateByUrl');
    fixture = TestBed.createComponent(LoginPage);
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

  function fill(login: string, password: string): void {
    const root = fixture.nativeElement as HTMLElement;
    const loginInput = root.querySelector<HTMLInputElement>('#login-input')!;
    const passwordInput =
      root.querySelector<HTMLInputElement>('#password-input')!;
    loginInput.value = login;
    loginInput.dispatchEvent(new Event('input'));
    passwordInput.value = password;
    passwordInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function submit(): void {
    submitButton().click();
    fixture.detectChanges();
  }

  it('AC login-disabled: кнопка «Войти» заблокирована при пустом логине ИЛИ пароле', () => {
    expect(submitButton().disabled).withContext('обе пустые').toBeTrue();

    fill('teacher', '');
    expect(submitButton().disabled).withContext('пустой пароль').toBeTrue();

    fill('', 'пароль');
    expect(submitButton().disabled).withContext('пустой логин').toBeTrue();

    fill('   ', 'пароль');
    expect(submitButton().disabled)
      .withContext('логин из одних пробелов')
      .toBeTrue();

    fill('teacher', 'пароль');
    expect(submitButton().disabled).withContext('оба заполнены').toBeFalse();
  });

  it('на /login нет полевых ошибок и баннера валидации, пока кнопка заблокирована', () => {
    submit(); // клик по disabled-кнопке ничего не делает

    expect(notifications.desktopMessage()).toBeNull();
    expect(notifications.mobileMessage()).toBeNull();
    expect(fixture.nativeElement.querySelector('.field__error')).toBeNull();
  });

  it('AC login-error-401: тост «Неверный логин или пароль», редиректа нет (desktop)', fakeAsync(() => {
    auth.login.and.callFake(() =>
      Promise.reject({
        status: 401,
        body: { message: 'Неверный логин или пароль' },
      }),
    );
    fill('teacher', 'неверный');
    submit();
    tick();

    const message = notifications.desktopMessage();
    expect(message).not.toBeNull();
    expect(message!.text).toBe('Неверный логин или пароль');
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  }));

  it('401 на мобильной ширине: inline-баннер под формой с якорем login-form', fakeAsync(() => {
    breakpoints.simulate(true);
    auth.login.and.callFake(() =>
      Promise.reject({
        status: 401,
        body: { message: 'Неверный логин или пароль' },
      }),
    );
    fill('teacher', 'неверный');
    submit();
    tick();
    fixture.detectChanges();

    const message = notifications.mobileMessage();
    expect(message).not.toBeNull();
    expect(message!.text).toBe('Неверный логин или пароль');
    expect(message!.formId).toBe('login-form');

    const form = fixture.nativeElement.querySelector('form')!;
    const anchor = fixture.nativeElement.querySelector(
      'app-notification-anchor',
    )!;
    expect(form.contains(anchor))
      .withContext('якорь смонтирован под формой')
      .toBeTrue();
    const banner = anchor.querySelector('app-notification-banner');
    expect(banner).not.toBeNull();
    expect(banner!.textContent).toContain('Неверный логин или пароль');
  }));

  it('429 на мобильной ширине: «Слишком много попыток. Повторите позже» с якорем login-form', fakeAsync(() => {
    breakpoints.simulate(true);
    auth.login.and.callFake(() =>
      Promise.reject({
        status: 429,
        body: { message: 'Слишком много попыток. Повторите позже' },
      }),
    );
    fill('teacher', 'teacher123!');
    submit();
    tick();
    fixture.detectChanges();

    const message = notifications.mobileMessage();
    expect(message).not.toBeNull();
    expect(message!.text).toBe('Слишком много попыток. Повторите позже');
    expect(message!.formId).toBe('login-form');
    expect(
      fixture.nativeElement.querySelector('app-notification-banner')!
        .textContent,
    ).toContain('Слишком много попыток. Повторите позже');
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  }));

  it('AC login-success-redirect: teacher → /works', fakeAsync(() => {
    auth.login.and.resolveTo(TEACHER);
    fill('teacher', 'teacher123!');
    submit();
    tick();

    expect(auth.login).toHaveBeenCalledWith({
      login: 'teacher',
      password: 'teacher123!',
    });
    expect(router.navigateByUrl).toHaveBeenCalledWith('/works');
  }));

  it('AC login-success-redirect: student → /my-submissions', fakeAsync(() => {
    auth.login.and.resolveTo(STUDENT);
    fill('student01', 'student123!');
    submit();
    tick();

    expect(router.navigateByUrl).toHaveBeenCalledWith('/my-submissions');
  }));

  it('значения триммятся перед DTO (login и пароль)', fakeAsync(() => {
    auth.login.and.resolveTo(STUDENT);
    fill('  student01  ', '  student123!  ');
    submit();
    tick();

    expect(auth.login).toHaveBeenCalledWith({
      login: 'student01',
      password: 'student123!',
    });
  }));

  it('кнопка в состоянии loading и заблокирована, пока запрос выполняется', fakeAsync(() => {
    let resolveLogin!: (me: MeDto) => void;
    auth.login.and.returnValue(
      new Promise<MeDto>((resolve) => (resolveLogin = resolve)),
    );
    fill('teacher', 'teacher123!');
    submit();

    expect(submitButton().className)
      .withContext('loading-класс p-button')
      .toContain('p-button-loading');
    expect(submitButton().disabled)
      .withContext('повторный сабмит заблокирован')
      .toBeTrue();
    expect(router.navigateByUrl).not.toHaveBeenCalled();

    // Повторная отправка, пока запрос в полёте, игнорируется (FR-025):
    // например, сабмит формы клавишей Enter при disabled-кнопке.
    fixture.componentInstance.submit();
    expect(auth.login).toHaveBeenCalledTimes(1);

    resolveLogin(TEACHER);
    tick();
    fixture.detectChanges();

    expect(submitButton().className).not.toContain('p-button-loading');
    expect(router.navigateByUrl).toHaveBeenCalledWith('/works');
  }));

  it('тексты и ссылки макета дословно: kicker, «Вход», поля, «Я забыл пароль» → /recovery, «Зарегистрироваться» → /register', () => {
    const root = fixture.nativeElement as HTMLElement;
    expect(root.textContent).toContain('Сдача лабораторных');
    expect(root.querySelector('h1')!.textContent!.trim()).toBe('Вход');
    const labels = Array.from(root.querySelectorAll('label')).map((l) =>
      l.textContent!.trim(),
    );
    expect(labels).toEqual(['Логин', 'Пароль']);
    expect(submitButton().textContent).toContain('Войти');

    // routerLink привязывает attr.href: после detectChanges href содержит путь.
    const hrefByLinkText = (text: string): string => {
      const anchor = Array.from(root.querySelectorAll('a')).find(
        (a) => a.textContent!.trim() === text,
      );
      expect(anchor).withContext(`ссылка «${text}»`).toBeDefined();
      return anchor!.getAttribute('href') ?? '';
    };
    expect(hrefByLinkText('Я забыл пароль')).toContain('/recovery');
    expect(hrefByLinkText('Зарегистрироваться')).toContain('/register');
  });
});
