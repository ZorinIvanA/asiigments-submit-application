/**
 * Юнит-тесты страницы «Профиль» (C-116, IF-107/IF-109, SCR-014,
 * FR-4.7/US-18): отображение профиля студента (с группой) и преподавателя
 * (без группы), клиентские валидации с текстами словаря ERROR_TEXTS,
 * отказ бэкенда 409 дословно под формой, успех «Сохранено» с мобильным
 * баннером под якорем formId ('profile-form', MockBreakpointObserver).
 * Карточка «Смена пароля» удалена вместе с аутентификацией.
 *
 * Транспортная граница — программируемый HttpTestingController: страница и
 * ProfileService реальные на production-цепочке HttpClient; GET/PUT
 * /me/profile программируются каждым сценарием, тела запросов проверяются
 * на границе HTTP (тримминг, состав DTO). Режимы ширины —
 * MockBreakpointObserver; задержек бэкенда нет — микрозадачи дренируются
 * макротаском, без fakeAsync.
 */
import { BreakpointObserver } from '@angular/cdk/layout';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
  TestRequest,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';

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

describe('ProfilePage (C-116, IF-107/IF-109, SCR-014)', () => {
  let breakpoints: MockBreakpointObserver;
  let notifications: NotificationService;
  let httpMock: HttpTestingController;
  let apiBase: string;

  beforeEach(() => {
    breakpoints = new MockBreakpointObserver();
    TestBed.configureTestingModule({
      imports: [ProfilePage],
      providers: [
        { provide: BreakpointObserver, useValue: breakpoints },
        provideHttpClient(),
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
    it('до загрузки профиля форма не показана (плашка загрузки)', async () => {
      breakpoints.simulate(false);

      const fixture = TestBed.createComponent(ProfilePage);
      fixture.detectChanges();

      expect(q(fixture, 'profile-form')).withContext('формы ещё нет').toBeNull();
      expect(textOf(fixture, 'loading')).toContain('Загрузка');

      // Ответ GET /me/profile: форма появляется.
      httpMock.expectOne(`${apiBase}/me/profile`).flush(STUDENT_PROFILE);
      await new Promise<void>((resolve) => setTimeout(resolve, 0));
      fixture.detectChanges();

      expect(q(fixture, 'profile-form')).not.toBeNull();
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

    it('тексты макета SCR-014 дословно: заголовки, подсказка ФИО, кнопка; карточки пароля нет', async () => {
      breakpoints.simulate(false);
      const fixture = await createPage(STUDENT_PROFILE);

      expect(textOf(fixture, 'page-title')).toBe('Профиль');
      expect(textOf(fixture, 'profile-card-title')).toBe('Основные данные');
      expect(textOf(fixture, 'full-name-hint')).toBe('От 1 до 200 символов');
      expect(buttonLabel(fixture, 'profile-submit')).toBe('Сохранить');
      expect(q(fixture, 'password-card-title'))
        .withContext('карточка «Смена пароля» удалена')
        .toBeNull();
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

    it('update-email-409: «Пользователь с таким email уже существует» под формой (мобильный якорь)', async () => {
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

    it('успех на мобильном: «Сохранено» без якоря формы (под шапкой), под формой баннера нет', async () => {
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
      notifications.dismissMobile();
    });
  });
});
