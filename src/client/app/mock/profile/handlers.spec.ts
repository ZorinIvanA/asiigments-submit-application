/**
 * Юнит-тесты обработчиков домена Profile (C-106, IF-107, FR-4.7/US-18):
 * profile.get (обе роли, студент с группой и без, актуальность groupName),
 * profile.update (400 по полям, 409 «Пользователь с таким email уже
 * существует», успех с изменением данных пользователя), profile.changePassword
 * (неверный текущий, слабый новый, несовпадение повтора, успех с реальной
 * сменой пароля в мок-БД), 401 «Не авторизован» без/с битой сессией.
 *
 * Изоляция — паттерн mock-db.spec.ts: очистка хранилищ в beforeEach/afterEach,
 * сид напрямую через ключ mock.db.v1, сессия выставляется напрямую в
 * localStorage по ключу mock.session.userId (то же значение читает
 * SessionStore домена Auth).
 * auth.me/auth.login в проверках «отражается в данных пользователя» и
 * «вход со старым паролем невозможен» эмулируются локально по контракту
 * IF-101 (те же коллекции mock.db.v1 и ci-сравнение логина), чтобы тесты
 * домена Profile оставались изолированными от остальных доменов.
 */
import { fakeAsync, tick } from '@angular/core/testing';

import { ApiError, Group, ProfileDto, STORAGE_KEYS, User } from '../../shared/models';
import { newRuntimeId } from '../ids';
import { MockApiClient } from '../mock-api-client';
import { emptyMockDbData, MockDbData } from '../mock-db';
import { registerProfileHandlers } from './handlers';

const SESSION_KEY = STORAGE_KEYS.session;
const DB_KEY = STORAGE_KEYS.mockDb;

const TEACHER_PASSWORD = 'Teacher#2026';
const STUDENT_PASSWORD = 'Student#2026';

let client: MockApiClient;
let userSeq = 0;

function makeUser(overrides: Partial<User> = {}): User {
  userSeq += 1;
  const login = overrides.login ?? `user${String(userSeq).padStart(2, '0')}`;
  return {
    id: newRuntimeId(),
    login,
    email: `${login}@example.com`,
    fullName: `Пользователь ${login}`,
    role: 'student',
    groupId: null,
    password: STUDENT_PASSWORD,
    ...overrides,
  };
}

function makeTeacher(): User {
  return makeUser({
    login: 'teacher',
    email: 'teacher@example.com',
    fullName: 'Сидоров Семён Семёнович',
    role: 'teacher',
    password: TEACHER_PASSWORD,
  });
}

function makeGroup(name: string): Group {
  return { id: newRuntimeId(), name, studentCount: 0 };
}

/** Сид мок-БД напрямую через ключ (до первого обращения клиента). */
function seedDb(patch: Partial<MockDbData> = {}): void {
  const data: MockDbData = { ...emptyMockDbData(), ...patch };
  localStorage.setItem(DB_KEY, JSON.stringify(data));
}

function stored(): MockDbData {
  return JSON.parse(localStorage.getItem(DB_KEY) ?? 'null') as MockDbData;
}

function loginAs(userId: string | null): void {
  if (userId === null) {
    localStorage.removeItem(SESSION_KEY);
  } else {
    localStorage.setItem(SESSION_KEY, userId);
  }
}

/** Мок-вызов с задержкой конвейера; в fakeAsync завершается tick'ом 500 мс. */
function call<T>(method: string, params: unknown): { resolved?: T; error?: ApiError } {
  const outcome: { resolved?: T; error?: ApiError } = {};
  client.call<T>(method, params).then(
    (value: T) => {
      outcome.resolved = value;
    },
    (error: unknown) => {
      outcome.error = error as ApiError;
    },
  );
  tick(500);
  return outcome;
}

/** Эмуляция auth.login (IF-101): ci-логин + дословное сравнение пароля. */
function emulatedLogin(login: string, password: string): User | null {
  const user = stored().users.find(
    (candidate) => candidate.login.toLowerCase() === login.toLowerCase(),
  );
  return user !== undefined && user.password === password ? user : null;
}

/** Эмуляция auth.me (IF-101): проекция пользователя сессии из mock.db.v1. */
function emulatedMe(): { login: string; email: string; fullName: string; role: string; groupName: string | null } {
  const data = stored();
  const user = data.users.find((candidate) => candidate.id === localStorage.getItem(SESSION_KEY));
  const groupName =
    user === undefined || user.groupId === null
      ? null
      : (data.groups.find((group) => group.id === user.groupId)?.name ?? null);
  return {
    login: user?.login ?? '',
    email: user?.email ?? '',
    fullName: user?.fullName ?? '',
    role: user?.role ?? 'student',
    groupName,
  };
}

