/**
 * Юнит-тесты мок-обработчиков домена Auth (C-101, IF-101, §4.1/§4.2/§8/§9):
 * регистрация (успех, 400-ветки, оба 409, лимит 5/час), вход (успех,
 * единый 401, 429 на 6-й неуспешной, скользящее окно, регистронезависимость),
 * me/logout (с сессией, без, битая сессия), восстановление (TTL кода 10 мин,
 * TTL токена 15 мин, одноразовость, ≤3/час, аннулирование на 5-й попытке,
 * незарегистрированный email), сброс пароля (успех, неверный/просроченный/
 * использованный токен, валидация пароля), подключение registerAuthHandlers
 * к конвейеру MockApiClient на фиксированных часах. Момент времени —
 * параметр обработчиков, поэтому все сценарии детерминированы на T0.
 */
import { fakeAsync, tick } from '@angular/core/testing';

import { ApiError, User } from '../../shared/models';
import { MockApiClient } from '../mock-api-client';
import {
  MockDb,
  MockDbData,
  configureMockDbSeed,
  emptyMockDbData,
  resetMockDbSeed,
} from '../mock-db';
import { MockErrorStatus, MockValidationError } from '../mock-error';
import { SessionStore } from './session-store';
import {
  ConfirmRecoveryResult,
  LoginParams,
  MeDto,
  RegisterParams,
  ResetPasswordParams,
  handleAuthLogin,
  handleAuthLogout,
  handleAuthMe,
  handleAuthRecoveryConfirm,
  handleAuthRecoveryRequest,
  handleAuthRegister,
  handleAuthResetPassword,
  registerAuthHandlers,
} from './handlers';

/** Фиксированный момент «сейчас» для всех сценариев (детерминизм). */
const T0 = 1_758_240_000_000;
const MINUTE = 60 * 1000;

const TEACHER_ID = 'aaaaaaaa-1111-4111-8111-111111111111';
const STUDENT_ID = 'bbbbbbbb-2222-4222-8222-222222222222';
const STUDENT_NO_GROUP_ID = 'bbbbbbbb-2222-4222-8222-222222229999';
const GROUP_ID = 'cccccccc-3333-4333-8333-333333333333';

const TEACHER: User = {
  id: TEACHER_ID,
  login: 'teacher',
  email: 'teacher@example.com',
  fullName: 'Сидоров Семён Семёнович',
  role: 'teacher',
  groupId: null,
  password: 'teacher123!',
};

const VALID_REGISTER: RegisterParams = {
  fullName: 'Петров Пётр Петрович',
  login: 'student33',
  email: 'student33@example.com',
  password: 'Password#2026',
  repeatPassword: 'Password#2026',
};

/** Исходное состояние мок-БД по сиду data_design (сокращённый вариант). */
function makeSeed(): MockDbData {
  return {
    ...emptyMockDbData(),
    users: [
      { ...TEACHER },
      {
        id: STUDENT_ID,
        login: 'student01',
        email: 'student01@example.com',
        fullName: 'Иванов Иван Иванович 01',
        role: 'student',
        groupId: GROUP_ID,
        password: 'student123!',
      },
      {
        id: STUDENT_NO_GROUP_ID,
        login: 'student32',
        email: 'student32@example.com',
        fullName: 'Иванов Иван Иванович 32',
        role: 'student',
        groupId: null,
        password: 'student123!',
      },
    ],
    groups: [{ id: GROUP_ID, name: 'ИК-221', studentCount: 2 }],
  };
}

/** Сид с живым кодом восстановления для student01. */
function seedWithCode(code: string, expiresAt: number, attempts = 0): MockDbData {
  const data = makeSeed();
  data.recoveryCodes.push({
    id: 'dddddddd-4444-4444-8444-444444444444',
    userId: STUDENT_ID,
    code,
    expiresAt: new Date(expiresAt).toISOString(),
    usedAt: null,
    attempts,
    createdAt: new Date(T0).toISOString(),
  });
  return data;
}

/** Сид с токеном сброса (значения токенов — произвольные непустые строки). */
function seedWithToken(tokenValue: string, expiresAt: number, usedAt: string | null = null): MockDbData {
  const data = makeSeed();
  data.resetTokens.push({
    id: 'eeeeeeee-5555-4555-8555-555555555555',
    userId: STUDENT_ID,
    token: tokenValue,
    expiresAt: new Date(expiresAt).toISOString(),
    usedAt,
    createdAt: new Date(T0).toISOString(),
  });
  return data;
}

function setup(seed: MockDbData): { db: MockDb; session: SessionStore } {
  configureMockDbSeed(() => structuredClone(seed));
  return { db: new MockDb(), session: new SessionStore() };
}

