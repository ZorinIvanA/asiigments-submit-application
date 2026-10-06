/**
 * Юнит-тесты мок-обработчиков домена Students (C-104, IF-105, FR-4.6):
 * поиск ci по каждому из трёх полей, фильтр 'none' и по uuid группы,
 * комбинированный фильтр+поиск, порядок ФИО↑ затем login↑ (не зависит от
 * поиска), пагинация/total (включая пустую страницу за пределами), граница
 * search 200 символов (400 + errors), setGroup включение/перевод/исключение
 * с пересчётом выборки в следующем getList, 404 студент/группа с
 * транзакционностью, роли 401/403.
 *
 * Изоляция хранилищ — clear в beforeEach/afterEach (паттерн mock-db.spec.ts);
 * сессия выставляется через SessionStore домена Auth (C-101, CR-001);
 * задержка мок-вызова гасится fakeAsync + tick(500) (паттерн
 * mock-api-client.spec.ts).
 */
import { fakeAsync, tick } from '@angular/core/testing';

import { SessionStore } from '../auth/session-store';
import { ApiError, PagedResult, STORAGE_KEYS, StudentDto, User } from '../../shared/models';
import { MockApiClient } from '../mock-api-client';
import { MockDbData, emptyMockDbData } from '../mock-db';
import {
  STUDENTS_PAGE_SIZE,
  StudentsGetListParams,
  registerStudentsHandlers,
} from './handlers';

const TEACHER_ID = 'aaaaaaa1-0000-4000-8000-000000000001';
const GROUP_221_ID = 'bbbbbbb1-0000-4000-8000-000000000001';
const GROUP_222_ID = 'bbbbbbb1-0000-4000-8000-000000000002';
const GROUP_223_ID = 'bbbbbbb1-0000-4000-8000-000000000003';
const UNKNOWN_ID = 'ddddddd4-0000-4000-8000-000000000004';

/** uuid по MOCK_ID_PATTERN, производный от номера студента. */
function studentId(nn: number): string {
  return `cccccccc-0000-4000-8000-${String(nn).padStart(12, '0')}`;
}

function makeStudent(nn: number, groupId: string | null): User {
  const suffix = String(nn).padStart(2, '0');
  return {
    id: studentId(nn),
    login: `student${suffix}`,
    email: `student${suffix}@example.com`,
    fullName: `Иванов Иван Иванович ${suffix}`,
    role: 'student',
    groupId,
    password: 'student123!',
  };
}

/** Сид по правилам data_design: 01–25 → ИК-221, 26–30 → ИК-222, 31–32 → без группы. */
function makeSeedData(): MockDbData {
  const users: User[] = [
    {
      id: TEACHER_ID,
      login: 'teacher',
      email: 'teacher@example.com',
      fullName: 'Сидоров Семён Семёнович',
      role: 'teacher',
      groupId: null,
      password: 'teacher123!',
    },
  ];
  for (let nn = 1; nn <= 25; nn++) {
    users.push(makeStudent(nn, GROUP_221_ID));
  }
  for (let nn = 26; nn <= 30; nn++) {
    users.push(makeStudent(nn, GROUP_222_ID));
  }
  users.push(makeStudent(31, null));
  users.push(makeStudent(32, null));
  // Студент с другой фамилией — для различимого поиска по ФИО и порядка ФИО↑.
  users.push({
    id: studentId(33),
    login: 'petrov',
    email: 'petrov@example.com',
    fullName: 'Петров Пётр Петрович',
    role: 'student',
    groupId: GROUP_222_ID,
    password: 'student123!',
  });
  return {
    ...emptyMockDbData(),
    users,
    groups: [
      { id: GROUP_221_ID, name: 'ИК-221', studentCount: 25 },
      { id: GROUP_222_ID, name: 'ИК-222', studentCount: 6 },
    ],
  };
}

function seedDb(data: MockDbData = makeSeedData()): void {
  localStorage.setItem(STORAGE_KEYS.mockDb, JSON.stringify(data));
}

/** Сессия выставляется через владельца ключа — SessionStore домена Auth (C-101). */
const sessionStore = new SessionStore();

function loginAs(userId: string | null): void {
  if (userId === null) {
    sessionStore.clear();
  } else {
    sessionStore.setUserId(userId);
  }
}

