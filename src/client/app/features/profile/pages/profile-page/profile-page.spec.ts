/**
 * Юнит-тесты страницы «Профиль» (C-116, IF-107/IF-109, SCR-014,
 * FR-4.7/US-18, FR-4.10): отображение профиля студента (с группой) и
 * преподавателя (без группы), независимость двух форм — мобильный баннер
 * уходит под якорь formId инициировавшей формы ('profile-form' /
 * 'password-form', MockBreakpointObserver), клиентские валидации с
 * текстами словаря ERROR_TEXTS, отказы мока 409/400 дословно под своей
 * формой, успехи «Сохранено»/«Пароль изменён».
 *
 * Изоляция — паттерн spec-файлов домена: очистка хранилищ, сид
 * seedFixtures напрямую в ключ mock.db.v1, сессия напрямую по ключу
 * mock.session.userId; реальный реестр обработчиков профиля (защита от
 * расхождений страницы со слоем домена). Режимы ширины —
 * MockBreakpointObserver; таймеры (задержка мока 500 мс, автозакрытие
 * уведомлений) — fakeAsync/tick.
 */
import { BreakpointObserver } from '@angular/cdk/layout';
import { ComponentFixture, fakeAsync, TestBed, tick } from '@angular/core/testing';

import { MockApiClient } from '../../../../mock/mock-api-client';
import { registerProfileHandlers } from '../../../../mock/profile/handlers';
import { seedFixtures } from '../../../../mock/seed';
import { MockBreakpointObserver } from '../../../../../testing/mock-breakpoint-observer';
import { NotificationService } from '../../../../shared/notifications/notification-service';
import { STORAGE_KEYS, User } from '../../../../shared/models';
import { ProfilePage } from './profile-page';

const DB_KEY = STORAGE_KEYS.mockDb;
const SESSION_KEY = STORAGE_KEYS.session;

const STUDENT_LOGIN = 'student01';
const TEACHER_LOGIN = 'teacher';
const STUDENT_PASSWORD = 'student123!';
const NEW_PASSWORD = 'NewPass#2027';