/** Ловит MockValidationError обработчика; успех без отказа — падение теста. */
function catchValidation(fn: () => unknown): MockValidationError {
  try {
    fn();
  } catch (error: unknown) {
    if (error instanceof MockValidationError) {
      return error;
    }
    throw error;
  }
  fail('обработчик не бросил MockValidationError');
  throw new Error('unreachable: fail() должен прервать тест');
}

/** Проверка формы отказа: статус и дословный текст. */
function expectRejection(
  fn: () => unknown,
  status: MockErrorStatus,
  message: string,
): MockValidationError {
  const error = catchValidation(fn);
  expect(error.status).withContext(`статус отказа «${message}»`).toBe(status);
  expect(error.message).withContext(`текст отказа`).toBe(message);
  return error;
}

/** Извлекает код из последней строки [mock-email] (формат из IF-101/§4.2). */
function lastLoggedCode(): { email: string; code: string } {
  const calls = (console.info as jasmine.Spy).calls.allArgs();
  const last = calls[calls.length - 1][0] as string;
  const match = /\[mock-email\] Код восстановления для (.+): (\d{6})$/.exec(last);
  expect(match).withContext(`формат строки [mock-email]: ${last}`).not.toBeNull();
  return { email: match![1], code: match![2] };
}

/** Код из строки [mock-email] по порядковому номеру запроса (0 — первый). */
function loggedCodeAt(index: number): string {
  const calls = (console.info as jasmine.Spy).calls.allArgs();
  const match = /\[mock-email\] Код восстановления для (.+): (\d{6})$/.exec(calls[index][0] as string);
  expect(match).withContext(`формат строки [mock-email] №${index}`).not.toBeNull();
  return match![2];
}

