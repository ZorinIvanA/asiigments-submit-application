/**
 * Юнит-тесты GroupsService (C-103, IF-104): каждый метод сервиса — ровно один
 * мок-вызов с корректным именем метода и параметрами, ответ/отказ проходят
 * полный конвейер MockApiClient (задержка на виртуальном времени fakeAsync,
 * трансляция MockValidationError → ApiError). Обработчики домена подключаются
 * registerGroupsHandlers — тот же код, что использует агрегатор мок-слоя.
 */
import { TestBed } from '@angular/core/testing';
import { fakeAsync, tick } from '@angular/core/testing';

import { ApiError, Group, STORAGE_KEYS, User } from '../../shared/models';
import { SessionStore } from '../../mock/auth/session-store';
import { MockApiClient } from '../../mock/mock-api-client';
import { MockDbData, emptyMockDbData } from '../../mock/mock-db';
import { GROUPS_PAGE_SIZE, GroupsService } from './groups.service';
import { registerGroupsHandlers } from '../../mock/groups/handlers';

const TEACHER_ID = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1';
const GROUP_221_ID = 'b1111111-1111-4111-8111-111111111111';

function makeTeacher(): User {
  return {
    id: TEACHER_ID,
    login: 'teacher',
    email: 'teacher@example.com',
    fullName: 'Сидоров Семён Семёнович',
    role: 'teacher',
    groupId: null,
    password: 'teacher123!',
  };
}

function makeStudent(id: string, login: string, groupId: string | null): User {
  return {
    id,
    login,
    email: `${login}@example.com`,
    fullName: `ФИО ${login}`,
    role: 'student',
    groupId,
    password: 'student123!',
  };
}

/** Сеет mock.db.v1; преподаватель присутствует всегда — сессия указывает на него. */
function seedDb(users: User[], groups: Group[]): void {
  const allUsers = users.some((u) => u.id === TEACHER_ID)
    ? users
    : [makeTeacher(), ...users];
  const data: MockDbData = { ...emptyMockDbData(), users: allUsers, groups };
  localStorage.setItem(STORAGE_KEYS.mockDb, JSON.stringify(data));
}

function stored(): MockDbData {
  return JSON.parse(localStorage.getItem(STORAGE_KEYS.mockDb) ?? 'null') as MockDbData;
}

describe('GroupsService (C-103, IF-104)', () => {
  let client: MockApiClient;
  let service: GroupsService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({});
    client = TestBed.inject(MockApiClient);
    service = TestBed.inject(GroupsService);
    registerGroupsHandlers(client);
    // Сессия через владельца ключа (домен Auth, C-101).
    new SessionStore().setUserId(TEACHER_ID);
  });

  afterEach(() => localStorage.clear());

  it('GROUPS_PAGE_SIZE = 10 (ADR-109, IF-104)', () => {
    expect(GROUPS_PAGE_SIZE).toBe(10);
  });

  it('getList возвращает группы с вычисленным studentCount', fakeAsync(() => {
    const student = makeStudent('c0c0c0c0-0000-4000-8000-000000000001', 'student01', GROUP_221_ID);
    seedDb([student, makeTeacher()], [
      { id: GROUP_221_ID, name: 'ИК-221', studentCount: 0 },
    ]);
    let result: Group[] | undefined;
    service.getList().then((r) => (result = r));
    tick(500);
    expect(result).toEqual([{ id: GROUP_221_ID, name: 'ИК-221', studentCount: 1 }]);
  }));

  it('create передаёт название и персистит созданную группу', fakeAsync(() => {
    seedDb([], []);
    let created: Group | undefined;
    service.create('ИК-224').then((g) => (created = g));
    tick(500);
    expect(created?.name).toBe('ИК-224');
    expect(stored().groups.map((g) => g.name)).toEqual(['ИК-224']);
  }));

  it('rename передаёт id и название, возвращает обновлённую группу', fakeAsync(() => {
    seedDb([], [{ id: GROUP_221_ID, name: 'ИК-221', studentCount: 0 }]);
    let renamed: Group | undefined;
    service.rename(GROUP_221_ID, 'ИК-222').then((g) => (renamed = g));
    tick(500);
    expect(renamed?.name).toBe('ИК-222');
    expect(stored().groups[0]?.name).toBe('ИК-222');
  }));

  it('remove удаляет группу и сбрасывает groupId студентов', fakeAsync(() => {
    const student = makeStudent('c0c0c0c0-0000-4000-8000-000000000001', 'student01', GROUP_221_ID);
    seedDb([student], [{ id: GROUP_221_ID, name: 'ИК-221', studentCount: 0 }]);
    let settled = false;
    service.remove(GROUP_221_ID).then(() => (settled = true));
    tick(500);
    expect(settled).toBeTrue();
    expect(stored().groups).toEqual([]);
    expect(stored().users.find((u) => u.id === student.id)?.groupId).toBeNull();
  }));

  it('getStudents передаёт (id, page) и возвращает PagedResult с pageSize=10', fakeAsync(() => {
    const users = Array.from({ length: 12 }, (_, i) =>
      makeStudent(
        `c0c0c0c0-0000-4000-8000-0000000000${String(i + 1).padStart(2, '0')}`,
        `student${String(i + 1).padStart(2, '0')}`,
        GROUP_221_ID,
      ),
    );
    seedDb(users, [{ id: GROUP_221_ID, name: 'ИК-221', studentCount: 0 }]);
    let result: unknown;
    service.getStudents(GROUP_221_ID, 2).then((r) => (result = r));
    tick(500);
    const paged = result as { items: unknown[]; total: number; page: number; pageSize: number };
    expect(paged.items.length).toBe(2);
    expect(paged.total).toBe(12);
    expect(paged.page).toBe(2);
    expect(paged.pageSize).toBe(GROUPS_PAGE_SIZE);
  }));

  it('отказ домена транслируется в ApiError: 404 «Группа не найдена»', fakeAsync(() => {
    seedDb([], []);
    let error: unknown;
    service.remove(GROUP_221_ID).catch((e: unknown) => (error = e));
    tick(500);
    expect(error).toEqual({
      status: 404,
      body: { message: 'Группа не найдена' },
    } satisfies ApiError);
  }));
});
