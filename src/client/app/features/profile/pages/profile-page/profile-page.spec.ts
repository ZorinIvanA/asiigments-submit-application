/**
 * Юнит-тесты страницы «Профиль» (C-116, IF-107/IF-109, SCR-014,
 * FR-4.7/US-18, FR-4.10): отображение профиля студента (с группой) и
 * преподавателя (без группы), независимость двух форм — мобильный баннер
 * уходит под якорь formId инициировавшей формы ('profile-form' /
 * 'password-form', MockBreakpointObserver), клиентские валидации с
 * текстами словаря ERROR_TEXTS, отказы бэкенда 409/400 дословно под своей
 * формой, успехи «Сохранено»/«Пароль изменён».
 *
 * Транспортная граница — программируемый HttpTestingController (FR-026):
 * страница и ProfileService реальные вместе с production цепочкой
 * HttpClient + authInterceptor; GET/PUT /me/profile и PUT /me/password
 * программируются каждым сценарием, тела запросов проверяются на границе
 * HTTP (тримминг, состав DTO). Режимы ширины — MockBreakpointObserver
 * (UI-двойник, не мок-слой); задержки бэкенда нет — микрозадачи дренируются
 * макротаском, без fakeAsync.
 */
import { BreakpointObserver } from '@angular/cdk/layout';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
  TestRequest,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { authInterceptor } from '../../../../core/auth-interceptor';
import { API_BASE_URL } from '../../../../core/api-base-url';
import { ProfileDto } from '../../../../shared/models';
import { NotificationService } from '../../../../shared/notifications/notification-service';
import { MockBreakpointObserver } from '../../../../../testing/mock-breakpoint-observer';
import { ProfilePage } from './profile-page';

const STUDENT_PROFILE: ProfileDto = {
  login: 'student01',
  email: 'student01@example.com',
  fullName: 'Иванов Иван Иванович 01',
  role: 'student',
  groupName: 'ИК-221',
};

const TEACHER_PROFILE: ProfileDto = {
  login: 'teacher',
  email: 'teacher@example.com',
  fullName: 'Сидоров Семён Семёнович',
  role: 'teacher',
  groupName: null,
};

const NEW_PASSWORD = 'NewPass#2027';