describe('Mock Auth — обработчики домена (C-101, IF-101)', () => {
  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    spyOn(console, 'info');
  });

  afterEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    resetMockDbSeed();
  });

  describe('auth.register — регистрация (§4.1)', () => {
    it('успех: создаёт student, ставит сессию, возвращает MeDto без пароля', () => {
      const { db, session } = setup(makeSeed());

      const me = handleAuthRegister(db, session, VALID_REGISTER, T0);

      expect(me).toEqual({
        login: 'student33',
        fullName: 'Петров Пётр Петрович',
        role: 'student',
        groupName: null,
      } satisfies MeDto);
      expect(Object.keys(me)).not.toContain('password');

      const users = db.read().users;
      const created = users.find((u) => u.login === 'student33');
      expect(created).withContext('пользователь создан в мок-БД').toBeDefined();
      expect(created!.role).toBe('student');
      expect(created!.groupId).toBeNull();
      expect(created!.password).toBe('Password#2026');
      expect(session.getUserId()).toBe(created!.id); // автоматический вход
    });

    it('пустая форма: 400 «Данные заполнены неверно» + «Заполните поле» у всех пяти полей', () => {
      const { db, session } = setup(makeSeed());
      const error = expectRejection(
        () =>
          handleAuthRegister(
            db,
            session,
            { fullName: '', login: '', email: '', password: '', repeatPassword: '' },
            T0,
          ),
        400,
        'Данные заполнены неверно',
      );
      expect(Object.keys(error.errors ?? {}).sort()).toEqual(
        ['email', 'fullName', 'login', 'password', 'repeatPassword'],
      );
      for (const field of ['email', 'fullName', 'login', 'password', 'repeatPassword']) {
        expect(error.errors![field]).toEqual(['Заполните поле']);
      }
      expect(db.read().users.length).toBe(3); // пользователь не создан
      expect(session.hasUser()).toBeFalse(); // сессия не ставилась
    });

    it('нарушение формата email: текст «Введите корректный email» из словаря', () => {
      const { db, session } = setup(makeSeed());
      const error = catchValidation(() =>
        handleAuthRegister(db, session, { ...VALID_REGISTER, email: 'not-an-email' }, T0),
      );
      expect(error.errors!['email']).toEqual(['Введите корректный email']);
    });

    it('логин с пробелом внутри: текст charset-правила; длинный логин: граница 100 после трима', () => {
      const { db, session } = setup(makeSeed());
      const charset = catchValidation(() =>
        handleAuthRegister(db, session, { ...VALID_REGISTER, login: 'student 33' }, T0),
      );
      expect(charset.errors!['login']).toEqual([
        'Логин может содержать только латинские буквы, цифры, точку, дефис и подчёркивание',
      ]);

      const long = catchValidation(() =>
        handleAuthRegister(db, session, { ...VALID_REGISTER, login: 'a'.repeat(101) }, T0),
      );
      expect(long.errors!['login']).toEqual(['Логин — от 1 до 100 символов']);
    });

    it('все нарушенные правила пароля возвращаются одновременно', () => {
      const { db, session } = setup(makeSeed());
      const error = catchValidation(() =>
        handleAuthRegister(db, session, { ...VALID_REGISTER, password: 'abc', repeatPassword: 'abc' }, T0),
      );
      expect(error.errors!['password']).toEqual([
        'Пароль должен содержать не менее 8 символов',
        'Пароль должен содержать хотя бы одну цифру',
        'Пароль должен содержать хотя бы один специальный знак',
      ]);
    });

    it('несовпадение повтора: текст у поля repeatPassword, сам пароль валиден', () => {
      const { db, session } = setup(makeSeed());
      const error = catchValidation(() =>
        handleAuthRegister(db, session, { ...VALID_REGISTER, repeatPassword: 'другой' }, T0),
      );
      expect(error.errors!['repeatPassword']).toEqual(['Пароли не совпадают']);
      expect(error.errors!['password']).toBeUndefined();
    });

    it('дубликат логина проверяется первым: оба дубликата заняты → текст про логин', () => {
      const { db, session } = setup(makeSeed());
      const usersBefore = db.read().users.length;
      const error = expectRejection(
        () =>
          handleAuthRegister(
            db,
            session,
            { ...VALID_REGISTER, login: 'student01', email: 'student01@example.com' },
            T0,
          ),
        409,
        'Пользователь с таким логином уже существует',
      );
      expect(db.read().users.length).toBe(usersBefore); // пользователь не создан
      expect(session.hasUser()).toBeFalse(); // сессия не менялась
    });

    it('дубликат email (логин свободен): «Пользователь с таким email уже существует»', () => {
      const { db, session } = setup(makeSeed());
      expectRejection(
        () =>
          handleAuthRegister(
            db,
            session,
            { ...VALID_REGISTER, email: 'student01@example.com' },
            T0,
          ),
        409,
        'Пользователь с таким email уже существует',
      );
    });

    it('уникальность без учёта регистра (ci): «Student01» и «STUDENT01@EXAMPLE.COM» — конфликты', () => {
      const { db, session } = setup(makeSeed());
      expectRejection(
        () => handleAuthRegister(db, session, { ...VALID_REGISTER, login: 'Student01' }, T0),
        409,
        'Пользователь с таким логином уже существует',
      );
      expectRejection(
        () =>
          handleAuthRegister(db, session, { ...VALID_REGISTER, login: 'fresh', email: 'STUDENT01@EXAMPLE.COM' }, T0),
        409,
        'Пользователь с таким email уже существует',
      );
    });

    it('лимит ≤5/час: шестая регистрация в окне → 429; после окна — снова доступна', () => {
      const { db, session } = setup(makeSeed());
      for (let i = 0; i < 5; i++) {
        handleAuthRegister(
          db,
          session,
          { ...VALID_REGISTER, login: `ratelimit${i}`, email: `ratelimit${i}@example.com` },
          T0 + i * MINUTE,
        );
      }
      expectRejection(
        () =>
          handleAuthRegister(
            db,
            session,
            { ...VALID_REGISTER, login: 'ratelimit5', email: 'ratelimit5@example.com' },
            T0 + 30 * MINUTE,
          ),
        429,
        'Слишком много попыток. Повторите позже',
      );
      // Скользящее окно: через 61 минуту первые метки выбыли — регистрация снова доступна.
      const me = handleAuthRegister(
        db,
        session,
        { ...VALID_REGISTER, login: 'ratelimit6', email: 'ratelimit6@example.com' },
        T0 + 61 * MINUTE,
      );
      expect(me.login).toBe('ratelimit6');
    });
  });

  describe('auth.login — вход (§4.1)', () => {
    const LOGIN_TEACHER: LoginParams = { login: 'teacher', password: 'teacher123!' };

    it('успех: MeDto с role teacher, сессия установлена', () => {
      const { db, session } = setup(makeSeed());
      const me = handleAuthLogin(db, session, LOGIN_TEACHER, T0);
      expect(me).toEqual({
        login: 'teacher',
        fullName: 'Сидоров Семён Семёнович',
        role: 'teacher',
        groupName: null,
      } satisfies MeDto);
      expect(session.getUserId()).toBe(TEACHER_ID);
    });

    it('логин без учёта регистра; имя группы студента резолвится в MeDto', () => {
      const { db, session } = setup(makeSeed());
      const me = handleAuthLogin(db, session, { ...LOGIN_TEACHER, login: 'Teacher' }, T0);
      expect(me.login).toBe('teacher'); // сохранённый регистр

      const student = handleAuthLogin(
        db,
        session,
        { login: 'student01', password: 'student123!' },
        T0,
      );
      expect(student.groupName).toBe('ИК-221');
    });

    it('любая неверная комбинация — единый 401 «Неверный логин или пароль»', () => {
      const { db, session } = setup(makeSeed());
      for (const params of [
        { login: 'teacher', password: 'неверный' }, // логин есть, пароль неверен
        { login: 'ghost', password: 'teacher123!' }, // логина нет
        { login: 'ghost', password: 'тоже-неверный' }, // неверны оба
        { login: '', password: '' }, // пустой вызов
      ]) {
        expectRejection(() => handleAuthLogin(db, session, params, T0), 401, 'Неверный логин или пароль');
      }
      expect(session.hasUser()).toBeFalse(); // сессия не ставилась
    });

    it('429 на шестой неуспешной попытке в минуту; успешный вход лимитом не считается', () => {
      const { db, session } = setup(makeSeed());
      for (let i = 0; i < 5; i++) {
        expectRejection(
          () => handleAuthLogin(db, session, { ...LOGIN_TEACHER, password: `плохой-${i}` }, T0 + i * 10_000),
          401,
          'Неверный логин или пароль',
        );
      }
      expectRejection(
        () => handleAuthLogin(db, session, { ...LOGIN_TEACHER, password: 'плохой-5' }, T0 + 50_000),
        429,
        'Слишком много попыток. Повторите позже',
      );
      // Успешные входы счётчиком неуспешных не считаются и не блокируются.
      const me = handleAuthLogin(db, session, LOGIN_TEACHER, T0 + 55_000);
      expect(me.role).toBe('teacher');
    });

    it('окно скользящее: через минуту счётчик неуспешных обнуляется — снова 401, а не 429', () => {
      const { db, session } = setup(makeSeed());
      for (let i = 0; i < 5; i++) {
        expectRejection(
          () => handleAuthLogin(db, session, { ...LOGIN_TEACHER, password: `плохой-${i}` }, T0 + i * 10_000),
          401,
          'Неверный логин или пароль',
        );
      }
      // Первая метка (T0) выбыла из окна: T0+61с − 60с < T0+10с.
      expectRejection(
        () => handleAuthLogin(db, session, { ...LOGIN_TEACHER, password: 'плохой-5' }, T0 + 61_000),
        401,
        'Неверный логин или пароль',
      );
    });

    it('счётчик неуспешных — по логину без учёта регистра', () => {
      const { db, session } = setup(makeSeed());
      for (let i = 0; i < 5; i++) {
        expectRejection(
          () => handleAuthLogin(db, session, { login: 'Teacher', password: `плохой-${i}` }, T0 + i * 1000),
          401,
          'Неверный логин или пароль',
        );
      }
      expectRejection(
        () => handleAuthLogin(db, session, { login: 'TEACHER', password: 'плохой-5' }, T0 + 6000),
        429,
        'Слишком много попыток. Повторите позже',
      );
    });
  });

  describe('auth.me / auth.logout — сессия (IF-101)', () => {
    it('me по сессии: MeDto студента с groupName; студента без группы — groupName null', () => {
      const { db, session } = setup(makeSeed());
      session.setUserId(STUDENT_ID);
      expect(handleAuthMe(db, session)).toEqual({
        login: 'student01',
        fullName: 'Иванов Иван Иванович 01',
        role: 'student',
        groupName: 'ИК-221',
      } satisfies MeDto);

      session.setUserId(STUDENT_NO_GROUP_ID);
      expect(handleAuthMe(db, session).groupName).toBeNull();
    });

    it('me без сессии: 401 «Не авторизован»', () => {
      const { db, session } = setup(makeSeed());
      expectRejection(() => handleAuthMe(db, session), 401, 'Не авторизован');
    });

    it('me с битой сессией (пользователя нет в БД): 401 и сессия очищена', () => {
      const { db, session } = setup(makeSeed());
      session.setUserId('ffffffff-ffff-4fff-8fff-ffffffffffff');
      expectRejection(() => handleAuthMe(db, session), 401, 'Не авторизован');
      expect(session.hasUser()).withContext('битая сессия очищена').toBeFalse();
    });

    it('logout очищает сессию; без сессии — тоже успех', () => {
      const { session } = setup(makeSeed());
      session.setUserId(STUDENT_ID);
      expect(handleAuthLogout(session)).toBeNull();
      expect(session.hasUser()).toBeFalse();

      expect(handleAuthLogout(session)).toBeNull(); // повторный выход без сессии
    });
  });

  describe('auth.recovery.request — запрос кода (§4.2)', () => {
    it('зарегистрированный email: код сохранён — 6 цифр, TTL ровно 10 минут', () => {
      const { db } = setup(makeSeed());
      handleAuthRecoveryRequest(db, { email: 'Student01@Example.com' }, T0);

      const codes = db.read().recoveryCodes;
      expect(codes.length).toBe(1);
      const code = codes[0]!;
      expect(code.userId).toBe(STUDENT_ID);
      expect(code.code).toMatch(/^\d{6}$/);
      expect(code.usedAt).toBeNull();
      expect(code.attempts).toBe(0);
      expect(new Date(code.expiresAt).getTime()).toBe(T0 + 10 * MINUTE);
    });

    it('незарегистрированный email: 200 и код в консоли, но в БД код не сохраняется', () => {
      const { db } = setup(makeSeed());
      expect(handleAuthRecoveryRequest(db, { email: 'ghost@example.com' }, T0)).toBeNull();

      const logged = lastLoggedCode();
      expect(logged.email).toBe('ghost@example.com');
      expect(logged.code).toMatch(/^\d{6}$/);
      expect(db.read().recoveryCodes).toEqual([]); // существование email не раскрывается
    });

    it('формат «письма»: [mock-email] Код восстановления для <email>: <код>', () => {
      const { db } = setup(makeSeed());
      handleAuthRecoveryRequest(db, { email: 'student01@example.com' }, T0);
      const logged = lastLoggedCode();
      expect(logged.email).toBe('student01@example.com');
      // Код из консоли совпадает с сохранённым (одно «письмо» — одна запись).
      expect(db.read().recoveryCodes[0]!.code).toBe(logged.code);
    });

    it('лимит ≤3/час на email (ci): четвёртый запрос → 429; после часа — доступен', () => {
      const { db } = setup(makeSeed());
      handleAuthRecoveryRequest(db, { email: 'student01@example.com' }, T0);
      handleAuthRecoveryRequest(db, { email: 'STUDENT01@Example.com' }, T0 + 10 * MINUTE);
      handleAuthRecoveryRequest(db, { email: 'student01@example.com' }, T0 + 20 * MINUTE);
      expectRejection(
        () => handleAuthRecoveryRequest(db, { email: 'Student01@example.com' }, T0 + 30 * MINUTE),
        429,
        'Слишком много попыток. Повторите позже',
      );
      // Окно скользящее: первая метка выбыла — запрос снова 200.
      expect(
        handleAuthRecoveryRequest(db, { email: 'student01@example.com' }, T0 + 61 * MINUTE),
      ).toBeNull();
    });

    it('лимит считается по каждому email отдельно', () => {
      const { db } = setup(makeSeed());
      for (let i = 0; i < 3; i++) {
        handleAuthRecoveryRequest(db, { email: `user${i}@example.com` }, T0);
      }
      expect(handleAuthRecoveryRequest(db, { email: 'student01@example.com' }, T0)).toBeNull();
    });

    it('аменда 9 (C): переотправка гасит прежний код — старый код после resend даёт 400, новый подтверждается', () => {
      const { db } = setup(makeSeed());
      handleAuthRecoveryRequest(db, { email: 'student01@example.com' }, T0);
      const firstCode = loggedCodeAt(0);
      handleAuthRecoveryRequest(db, { email: 'student01@example.com' }, T0 + MINUTE);
      const secondCode = loggedCodeAt(1);

      const codes = db.read().recoveryCodes;
      expect(codes.length).toBe(2);
      expect(codes.find((c) => c.code === firstCode)!.usedAt)
        .withContext('прежний код погашен переотправкой (usedAt)')
        .not.toBeNull();
      expect(codes.find((c) => c.code === secondCode)!.usedAt)
        .withContext('новый код жив — единственный')
        .toBeNull();

      expectRejection(
        () =>
          handleAuthRecoveryConfirm(db, { email: 'student01@example.com', code: firstCode }, T0 + 2 * MINUTE),
        400,
        'Код восстановления не подходит',
      );
      const result = handleAuthRecoveryConfirm(
        db,
        { email: 'student01@example.com', code: secondCode },
        T0 + 2 * MINUTE,
      );
      expect(result.resetToken).withContext('новый код подтверждается').toBeDefined();
    });

    it('аменда 9 (C): просроченный прежний код при переотправке не гасится повторно, TTL нового кода — от нового момента', () => {
      const { db } = setup(makeSeed());
      handleAuthRecoveryRequest(db, { email: 'student01@example.com' }, T0);
      const firstCode = loggedCodeAt(0);
      // К моменту resend первый код уже просрочен (TTL 10 минут истёк).
      handleAuthRecoveryRequest(db, { email: 'student01@example.com' }, T0 + 11 * MINUTE);
      const secondCode = loggedCodeAt(1);

      const codes = db.read().recoveryCodes;
      const expired = codes.find((c) => c.code === firstCode)!;
      const fresh = codes.find((c) => c.code === secondCode)!;
      expect(expired.usedAt).withContext('просроченный код не трогается').toBeNull();
      expect(new Date(fresh.expiresAt).getTime()).toBe(T0 + 11 * MINUTE + 10 * MINUTE);
      expect(
        codes.filter((c) => c.usedAt === null && new Date(c.expiresAt).getTime() > T0 + 11 * MINUTE).length,
      ).withContext('живых кодов — максимум один').toBe(1);
    });
  });

  describe('auth.recovery.confirm — подтверждение кода (§4.2)', () => {
    const CODE = '123456';
    const CONFIRM = { email: 'student01@example.com', code: CODE };

    function confirmResult(db: MockDb): ConfirmRecoveryResult {
      return handleAuthRecoveryConfirm(db, CONFIRM, T0);
    }

    it('верный код: resetToken с TTL 15 минут, код погашен, токен сохранён в БД', () => {
      const { db } = setup(seedWithCode(CODE, T0 + 10 * MINUTE));
      const result = confirmResult(db);

      expect(result.resetToken).toMatch(/^\S+$/);
      const tokens = db.read().resetTokens;
      expect(tokens.length).toBe(1);
      const token = tokens[0]!;
      expect(token.token).toBe(result.resetToken);
      expect(token.userId).toBe(STUDENT_ID);
      expect(token.usedAt).toBeNull();
      expect(new Date(token.expiresAt).getTime()).toBe(T0 + 15 * MINUTE);
      expect(db.read().recoveryCodes[0]!.usedAt).withContext('код одноразовый').not.toBeNull();
    });

    it('верный код на границе TTL: за мгновение до 10 минут — успех, ровно на границе — отказ', () => {
      const { db: fresh } = setup(seedWithCode(CODE, T0 + 10 * MINUTE));
      const result = handleAuthRecoveryConfirm(fresh, CONFIRM, T0 + 10 * MINUTE - 1);
      expect(result.resetToken).toBeDefined();

      const { db } = setup(seedWithCode(CODE, T0 + 10 * MINUTE));
      expectRejection(
        () => handleAuthRecoveryConfirm(db, CONFIRM, T0 + 10 * MINUTE),
        400,
        'Код восстановления не подходит',
      );
    });

    it('неверный код: 400, счётчик попыток растёт; верный код на 4-й попытке ещё работает', () => {
      const { db } = setup(seedWithCode(CODE, T0 + 10 * MINUTE));
      for (let i = 1; i <= 4; i++) {
        expectRejection(
          () => handleAuthRecoveryConfirm(db, { email: 'student01@example.com', code: '000000' }, T0 + i),
          400,
          'Код восстановления не подходит',
        );
        expect(db.read().recoveryCodes[0]!.attempts).toBe(i);
      }
      expect(confirmResult(db).resetToken).withContext('после 4 неудач код ещё жив').toBeDefined();
    });

    it('аннулирование: пятая неверная попытка гасит код — дальше верный код тоже отвергается', () => {
      const { db } = setup(seedWithCode(CODE, T0 + 10 * MINUTE));
      for (let i = 1; i <= 5; i++) {
        expectRejection(
          () => handleAuthRecoveryConfirm(db, { email: 'student01@example.com', code: '999999' }, T0 + i),
          400,
          'Код восстановления не подходит',
        );
      }
      const code = db.read().recoveryCodes[0]!;
      expect(code.attempts).toBe(5);
      expect(code.usedAt).withContext('на 5-й неверной попытке код аннулирован').not.toBeNull();
      expectRejection(
        () => confirmResult(db),
        400,
        'Код восстановления не подходит',
      );
    });

    it('незарегистрированный email: тот же 400 «Код восстановления не подходит», токен не создаётся', () => {
      const { db } = setup(makeSeed());
      expectRejection(
        () => handleAuthRecoveryConfirm(db, { email: 'ghost@example.com', code: '123456' }, T0),
        400,
        'Код восстановления не подходит',
      );
      expect(db.read().resetTokens).toEqual([]);
    });

    it('одноразовость: повторное подтверждение тем же кодом после успеха — 400', () => {
      const { db } = setup(seedWithCode(CODE, T0 + 10 * MINUTE));
      confirmResult(db);
      expectRejection(
        () => handleAuthRecoveryConfirm(db, CONFIRM, T0 + 1),
        400,
        'Код восстановления не подходит',
      );
      expect(db.read().resetTokens.length).toBe(1); // второй токен не создан
    });
  });

  describe('auth.reset-password — сброс пароля (§4.2)', () => {
    const TOKEN = 'reset-token-abcdef';
    const RESET: ResetPasswordParams = {
      resetToken: TOKEN,
      password: 'NewPassword#2026',
      confirmPassword: 'NewPassword#2026',
    };

    it('успех: пароль сменён, токен погашен, вход с новым паролем работает', () => {
      const { db, session } = setup(seedWithToken(TOKEN, T0 + 15 * MINUTE));
      expect(handleAuthResetPassword(db, session, RESET, T0)).toBeNull();

      const student = db.read().users.find((u) => u.id === STUDENT_ID)!;
      expect(student.password).toBe('NewPassword#2026');
      expect(db.read().resetTokens[0]!.usedAt).withContext('токен использован').not.toBeNull();

      const me = handleAuthLogin(db, session, { login: 'student01', password: 'NewPassword#2026' }, T0 + 1);
      expect(me.role).toBe('student'); // behavioral: новый пароль действителен
    });

    it('аменда 9 (D): успех отзывает сессию самого сбрасывающего пользователя (isAuthenticated → false)', () => {
      const { db, session } = setup(seedWithToken(TOKEN, T0 + 15 * MINUTE));
      session.setUserId(STUDENT_ID); // пользователь сбрасывал пароль, будучи «вошедшим»
      expect(handleAuthResetPassword(db, session, RESET, T0)).toBeNull();
      expect(session.hasUser()).withContext('сессия сбрасывающего очищена').toBeFalse();
      expect(session.getUserId()).toBeNull();
    });

    it('аменда 9 (D): сессия другого пользователя при сбросе не затрагивается', () => {
      const { db, session } = setup(seedWithToken(TOKEN, T0 + 15 * MINUTE));
      session.setUserId(TEACHER_ID); // преподаватель вошёл, сбрасывает пароль студента
      expect(handleAuthResetPassword(db, session, RESET, T0)).toBeNull();
      expect(session.getUserId()).withContext('чужая сессия сохранена').toBe(TEACHER_ID);
    });

    it('аменда 9 (D): без сессии сброс проходит и сессию не создаёт', () => {
      const { db, session } = setup(seedWithToken(TOKEN, T0 + 15 * MINUTE));
      expect(handleAuthResetPassword(db, session, RESET, T0)).toBeNull();
      expect(session.hasUser()).toBeFalse();
    });

    it('успех гасит и другие живые токены того же пользователя', () => {
      const seed = seedWithToken(TOKEN, T0 + 15 * MINUTE);
      seed.resetTokens.push({
        id: 'eeeeeeee-5555-4555-8555-555555555556',
        userId: STUDENT_ID,
        token: 'второй-токен',
        expiresAt: new Date(T0 + 15 * MINUTE).toISOString(),
        usedAt: null,
        createdAt: new Date(T0).toISOString(),
      });
      const { db, session } = setup(seed);
      handleAuthResetPassword(db, session, RESET, T0);
      for (const token of db.read().resetTokens) {
        expect(token.usedAt).not.toBeNull();
      }
    });

    it('неверный токен: 400 «Ссылка восстановления недействительна или истекла», пароль не изменён', () => {
      const { db, session } = setup(seedWithToken(TOKEN, T0 + 15 * MINUTE));
      expectRejection(
        () => handleAuthResetPassword(db, session, { ...RESET, resetToken: 'не-токен' }, T0),
        400,
        'Ссылка восстановления недействительна или истекла',
      );
      expect(db.read().users.find((u) => u.id === STUDENT_ID)!.password).toBe('student123!');
    });

    it('просроченный токен: 400 и токен погашен (граница TTL 15 минут включена)', () => {
      const { db, session } = setup(seedWithToken(TOKEN, T0 + 15 * MINUTE));
      expectRejection(
        () => handleAuthResetPassword(db, session, RESET, T0 + 15 * MINUTE),
        400,
        'Ссылка восстановления недействительна или истекла',
      );
      expect(db.read().resetTokens[0]!.usedAt).withContext('просроченный токен гасится').not.toBeNull();
    });

    it('использованный токен: 400, повторный сброс невозможен', () => {
      const { db, session } = setup(seedWithToken(TOKEN, T0 + 15 * MINUTE, new Date(T0 - 1).toISOString()));
      expectRejection(
        () => handleAuthResetPassword(db, session, RESET, T0),
        400,
        'Ссылка восстановления недействительна или истекла',
      );
      expect(db.read().users.find((u) => u.id === STUDENT_ID)!.password).toBe('student123!');
    });

    it('слабый пароль: 400 «Данные заполнены неверно» + тексты правил; токен при этом не гасится', () => {
      const { db, session } = setup(seedWithToken(TOKEN, T0 + 15 * MINUTE));
      const error = catchValidation(() =>
        handleAuthResetPassword(db, session, { ...RESET, password: 'abc', confirmPassword: 'abc' }, T0),
      );
      expect(error.status).toBe(400);
      expect(error.message).toBe('Данные заполнены неверно');
      expect(error.errors!['password']).toEqual([
        'Пароль должен содержать не менее 8 символов',
        'Пароль должен содержать хотя бы одну цифру',
        'Пароль должен содержать хотя бы один специальный знак',
      ]);
      expect(db.read().resetTokens[0]!.usedAt).withContext('токен жив — можно исправить пароль').toBeNull();

      // Повтор с корректным паролем тем же токеном проходит.
      expect(handleAuthResetPassword(db, session, RESET, T0 + 1)).toBeNull();
    });

    it('несовпадение повтора: текст «Пароли не совпадают» у repeatPassword, пароль не изменён', () => {
      const { db, session } = setup(seedWithToken(TOKEN, T0 + 15 * MINUTE));
      const error = catchValidation(() =>
        handleAuthResetPassword(db, session, { ...RESET, confirmPassword: 'другой' }, T0),
      );
      expect(error.errors!['repeatPassword']).toEqual(['Пароли не совпадают']);
      expect(db.read().users.find((u) => u.id === STUDENT_ID)!.password).toBe('student123!');
    });
  });

  describe('registerAuthHandlers — подключение к конвейеру MockApiClient', () => {
    it('все семь методов auth.* зарегистрированы и отвечают через call (задержка ~500 мс)', fakeAsync(() => {
      configureMockDbSeed(() => makeSeed());
      const client = new MockApiClient();
      let clock = T0;
      registerAuthHandlers(client, () => clock);
      const session = new SessionStore();

      const done: unknown[] = [];
      client.call<MeDto>('auth.login', { login: 'teacher', password: 'teacher123!' }).then((r) => done.push(r));
      client.call<null>('auth.logout', null).then((r) => done.push(r));
      client.call<MeDto>('auth.register', { ...VALID_REGISTER, login: 'pipeline', email: 'pipeline@example.com' }).then((r) => done.push(r));
      client.call<MeDto>('auth.me', null).then((r) => done.push(r));
      client.call<null>('auth.recovery.request', { email: 'ghost@example.com' }).then((r) => done.push(r));
      client.call<ConfirmRecoveryResult>('auth.recovery.confirm', { email: 'x', code: 'y' }).catch(() => done.push('confirm-400'));
      client.call<null>('auth.reset-password', { resetToken: 'x', password: 'y', confirmPassword: 'y' }).catch(() => done.push('reset-400'));

      tick(500);
      expect(done.length).toBe(7);
      expect(session.getUserId()).withContext('login/register ставят сессию через общий ключ').not.toBeNull();
    }));

    it('401 входа выходит из конвейера транспортной формой ApiError (IF-001)', fakeAsync(() => {
      configureMockDbSeed(() => makeSeed());
      const client = new MockApiClient();
      registerAuthHandlers(client, () => T0);

      let rejection: unknown = null;
      client.call<MeDto>('auth.login', { login: 'teacher', password: 'неверный' }).catch((e: unknown) => (rejection = e));
      tick(500);

      expect(rejection).toEqual({
        status: 401,
        body: { message: 'Неверный логин или пароль' },
      } satisfies ApiError);
    }));

    it('инжектируемые часы: код, созданный на T0, жив за мгновение до TTL и истёк ровно на границе', fakeAsync(() => {
      // Часть A: подтверждение на T0 + 10 мин − 1 мс — код ещё жив.
      configureMockDbSeed(() => makeSeed());
      const clientA = new MockApiClient();
      let clockA = T0;
      registerAuthHandlers(clientA, () => clockA);
      let logged: { email: string; code: string } | null = null;
      (console.info as jasmine.Spy).and.callFake((line: unknown) => {
        const match = /\[mock-email\] Код восстановления для (.+): (\d{6})$/.exec(String(line));
        if (match !== null) {
          logged = { email: match[1], code: match[2] };
        }
      });

      clientA.call<null>('auth.recovery.request', { email: 'student01@example.com' }).then();
      tick(500);
      const code = (logged as { email: string; code: string } | null)!.code;

      clockA = T0 + 10 * MINUTE - 1;
      let before: unknown = null;
      clientA
        .call<ConfirmRecoveryResult>('auth.recovery.confirm', { email: 'student01@example.com', code })
        .then((r) => (before = r));
      tick(500);
      expect((before as ConfirmRecoveryResult).resetToken).toBeDefined();
    }));

    it('инжектируемые часы: если бы «создание» шло по реальным часам, код на T0+10 мин был бы жив — он истёк', fakeAsync(() => {
      // Часть B: подтверждение ровно на T0 + 10 мин — отказ. Детерминизм:
      // и создание, и подтверждение идут по одним инжектируемым часам.
      configureMockDbSeed(() => makeSeed());
      const client = new MockApiClient();
      let clock = T0;
      registerAuthHandlers(client, () => clock);
      let logged: { email: string; code: string } | null = null;
      (console.info as jasmine.Spy).and.callFake((line: unknown) => {
        const match = /\[mock-email\] Код восстановления для (.+): (\d{6})$/.exec(String(line));
        if (match !== null) {
          logged = { email: match[1], code: match[2] };
        }
      });

      client.call<null>('auth.recovery.request', { email: 'student01@example.com' }).then();
      tick(500);
      const code = (logged as { email: string; code: string } | null)!.code;

      clock = T0 + 10 * MINUTE;
      let rejection: unknown = null;
      client
        .call<ConfirmRecoveryResult>('auth.recovery.confirm', { email: 'student01@example.com', code })
        .catch((e: unknown) => (rejection = e));
      tick(500);
      expect((rejection as ApiError).body.message).toBe('Код восстановления не подходит');
    }));
  });
});
