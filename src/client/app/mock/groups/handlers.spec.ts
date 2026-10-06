/**
 * Юнит-тесты мок-обработчиков домена Groups (C-103, контракт IF-104, §4.5,
 * ADR-109) — прогон через полный конвейер MockApiClient (register +
 * задержка + трансляция отказов в ApiError) на виртуальном времени
 * fakeAsync: логика обработчиков проверяется на транспортной форме контракта.
 *
 * Покрытие: getList (сортировка name↑ ci, вычисляемый studentCount), create
 * (400 пусто/пробелы/101 символ/не-строка, граница 100, 409 ci, транзакционность
 * 409), rename (успех, 404, 400, 409 ci, собственное имя в другом регистре),
 * remove (сброс groupId у студентов, 404, чужие группы не затронуты),
 * getStudents (пагинация/порядок/пустая группа/вне диапазона; нормализация
 * некорректного page → 1 и первая страница данных — ADR-109 аддендум 1;
 * состав StudentDto с groupId — аменда 6), роли (student → 403, нет/битая
 * сессия → 401).
 */
import { fakeAsync, tick } from '@angular/core/testing';

import { ApiError, Group, STORAGE_KEYS, User } from '../../shared/models';
import { SessionStore } from '../auth/session-store';
import { MockApiClient } from '../mock-api-client';
import { MockDbData, emptyMockDbData } from '../mock-db';
import { MOCK_ID_PATTERN } from '../ids';
import {
  GroupStudentsParams,
  registerGroupsHandlers,
} from './handlers';

const TEACHER_ID = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1';
const GROUP_221_ID = 'b1111111-1111-4111-8111-111111111111';
const GROUP_222_ID = 'b2222222-2222-4222-8222-222222222222';
const GROUP_223_ID = 'b3333333-3333-4333-8333-333333333333';

let nextStudentNo = 0;

/** Студент с детерминированным uuid по номеру (последняя группа — 12 hex). */
function studentId(no: number): string {
  return `c0c0c0c0-0000-4000-8000-0000000000${String(no).padStart(2, '0')}`;
}

function makeGroup(id: string, name: string): Group {
  return { id, name, studentCount: 0 };
}

function makeStudent(
  id: string,
  login: string,
  fullName: string,
  groupId: string | null,
): User {
  return {
    id,
    login,
    email: `${login}@example.com`,
    fullName,
    role: 'student',
    groupId,
    password: 'student123!',
  };
}

function nextStudent(
  login: string,
  fullName: string,
  groupId: string | null,
): User {
  nextStudentNo += 1;
  return makeStudent(studentId(nextStudentNo), login, fullName, groupId);
}

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

/** Доступ к ключу сессии — через владельца ключа (домен Auth, C-101). */
const sessionStore = new SessionStore();

/** Сеанс преподавателя (по умолчанию); ключ пишет SessionStore. */
function loginAsTeacher(): void {
  sessionStore.setUserId(TEACHER_ID);
}

function loginAs(user: User): void {
  sessionStore.setUserId(user.id);
}

/**
 * Сеет mock.db.v1; пользователь-преподаватель присутствует всегда — сессия
 * тестов по умолчанию указывает на него (requireTeacher ищет пользователя
 * сессии в users; сессия без пользователя — это 401 по IF-101).
 */
function seedDb(users: User[] = [], groups: Group[] = []): void {
  const allUsers = users.some((u) => u.id === TEACHER_ID)
    ? users
    : [makeTeacher(), ...users];
  const data: MockDbData = { ...emptyMockDbData(), users: allUsers, groups };
  localStorage.setItem(STORAGE_KEYS.mockDb, JSON.stringify(data));
}

function stored(): MockDbData {
  return JSON.parse(localStorage.getItem(STORAGE_KEYS.mockDb) ?? 'null') as MockDbData;
}

function makeClient(): MockApiClient {
  const client = new MockApiClient();
  registerGroupsHandlers(client);
  return client;
}

