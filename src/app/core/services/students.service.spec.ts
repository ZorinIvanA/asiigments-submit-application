/**
 * Юнит-тесты StudentsService (C-104, IF-105): проброс параметров getList/
 * setGroup в мок-обработчики домена, форма ответа PagedResult с pageSize
 * STUDENTS_PAGE_SIZE, реэкспорт константы, проброс ошибок мока как ApiError
 * (401/403/404). Обработчики домена регистрируются на том же экземпляре
 * MockApiClient, который внедрён в сервис (TestBed).
 *
 * Изоляция хранилищ — clear в beforeEach/afterEach; сессия выставляется
 * через SessionStore домена Auth (C-101, CR-001); задержка мок-вызова
 * гасится fakeAsync + tick(500).
 */
import { fakeAsync, TestBed, tick } from '@angular/core/testing';

import { SessionStore } from '../../mock/auth/session-store';
import { MockApiClient } from '../../mock/mock-api-client';
import { MockDbData, emptyMockDbData } from '../../mock/mock-db';
import { registerStudentsHandlers } from '../../mock/students/handlers';
import { ApiError, STORAGE_KEYS, StudentDto, User } from '../../shared/models';
import { STUDENTS_PAGE_SIZE, StudentsService } from './students.service';

const TEACHER_ID = 'aaaaaaa1-0000-4000-8000-000000000001';
const STUDENT_ID = 'cccccccc-0000-4000-8000-000000000001';
const GROUP_ID = 'bbbbbbb1-0000-4000-8000-000000000001';
const UNKNOWN_ID = 'ddddddd4-0000-4000-8000-000000000004';

function makeUser(id: string, overrides: Partial<User> = {}): User {
  return {
    id,
    login: 'student01',
    email: 'student01@example.com',
    fullName: 'Иванов Иван Иванович 01',
    role: 'student',
    groupId: null,
    password: 'student123!',
    ...overrides,
  };
}

function seedDb(): void {
  const data: MockDbData = {
    ...emptyMockDbData(),
    users: [
      makeUser(TEACHER_ID, {
        login: 'teacher',
        email: 'teacher@example.com',
        fullName: 'Сидоров Семён Семёнович',
        role: 'teacher',
      }),
      makeUser(STUDENT_ID),
    ],
    groups: [{ id: GROUP_ID, name: 'ИК-221', studentCount: 0 }],
  };
  localStorage.setItem(STORAGE_KEYS.mockDb, JSON.stringify(data));
}

/** Сессия выставляется через владельца ключа — SessionStore домена Auth (C-101). */
const sessionStore = new SessionStore();

function loginAs(userId: string): void {
  sessionStore.setUserId(userId);
}

/** Результат промиса, пойманный в переменную (fakeAsync-дружелюбно). */
interface Settled<T> {
  resolved?: T;
  error?: unknown;
}

function settle<T>(promise: Promise<T>): Settled<T> {
  const settled: Settled<T> = {};
  promise.then((value) => (settled.resolved = value), (error) => (settled.error = error));
  return settled;
}

describe('StudentsService (C-104, IF-105)', () => {
  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
  });

  afterEach(() => {
    localStorage.clear();
    sessionStorage.clear();
  });

  it('STUDENTS_PAGE_SIZE = 10 (контракт IF-105, ADR-109)', () => {
    expect(STUDENTS_PAGE_SIZE).toBe(10);
  });

  it('getList пробрасывает search/groupId/page и возвращает PagedResult с pageSize 10', fakeAsync(() => {
    seedDb();
    loginAs(TEACHER_ID);
    registerStudentsHandlers(TestBed.inject(MockApiClient));
    const service = TestBed.inject(StudentsService);

    const settled = settle(service.getList({ search: 'иванов', groupId: 'none', page: 1 }));
    tick(500);

    const result = settled.resolved;
    expect(result?.total).toBe(1);
    expect(result?.page).toBe(1);
    expect(result?.pageSize).toBe(STUDENTS_PAGE_SIZE);
    expect(result?.items.length).toBe(1);
    const student = result?.items[0] as StudentDto;
    expect(student.id).toBe(STUDENT_ID);
    expect(student.groupName).toBeNull();
  }));

  it('setGroup передаёт пару (studentId, groupId) и завершается без значения', fakeAsync(() => {
    seedDb();
    loginAs(TEACHER_ID);
    registerStudentsHandlers(TestBed.inject(MockApiClient));
    const service = TestBed.inject(StudentsService);

    const settled = settle(service.setGroup(STUDENT_ID, GROUP_ID));
    tick(500);

    expect(settled.error).toBeUndefined();
    expect(settled.resolved).toBeUndefined();
    const stored = JSON.parse(localStorage.getItem(STORAGE_KEYS.mockDb) ?? 'null') as MockDbData;
    expect(stored.users.find((u) => u.id === STUDENT_ID)?.groupId).toBe(GROUP_ID);
  }));

  it('setGroup null (исключение) пробрасывается как есть', fakeAsync(() => {
    seedDb();
    loginAs(TEACHER_ID);
    registerStudentsHandlers(TestBed.inject(MockApiClient));
    const service = TestBed.inject(StudentsService);

    const settled = settle(service.setGroup(STUDENT_ID, null));
    tick(500);

    expect(settled.error).toBeUndefined();
  }));

  it('ошибка мока пробрасывается как ApiError: 404 «Студент не найден»', fakeAsync(() => {
    seedDb();
    loginAs(TEACHER_ID);
    registerStudentsHandlers(TestBed.inject(MockApiClient));
    const service = TestBed.inject(StudentsService);

    const settled = settle(service.setGroup(UNKNOWN_ID, GROUP_ID));
    tick(500);

    const error = settled.error as ApiError;
    expect(error.status).toBe(404);
    expect(error.body.message).toBe('Студент не найден');
  }));

  it('ошибка роли пробрасывается как ApiError: 403 «Доступ запрещён» для студента', fakeAsync(() => {
    seedDb();
    loginAs(STUDENT_ID);
    registerStudentsHandlers(TestBed.inject(MockApiClient));
    const service = TestBed.inject(StudentsService);

    const settled = settle(service.getList({ page: 1 }));
    tick(500);

    const error = settled.error as ApiError;
    expect(error.status).toBe(403);
    expect(error.body.message).toBe('Доступ запрещён');
  }));

  it('ошибка сессии пробрасывается как ApiError: 401 «Не авторизован»', fakeAsync(() => {
    seedDb();
    registerStudentsHandlers(TestBed.inject(MockApiClient));
    const service = TestBed.inject(StudentsService);

    const settled = settle(service.getList({ page: 1 }));
    tick(500);

    const error = settled.error as ApiError;
    expect(error.status).toBe(401);
    expect(error.body.message).toBe('Не авторизован');
  }));
});
