/**
 * Юнит-тесты мок-обработчиков домена Submissions (C-105, IF-106, FR-4.4).
 *
 * Покрываются критерии приёмки подзадачи T-105 и требования
 * unit_test_requirements:
 *  - getGrid: пагинация по 5 студентов, порядок fullName↑/login↑, работы
 *    семестра по number↑, записи только для пар текущей страницы, пустая
 *    группа (total=0), 404 «Группа не найдена», роли (403 студенту, 401
 *    без сессии);
 *  - update: insert (пары нет) / update (без дублирования) / сброс null
 *    обеих дат, updated_by/updated_at, 404 студент/лабораторная,
 *    неконтрактная дата → 400, роли, транзакционность отказа;
 *  - getMy: обе ветки hasGroup, изоляция данных (только свои сдачи),
 *    фильтр семестра, 403 для teacher, 401 без сессии;
 *  - регистрация трёх методов в реестре MockApiClient (полный конвейер).
 *
 * Сессия выставляется через SessionStore домена Auth (C-101) — владельца
 * ключа mock.session.userId; прямых записей ключа в фикстурах нет.
 */
import { fakeAsync, tick } from '@angular/core/testing';

import {
  MySubmissionsDto,
  Submission,
  SubmissionsGridDto,
  User,
} from '../../shared/models';
import { SessionStore } from '../auth/session-store';
import { MOCK_ID_PATTERN } from '../ids';
import { MockApiClient } from '../mock-api-client';
import {
  configureMockDbSeed,
  emptyMockDbData,
  MockDb,
  MockDbData,
  resetMockDbSeed,
} from '../mock-db';
import { MockValidationError } from '../mock-error';
import {
  handleSubmissionsGetGrid,
  handleSubmissionsGetMy,
  handleSubmissionsUpdate,
  registerSubmissionsHandlers,
  SubmissionsGetGridParams,
  SubmissionsUpdateParams,
} from './handlers';

// --- Детерминированные идентификаторы сида (uuid по MOCK_ID_PATTERN). ---

const GROUP_A_ID = 'a0000000-0000-4000-8000-000000000001';
const GROUP_B_ID = 'a0000000-0000-4000-8000-000000000002';
const TEACHER_ID = 'b0000000-0000-4000-8000-000000000001';
const STUDENT_NO_GROUP_ID = 'b0000000-0000-4000-8000-000000000002';
const STUDENT_OTHER_GROUP_ID = 'b0000000-0000-4000-8000-000000000003';
const LAB_S1_N1_ID = 'd0000000-0000-4000-8000-000000000001';
const LAB_S1_N2_ID = 'd0000000-0000-4000-8000-000000000002';
const LAB_S1_N3_ID = 'd0000000-0000-4000-8000-000000000003';
const LAB_S2_N1_ID = 'd0000000-0000-4000-8000-000000000004';
const UNKNOWN_ID = 'e0000000-0000-4000-8000-000000000000';

/** uuid студента группы A с порядковым номером 1..25. */
function studentId(index: number): string {
  return `c0000000-0000-4000-8000-${String(index).padStart(12, '0')}`;
}

/** 25 различных кириллических фамилий: при сортировке по ФИО дают порядок
 * student01..student25 (Фамилия А — первый, Фамилия Щ — последний). */
const SURNAMES = 'АБВГДЕЖЗИКЛМНОПРСТУФХЦЧШЩ'.split('');

/** Пользователь-студент группы A (в сида кладутся в обратном порядке). */
function makeStudent(index: number): User {
  return {
    id: studentId(index),
    login: `student${String(index).padStart(2, '0')}`,
    email: `student${String(index).padStart(2, '0')}@example.com`,
    fullName: `Фамилия ${SURNAMES[index - 1]}`,
    role: 'student',
    groupId: GROUP_A_ID,
    password: 'Password#2026',
  };
}

function makeUser(
  id: string,
  login: string,
  fullName: string,
  role: User['role'],
  groupId: string | null,
): User {
  return {
    id,
    login,
    email: `${login}@example.com`,
    fullName,
    role,
    groupId,
    password: 'Password#2026',
  };
}