/** Ловит отказ промиса в переменную (fakeAsync-дружелюбно). */
function captureRejection(promise: Promise<unknown>): { error?: unknown } {
  const captured: { error?: unknown } = {};
  promise.catch((error: unknown) => {
    captured.error = error;
  });
  return captured;
}

describe('Мок-обработчики groups.* (C-103, IF-104)', () => {
  beforeEach(() => {
    localStorage.clear();
    nextStudentNo = 0;
  });

  afterEach(() => localStorage.clear());

  describe('groups.getList', () => {
    it('отсутствие групп → пустой массив', fakeAsync(() => {
      loginAsTeacher();
      seedDb([], []);
      const client = makeClient();
      let result: Group[] | undefined;
      client.call<Group[]>('groups.getList', null).then((r) => (result = r));
      tick(500);
      expect(result).toEqual([]);
    }));

    it('studentCount вычисляется по студентам группы; teacher и студенты без группы не считаются', fakeAsync(() => {
      loginAsTeacher();
      seedDb(
        [
          nextStudent('student01', 'Иванов Иван Иванович', GROUP_221_ID),
          nextStudent('student02', 'Петров Пётр Петрович', GROUP_221_ID),
          nextStudent('student26', 'Сидоров Сидор Сидорович', GROUP_222_ID),
          nextStudent('student31', 'Безгруппов Без Группы', null),
          makeTeacher(),
        ],
        [makeGroup(GROUP_221_ID, 'ИК-221'), makeGroup(GROUP_222_ID, 'ИК-222'), makeGroup(GROUP_223_ID, 'ИК-223')],
      );
      const client = makeClient();
      let result: Group[] | undefined;
      client.call<Group[]>('groups.getList', null).then((r) => (result = r));
      tick(500);
      expect(result?.map((g) => [g.name, g.studentCount])).toEqual([
        ['ИК-221', 2],
        ['ИК-222', 1],
        ['ИК-223', 0],
      ]);
    }));

    it('порядок name↑ без учёта регистра', fakeAsync(() => {
      loginAsTeacher();
      seedDb(
        [],
        [makeGroup(GROUP_222_ID, 'бета'), makeGroup(GROUP_221_ID, 'Альфа'), makeGroup(GROUP_223_ID, 'альфа-2')],
      );
      const client = makeClient();
      let result: Group[] | undefined;
      client.call<Group[]>('groups.getList', null).then((r) => (result = r));
      tick(500);
      expect(result?.map((g) => g.name)).toEqual(['Альфа', 'альфа-2', 'бета']);
    }));

    it('роль student → 403 «Доступ запрещён»', fakeAsync(() => {
      const student = nextStudent('student01', 'Иванов Иван Иванович', null);
      loginAs(student);
      seedDb([student], []);
      const client = makeClient();
      const captured = captureRejection(client.call('groups.getList', null));
      tick(500);
      expect(captured.error).toEqual({
        status: 403,
        body: { message: 'Доступ запрещён' },
      } satisfies ApiError);
    }));

    it('нет сессии → 401 «Не авторизован» (IF-101)', fakeAsync(() => {
      seedDb([], []);
      const client = makeClient();
      const captured = captureRejection(client.call('groups.getList', null));
      tick(500);
      expect(captured.error).toEqual({
        status: 401,
        body: { message: 'Не авторизован' },
      } satisfies ApiError);
    }));

    it('сессия указывает на несуществующего пользователя → 401', fakeAsync(() => {
      loginAsTeacher();
      seedDb([], []);
      sessionStore.setUserId('deadbeef-dead-4ead-8ead-deaddeaddead');
      const client = makeClient();
      const captured = captureRejection(client.call('groups.getList', null));
      tick(500);
      expect(captured.error).toEqual({
        status: 401,
        body: { message: 'Не авторизован' },
      } satisfies ApiError);
    }));
  });

  describe('groups.create', () => {
    it('успех: трим названия, uuid-идентификатор, studentCount=0, персистентность', fakeAsync(() => {
      loginAsTeacher();
      seedDb([], []);
      const client = makeClient();
      let created: Group | undefined;
      client.call<Group>('groups.create', { name: '  ИК-224  ' }).then((g) => (created = g));
      tick(500);
      expect(created?.name).toBe('ИК-224');
      expect(created?.studentCount).toBe(0);
      expect(created?.id ?? '').toMatch(MOCK_ID_PATTERN);
      expect(stored().groups.map((g) => g.name)).toEqual(['ИК-224']);
    }));

    it('граница: ровно 100 символов после трима → успех', fakeAsync(() => {
      loginAsTeacher();
      seedDb([], []);
      const client = makeClient();
      const name = 'Г' + 'о'.repeat(98) + 'п'; // 100 символов
      let created: Group | undefined;
      client.call<Group>('groups.create', { name }).then((g) => (created = g));
      tick(500);
      expect(created?.name).toBe(name);
    }));

    it('400 при пустой строке: «Данные заполнены неверно» + errors {name} из ERROR_TEXTS', fakeAsync(() => {
      loginAsTeacher();
      seedDb([], []);
      const client = makeClient();
      const captured = captureRejection(client.call('groups.create', { name: '' }));
      tick(500);
      expect(captured.error).toEqual({
        status: 400,
        body: {
          message: 'Данные заполнены неверно',
          errors: { name: ['Название группы — от 1 до 100 символов'] },
        },
      } satisfies ApiError);
      expect(stored().groups).toEqual([]);
    }));

    it('400 при строке из одних пробелов', fakeAsync(() => {
      loginAsTeacher();
      seedDb([], []);
      const client = makeClient();
      const captured = captureRejection(client.call('groups.create', { name: '   ' }));
      tick(500);
      expect((captured.error as ApiError).status).toBe(400);
      expect((captured.error as ApiError).body.errors?.['name']).toBeDefined();
    }));

    it('400 при 101 символе после трима', fakeAsync(() => {
      loginAsTeacher();
      seedDb([], []);
      const client = makeClient();
      const captured = captureRejection(
        client.call('groups.create', { name: '  ' + 'И'.repeat(101) + ' ' }),
      );
      tick(500);
      expect(captured.error).toEqual({
        status: 400,
        body: {
          message: 'Данные заполнены неверно',
          errors: { name: ['Название группы — от 1 до 100 символов'] },
        },
      } satisfies ApiError);
    }));

    it('400 при не-строковом названии (undefined)', fakeAsync(() => {
      loginAsTeacher();
      seedDb([], []);
      const client = makeClient();
      const captured = captureRejection(
        client.call('groups.create', { name: undefined }),
      );
      tick(500);
      expect((captured.error as ApiError).status).toBe(400);
    }));

    it('AC create-duplicate: «ИК-221» существует, create «ик-221» → 409 с точным текстом', fakeAsync(() => {
      loginAsTeacher();
      seedDb([], [makeGroup(GROUP_221_ID, 'ИК-221')]);
      const client = makeClient();
      const captured = captureRejection(client.call('groups.create', { name: 'ик-221' }));
      tick(500);
      expect(captured.error).toEqual({
        status: 409,
        body: { message: 'Группа с таким названием уже существует' },
      } satisfies ApiError);
    }));

    it('409 транзакционен: БД не изменяется', fakeAsync(() => {
      loginAsTeacher();
      seedDb([], [makeGroup(GROUP_221_ID, 'ИК-221')]);
      const before = localStorage.getItem(STORAGE_KEYS.mockDb);
      const client = makeClient();
      const captured = captureRejection(client.call('groups.create', { name: 'ИК-221' }));
      tick(500);
      expect((captured.error as ApiError).status).toBe(409);
      expect(localStorage.getItem(STORAGE_KEYS.mockDb)).toBe(before);
      expect(stored().groups.length).toBe(1);
    }));

    it('роль student → 403 и БД не изменяется', fakeAsync(() => {
      const student = nextStudent('student01', 'Иванов Иван Иванович', null);
      loginAs(student);
      seedDb([student], []);
      const client = makeClient();
      const captured = captureRejection(client.call('groups.create', { name: 'Хак' }));
      tick(500);
      expect((captured.error as ApiError).status).toBe(403);
      expect(stored().groups).toEqual([]);
    }));
  });

  describe('groups.rename', () => {
    it('успех: имя обновлено, возвращён GroupDto с изменением', fakeAsync(() => {
      loginAsTeacher();
      const student = nextStudent('student01', 'Иванов Иван Иванович', GROUP_221_ID);
      seedDb([student], [makeGroup(GROUP_221_ID, 'ИК-221')]);
      const client = makeClient();
      let renamed: Group | undefined;
      client.call<Group>('groups.rename', { id: GROUP_221_ID, name: 'ИК-224' }).then(
        (g) => (renamed = g),
      );
      tick(500);
      expect(renamed).toEqual({ id: GROUP_221_ID, name: 'ИК-224', studentCount: 1 });
      expect(stored().groups[0]?.name).toBe('ИК-224');
    }));

    it('переименование в собственное имя в другом регистре — не дубликат', fakeAsync(() => {
      loginAsTeacher();
      seedDb(
        [],
        [makeGroup(GROUP_221_ID, 'ИК-221'), makeGroup(GROUP_222_ID, 'ИК-222')],
      );
      const client = makeClient();
      let renamed: Group | undefined;
      client.call<Group>('groups.rename', { id: GROUP_221_ID, name: 'ик-221' }).then(
        (g) => (renamed = g),
      );
      tick(500);
      expect(renamed?.name).toBe('ик-221');
    }));

    it('404 несуществующей группы: «Группа не найдена»', fakeAsync(() => {
      loginAsTeacher();
      seedDb([], [makeGroup(GROUP_221_ID, 'ИК-221')]);
      const client = makeClient();
      const captured = captureRejection(
        client.call('groups.rename', { id: GROUP_223_ID, name: 'Новое' }),
      );
      tick(500);
      expect(captured.error).toEqual({
        status: 404,
        body: { message: 'Группа не найдена' },
      } satisfies ApiError);
    }));

    it('400 при пустом новом названии', fakeAsync(() => {
      loginAsTeacher();
      seedDb([], [makeGroup(GROUP_221_ID, 'ИК-221')]);
      const client = makeClient();
      const captured = captureRejection(
        client.call('groups.rename', { id: GROUP_221_ID, name: '' }),
      );
      tick(500);
      expect(captured.error).toEqual({
        status: 400,
        body: {
          message: 'Данные заполнены неверно',
          errors: { name: ['Название группы — от 1 до 100 символов'] },
        },
      } satisfies ApiError);
    }));

    it('400 при 101 символе', fakeAsync(() => {
      loginAsTeacher();
      seedDb([], [makeGroup(GROUP_221_ID, 'ИК-221')]);
      const client = makeClient();
      const captured = captureRejection(
        client.call('groups.rename', { id: GROUP_221_ID, name: 'И'.repeat(101) }),
      );
      tick(500);
      expect((captured.error as ApiError).status).toBe(400);
    }));

    it('409 при дубликате чужого имени без учёта регистра; имя не меняется', fakeAsync(() => {
      loginAsTeacher();
      seedDb(
        [],
        [makeGroup(GROUP_221_ID, 'ИК-221'), makeGroup(GROUP_222_ID, 'ИК-222')],
      );
      const client = makeClient();
      const captured = captureRejection(
        client.call('groups.rename', { id: GROUP_222_ID, name: 'ик-221' }),
      );
      tick(500);
      expect(captured.error).toEqual({
        status: 409,
        body: { message: 'Группа с таким названием уже существует' },
      } satisfies ApiError);
      expect(stored().groups.find((g) => g.id === GROUP_222_ID)?.name).toBe('ИК-222');
    }));

    it('роль student → 403', fakeAsync(() => {
      const student = nextStudent('student01', 'Иванов Иван Иванович', null);
      loginAs(student);
      seedDb([student], [makeGroup(GROUP_221_ID, 'ИК-221')]);
      const client = makeClient();
      const captured = captureRejection(
        client.call('groups.rename', { id: GROUP_221_ID, name: 'Новое' }),
      );
      tick(500);
      expect((captured.error as ApiError).status).toBe(403);
    }));
  });

  describe('groups.remove', () => {
    it('AC remove-сброс-группы: 2 студента → группа удалена, у обоих groupId=null', fakeAsync(() => {
      loginAsTeacher();
      const first = nextStudent('student01', 'Иванов Иван Иванович', GROUP_221_ID);
      const second = nextStudent('student02', 'Петров Пётр Петрович', GROUP_221_ID);
      seedDb([first, second], [makeGroup(GROUP_221_ID, 'ИК-221')]);
      const client = makeClient();
      let settled = false;
      client.call<void>('groups.remove', { id: GROUP_221_ID }).then(() => (settled = true));
      tick(500);
      expect(settled).toBeTrue();
      const snapshot = stored();
      expect(snapshot.groups).toEqual([]);
      // Студенты сохранены и попали в фильтр «без группы» (groupId=null, §4.5/§4.6);
      // преподаватель (сеанс) не входит в группу — его groupId не менялся.
      expect(snapshot.users.map((u) => [u.id, u.groupId])).toEqual([
        [TEACHER_ID, null],
        [first.id, null],
        [second.id, null],
      ]);
    }));

    it('студенты других групп не затрагиваются', fakeAsync(() => {
      loginAsTeacher();
      const inGroup = nextStudent('student01', 'Иванов Иван Иванович', GROUP_221_ID);
      const other = nextStudent('student26', 'Петров Пётр Петрович', GROUP_222_ID);
      seedDb([inGroup, other], [
        makeGroup(GROUP_221_ID, 'ИК-221'),
        makeGroup(GROUP_222_ID, 'ИК-222'),
      ]);
      const client = makeClient();
      client.call<void>('groups.remove', { id: GROUP_221_ID });
      tick(500);
      const snapshot = stored();
      expect(snapshot.groups.map((g) => g.id)).toEqual([GROUP_222_ID]);
      expect(snapshot.users.find((u) => u.id === other.id)?.groupId).toBe(GROUP_222_ID);
      expect(snapshot.users.find((u) => u.id === inGroup.id)?.groupId).toBeNull();
    }));

    it('404 несуществующей группы: «Группа не найдена», БД не меняется', fakeAsync(() => {
      loginAsTeacher();
      seedDb([], [makeGroup(GROUP_221_ID, 'ИК-221')]);
      const before = localStorage.getItem(STORAGE_KEYS.mockDb);
      const client = makeClient();
      const captured = captureRejection(client.call('groups.remove', { id: GROUP_223_ID }));
      tick(500);
      expect(captured.error).toEqual({
        status: 404,
        body: { message: 'Группа не найдена' },
      } satisfies ApiError);
      expect(localStorage.getItem(STORAGE_KEYS.mockDb)).toBe(before);
    }));

    it('роль student → 403 и БД не меняется', fakeAsync(() => {
      const student = nextStudent('student01', 'Иванов Иван Иванович', GROUP_221_ID);
      loginAs(student);
      seedDb([student], [makeGroup(GROUP_221_ID, 'ИК-221')]);
      const client = makeClient();
      const captured = captureRejection(client.call('groups.remove', { id: GROUP_221_ID }));
      tick(500);
      expect((captured.error as ApiError).status).toBe(403);
      expect(stored().groups.length).toBe(1);
    }));
  });

  describe('groups.getStudents', () => {
    /** 25 студентов: 20 с уникальными ФИО + 5 с одинаковым ФИО (хвост 3-й страницы). */
    function seed25Students(groupId: string): User[] {
      const users: User[] = [];
      for (let i = 1; i <= 20; i++) {
        users.push(
          nextStudent(`s${String(i).padStart(2, '0')}`, `Студент ${String(i).padStart(2, '0')}`, groupId),
        );
      }
      const tailLogins = ['e25', 'e21', 'e23', 'e22', 'e24'];
      for (const login of tailLogins) {
        users.push(nextStudent(login, 'Студент 21', groupId));
      }
      return users;
    }

    it('AC students-пагинация: 25 студентов, page=3 → 5 записей, total=25, порядок ФИО↑ затем login↑', fakeAsync(() => {
      loginAsTeacher();
      seedDb(seed25Students(GROUP_221_ID), [makeGroup(GROUP_221_ID, 'ИК-221')]);
      const client = makeClient();
      let result: unknown;
      client
        .call<unknown>('groups.getStudents', { id: GROUP_221_ID, page: 3 } satisfies GroupStudentsParams)
        .then((r) => (result = r));
      tick(500);
      const paged = result as { items: Array<{ login: string }>; total: number; page: number; pageSize: number };
      expect(paged.total).toBe(25);
      expect(paged.page).toBe(3);
      expect(paged.pageSize).toBe(10);
      expect(paged.items.length).toBe(5);
      // Третья страница — последние 5 после сортировки: пять «Студент 21» по login↑.
      expect(paged.items.map((s) => s.login)).toEqual(['e21', 'e22', 'e23', 'e24', 'e25']);
    }));

    it('полный порядок первых страниц: ФИО↑, затем login↑ при равных ФИО', fakeAsync(() => {
      loginAsTeacher();
      const users = [
        nextStudent('ivanov.b', 'Иванов Иван', GROUP_221_ID),
        nextStudent('antonov.a', 'Антонов Антон', GROUP_221_ID),
        nextStudent('ivanov.a', 'Иванов Иван', GROUP_221_ID),
      ];
      seedDb(users, [makeGroup(GROUP_221_ID, 'ИК-221')]);
      const client = makeClient();
      let result: unknown;
      client
        .call<unknown>('groups.getStudents', { id: GROUP_221_ID, page: 1 })
        .then((r) => (result = r));
      tick(500);
      const paged = result as { items: Array<{ login: string }> };
      expect(paged.items.map((s) => s.login)).toEqual(['antonov.a', 'ivanov.a', 'ivanov.b']);
    }));

    it('StudentDto: id/fullName/login/email/groupId/groupName (в составе группы константны)', fakeAsync(() => {
      loginAsTeacher();
      const user = nextStudent('student01', 'Иванов Иван Иванович', GROUP_221_ID);
      seedDb([user], [makeGroup(GROUP_221_ID, 'ИК-221')]);
      const client = makeClient();
      let result: unknown;
      client
        .call<unknown>('groups.getStudents', { id: GROUP_221_ID, page: 1 })
        .then((r) => (result = r));
      tick(500);
      const paged = result as { items: Array<Record<string, string | null>> };
      expect(paged.items).toEqual([
        {
          id: user.id,
          fullName: 'Иванов Иван Иванович',
          login: 'student01',
          email: 'student01@example.com',
          // Аменда 6: groupId = id группы (из user.groupId).
          groupId: GROUP_221_ID,
          groupName: 'ИК-221',
        },
      ]);
    }));

    it('пустая группа → items=[], total=0', fakeAsync(() => {
      loginAsTeacher();
      seedDb([], [makeGroup(GROUP_221_ID, 'ИК-221')]);
      const client = makeClient();
      let result: unknown;
      client
        .call<unknown>('groups.getStudents', { id: GROUP_221_ID, page: 1 })
        .then((r) => (result = r));
      tick(500);
      expect(result).toEqual({ items: [], total: 0, page: 1, pageSize: 10 });
    }));

    it('page>последней (валидный номер) → items=[], total сохраняется', fakeAsync(() => {
      loginAsTeacher();
      seedDb(seed25Students(GROUP_221_ID), [makeGroup(GROUP_221_ID, 'ИК-221')]);
      const client = makeClient();
      let result: unknown;
      client
        .call<unknown>('groups.getStudents', { id: GROUP_221_ID, page: 9 })
        .then((r) => (result = r));
      tick(500);
      expect(result).toEqual({ items: [], total: 25, page: 9, pageSize: 10 });
    }));

    describe('нормализация некорректного page → 1 (ADR-109 аддендум 1)', () => {
      /** 12 студентов: первая страница = s01..s10, вторая = s11..s12. */
      function seed12Students(): void {
        seedDb(
          Array.from({ length: 12 }, (_, i) =>
            nextStudent(
              `s${String(i + 1).padStart(2, '0')}`,
              `Студент ${String(i + 1).padStart(2, '0')}`,
              GROUP_221_ID,
            ),
          ),
          [makeGroup(GROUP_221_ID, 'ИК-221')],
        );
      }

      /** Прогон getStudents с произвольными params (вызывать внутри fakeAsync). */
      function callWith(params: object): {
        page: number;
        items: Array<{ login: string }>;
        total: number;
        pageSize: number;
      } {
        let result: unknown;
        makeClient()
          .call<unknown>('groups.getStudents', params)
          .then((r) => (result = r));
        tick(500);
        return result as { page: number; items: Array<{ login: string }>; total: number; pageSize: number };
      }

      const FIRST_PAGE_LOGINS = Array.from({ length: 10 }, (_, i) => `s${String(i + 1).padStart(2, '0')}`);

      it('page=0 → PagedResult.page=1 и первая страница данных', fakeAsync(() => {
        loginAsTeacher();
        seed12Students();
        const paged = callWith({ id: GROUP_221_ID, page: 0 });
        expect(paged.page).toBe(1);
        expect(paged.items.map((s) => s.login)).toEqual(FIRST_PAGE_LOGINS);
        expect(paged.total).toBe(12);
        expect(paged.pageSize).toBe(10);
      }));

      it('page<1 (отрицательная) → PagedResult.page=1 и первая страница данных', fakeAsync(() => {
        loginAsTeacher();
        seed12Students();
        const paged = callWith({ id: GROUP_221_ID, page: -3 });
        expect(paged.page).toBe(1);
        expect(paged.items.map((s) => s.login)).toEqual(FIRST_PAGE_LOGINS);
      }));

      it('нечисловой page (undefined) → PagedResult.page=1 и первая страница данных', fakeAsync(() => {
        loginAsTeacher();
        seed12Students();
        const paged = callWith({ id: GROUP_221_ID, page: undefined } as unknown as GroupStudentsParams);
        expect(paged.page).toBe(1);
        expect(paged.items.map((s) => s.login)).toEqual(FIRST_PAGE_LOGINS);
      }));
    });

    it('в состав не попадают не-студенты (teacher с groupId группы — аномалия данных)', fakeAsync(() => {
      loginAsTeacher();
      const teacherInGroup: User = { ...makeTeacher(), groupId: GROUP_221_ID };
      const student = nextStudent('student01', 'Иванов Иван Иванович', GROUP_221_ID);
      seedDb([teacherInGroup, student], [makeGroup(GROUP_221_ID, 'ИК-221')]);
      const client = makeClient();
      let result: unknown;
      client
        .call<unknown>('groups.getStudents', { id: GROUP_221_ID, page: 1 })
        .then((r) => (result = r));
      tick(500);
      const paged = result as { items: Array<{ id: string }>; total: number };
      expect(paged.total).toBe(1);
      expect(paged.items.map((s) => s.id)).toEqual([student.id]);
    }));

    it('404 несуществующей группы: «Группа не найдена»', fakeAsync(() => {
      loginAsTeacher();
      seedDb([], []);
      const client = makeClient();
      const captured = captureRejection(
        client.call('groups.getStudents', { id: GROUP_221_ID, page: 1 }),
      );
      tick(500);
      expect(captured.error).toEqual({
        status: 404,
        body: { message: 'Группа не найдена' },
      } satisfies ApiError);
    }));

    it('роль student → 403', fakeAsync(() => {
      const student = nextStudent('student01', 'Иванов Иван Иванович', GROUP_221_ID);
      loginAs(student);
      seedDb([student], [makeGroup(GROUP_221_ID, 'ИК-221')]);
      const client = makeClient();
      const captured = captureRejection(
        client.call('groups.getStudents', { id: GROUP_221_ID, page: 1 }),
      );
      tick(500);
      expect((captured.error as ApiError).status).toBe(403);
    }));
  });
});