describe('ProfilePage (C-116, IF-107/IF-109, SCR-014)', () => {
  let breakpoints: MockBreakpointObserver;
  let notifications: NotificationService;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    breakpoints = new MockBreakpointObserver();
    TestBed.configureTestingModule({
      providers: [{ provide: BreakpointObserver, useValue: breakpoints }],
    });
    registerProfileHandlers(TestBed.inject(MockApiClient));
    notifications = TestBed.inject(NotificationService);
  });

  afterEach(() => {
    // Гасим возможный незавершённый таймер автозакрытия (реальные таймеры).
    notifications.dismissMobile();
    localStorage.clear();
    sessionStorage.clear();
  });

  /** Сид демо-данных (student01 в ИК-221, teacher) напрямую в mock.db.v1. */
  function seedDb(): void {
    localStorage.setItem(DB_KEY, JSON.stringify(seedFixtures()));
  }

  /** id пользователя сида по логину. */
  function seededUserId(login: string): string {
    const user = seedFixtures().users.find((candidate) => candidate.login === login);
    if (user === undefined) {
      throw new Error(`в сиде нет пользователя ${login}`);
    }
    return user.id;
  }

  /** Сессия пользователя сида по логину (напрямую через ключ, как в домене). */
  function loginAs(login: string): void {
    localStorage.setItem(SESSION_KEY, seededUserId(login));
  }

  /** Пользователь из мок-БД по логину (свежий срез localStorage). */
  function storedUser(login: string): User {
    const data = JSON.parse(localStorage.getItem(DB_KEY) ?? 'null') as ReturnType<
      typeof seedFixtures
    >;
    return data.users.find((candidate) => candidate.login === login)!;
  }

  /** Создаёт страницу: первый CD запускает profile.get, tick ждёт мок. */
  function createPage(): ComponentFixture<ProfilePage> {
    const fixture = TestBed.createComponent(ProfilePage);
    fixture.detectChanges();
    tick(500);
    fixture.detectChanges();
    return fixture;
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
    it('до загрузки профиля формы не показаны (плашка загрузки)', fakeAsync(() => {
      seedDb();
      loginAs(STUDENT_LOGIN);
      breakpoints.simulate(false);

      const fixture = TestBed.createComponent(ProfilePage);
      fixture.detectChanges();

      expect(q(fixture, 'profile-form')).withContext('формы ещё нет').toBeNull();
      expect(q(fixture, 'password-form')).withContext('формы ещё нет').toBeNull();
      expect(textOf(fixture, 'loading')).toContain('Загрузка');

      tick(500);
      fixture.detectChanges();

      expect(q(fixture, 'profile-form')).not.toBeNull();
      expect(q(fixture, 'password-form')).not.toBeNull();
    }));

    it('view-student: логин student01 только для чтения, «Студент», «ИК-221», email/ФИО в полях', fakeAsync(() => {
      seedDb();
      loginAs(STUDENT_LOGIN);
      breakpoints.simulate(false);

      const fixture = createPage();

      expect(textOf(fixture, 'login-value')).toBe('student01');
      expect(q(fixture, 'login-value')?.querySelector('input'))
        .withContext('логин не редактируется — значение не в input')
        .toBeNull();
      expect(textOf(fixture, 'role-value')).toBe('Студент');
      expect(textOf(fixture, 'group-value')).toBe('ИК-221');
      expect(inputValue(fixture, 'email-input')).toBe('student01@example.com');
      expect(inputValue(fixture, 'full-name-input')).toBe('Иванов Иван Иванович 01');
    }));

    it('view-teacher: «Преподаватель», поля группы нет, данные преподавателя в полях', fakeAsync(() => {
      seedDb();
      loginAs(TEACHER_LOGIN);
      breakpoints.simulate(false);

      const fixture = createPage();

      expect(textOf(fixture, 'login-value')).toBe('teacher');
      expect(textOf(fixture, 'role-value')).toBe('Преподаватель');
      expect(q(fixture, 'group-field')).withContext('у преподавателя группы нет').toBeNull();
      expect(inputValue(fixture, 'email-input')).toBe('teacher@example.com');
      expect(inputValue(fixture, 'full-name-input')).toBe('Сидоров Семён Семёнович');
    }));

    it('тексты макета SCR-014 дословно: заголовки, подсказка ФИО, требования к паролю, кнопки', fakeAsync(() => {
      seedDb();
      loginAs(STUDENT_LOGIN);
      breakpoints.simulate(false);

      const fixture = createPage();

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
    }));
  });

  describe('форма «Основные данные»', () => {
    it('успех: «Сохранено», ФИО триммировано и изменено в мок-БД, поля актуализированы', fakeAsync(() => {
      seedDb();
      loginAs(STUDENT_LOGIN);
      breakpoints.simulate(false);

      const fixture = createPage();
      setInput(fixture, 'full-name-input', '  Петров Пётр Петрович  ');
      submit(fixture, 'profile-submit');
      tick(500);
      fixture.detectChanges();

      expect(notifications.desktopMessage()).toEqual({ severity: 'success', text: 'Сохранено' });
      expect(storedUser(STUDENT_LOGIN).fullName).toBe('Петров Пётр Петрович');
      expect(inputValue(fixture, 'full-name-input')).toBe('Петров Пётр Петрович');
      notifications.dismissMobile();
    }));

    it('update-email-409: «Пользователь с таким email уже существует» под формой «Основные данные» (мобильный якорь)', fakeAsync(() => {
      seedDb();
      loginAs(STUDENT_LOGIN);
      breakpoints.simulate(true);

      const fixture = createPage();
      setInput(fixture, 'email-input', 'TEACHER@example.com');
      submit(fixture, 'profile-submit');
      tick(500);
      fixture.detectChanges();

      const banner = bannerUnder(fixture, 'profile-anchor');
      expect(banner).withContext('баннер под «Основные данные»').not.toBeNull();
      expect(banner!.textContent).toContain('Пользователь с таким email уже существует');
      expect(bannerUnder(fixture, 'password-anchor'))
        .withContext('под формой смены пароля баннера нет')
        .toBeNull();
      expect(storedUser(STUDENT_LOGIN).email).withContext('email не изменён').toBe('student01@example.com');
      notifications.dismissMobile();
    }));

    it('update-email-409 на десктопе: текст 409 дословно в Toast-режиме (desktopMessage)', fakeAsync(() => {
      seedDb();
      loginAs(STUDENT_LOGIN);
      breakpoints.simulate(false);

      const fixture = createPage();
      setInput(fixture, 'email-input', 'teacher@example.com');
      submit(fixture, 'profile-submit');
      tick(500);
      fixture.detectChanges();

      expect(notifications.desktopMessage()).toEqual({
        severity: 'error',
        text: 'Пользователь с таким email уже существует',
      });
      notifications.dismissMobile();
    }));

    it('клиентская валидация: некорректный email — баннер «Данные заполнены неверно» + текст словаря, мок не вызывался', fakeAsync(() => {
      seedDb();
      loginAs(STUDENT_LOGIN);
      breakpoints.simulate(true);

      const fixture = createPage();
      setInput(fixture, 'email-input', 'abc');
      submit(fixture, 'profile-submit');

      const banner = bannerUnder(fixture, 'profile-anchor');
      expect(banner!.textContent).toContain('Данные заполнены неверно');
      expect(fieldErrorText(fixture, 'email-field')).toBe('Введите корректный email');
      expect(storedUser(STUDENT_LOGIN).email)
        .withContext('мок не вызывался — email прежний')
        .toBe('student01@example.com');
      notifications.dismissMobile();
    }));

    it('клиентская валидация: пустое ФИО — «Заполните поле»; длиннее 200 — «ФИО — от 1 до 200 символов»', fakeAsync(() => {
      seedDb();
      loginAs(STUDENT_LOGIN);
      breakpoints.simulate(true);

      const fixture = createPage();
      setInput(fixture, 'full-name-input', '   ');
      submit(fixture, 'profile-submit');

      expect(bannerUnder(fixture, 'profile-anchor')).not.toBeNull();
      expect(fieldErrorText(fixture, 'full-name-field')).toBe('Заполните поле');

      setInput(fixture, 'full-name-input', 'Ф'.repeat(201));
      submit(fixture, 'profile-submit');

      expect(fieldErrorText(fixture, 'full-name-field')).toBe('ФИО — от 1 до 200 символов');
      notifications.dismissMobile();
    }));

    it('успех на мобильном: «Сохранено» без якоря формы (под шапкой), под формами баннеров нет', fakeAsync(() => {
      seedDb();
      loginAs(STUDENT_LOGIN);
      breakpoints.simulate(true);

      const fixture = createPage();
      setInput(fixture, 'full-name-input', 'Новое ФИО');
      submit(fixture, 'profile-submit');
      tick(500);
      fixture.detectChanges();

      expect(notifications.mobileMessage()).toEqual({
        severity: 'success',
        text: 'Сохранено',
        formId: null,
      });
      expect(bannerUnder(fixture, 'profile-anchor')).toBeNull();
      expect(bannerUnder(fixture, 'password-anchor')).toBeNull();
      notifications.dismissMobile();
    }));
  });

  describe('форма «Смена пароля»', () => {
    it('password-success: «Пароль изменён», поля формы сброшены, в мок-БД новый пароль', fakeAsync(() => {
      seedDb();
      loginAs(STUDENT_LOGIN);
      breakpoints.simulate(false);

      const fixture = createPage();
      setInput(fixture, 'current-password-input', STUDENT_PASSWORD);
      setInput(fixture, 'new-password-input', NEW_PASSWORD);
      setInput(fixture, 'repeat-password-input', NEW_PASSWORD);
      submit(fixture, 'password-submit');
      tick(500);
      fixture.detectChanges();

      expect(notifications.desktopMessage()).toEqual({
        severity: 'success',
        text: 'Пароль изменён',
      });
      expect(storedUser(STUDENT_LOGIN).password).toBe(NEW_PASSWORD);
      expect(inputValue(fixture, 'current-password-input')).toBe('');
      expect(inputValue(fixture, 'new-password-input')).toBe('');
      expect(inputValue(fixture, 'repeat-password-input')).toBe('');
      notifications.dismissMobile();
    }));

    it('password-wrong-current: «Неверный текущий пароль» под формой смены, поля нового не сброшены', fakeAsync(() => {
      seedDb();
      loginAs(STUDENT_LOGIN);
      breakpoints.simulate(true);

      const fixture = createPage();
      setInput(fixture, 'current-password-input', 'не-текущий-пароль');
      setInput(fixture, 'new-password-input', NEW_PASSWORD);
      setInput(fixture, 'repeat-password-input', NEW_PASSWORD);
      submit(fixture, 'password-submit');
      tick(500);
      fixture.detectChanges();

      const banner = bannerUnder(fixture, 'password-anchor');
      expect(banner).withContext('баннер под формой смены пароля').not.toBeNull();
      expect(banner!.textContent).toContain('Неверный текущий пароль');
      expect(bannerUnder(fixture, 'profile-anchor'))
        .withContext('под формой профиля баннера нет')
        .toBeNull();
      expect(storedUser(STUDENT_LOGIN).password).withContext('пароль не изменён').toBe(STUDENT_PASSWORD);
      expect(inputValue(fixture, 'new-password-input')).withContext('не сброшено').toBe(NEW_PASSWORD);
      expect(inputValue(fixture, 'repeat-password-input')).withContext('не сброшено').toBe(NEW_PASSWORD);
      notifications.dismissMobile();
    }));

    it('клиентская валидация: слабый новый пароль — тексты словаря, несовпадение — «Пароли не совпадают», мок не вызывался', fakeAsync(() => {
      seedDb();
      loginAs(STUDENT_LOGIN);
      breakpoints.simulate(false);

      const fixture = createPage();
      setInput(fixture, 'current-password-input', STUDENT_PASSWORD);
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
      expect(storedUser(STUDENT_LOGIN).password)
        .withContext('мок не вызывался — пароль прежний')
        .toBe(STUDENT_PASSWORD);
      notifications.dismissMobile();
    }));
  });

  describe('независимость форм (AR-017)', () => {
    it('ошибка одной формы не трогает другую: баннер уходит под якорь инициировавшей формы', fakeAsync(() => {
      seedDb();
      loginAs(STUDENT_LOGIN);
      breakpoints.simulate(true);

      const fixture = createPage();

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
    }));
  });
});