function makeClient(): MockApiClient {
  const client = new MockApiClient();
  registerStudentsHandlers(client);
  return client;
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

function getList(
  client: MockApiClient,
  params: Partial<StudentsGetListParams> = {},
): Settled<PagedResult<StudentDto>> {
  return settle(client.call<PagedResult<StudentDto>>('students.getList', { page: 1, ...params }));
}

function setGroup(
  client: MockApiClient,
  studentIdValue: string,
  groupId: string | null,
): Settled<void> {
  return settle(client.call<void>('students.setGroup', { studentId: studentIdValue, groupId }));
}

function storedDb(): MockDbData {
  return JSON.parse(localStorage.getItem(STORAGE_KEYS.mockDb) ?? 'null') as MockDbData;
}

describe('Мок-обработчики Students (C-104, IF-105)', () => {
  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
  });

  afterEach(() => {
    localStorage.clear();
    sessionStorage.clear();
  });

  describe('students.getList — пагинация и порядок', () => {
    it('страница 1 — 10 записей ФИО↑, total 33; преподаватель (role teacher) в список не попадает', fakeAsync(() => {
      seedDb();
      loginAs(TEACHER_ID);
      const client = makeClient();

      const settled = getList(client);
      tick(500);

      const result = settled.resolved;
      expect(result?.pageSize).withContext('размер страницы IF-105').toBe(STUDENTS_PAGE_SIZE);
      expect(result?.page).toBe(1);
      expect(result?.total).toBe(33);
      expect(result?.items.length).toBe(10);
      expect(result?.items.map((s) => s.login)).toEqual([
        'student01',
        'student02',
        'student03',
        'student04',
        'student05',
        'student06',
        'student07',
        'student08',
        'student09',
        'student10',
      ]);
      expect(result?.items.every((s) => s.login.startsWith('student')))
        .withContext('только студенты')
        .toBeTrue();
    }));

    it('последняя страница неполная; выход за пределы — items [] при корректном total', fakeAsync(() => {
      seedDb();
      loginAs(TEACHER_ID);
      const client = makeClient();

      const last = getList(client, { page: 4 });
      tick(500);
      expect(last.resolved?.items.map((s) => s.login)).toEqual(['student31', 'student32', 'petrov']);
      expect(last.resolved?.total).toBe(33);

      const beyond = getList(client, { page: 5 });
      tick(500);
      expect(beyond.resolved?.items).toEqual([]);
      expect(beyond.resolved?.total).toBe(33);
    }));

    it('порядок ФИО↑ затем login↑: при равных ФИО сортировка по логину', fakeAsync(() => {
      const data = makeSeedData();
      const twin = (id: string, login: string): User => ({
        id,
        login,
        email: `${login}@example.com`,
        fullName: 'Двойнин Двойн Двойнович',
        role: 'student',
        groupId: null,
        password: 'student123!',
      });
      data.users.push(twin('eeeeeee5-0000-4000-8000-000000000001', 'b-login'));
      data.users.push(twin('eeeeeee5-0000-4000-8000-000000000002', 'a-login'));
      seedDb(data);
      loginAs(TEACHER_ID);
      const client = makeClient();

      const settled = getList(client, { search: 'двойнин' });
      tick(500);

      expect(settled.resolved?.items.map((s) => s.login)).toEqual(['a-login', 'b-login']);
    }));

    it('порядок — русская локаль-коллация (не code-unit): Дмитриев < Ёлкин < Жуков', fakeAsync(() => {
      const data = makeSeedData();
      const extra = (id: string, login: string, fullName: string): User => ({
        id,
        login,
        email: `${login}@example.com`,
        fullName,
        role: 'student',
        groupId: GROUP_223_ID,
        password: 'student123!',
      });
      data.users.push(
        extra('f7000007-0000-4000-8000-000000000001', 'yolkin', 'Ёлкин Алексей'),
        extra('f7000007-0000-4000-8000-000000000002', 'dmitriev', 'Дмитриев Дмитрий'),
        extra('f7000007-0000-4000-8000-000000000003', 'zhukov', 'Жуков Жора'),
      );
      data.groups.push({ id: GROUP_223_ID, name: 'ИК-223', studentCount: 3 });
      seedDb(data);
      loginAs(TEACHER_ID);
      const client = makeClient();

      const settled = getList(client, { groupId: GROUP_223_ID });
      tick(500);

      // В code-unit порядке «Ё» (U+0401) стоит раньше «Д» (U+0414), поэтому
      // Ёлкин шёл бы первым; русская коллация даёт алфавитный порядок
      // Д < Ё/Е-блок < Ж — одинаковый с группами и ведомостью.
      expect(settled.resolved?.items.map((s) => s.login)).toEqual([
        'dmitriev',
        'yolkin',
        'zhukov',
      ]);
    }));

    it('StudentDto: groupName по группе, null у студентов без группы', fakeAsync(() => {
      seedDb();
      loginAs(TEACHER_ID);
      const client = makeClient();

      const grouped = getList(client, { groupId: GROUP_222_ID });
      tick(500);
      expect(grouped.resolved?.items.map((s) => s.groupName)).toEqual([
        'ИК-222',
        'ИК-222',
        'ИК-222',
        'ИК-222',
        'ИК-222',
        'ИК-222',
      ]);
      // Аменда 6: groupId — ключевое значение селекторов, проставлен из user.groupId.
      expect(grouped.resolved?.items.every((s) => s.groupId === GROUP_222_ID))
        .withContext('groupId проставлен для студента с группой')
        .toBeTrue();

      const none = getList(client, { groupId: 'none' });
      tick(500);
      expect(none.resolved?.items.every((s) => s.groupId === null && s.groupName === null))
        .withContext('у безгруппного студента groupId и groupName равны null')
        .toBeTrue();
    }));
  });

  describe('students.getList — нормализация страницы (ADR-109 аддендум 1)', () => {
    it('page=0 → PagedResult.page=1 и первая страница (эхо исходного значения запрещено)', fakeAsync(() => {
      seedDb();
      loginAs(TEACHER_ID);
      const client = makeClient();

      const settled = getList(client, { page: 0 });
      tick(500);

      expect(settled.resolved?.page).toBe(1);
      expect(settled.resolved?.items.map((s) => s.login)).toEqual([
        'student01',
        'student02',
        'student03',
        'student04',
        'student05',
        'student06',
        'student07',
        'student08',
        'student09',
        'student10',
      ]);
    }));

    it('page<1 (отрицательная) → нормализована к 1', fakeAsync(() => {
      seedDb();
      loginAs(TEACHER_ID);
      const client = makeClient();

      const settled = getList(client, { page: -5 });
      tick(500);

      expect(settled.resolved?.page).toBe(1);
      expect(settled.resolved?.items[0]?.login).toBe('student01');
    }));

    it('нечисловой page (NaN) → нормализован к 1', fakeAsync(() => {
      seedDb();
      loginAs(TEACHER_ID);
      const client = makeClient();

      const settled = getList(client, { page: Number.NaN });
      tick(500);

      expect(settled.resolved?.page).toBe(1);
      expect(settled.resolved?.items[0]?.login).toBe('student01');
      expect(settled.resolved?.total).toBe(33);
    }));

    it('page дробный → нормализован к 1', fakeAsync(() => {
      seedDb();
      loginAs(TEACHER_ID);
      const client = makeClient();

      const settled = getList(client, { page: 2.5 });
      tick(500);

      expect(settled.resolved?.page).toBe(1);
      expect(settled.resolved?.items[0]?.login).toBe('student01');
    }));
  });

  describe('students.getList — поиск (search-ci и границы)', () => {
    it('search ci находит по ФИО, логину и email; total — размер найденного', fakeAsync(() => {
      seedDb();
      loginAs(TEACHER_ID);
      const client = makeClient();

      const byFullName = getList(client, { search: 'иванов ив' });
      tick(500);
      expect(byFullName.resolved?.total).toBe(32);
      expect(byFullName.resolved?.items.some((s) => s.login === 'student01'))
        .withContext('AC search-ci: «Иванов Иван Иванович 01» найден по «иванов ив»')
        .toBeTrue();

      const byFullNameUpper = getList(client, { search: 'ПЕТРОВ ПЁТР' });
      tick(500);
      expect(byFullNameUpper.resolved?.items.map((s) => s.login)).toEqual(['petrov']);

      const byLogin = getList(client, { search: 'STUDENT01' });
      tick(500);
      expect(byLogin.resolved?.items.map((s) => s.login)).toEqual(['student01']);

      const byEmail = getList(client, { search: 'Student30@Example.Com' });
      tick(500);
      expect(byEmail.resolved?.items.map((s) => s.login)).toEqual(['student30']);
    }));

    it('поиск не меняет порядок: страница 1 при search совпадает с полной выборкой', fakeAsync(() => {
      seedDb();
      loginAs(TEACHER_ID);
      const client = makeClient();

      const all = getList(client);
      tick(500);
      const filtered = getList(client, { search: 'иванов' });
      tick(500);

      expect(filtered.resolved?.total).toBe(32);
      expect(filtered.resolved?.items.map((s) => s.login)).toEqual(
        all.resolved?.items.map((s) => s.login),
      );
    }));

    it('search пустая строка или не передан — фильтр не применяется', fakeAsync(() => {
      seedDb();
      loginAs(TEACHER_ID);
      const client = makeClient();

      const empty = getList(client, { search: '' });
      tick(500);
      const omitted = getList(client);
      tick(500);

      expect(empty.resolved?.total).toBe(33);
      expect(omitted.resolved?.total).toBe(33);
    }));

    it('search ровно 200 символов валиден (граница) и ничего не находит; 201 — 400 + errors {search}', fakeAsync(() => {
      seedDb();
      loginAs(TEACHER_ID);
      const client = makeClient();

      const ok = getList(client, { search: 'и'.repeat(200) });
      tick(500);
      expect(ok.error).withContext('200 — не «длиннее 200»').toBeUndefined();
      expect(ok.resolved?.total).toBe(0);

      const bad = getList(client, { search: 'и'.repeat(201) });
      tick(500);
      const error = bad.error as ApiError;
      expect(error.status).toBe(400);
      expect(error.body.message).toBe('Данные заполнены неверно');
      expect(error.body.errors).toEqual({ search: ['Поиск — не более 200 символов'] });
    }));
  });

  describe('students.getList — многословный поиск (IF-105 уточнение, CR-010)', () => {
    it('«иванов иванович 07» — все токены в одном поле (ФИО) → только student07', fakeAsync(() => {
      seedDb();
      loginAs(TEACHER_ID);
      const client = makeClient();

      const settled = getList(client, { search: 'иванов иванович 07' });
      tick(500);

      expect(settled.resolved?.items.map((s) => s.login)).toEqual(['student07']);
      expect(settled.resolved?.total).toBe(1);
    }));

    it('«иванов 05» — оба токена входят в ФИО student05 → student05 (per-field AND)', fakeAsync(() => {
      seedDb();
      loginAs(TEACHER_ID);
      const client = makeClient();

      const settled = getList(client, { search: 'иванов 05' });
      tick(500);

      // Постановка CR-010 ожидала 0 («ФИО не содержит 05»), но в сид-данных
      // data_design ФИО = «Иванов Иван Иванович 05» — токен «05» входит в то
      // же поле, что и «иванов», поэтому per-field AND даёт 1 запись.
      // Расхождение примера с реализованной семантикой зафиксировано в отчёте.
      expect(settled.resolved?.items.map((s) => s.login)).toEqual(['student05']);
      expect(settled.resolved?.total).toBe(1);
    }));

    it('«иванов student1» — токены не имеют общего поля ни у одной записи → 0 (отличает per-field AND от cross-field AND)', fakeAsync(() => {
      seedDb();
      loginAs(TEACHER_ID);
      const client = makeClient();

      const settled = getList(client, { search: 'иванов student1' });
      tick(500);

      // При регрессии к cross-field AND (токены независимо по разным полям)
      // прошли бы student10–student19 («иванов» в ФИО + «student1» в логине);
      // per-field AND даёт 0: ни одно поле не содержит оба токена.
      expect(settled.resolved?.items).toEqual([]);
      expect(settled.resolved?.total).toBe(0);
    }));

    it('порядок токенов не значим: «05 иванов» → student05 (оба токена в его ФИО)', fakeAsync(() => {
      seedDb();
      loginAs(TEACHER_ID);
      const client = makeClient();

      const settled = getList(client, { search: '05 иванов' });
      tick(500);

      expect(settled.resolved?.items.map((s) => s.login)).toEqual(['student05']);
    }));

    it('«STUDENT31» — одиночный токен ci по логину → student31', fakeAsync(() => {
      seedDb();
      loginAs(TEACHER_ID);
      const client = makeClient();

      const settled = getList(client, { search: 'STUDENT31' });
      tick(500);

      expect(settled.resolved?.items.map((s) => s.login)).toEqual(['student31']);
    }));

    it('«student3» → 3 записи: student30, student31, student32', fakeAsync(() => {
      seedDb();
      loginAs(TEACHER_ID);
      const client = makeClient();

      const settled = getList(client, { search: 'student3' });
      tick(500);

      expect(settled.resolved?.items.map((s) => s.login)).toEqual([
        'student30',
        'student31',
        'student32',
      ]);
      expect(settled.resolved?.total).toBe(3);
    }));

    it('поиск из одних пробелов — токенов нет, фильтр не применяется (33 записи)', fakeAsync(() => {
      seedDb();
      loginAs(TEACHER_ID);
      const client = makeClient();

      const settled = getList(client, { search: '   ' });
      tick(500);

      expect(settled.resolved?.total).toBe(33);
    }));
  });

  describe('students.getList — фильтр группы', () => {
    it("groupId='none' — только студенты без группы; преподаватель (тоже без группы) не попадает", fakeAsync(() => {
      seedDb();
      loginAs(TEACHER_ID);
      const client = makeClient();

      const settled = getList(client, { groupId: 'none' });
      tick(500);

      expect(settled.resolved?.items.map((s) => s.login)).toEqual(['student31', 'student32']);
      expect(settled.resolved?.total).toBe(2);
    }));

    it('groupId=uuid — только студенты этой группы', fakeAsync(() => {
      seedDb();
      loginAs(TEACHER_ID);
      const client = makeClient();

      const settled = getList(client, { groupId: GROUP_221_ID });
      tick(500);

      expect(settled.resolved?.total).toBe(25);
      expect(settled.resolved?.items.every((s) => s.login >= 'student01' && s.login <= 'student25'))
        .toBeTrue();
    }));

    it('комбинированный фильтр+поиск: ИК-221 + «STUDENT2» — студенты 20–25', fakeAsync(() => {
      seedDb();
      loginAs(TEACHER_ID);
      const client = makeClient();

      const settled = getList(client, { groupId: GROUP_221_ID, search: 'STUDENT2' });
      tick(500);

      expect(settled.resolved?.items.map((s) => s.login)).toEqual([
        'student20',
        'student21',
        'student22',
        'student23',
        'student24',
        'student25',
      ]);
      expect(settled.resolved?.total).toBe(6);
    }));

    it('groupId=несуществующий uuid — пустая выборка без ошибки (404 у getList не предусмотрен)', fakeAsync(() => {
      seedDb();
      loginAs(TEACHER_ID);
      const client = makeClient();

      const settled = getList(client, { groupId: UNKNOWN_ID });
      tick(500);

      expect(settled.resolved?.items).toEqual([]);
      expect(settled.resolved?.total).toBe(0);
    }));
  });

  describe('students.setGroup', () => {
    it('включение: студент без группы попадает в группу, следующий getList это отражает (AC set-group-translate)', fakeAsync(() => {
      seedDb();
      loginAs(TEACHER_ID);
      const client = makeClient();

      const settled = setGroup(client, studentId(31), GROUP_222_ID);
      tick(500);
      expect(settled.error).toBeUndefined();
      expect(settled.resolved).withContext('setGroup не возвращает тела').toBeUndefined();
      expect(storedDb().users.find((u) => u.id === studentId(31))?.groupId).toBe(GROUP_222_ID);

      const none = getList(client, { groupId: 'none' });
      tick(500);
      expect(none.resolved?.items.map((s) => s.login)).toEqual(['student32']);

      const group222 = getList(client, { groupId: GROUP_222_ID });
      tick(500);
      expect(group222.resolved?.total).toBe(7);
      expect(group222.resolved?.items.some((s) => s.id === studentId(31))).toBeTrue();
    }));

    it('перевод: студент уходит из ИК-221 в ИК-222, выборки обеих групп пересчитываются', fakeAsync(() => {
      seedDb();
      loginAs(TEACHER_ID);
      const client = makeClient();

      const settled = setGroup(client, studentId(1), GROUP_222_ID);
      tick(500);
      expect(settled.error).toBeUndefined();
      expect(storedDb().users.find((u) => u.id === studentId(1))?.groupId).toBe(GROUP_222_ID);

      const group221 = getList(client, { groupId: GROUP_221_ID });
      tick(500);
      expect(group221.resolved?.total).toBe(24);
      expect(group221.resolved?.items.some((s) => s.id === studentId(1))).toBeFalse();

      const group222 = getList(client, { groupId: GROUP_222_ID });
      tick(500);
      expect(group222.resolved?.total).toBe(7);
    }));

    it('исключение: groupId=null сбрасывает группу; повтор для студента без группы идемпотентен', fakeAsync(() => {
      seedDb();
      loginAs(TEACHER_ID);
      const client = makeClient();

      const settled = setGroup(client, studentId(1), null);
      tick(500);
      expect(settled.error).toBeUndefined();
      expect(storedDb().users.find((u) => u.id === studentId(1))?.groupId).toBeNull();

      const again = setGroup(client, studentId(31), null);
      tick(500);
      expect(again.error).toBeUndefined();
      expect(storedDb().users.find((u) => u.id === studentId(31))?.groupId).toBeNull();
    }));

    it('несуществующий студент — 404 «Студент не найден», состояние не изменилось (транзакционность)', fakeAsync(() => {
      seedDb();
      loginAs(TEACHER_ID);
      const client = makeClient();
      const before = localStorage.getItem(STORAGE_KEYS.mockDb);

      const settled = setGroup(client, UNKNOWN_ID, GROUP_221_ID);
      tick(500);

      const error = settled.error as ApiError;
      expect(error.status).toBe(404);
      expect(error.body.message).toBe('Студент не найден');
      expect(localStorage.getItem(STORAGE_KEYS.mockDb)).toBe(before);
    }));

    it('id преподавателя — 404 «Студент не найден» (студент — только User с ролью student)', fakeAsync(() => {
      seedDb();
      loginAs(TEACHER_ID);
      const client = makeClient();
      const before = localStorage.getItem(STORAGE_KEYS.mockDb);

      const settled = setGroup(client, TEACHER_ID, GROUP_221_ID);
      tick(500);

      expect((settled.error as ApiError).status).toBe(404);
      expect((settled.error as ApiError).body.message).toBe('Студент не найден');
      expect(localStorage.getItem(STORAGE_KEYS.mockDb)).toBe(before);
    }));

    it('несуществующая группа — 404 «Группа не найдена», group_id студента не изменился', fakeAsync(() => {
      seedDb();
      loginAs(TEACHER_ID);
      const client = makeClient();
      const before = localStorage.getItem(STORAGE_KEYS.mockDb);

      const settled = setGroup(client, studentId(1), UNKNOWN_ID);
      tick(500);

      const error = settled.error as ApiError;
      expect(error.status).toBe(404);
      expect(error.body.message).toBe('Группа не найдена');
      expect(storedDb().users.find((u) => u.id === studentId(1))?.groupId).toBe(GROUP_221_ID);
      expect(localStorage.getItem(STORAGE_KEYS.mockDb)).toBe(before);
    }));
  });

  describe('доступ по роли (IF-105 teacher-only)', () => {
    it('getList без сессии — 401 «Не авторизован»', fakeAsync(() => {
      seedDb();
      const client = makeClient();

      const settled = getList(client);
      tick(500);

      expect((settled.error as ApiError).status).toBe(401);
      expect((settled.error as ApiError).body.message).toBe('Не авторизован');
    }));

    it('getList с сессией студента — 403 «Доступ запрещён»', fakeAsync(() => {
      seedDb();
      loginAs(studentId(1));
      const client = makeClient();

      const settled = getList(client);
      tick(500);

      expect((settled.error as ApiError).status).toBe(403);
      expect((settled.error as ApiError).body.message).toBe('Доступ запрещён');
    }));

    it('setGroup без сессии — 401; с сессией студента — 403, группа не менялась', fakeAsync(() => {
      seedDb();
      const client = makeClient();

      const anonymous = setGroup(client, studentId(1), GROUP_221_ID);
      tick(500);
      expect((anonymous.error as ApiError).status).toBe(401);
      expect((anonymous.error as ApiError).body.message).toBe('Не авторизован');

      loginAs(studentId(2));
      const forbidden = setGroup(client, studentId(1), GROUP_221_ID);
      tick(500);
      expect((forbidden.error as ApiError).status).toBe(403);
      expect((forbidden.error as ApiError).body.message).toBe('Доступ запрещён');
      expect(storedDb().users.find((u) => u.id === studentId(1))?.groupId).toBe(GROUP_221_ID);
    }));
  });
});