/** Сборка сида: преподаватель, 25 студентов группы A (в обратном порядке),
 * студент без группы, студент чужой группы, пустая группа B, работы двух
 * семестров (sem1 вставлен в перепутанном порядке — 3,1,2) и сдачи,
 * включая чужие (другая группа/семестр). */
function buildSeed(): MockDbData {
  const students = Array.from({ length: 25 }, (_, i) => makeStudent(i + 1)).reverse();
  return {
    ...emptyMockDbData(),
    users: [
      makeUser(TEACHER_ID, 'teacher', 'Преподаватель Пётр', 'teacher', null),
      makeUser(STUDENT_NO_GROUP_ID, 'nogroup', 'Безгруппов Без Группы', 'student', null),
      makeUser(STUDENT_OTHER_GROUP_ID, 'other', 'Чужой Другой', 'student', GROUP_B_ID),
      ...students,
    ],
    groups: [
      { id: GROUP_A_ID, name: 'ИК-221', studentCount: 25 },
      { id: GROUP_B_ID, name: 'ИК-222', studentCount: 1 },
    ],
    labs: [
      { id: LAB_S1_N3_ID, semester: 1, number: 3, content: 'Лаб 3', assignmentUrl: null, defenseRequired: true },
      { id: LAB_S1_N1_ID, semester: 1, number: 1, content: 'Лаб 1', assignmentUrl: null, defenseRequired: true },
      { id: LAB_S1_N2_ID, semester: 1, number: 2, content: 'Лаб 2', assignmentUrl: null, defenseRequired: false },
      { id: LAB_S2_N1_ID, semester: 2, number: 1, content: 'Лаб 1 (сем 2)', assignmentUrl: null, defenseRequired: false },
    ],
    submissions: [
      { id: 'f0000000-0000-4000-8000-000000000001', studentId: studentId(1), labId: LAB_S1_N1_ID, submitDate: '2026-09-01', defenseDate: '2026-09-11', updatedAt: '2026-09-01T08:00:00.000Z', updatedBy: null },
      { id: 'f0000000-0000-4000-8000-000000000002', studentId: studentId(2), labId: LAB_S1_N2_ID, submitDate: '2026-09-02', defenseDate: null, updatedAt: '2026-09-02T08:00:00.000Z', updatedBy: null },
      { id: 'f0000000-0000-4000-8000-000000000003', studentId: studentId(25), labId: LAB_S1_N1_ID, submitDate: '2026-09-03', defenseDate: null, updatedAt: '2026-09-03T08:00:00.000Z', updatedBy: null },
      { id: 'f0000000-0000-4000-8000-000000000004', studentId: STUDENT_OTHER_GROUP_ID, labId: LAB_S1_N1_ID, submitDate: '2026-09-04', defenseDate: null, updatedAt: '2026-09-04T08:00:00.000Z', updatedBy: null },
      { id: 'f0000000-0000-4000-8000-000000000005', studentId: studentId(1), labId: LAB_S2_N1_ID, submitDate: '2026-08-20', defenseDate: null, updatedAt: '2026-08-20T08:00:00.000Z', updatedBy: null },
    ],
  };
}

/** Ловит MockValidationError обработчика; иначе падение теста. */
function expectFailure(action: () => unknown): MockValidationError {
  try {
    action();
  } catch (error) {
    expect(error).toBeInstanceOf(MockValidationError);
    return error as MockValidationError;
  }
  throw new Error('обработчик не выбросил отказ');
}

/** Экземпляр SessionStore фикстур — тот же владелец ключа сессии, что и в
 * обработчиках; состояние в памяти отсутствует, все операции в localStorage. */
const sessionStore = new SessionStore();

function loginAs(userId: string): void {
  sessionStore.setUserId(userId);
}

function logout(): void {
  sessionStore.clear();
}