describe('ProfilePage (C-116, IF-107/IF-109, SCR-014)', () => {
  let breakpoints: MockBreakpointObserver;
  let notifications: NotificationService;
  let httpMock: HttpTestingController;
  let apiBase: string;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    breakpoints = new MockBreakpointObserver();
    TestBed.configureTestingModule({
      imports: [ProfilePage],
      providers: [
        { provide: BreakpointObserver, useValue: breakpoints },
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
    apiBase = TestBed.inject(API_BASE_URL);
    notifications = TestBed.inject(NotificationService);
  });

  afterEach(() => {
    // Гасим возможный незавершённый таймер автозакрытия (реальные таймеры).
    notifications.dismissMobile();
    // Ни одного незакрытого/лишнего запроса (валидационные ветки — ни одного).
    httpMock.verify();
    localStorage.clear();
    sessionStorage.clear();
  });

  /**
   * Создаёт страницу и программирует первичный GET /me/profile: первый CD
   * запускает ngOnInit (запрос перехватывается), ответ завершает загрузку.
   */
  async function createPage(profile: ProfileDto): Promise<ComponentFixture<ProfilePage>> {
    const fixture = TestBed.createComponent(ProfilePage);
    fixture.detectChanges();
    const request = httpMock.expectOne(`${apiBase}/me/profile`);
    expect(request.request.method).toBe('GET');
    expect(request.request.withCredentials).toBeTrue();
    request.flush(profile);
    await new Promise<void>((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
    return fixture;
  }

  /** Программирует ответ PUT /me/profile (обновление «Основных данных»). */
  async function flushUpdate(
    fixture: ComponentFixture<ProfilePage>,
    status: number,
    body: unknown,
  ): Promise<TestRequest> {
    const request = httpMock.expectOne(`${apiBase}/me/profile`);
    expect(request.request.method).toBe('PUT');
    request.flush(body as object | null, {
      status,
      statusText: status < 300 ? 'OK' : 'Error',
    });
    await new Promise<void>((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
    return request;
  }

  /** Программирует ответ PUT /me/password (смена пароля). */
  async function flushPasswordChange(
    fixture: ComponentFixture<ProfilePage>,
    status: number,
    body: unknown = null,
  ): Promise<TestRequest> {
    const request = httpMock.expectOne(`${apiBase}/me/password`);
    expect(request.request.method).toBe('PUT');
    request.flush(body as object | null, {
      status,
      statusText: status < 300 ? 'No Content' : 'Error',
    });
    await new Promise<void>((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
    return request;
  }

  function q(fixture: ComponentFixture<ProfilePage>, testId: string): HTMLElement {
    return fixture.nativeElement.querySelector(`[data-test="${testId}"]`) as HTMLElement;
  }

  function textOf(fixture: ComponentFixture<ProfilePage>, testId: string): string {
    return (q(fixture, testId)?.textContent ?? '').replace(/\s+/g, ' ').trim();
  }

  function inputValue(fixture: ComponentFixture<ProfilePage>, testId: string): string {
    return (q(fixture, testId) as HTMLInputElement).value;
  }

  /** Ввод значения в поле как пользователь (input-событие). */
  function setInput(
    fixture: ComponentFixture<ProfilePage>,
    testId: string,
    value: string,
  ): void {
    const input = q(fixture, testId) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  /** Клик по кнопке действия (p-button рендерит вложенный <button>). */
  function submit(fixture: ComponentFixture<ProfilePage>, buttonTestId: string): void {
    const host = q(fixture, buttonTestId);
    const button = (host.querySelector('button') ?? host) as HTMLElement;
    button.click();
    fixture.detectChanges();
  }

  /** Баннер под якорем формы либо null. */
  function bannerUnder(
    fixture: ComponentFixture<ProfilePage>,
    anchorTestId: string,
  ): HTMLElement | null {
    return fixture.nativeElement.querySelector(
      `[data-test="${anchorTestId}"] app-notification-banner`,
    );
  }

  /** Текст ошибки под полем (словарь ERROR_TEXTS) либо null. */
  function fieldErrorText(
    fixture: ComponentFixture<ProfilePage>,
    fieldTestId: string,
  ): string | null {
    const error = q(fixture, fieldTestId)?.querySelector('.profile-field__error') ?? null;
    return error === null ? null : (error.textContent ?? '').trim();
  }

  function buttonLabel(fixture: ComponentFixture<ProfilePage>, buttonTestId: string): string {
    const label = q(fixture, buttonTestId)?.querySelector('.p-button-label');
    return (label?.textContent ?? '').trim();
  }

  describe('просмотр профиля', () => {
    it('до загрузки профиля формы не показаны (плашка загрузки)', async () => {
      breakpoints.simulate(false);

      const fixture = TestBed.createComponent(ProfilePage);
      fixture.detectChanges();

      expect(q(fixture, 'profile-form')).withContext('формы ещё нет').toBeNull();
      expect(q(fixture, 'password-form')).withContext('формы ещё нет').toBeNull();
      expect(textOf(fixture, 'loading')).toContain('Загрузка');

      // Ответ GET /me/profile: формы появляются.
      httpMock.expectOne(`${apiBase}/me/profile`).flush(STUDENT_PROFILE);
      await new Promise<void>((resolve) => setTimeout(resolve, 0));
      fixture.detectChanges();

      expect(q(fixture, 'profile-form')).not.toBeNull();
      expect(q(fixture, 'password-form')).not.toBeNull();
    });

    it('view-student: логин student01 только для чтения, «Студент», «ИК-221», email/ФИО в полях', async () => {
      breakpoints.simulate(false);
      const fixture = await createPage(STUDENT_PROFILE);

      expect(textOf(fixture, 'login-value')).toBe('student01');
      expect(q(fixture, 'login-value')?.querySelector('input'))
        .withContext('логин не редактируется — значение не в input')
        .toBeNull();
      expect(textOf(fixture, 'role-value')).toBe('Студент');
      expect(textOf(fixture, 'group-value')).toBe('ИК-221');
      expect(inputValue(fixture, 'email-input')).toBe('student01@example.com');
      expect(inputValue(fixture, 'full-name-input')).toBe('Иванов Иван Иванович 01');
    });

    it('view-teacher: «Преподаватель», поля группы нет, данные преподавателя в полях', async () => {
      breakpoints.simulate(false);
      const fixture = await createPage(TEACHER_PROFILE);

      expect(textOf(fixture, 'login-value')).toBe('teacher');
      expect(textOf(fixture, 'role-value')).toBe('Преподаватель');
      expect(q(fixture, 'group-field')).withContext('у преподавателя группы нет').toBeNull();
      expect(inputValue(fixture, 'email-input')).toBe('teacher@example.com');
      expect(inputValue(fixture, 'full-name-input')).toBe('Сидоров Семён Семёнович');
    });

    it('тексты макета SCR-014 дословно: заголовки, подсказка ФИО, требования к паролю, кнопки', async () => {
      breakpoints.simulate(false);
      const fixture = await createPage(STUDENT_PROFILE);

      expect(textOf(fixture, 'page-title')).toBe('Профиль');
      expect(textOf(fixture, 'profile-card-title')).toBe('Основные данные');
      expect(textOf(fixture, 'password-card-title')).toBe('Смена пароля');
      expect(textOf(fixture, 'full-name-hint')).toBe('От 1 до 200 символов');
      expect(textOf(fixture, 'password-requirements-title')).toBe('Требования к новому паролю');
      expect(textOf(fixture, 'password-requirements-text')).toBe(
        'не менее 8 символов; минимум одна цифра; минимум одна буква; минимум один спецзнак; пароли должны совпадать',
      );
      expect(buttonLabel(fixture, 'profile-submit')).toBe('Сохранить');
      expect(buttonLabel(fixture, 'password-submit')).toBe('Сменить пароль');
    });
  });

  describe('форма «Основные данные»', () => {
    it('успех: «Сохранено», DTO PUT с триммированными значениями, поля актуализированы ответом', async () => {
      breakpoints.simulate(false);
      const fixture = await createPage(STUDENT_PROFILE);
      setInput(fixture, 'full-name-input', '  Петров Пётр Петрович  ');
      setInput(fixture, 'email-input', '  petrov.petrov@example.ru ');
      submit(fixture, 'profile-submit');
      const request = await flushUpdate(fixture, 200, {
        ...STUDENT_PROFILE,
        fullName: 'Петров Пётр Петрович',
        email: 'petrov.petrov@example.ru',
      });

      expect(notifications.desktopMessage()).toEqual({ severity: 'success', text: 'Сохранено' });
      // Триммированные значения уходят в DTO PUT-запроса (на границе HTTP).
      expect(request.request.body).toEqual({
        fullName: 'Петров Пётр Петрович',
        email: 'petrov.petrov@example.ru',
      });
      expect(inputValue(fixture, 'full-name-input')).toBe('Петров Пётр Петрович');
      notifications.dismissMobile();
    });

    it('update-email-409: «Пользователь с таким email уже существует» под формой «Основные данные» (мобильный якорь)', async () => {
      breakpoints.simulate(true);
      const fixture = await createPage(STUDENT_PROFILE);
      setInput(fixture, 'email-input', 'TEACHER@example.com');
      submit(fixture, 'profile-submit');
      await flushUpdate(fixture, 409, {
        message: 'Пользователь с таким email уже существует',
      });

      const banner = bannerUnder(fixture, 'profile-anchor');
      expect(banner).withContext('баннер под «Основные данные»').not.toBeNull();
      expect(banner!.textContent).toContain('Пользователь с таким email уже существует');
      expect(bannerUnder(fixture, 'password-anchor'))
        .withContext('под формой смены пароля баннера нет')
        .toBeNull();
      // Отказ кэш формы не обновляет: прежнее значение email.
      expect(inputValue(fixture, 'email-input')).toBe('TEACHER@example.com');
      notifications.dismissMobile();
    });

    it('update-email-409 на десктопе: текст 409 дословно в Toast-режиме (desktopMessage)', async () => {
      breakpoints.simulate(false);
      const fixture = await createPage(STUDENT_PROFILE);
      setInput(fixture, 'email-input', 'teacher@example.com');
      submit(fixture, 'profile-submit');
      await flushUpdate(fixture, 409, {
        message: 'Пользователь с таким email уже существует',
      });

      expect(notifications.desktopMessage()).toEqual({
        severity: 'error',
        text: 'Пользователь с таким email уже существует',
      });
      notifications.dismissMobile();
    });

    it('клиентская валидация: некорректный email — баннер «Данные заполнены неверно» + текст словаря, запроса нет', async () => {
      breakpoints.simulate(true);
      const fixture = await createPage(STUDENT_PROFILE);
      setInput(fixture, 'email-input', 'abc');
      submit(fixture, 'profile-submit');

      const banner = bannerUnder(fixture, 'profile-anchor');
      expect(banner!.textContent).toContain('Данные заполнены неверно');
      expect(fieldErrorText(fixture, 'email-field')).toBe('Введите корректный email');
      expect(httpMock.match(`${apiBase}/me/profile`).length)
        .withContext('запрос не отправлен')
        .toBe(0);
      notifications.dismissMobile();
    });

    it('клиентская валидация: пустое ФИО — «Заполните поле»; длиннее 200 — «ФИО — от 1 до 200 символов»', async () => {
      breakpoints.simulate(true);
      const fixture = await createPage(STUDENT_PROFILE);
      setInput(fixture, 'full-name-input', '   ');
      submit(fixture, 'profile-submit');

      expect(bannerUnder(fixture, 'profile-anchor')).not.toBeNull();
      expect(fieldErrorText(fixture, 'full-name-field')).toBe('Заполните поле');

      setInput(fixture, 'full-name-input', 'Ф'.repeat(201));
      submit(fixture, 'profile-submit');

      expect(fieldErrorText(fixture, 'full-name-field')).toBe('ФИО — от 1 до 200 символов');
      expect(httpMock.match(`${apiBase}/me/profile`).length)
        .withContext('невалидные варианты не отправлялись')
        .toBe(0);
      notifications.dismissMobile();
    });

    it('успех на мобильном: «Сохранено» без якоря формы (под шапкой), под формами баннеров нет', async () => {
      breakpoints.simulate(true);
      const fixture = await createPage(STUDENT_PROFILE);
      setInput(fixture, 'full-name-input', 'Новое ФИО');
      submit(fixture, 'profile-submit');
      await flushUpdate(fixture, 200, { ...STUDENT_PROFILE, fullName: 'Новое ФИО' });

      expect(notifications.mobileMessage()).toEqual({
        severity: 'success',
        text: 'Сохранено',
        formId: null,
      });
      expect(bannerUnder(fixture, 'profile-anchor')).toBeNull();
      expect(bannerUnder(fixture, 'password-anchor')).toBeNull();
      notifications.dismissMobile();
    });
  });

  describe('форма «Смена пароля»', () => {
    it('password-success: «Пароль изменён», поля формы сброшены, PUT /me/password 204 с триммированным DTO', async () => {
      breakpoints.simulate(false);
      const fixture = await createPage(STUDENT_PROFILE);
      setInput(fixture, 'current-password-input', 'student123!');
      setInput(fixture, 'new-password-input', NEW_PASSWORD);
      setInput(fixture, 'repeat-password-input', NEW_PASSWORD);
      submit(fixture, 'password-submit');
      const request = await flushPasswordChange(fixture, 204);

      expect(request.request.body).toEqual({
        currentPassword: 'student123!',
        password: NEW_PASSWORD,
        confirmPassword: NEW_PASSWORD,
      });
      expect(notifications.desktopMessage()).toEqual({
        severity: 'success',
        text: 'Пароль изменён',
      });
      expect(inputValue(fixture, 'current-password-input')).toBe('');
      expect(inputValue(fixture, 'new-password-input')).toBe('');
      expect(inputValue(fixture, 'repeat-password-input')).toBe('');
      notifications.dismissMobile();
    });

    it('password-wrong-current: «Неверный текущий пароль» под формой смены, поля нового не сброшены', async () => {
      breakpoints.simulate(true);
      const fixture = await createPage(STUDENT_PROFILE);
      setInput(fixture, 'current-password-input', 'не-текущий-пароль');
      setInput(fixture, 'new-password-input', NEW_PASSWORD);
      setInput(fixture, 'repeat-password-input', NEW_PASSWORD);
      submit(fixture, 'password-submit');
      await flushPasswordChange(fixture, 400, { message: 'Неверный текущий пароль' });

      const banner = bannerUnder(fixture, 'password-anchor');
      expect(banner).withContext('баннер под формой смены пароля').not.toBeNull();
      expect(banner!.textContent).toContain('Неверный текущий пароль');
      expect(bannerUnder(fixture, 'profile-anchor'))
        .withContext('под формой профиля баннера нет')
        .toBeNull();
      expect(inputValue(fixture, 'new-password-input')).withContext('не сброшено').toBe(NEW_PASSWORD);
      expect(inputValue(fixture, 'repeat-password-input')).withContext('не сброшено').toBe(NEW_PASSWORD);
      notifications.dismissMobile();
    });

    it('клиентская валидация: слабый новый пароль — тексты словаря, несовпадение — «Пароли не совпадают», запроса нет', async () => {
      breakpoints.simulate(false);
      const fixture = await createPage(STUDENT_PROFILE);
      setInput(fixture, 'current-password-input', 'student123!');
      setInput(fixture, 'new-password-input', 'abcdefgh');
      setInput(fixture, 'repeat-password-input', 'abcdefgh');
      submit(fixture, 'password-submit');

      expect(fieldErrorText(fixture, 'new-password-field')).toBe(
        'Пароль должен содержать хотя бы одну цифру',
      );
      expect(fieldErrorText(fixture, 'current-password-field')).toBeNull();

      setInput(fixture, 'new-password-input', NEW_PASSWORD);
      setInput(fixture, 'repeat-password-input', `${NEW_PASSWORD}!`);
      submit(fixture, 'password-submit');

      expect(fieldErrorText(fixture, 'repeat-password-field')).toBe('Пароли не совпадают');
      expect(httpMock.match(`${apiBase}/me/password`).length)
        .withContext('запрос не отправлен')
        .toBe(0);
      notifications.dismissMobile();
    });
  });

  describe('независимость форм (AR-017)', () => {
    it('ошибка одной формы не трогает другую: баннер уходит под якорь инициировавшей формы', async () => {
      breakpoints.simulate(true);
      const fixture = await createPage(STUDENT_PROFILE);

      // 1) Пустая форма пароля: баннер под «Смена пароля», профиль не тронут.
      submit(fixture, 'password-submit');
      expect(bannerUnder(fixture, 'password-anchor')!.textContent).toContain(
        'Данные заполнены неверно',
      );
      expect(bannerUnder(fixture, 'profile-anchor')).toBeNull();
      expect(fieldErrorText(fixture, 'current-password-field')).toBe('Заполните поле');
      expect(fieldErrorText(fixture, 'email-field'))
        .withContext('поля формы профиля не затронуты')
        .toBeNull();

      // 2) Затем невалидная форма профиля: баннер переезжает под «Основные данные».
      setInput(fixture, 'email-input', 'abc');
      submit(fixture, 'profile-submit');
      expect(bannerUnder(fixture, 'profile-anchor')!.textContent).toContain(
        'Данные заполнены неверно',
      );
      expect(bannerUnder(fixture, 'password-anchor')).toBeNull();
      expect(fieldErrorText(fixture, 'email-field')).toBe('Введите корректный email');
      // Ошибки полей пароля — её собственные (остались от шага 1), действие
      // формы профиля их не добавляет и не сбрасывает.
      expect(fieldErrorText(fixture, 'current-password-field')).toBe('Заполните поле');
      notifications.dismissMobile();
    });
  });
});