describe('Обработчики profile — домен Profile (C-106, IF-107)', () => {
  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    client = new MockApiClient();
    registerProfileHandlers(client);
  });

  afterEach(() => {
    localStorage.clear();
    sessionStorage.clear();
  });

  describe('profile.get', () => {
    it('преподаватель: role=teacher, groupName=null, логин/email/ФИО учителя (AC get-teacher)', fakeAsync(() => {
      const teacher = makeTeacher();
      seedDb({ users: [teacher] });
      loginAs(teacher.id);

      const { resolved } = call<ProfileDto>('profile.get', null);

      expect(resolved).toEqual({
        login: 'teacher',
        email: 'teacher@example.com',
        fullName: 'Сидоров Семён Семёнович',
        role: 'teacher',
        groupName: null,
      });
    }));

    it('студент с группой: groupName — имя группы по group_id', fakeAsync(() => {
      const group = makeGroup('ИК-221');
      const student = makeUser({ groupId: group.id });
      seedDb({ users: [student], groups: [group] });
      loginAs(student.id);

      const { resolved } = call<ProfileDto>('profile.get', null);

      expect(resolved).toEqual({
        login: student.login,
        email: student.email,
        fullName: student.fullName,
        role: 'student',
        groupName: 'ИК-221',
      });
    }));

    it('студент без группы: groupName=null; висячий group_id тоже даёт null', fakeAsync(() => {
      const withoutGroup = makeUser();
      const dangling = makeUser({ groupId: newRuntimeId() });
      seedDb({ users: [withoutGroup, dangling] });
      loginAs(withoutGroup.id);
      expect(call<ProfileDto>('profile.get', null).resolved?.groupName).toBeNull();

      loginAs(dangling.id);
      expect(call<ProfileDto>('profile.get', null).resolved?.groupName).toBeNull();
    }));

    it('без сессии — 401 «Не авторизован»', fakeAsync(() => {
      seedDb({ users: [makeTeacher()] });
      loginAs(null);

      const { error } = call<ProfileDto>('profile.get', null);

      expect(error?.status).toBe(401);
      expect(error?.body.message).toBe('Не авторизован');
    }));

    it('битая сессия (пользователь удалён) — 401 «Не авторизован»', fakeAsync(() => {
      seedDb({ users: [makeTeacher()] });
      loginAs(newRuntimeId());

      const { error } = call<ProfileDto>('profile.get', null);

      expect(error?.status).toBe(401);
      expect(error?.body.message).toBe('Не авторизован');
    }));
  });

  describe('profile.update', () => {
    it('успех: ФИО и email изменены в мок-БД (триммированы), DTO обновлён, прочие поля не тронуты', fakeAsync(() => {
      const group = makeGroup('ИК-221');
      const student = makeUser({ groupId: group.id });
      seedDb({ users: [student], groups: [group] });
      loginAs(student.id);

      const { resolved, error } = call<ProfileDto>('profile.update', {
        fullName: '  Петров Пётр Петрович  ',
        email: '  petr@example.com  ',
      });

      expect(error).toBeUndefined();
      expect(resolved).toEqual({
        login: student.login,
        email: 'petr@example.com',
        fullName: 'Петров Пётр Петрович',
        role: 'student',
        groupName: 'ИК-221',
      });
      const savedUser = stored().users[0];
      expect(savedUser.fullName).toBe('Петров Пётр Петрович');
      expect(savedUser.email).toBe('petr@example.com');
      expect(savedUser.login).toBe(student.login);
      expect(savedUser.role).toBe('student');
      expect(savedUser.groupId).toBe(group.id);
      expect(savedUser.password).toBe(STUDENT_PASSWORD);
    }));

    it('изменения отражаются в данных пользователя, которые читает auth.me', fakeAsync(() => {
      const student = makeUser();
      seedDb({ users: [student] });
      loginAs(student.id);

      call<ProfileDto>('profile.update', { fullName: 'Новое ФИО', email: 'new@example.com' });

      expect(emulatedMe()).toEqual({
        login: student.login,
        email: 'new@example.com',
        fullName: 'Новое ФИО',
        role: 'student',
        groupName: null,
      });
    }));

    it('400 «Данные заполнены неверно» + errors {fullName, email}: пустое ФИО и email не формата; профиль не изменён', fakeAsync(() => {
      const student = makeUser();
      seedDb({ users: [student] });
      loginAs(student.id);

      const { error } = call<ProfileDto>('profile.update', { fullName: '   ', email: 'не-почта' });

      expect(error?.status).toBe(400);
      expect(error?.body.message).toBe('Данные заполнены неверно');
      expect(error?.body.errors?.['fullName']).toEqual(['Заполните поле']);
      expect(error?.body.errors?.['email']).toEqual(['Введите корректный email']);
      expect(stored().users[0]).toEqual(student);
    }));

    it('400: ФИО длиннее 200 символов — текст словаря у поля fullName', fakeAsync(() => {
      const student = makeUser();
      seedDb({ users: [student] });
      loginAs(student.id);

      const { error } = call<ProfileDto>('profile.update', {
        fullName: 'Ф'.repeat(201),
        email: student.email,
      });

      expect(error?.status).toBe(400);
      expect(error?.body.errors?.['fullName']).toEqual(['ФИО — от 1 до 200 символов']);
    }));

    it('email занят другим пользователем — 409 «Пользователь с таким email уже существует», профиль не изменён (AC update-email-conflict)', fakeAsync(() => {
      const teacher = makeTeacher();
      const student = makeUser({ email: 'student01@example.com' });
      seedDb({ users: [teacher, student] });
      loginAs(student.id);

      const { error } = call<ProfileDto>('profile.update', {
        fullName: 'Другое ФИО',
        email: 'TEACHER@EXAMPLE.COM', // занят учителем, без учёта регистра
      });

      expect(error?.status).toBe(409);
      expect(error?.body.message).toBe('Пользователь с таким email уже существует');
      expect(error?.body.errors).toBeUndefined();
      expect(stored().users.find((candidate) => candidate.id === student.id)).toEqual(student);
    }));

    it('собственный email (даже в другом регистре) конфликтом не считается', fakeAsync(() => {
      const student = makeUser({ email: 'student01@example.com' });
      seedDb({ users: [student] });
      loginAs(student.id);

      const { resolved, error } = call<ProfileDto>('profile.update', {
        fullName: 'Новое ФИО',
        email: 'STUDENT01@example.com',
      });

      expect(error).toBeUndefined();
      expect(resolved?.email).toBe('STUDENT01@example.com');
    }));

    it('groupName в ответе актуализируется: переименование группы видно без пересоздания сессии', fakeAsync(() => {
      const group = makeGroup('ИК-221');
      const student = makeUser({ groupId: group.id });
      seedDb({ users: [student], groups: [group] });
      loginAs(student.id);

      // Кэш БД гидратирован первым вызовом; переименование — мутация того же
      // экземпляра MockDb (тестовый обработчик, как в mock-api-client.spec).
      client.register('test.renameGroup', (db, params: { id: string; name: string }) =>
        db.mutate((data) => {
          const renamed = data.groups.find((candidate) => candidate.id === params.id);
          if (renamed !== undefined) {
            renamed.name = params.name;
          }
          return renamed?.name ?? null;
        }),
      );
      call<ProfileDto>('profile.get', null);
      call('test.renameGroup', { id: group.id, name: 'ИК-931' });

      const { resolved } = call<ProfileDto>('profile.update', {
        fullName: student.fullName,
        email: student.email,
      });

      expect(resolved?.groupName).toBe('ИК-931');
      expect(emulatedMe().groupName).toBe('ИК-931');
    }));

    it('без сессии — 401 «Не авторизован», данные не изменяются', fakeAsync(() => {
      const student = makeUser();
      seedDb({ users: [student] });
      loginAs(null);

      const { error } = call<ProfileDto>('profile.update', { fullName: 'ФИО', email: 'x@example.com' });

      expect(error?.status).toBe(401);
      expect(error?.body.message).toBe('Не авторизован');
      expect(stored().users[0]).toEqual(student);
    }));
  });

  describe('profile.changePassword', () => {
    const NEW_PASSWORD = 'NewPass#2027';

    it('неверный текущий пароль — 400 «Неверный текущий пароль», пароль не изменён (AC change-password-wrong-current)', fakeAsync(() => {
      const student = makeUser();
      seedDb({ users: [student] });
      loginAs(student.id);

      const { error } = call<void>('profile.changePassword', {
        currentPassword: 'Точно-не-пароль',
        password: NEW_PASSWORD,
        confirmPassword: NEW_PASSWORD,
      });

      expect(error?.status).toBe(400);
      expect(error?.body.message).toBe('Неверный текущий пароль');
      expect(error?.body.errors).toBeUndefined();
      expect(stored().users[0].password).toBe(STUDENT_PASSWORD);
    }));

    it('неверный текущий приоритетнее полевых ошибок нового пароля: единственная ошибка — «Неверный текущий пароль»', fakeAsync(() => {
      const student = makeUser();
      seedDb({ users: [student] });
      loginAs(student.id);

      const { error } = call<void>('profile.changePassword', {
        currentPassword: 'неверный',
        password: 'слабый',
        confirmPassword: 'другой',
      });

      expect(error?.status).toBe(400);
      expect(error?.body.message).toBe('Неверный текущий пароль');
      expect(error?.body.errors).toBeUndefined();
    }));

    it('слабый новый пароль — 400 errors {password} со всеми нарушенными правилами; пароль не изменён', fakeAsync(() => {
      const student = makeUser();
      seedDb({ users: [student] });
      loginAs(student.id);

      const { error } = call<void>('profile.changePassword', {
        currentPassword: STUDENT_PASSWORD,
        password: 'abcdefgh',
        confirmPassword: 'abcdefgh',
      });

      expect(error?.status).toBe(400);
      expect(error?.body.message).toBe('Данные заполнены неверно');
      expect(error?.body.errors?.['password']).toEqual([
        'Пароль должен содержать хотя бы одну цифру',
        'Пароль должен содержать хотя бы один специальный знак',
      ]);
      expect(error?.body.errors?.['confirmPassword']).toBeUndefined();
      expect(stored().users[0].password).toBe(STUDENT_PASSWORD);
    }));

    it('пустые новый пароль и повтор — errors {password, confirmPassword} с «Заполните поле»', fakeAsync(() => {
      const student = makeUser();
      seedDb({ users: [student] });
      loginAs(student.id);

      const { error } = call<void>('profile.changePassword', {
        currentPassword: STUDENT_PASSWORD,
        password: '',
        confirmPassword: '',
      });

      expect(error?.body.errors?.['password']).toEqual(['Заполните поле']);
      expect(error?.body.errors?.['confirmPassword']).toEqual(['Заполните поле']);
    }));

    it('несовпадение повтора — errors {confirmPassword: [«Пароли не совпадают»]}', fakeAsync(() => {
      const student = makeUser();
      seedDb({ users: [student] });
      loginAs(student.id);

      const { error } = call<void>('profile.changePassword', {
        currentPassword: STUDENT_PASSWORD,
        password: NEW_PASSWORD,
        confirmPassword: `${NEW_PASSWORD}!`,
      });

      expect(error?.body.errors?.['confirmPassword']).toEqual(['Пароли не совпадают']);
      expect(error?.body.errors?.['password']).toBeUndefined();
      expect(stored().users[0].password).toBe(STUDENT_PASSWORD);
    }));

    it('успех: пароль реально изменён в мок-БД; вход со старым паролем более невозможен', fakeAsync(() => {
      const teacher = makeTeacher();
      seedDb({ users: [teacher] });
      loginAs(teacher.id);
      expect(emulatedLogin('teacher', TEACHER_PASSWORD)).not.toBeNull();

      const { error } = call<void>('profile.changePassword', {
        currentPassword: TEACHER_PASSWORD,
        password: NEW_PASSWORD,
        confirmPassword: NEW_PASSWORD,
      });

      expect(error).toBeUndefined();
      expect(stored().users[0].password).toBe(NEW_PASSWORD);
      expect(emulatedLogin('teacher', TEACHER_PASSWORD)).toBeNull();
      expect(emulatedLogin('TEACHER', NEW_PASSWORD)).not.toBeNull();
    }));

    it('без сессии — 401 «Не авторизован», пароль не изменён', fakeAsync(() => {
      const student = makeUser();
      seedDb({ users: [student] });
      loginAs(null);

      const { error } = call<void>('profile.changePassword', {
        currentPassword: STUDENT_PASSWORD,
        password: NEW_PASSWORD,
        confirmPassword: NEW_PASSWORD,
      });

      expect(error?.status).toBe(401);
      expect(error?.body.message).toBe('Не авторизован');
      expect(stored().users[0].password).toBe(STUDENT_PASSWORD);
    }));
  });
});