describe('Мок-обработчики submissions (C-105, IF-106)', () => {
  let db: MockDb;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    resetMockDbSeed();
    configureMockDbSeed(() => buildSeed());
    db = new MockDb();
  });

  afterEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    resetMockDbSeed();
  });

  describe('submissions.getGrid — ведомость группы×семестр (US-10)', () => {
    const getGrid = (params: Partial<SubmissionsGetGridParams> & { groupId: string }) =>
      handleSubmissionsGetGrid(db, { semester: 1, page: 1, ...params });

    beforeEach(() => loginAs(TEACHER_ID));

    it('AC grid-страница: страница 1 — первые 5 по ФИО, 3 работы по number↑, записи только этих 5, total=25', () => {
      const grid = getGrid({ groupId: GROUP_A_ID });

      expect(grid.total).toBe(25);
      expect(grid.students.map((s) => s.fullName)).toEqual([
        'Фамилия А',
        'Фамилия Б',
        'Фамилия В',
        'Фамилия Г',
        'Фамилия Д',
      ]);
      expect(grid.students.map((s) => s.id)).toEqual([
        studentId(1),
        studentId(2),
        studentId(3),
        studentId(4),
        studentId(5),
      ]);
      // Работы семестра 1 по number↑, работа семестра 2 не попала.
      expect(grid.labs.map((l) => l.number)).toEqual([1, 2, 3]);
      expect(grid.labs.map((l) => l.id)).toEqual([LAB_S1_N1_ID, LAB_S1_N2_ID, LAB_S1_N3_ID]);
      // Сдачи есть только у двоих с первой страницы; запись студента 25
      // (страница 5) и чужой группы сюда не попадают.
      expect(grid.submissions).toEqual([
        { studentId: studentId(1), labId: LAB_S1_N1_ID, submitDate: '2026-09-01', defenseDate: '2026-09-11' },
        { studentId: studentId(2), labId: LAB_S1_N2_ID, submitDate: '2026-09-02', defenseDate: null },
      ]);
      // Корректный page проходит в ответ без изменений.
      expect(grid.page).toBe(1);
    });

    it('последняя страница (5 из 5) содержит последних студентов и их сдачи', () => {
      const grid = getGrid({ groupId: GROUP_A_ID, page: 5 });

      expect(grid.students.map((s) => s.fullName)).toEqual([
        'Фамилия Х',
        'Фамилия Ц',
        'Фамилия Ч',
        'Фамилия Ш',
        'Фамилия Щ',
      ]);
      expect(grid.submissions).toEqual([
        { studentId: studentId(25), labId: LAB_S1_N1_ID, submitDate: '2026-09-03', defenseDate: null },
      ]);
      expect(grid.total).toBe(25);
      expect(grid.page).toBe(5);
    });

    it('страница за пределами выборки: студенты и записи пустые, total сохраняется', () => {
      const grid = getGrid({ groupId: GROUP_A_ID, page: 6 });

      expect(grid.students).toEqual([]);
      expect(grid.submissions).toEqual([]);
      expect(grid.labs.map((l) => l.number)).toEqual([1, 2, 3]);
      expect(grid.total).toBe(25);
      expect(grid.page).toBe(6); // корректный page правее последней не искажается
    });

    it('ADR-109 аддендум 1: page=0 и нечисловой page нормализуются к 1 — первая страница данных, без эха', () => {
      const zero = getGrid({ groupId: GROUP_A_ID, page: 0 });
      expect(zero.page).toBe(1);
      expect(zero.students.map((s) => s.fullName)[0]).toBe('Фамилия А');
      expect(zero.total).toBe(25);

      const notNumber = getGrid({ groupId: GROUP_A_ID, page: 'abc' as unknown as number });
      expect(notNumber.page).toBe(1);
      expect(notNumber.students.map((s) => s.fullName)[0]).toBe('Фамилия А');

      const nan = getGrid({ groupId: GROUP_A_ID, page: Number.NaN });
      expect(nan.page).toBe(1);

      // Числовая строка коэрсируется по образцу labs.getList (Number(page)).
      const numericString = getGrid({ groupId: GROUP_A_ID, page: '2' as unknown as number });
      expect(numericString.page).toBe(2);
      expect(numericString.students[0]?.fullName).toBe('Фамилия Е');
    });

    it('порядок студентов при равных ФИО определяется login↑', () => {
      const seed = buildSeed();
      // Пара с одинаковым ФИО «Аванесов А.» (раньше «Фамилия А» по collation);
      // в сид кладутся в обратном порядке логинов.
      seed.users.push(
        makeUser('c0000000-0000-4000-8000-000000000026', 'studenta2', 'Аванесов А.', 'student', GROUP_A_ID),
        makeUser('c0000000-0000-4000-8000-000000000027', 'studenta1', 'Аванесов А.', 'student', GROUP_A_ID),
      );
      configureMockDbSeed(() => seed);
      db = new MockDb();

      const grid = handleSubmissionsGetGrid(db, { groupId: GROUP_A_ID, semester: 1, page: 1 });

      expect(grid.total).toBe(27);
      // Пара «Аванесов А.» упорядочена по login↑ (studenta1, studenta2),
      // далее первые по ФИО из сида.
      expect(grid.students.map((s) => s.id)).toEqual([
        'c0000000-0000-4000-8000-000000000027',
        'c0000000-0000-4000-8000-000000000026',
        studentId(1),
        studentId(2),
        studentId(3),
      ]);
    });

    it('пустая группа: students и submissions пустые, total=0, работы семестра отдаются', () => {
      const seed = buildSeed();
      seed.users = seed.users.filter((u) => u.groupId !== GROUP_B_ID);
      seed.submissions = seed.submissions.filter((s) => s.studentId !== STUDENT_OTHER_GROUP_ID);
      configureMockDbSeed(() => seed);
      db = new MockDb();

      const grid = getGrid({ groupId: GROUP_B_ID });

      expect(grid.total).toBe(0);
      expect(grid.students).toEqual([]);
      expect(grid.submissions).toEqual([]);
      expect(grid.labs.map((l) => l.number)).toEqual([1, 2, 3]);
    });

    it('несуществующая группа → 404 «Группа не найдена»', () => {
      const error = expectFailure(() => getGrid({ groupId: UNKNOWN_ID }));
      expect(error.status).toBe(404);
      expect(error.message).toBe('Группа не найдена');
    });

    it('роли: студенту → 403 «Доступ запрещён», без сессии → 401 «Не авторизован»', () => {
      loginAs(studentId(1));
      const forbidden = expectFailure(() => getGrid({ groupId: GROUP_A_ID }));
      expect(forbidden.status).toBe(403);
      expect(forbidden.message).toBe('Доступ запрещён');

      logout();
      const unauthorized = expectFailure(() => getGrid({ groupId: GROUP_A_ID }));
      expect(unauthorized.status).toBe(401);
      expect(unauthorized.message).toBe('Не авторизован');
    });

    it('битая сессия (пользователь удалён) трактуется как отсутствие сессии → 401', () => {
      loginAs(UNKNOWN_ID);
      const error = expectFailure(() => getGrid({ groupId: GROUP_A_ID }));
      expect(error.status).toBe(401);
    });
  });

  describe('submissions.update — upsert по паре (US-10)', () => {
    const baseParams: SubmissionsUpdateParams = {
      studentId: studentId(3),
      labId: LAB_S1_N3_ID,
      submitDate: '2026-09-15',
      defenseDate: null,
    };

    const update = (params: Partial<SubmissionsUpdateParams> = {}): Submission =>
      handleSubmissionsUpdate(db, { ...baseParams, ...params });

    beforeEach(() => {
      loginAs(TEACHER_ID);
      jasmine.clock().install();
      jasmine.clock().mockDate(new Date('2026-09-27T12:00:00'));
    });

    afterEach(() => {
      jasmine.clock().uninstall();
    });

    it('AC update-upsert: пары нет → запись создана с id-uuid, updated_by — преподаватель сессии', () => {
      const before = db.read().submissions.length;

      const saved = update();

      expect(saved.id).toMatch(MOCK_ID_PATTERN);
      expect(saved.studentId).toBe(baseParams.studentId);
      expect(saved.labId).toBe(baseParams.labId);
      expect(saved.submitDate).toBe('2026-09-15');
      expect(saved.defenseDate).toBeNull();
      expect(saved.updatedBy).toBe(TEACHER_ID);
      expect(saved.updatedAt).toBe(new Date().toISOString());

      const after = db.read().submissions;
      expect(after.length).toBe(before + 1); // создана ровно одна запись
      expect(after.find((s) => s.id === saved.id)).toEqual(saved); // и персистентна
    });

    it('повторный update той же пары изменяет запись без дублирования и обновляет updated_at', () => {
      const created = update();
      const countAfterCreate = db.read().submissions.length;
      jasmine.clock().tick(1500); // время зафиксировано моком часов

      const updated = update({ submitDate: '2026-09-20', defenseDate: '2026-09-30' });

      expect(updated.id).toBe(created.id);
      expect(db.read().submissions.length).toBe(countAfterCreate); // без дублирования
      expect(updated.submitDate).toBe('2026-09-20');
      expect(updated.defenseDate).toBe('2026-09-30');
      expect(updated.updatedBy).toBe(TEACHER_ID);
      expect(updated.updatedAt).toBe(new Date().toISOString());
      expect(updated.updatedAt).not.toBe(created.updatedAt);
    });

    it('AC сброс: null по обеим датам обнуляет существующую запись (сброс сдачи и защиты)', () => {
      const created = update({ submitDate: '2026-09-15', defenseDate: '2026-09-25' });

      const reset = update({ submitDate: null, defenseDate: null });

      expect(reset.id).toBe(created.id);
      expect(reset.submitDate).toBeNull();
      expect(reset.defenseDate).toBeNull();
      expect(db.read().submissions.find((s) => s.id === created.id)?.submitDate).toBeNull();
    });

    it('update меняет и существующую сида-запись, сохраняя её id (без дублей)', () => {
      const seedRecord = db
        .read()
        .submissions.find((s) => s.studentId === studentId(1) && s.labId === LAB_S1_N1_ID);
      if (seedRecord === undefined) {
        throw new Error('сид-запись сдачи студента 1 по лаб 1 не найдена');
      }

      const updated = handleSubmissionsUpdate(db, {
        studentId: studentId(1),
        labId: LAB_S1_N1_ID,
        submitDate: '2026-09-10',
        defenseDate: '2026-09-12',
      });

      expect(updated.id).toBe(seedRecord.id);
      expect(updated.updatedBy).toBe(TEACHER_ID);
      const records = db.read().submissions.filter(
        (s) => s.studentId === studentId(1) && s.labId === LAB_S1_N1_ID,
      );
      expect(records.length).toBe(1);
      expect(records[0]?.defenseDate).toBe('2026-09-12');
    });

    it('несуществующий студент → 404 «Студент не найден» (и преподаватель — тоже не студент)', () => {
      const countBefore = db.read().submissions.length;

      const unknown = expectFailure(() => update({ studentId: UNKNOWN_ID }));
      expect(unknown.status).toBe(404);
      expect(unknown.message).toBe('Студент не найден');

      const teacherAsStudent = expectFailure(() => update({ studentId: TEACHER_ID }));
      expect(teacherAsStudent.status).toBe(404);
      expect(teacherAsStudent.message).toBe('Студент не найден');
      expect(db.read().submissions.length).toBe(countBefore); // отказ ничего не записал
    });

    it('несуществующая работа → 404 «Лабораторная не найдена»', () => {
      const error = expectFailure(() => update({ labId: UNKNOWN_ID }));
      expect(error.status).toBe(404);
      expect(error.message).toBe('Лабораторная не найдена');
    });

    it('неконтрактная дата → 400 «Данные заполнены неверно» с ошибкой по полю submitDate', () => {
      for (const bad of ['15.09.2026', '2026-9-15', '2026-02-30', '']) {
        const error = expectFailure(() => update({ submitDate: bad }));
        expect(error.status).withContext(`значение «${bad}»`).toBe(400);
        expect(error.message).toBe('Данные заполнены неверно');
        expect(error.errors?.['submitDate']).toEqual([jasmine.any(String)]);
      }
    });

    it('неконтрактная defenseDate → 400 с ошибкой по полю defenseDate; нестроковое значение тоже 400', () => {
      const badString = expectFailure(() => update({ defenseDate: '30.09.2026' }));
      expect(badString.status).toBe(400);
      expect(badString.errors?.['defenseDate']).toEqual([jasmine.any(String)]);

      const badType = expectFailure(
        () => update({ defenseDate: 20260930 as unknown as string }),
      );
      expect(badType.status).toBe(400);
      expect(badType.errors?.['defenseDate']).toEqual([jasmine.any(String)]);
    });

    it('роли: студенту → 403, без сессии → 401; отказ не меняет мок-БД', () => {
      const countBefore = db.read().submissions.length;

      loginAs(studentId(3));
      const forbidden = expectFailure(() => update());
      expect(forbidden.status).toBe(403);
      expect(forbidden.message).toBe('Доступ запрещён');

      logout();
      const unauthorized = expectFailure(() => update());
      expect(unauthorized.status).toBe(401);

      expect(db.read().submissions.length).toBe(countBefore);
    });
  });

  describe('submissions.getMy — сдачи текущего студента (US-11)', () => {
    it('AC my-isolation: возвращены работы семестра (number↑) и только собственные сдачи', () => {
      loginAs(studentId(1));

      const mine = handleSubmissionsGetMy(db, { semester: 1 });

      expect(mine.hasGroup).toBeTrue();
      expect(mine.labs.map((l) => l.number)).toEqual([1, 2, 3]);
      // Сдача студента 1 по лаб 1; записи студента 2 и студента 25 по тем же
      // лабам чужие и не отдаются.
      expect(mine.submissions).toEqual([
        { labId: LAB_S1_N1_ID, submitDate: '2026-09-01', defenseDate: '2026-09-11' },
      ]);
    });

    it('фильтр семестра: для семестра 2 — только его работы и сдачи по ним', () => {
      loginAs(studentId(1));

      const mine = handleSubmissionsGetMy(db, { semester: 2 });

      expect(mine.hasGroup).toBeTrue();
      expect(mine.labs.map((l) => l.id)).toEqual([LAB_S2_N1_ID]);
      expect(mine.submissions).toEqual([
        { labId: LAB_S2_N1_ID, submitDate: '2026-08-20', defenseDate: null },
      ]);
    });

    it('AC my-without-group: студент без группы → hasGroup=false и пустые массивы', () => {
      loginAs(STUDENT_NO_GROUP_ID);

      const mine = handleSubmissionsGetMy(db, { semester: 1 });

      expect(mine).toEqual({ hasGroup: false, labs: [], submissions: [] });
    });

    it('роли: teacher → 403 «Доступ запрещён», без сессии → 401', () => {
      loginAs(TEACHER_ID);
      const forbidden = expectFailure(() => handleSubmissionsGetMy(db, { semester: 1 }));
      expect(forbidden.status).toBe(403);
      expect(forbidden.message).toBe('Доступ запрещён');

      logout();
      const unauthorized = expectFailure(() => handleSubmissionsGetMy(db, { semester: 1 }));
      expect(unauthorized.status).toBe(401);
    });
  });

  describe('регистрация в реестре MockApiClient', () => {
    it('все три метода зарегистрированы и отвечают через полный конвейер вызова (500 мс)', fakeAsync(() => {
      const client = new MockApiClient();
      registerSubmissionsHandlers(client);

      loginAs(TEACHER_ID);
      let grid: SubmissionsGridDto | undefined;
      client
        .call<SubmissionsGridDto>('submissions.getGrid', { groupId: GROUP_A_ID, semester: 1, page: 1 })
        .then((result) => (grid = result));
      tick(500);
      expect(grid?.total).toBe(25);

      let saved: Submission | undefined;
      client
        .call<Submission>('submissions.update', {
          studentId: studentId(3),
          labId: LAB_S1_N3_ID,
          submitDate: '2026-09-10',
          defenseDate: null,
        })
        .then((result) => (saved = result));
      tick(500);
      expect(saved?.updatedBy).toBe(TEACHER_ID);

      loginAs(studentId(3));
      let mine: MySubmissionsDto | undefined;
      client.call<MySubmissionsDto>('submissions.getMy', { semester: 1 }).then((result) => (mine = result));
      tick(500);
      expect(mine?.hasGroup).toBeTrue();
      expect(mine?.submissions.some((s) => s.labId === LAB_S1_N3_ID)).toBeTrue();
    }));
  });
});
