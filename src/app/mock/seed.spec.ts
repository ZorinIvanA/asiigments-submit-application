/**
 * Юнит-тесты сид-фикстур (C-107, FR-4.1, ADR-107, data_design).
 *
 * Покрытие по unit_test_requirements подзадачи T-107:
 *  - AC seed-deterministic: два независимых построения — побайтовое
 *    совпадение JSON, все id в формате MOCK_ID_PATTERN и уникальны;
 *  - состав коллекций по data_design: группы ИК-221/222/223, преподаватель,
 *    студенты 01–32 с распределением 25/5/2 (AC seed-counts на уровне сида);
 *  - работы: 20 + 3, содержание, защита у чётных, ссылки у кратных 5;
 *  - сид-сдачи в точности по макетам SCR-009/SCR-010, updated_by — teacher;
 *  - recoveryCodes/resetTokens/rateLimitCounters пусты;
 *  - повторный re-seed мок-БД воспроизводит тот же ключ (FR-026).
 */
import { MockDb, MockDbData, configureMockDbSeed, resetMockDbSeed } from './mock-db';
import { MOCK_ID_PATTERN } from './ids';
import { STORAGE_KEYS, Submission, User } from '../shared/models';
import { seedFixtures } from './seed';

type Lab = ReturnType<typeof seedFixtures>['labs'][number];

const IK221 = 'ИК-221';
const IK222 = 'ИК-222';
const IK223 = 'ИК-223';

function studentByLogin(users: User[], login: string): User {
  const user = users.find((candidate) => candidate.login === login);
  expect(user).withContext(`пользователь ${login} в сида`).toBeDefined();
  return user as User;
}

function groupIdOf(seed: MockDbData, name: string): string {
  const id = seed.groups.find((group) => group.name === name)?.id;
  expect(id).withContext(`группа ${name} в сида`).toBeDefined();
  return id as string;
}

function labIdOf(seed: MockDbData, semester: number, number: number): string {
  const id = seed.labs.find(
    (candidate) => candidate.semester === semester && candidate.number === number,
  )?.id;
  expect(id).withContext(`работа ${semester}/${number} в сида`).toBeDefined();
  return id as string;
}

function submissionOf(
  seed: MockDbData,
  studentLogin: string,
  semester: number,
  labNumber: number,
): Submission {
  const student = studentByLogin(seed.users, studentLogin);
  const labId = labIdOf(seed, semester, labNumber);
  const submission = seed.submissions.find(
    (candidate) => candidate.studentId === student.id && candidate.labId === labId,
  );
  expect(submission)
    .withContext(`сдача ${studentLogin} по работе ${semester}/${labNumber}`)
    .toBeDefined();
  return submission as Submission;
}

describe('seedFixtures — сид мок-БД (C-107, ADR-107)', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  afterEach(() => {
    // Изоляция: сид, подключенный в тесте re-seed, не протекает в другие
    // спеки (ревью CR-002 T-107).
    resetMockDbSeed();
    localStorage.clear();
  });

  describe('AC seed-deterministic: детерминизм', () => {
    it('два независимых построения совпадают байт в байт (JSON)', () => {
      const first = seedFixtures();
      const second = seedFixtures();
      expect(JSON.stringify(first)).toBe(JSON.stringify(second));
    });

    it('все id удовлетворяют MOCK_ID_PATTERN', () => {
      const seed = seedFixtures();
      const ids = [
        ...seed.users.map((u) => u.id),
        ...seed.groups.map((g) => g.id),
        ...seed.labs.map((l) => l.id),
        ...seed.submissions.map((s) => s.id),
      ];
      expect(ids.length).toBeGreaterThan(0);
      for (const id of ids) {
        expect(id).toMatch(MOCK_ID_PATTERN);
      }
    });

    it('все id уникальны в пределах построения', () => {
      const seed = seedFixtures();
      const ids = [
        ...seed.users.map((u) => u.id),
        ...seed.groups.map((g) => g.id),
        ...seed.labs.map((l) => l.id),
        ...seed.submissions.map((s) => s.id),
      ];
      expect(new Set(ids).size).toBe(ids.length);
    });

    it('повторный re-seed мок-БД воспроизводит тот же ключ mock.db.v1 (FR-026, CR-002 ревью T-107)', () => {
      // Re-seed идёт через официальную точку подключения сида, а не через
      // дефолтного поставщика; в afterEach сбрасывается resetMockDbSeed().
      configureMockDbSeed(seedFixtures);

      const first = new MockDb();
      first.read(); // первое обращение инициализирует ключ сидом
      const jsonFirst = localStorage.getItem(STORAGE_KEYS.mockDb);
      expect(jsonFirst).not.toBeNull();
      // Записанный ключ — в точности сериализация текущих фикстур.
      expect(jsonFirst).toBe(JSON.stringify(seedFixtures()));

      localStorage.clear();
      const second = new MockDb();
      second.read(); // первое обращение инициализирует ключ сидом
      const jsonSecond = localStorage.getItem(STORAGE_KEYS.mockDb);

      expect(jsonSecond).toBe(jsonFirst);
    });
  });

  describe('состав коллекций (data_design)', () => {
    it('группы: ровно ИК-221, ИК-222, ИК-223 в порядке генерации uuid', () => {
      const seed = seedFixtures();
      expect(seed.groups.map((g) => g.name)).toEqual([IK221, IK222, IK223]);
      // studentCount вычисляется при выдаче — в хранилище нулевой.
      expect(seed.groups.map((g) => g.studentCount)).toEqual([0, 0, 0]);
    });

    it('пользователи: 1 преподаватель + 32 студента; учётки в точности по data_design', () => {
      const seed = seedFixtures();
      expect(seed.users.length).toBe(33);
      expect(seed.users[0]).toEqual({
        id: seed.users[0].id,
        login: 'teacher',
        email: 'teacher@example.com',
        fullName: 'Сидоров Семён Семёнович',
        role: 'teacher',
        groupId: null,
        password: 'teacher123!',
      });

      const students = seed.users.filter((u) => u.role === 'student');
      expect(students.length).toBe(32);

      const student01 = studentByLogin(seed.users, 'student01');
      expect(student01.login).toBe('student01');
      expect(student01.email).toBe('student01@example.com');
      expect(student01.fullName).toBe('Иванов Иван Иванович 01');
      expect(student01.password).toBe('student123!');
      expect(student01.groupId).toBe(groupIdOf(seed, IK221));
      // Двузначный суффикс NN = 01..32 (не «7», а «07»).
      expect(studentByLogin(seed.users, 'student07').fullName).toBe('Иванов Иван Иванович 07');
      expect(studentByLogin(seed.users, 'student32').login).toBe('student32');
    });

    it('AC seed-counts: ИК-221 = 25, ИК-222 = 5, ИК-223 = 0; без группы — 2', () => {
      const seed = seedFixtures();
      const ik221 = groupIdOf(seed, IK221);
      const ik222 = groupIdOf(seed, IK222);
      const ik223 = groupIdOf(seed, IK223);

      const students = seed.users.filter((u) => u.role === 'student');
      expect(students.filter((s) => s.groupId === ik221).length).toBe(25);
      expect(students.filter((s) => s.groupId === ik222).length).toBe(5);
      expect(students.filter((s) => s.groupId === ik223).length).toBe(0);
      expect(students.filter((s) => s.groupId === null).length).toBe(2);
      // Без группы ровно student31 и student32.
      expect(studentByLogin(seed.users, 'student31').groupId).toBeNull();
      expect(studentByLogin(seed.users, 'student32').groupId).toBeNull();
      expect(studentByLogin(seed.users, 'student25').groupId).toBe(ik221);
      expect(studentByLogin(seed.users, 'student26').groupId).toBe(ik222);
    });

    it('работы: 20 семестра 1 (№1–20) + 3 семестра 2 (№1–3)', () => {
      const seed = seedFixtures();
      expect(seed.labs.length).toBe(23);
      expect(seed.labs.filter((l) => l.semester === 1).map((l) => l.number)).toEqual(
        Array.from({ length: 20 }, (_, i) => i + 1),
      );
      expect(seed.labs.filter((l) => l.semester === 2).map((l) => l.number)).toEqual([1, 2, 3]);
    });

    it('работы: содержание, защита у чётных, ссылки у кратных 5 — по правилам макетов', () => {
      const seed = seedFixtures();
      // Содержание дословно: «Содержание лабораторной работы №N».
      expect(seed.labs.map((l) => l.content)).toEqual(
        seed.labs.map((l) => `Содержание лабораторной работы №${l.number}`),
      );

      const lab = (semester: number, number: number): Lab => {
        const found = seed.labs.find((l) => l.semester === semester && l.number === number);
        expect(found).withContext(`работа ${semester}/${number}`).toBeDefined();
        return found as Lab;
      };

      for (const labRecord of seed.labs) {
        expect(labRecord.defenseRequired).toBe(labRecord.number % 2 === 0);
        expect(labRecord.assignmentUrl).toBe(
          labRecord.number % 5 === 0
            ? `https://git.example.com/assignments/${labRecord.semester}/${labRecord.number}`
            : null,
        );
      }

      // Точечные значения на границах правил.
      expect(lab(1, 1).defenseRequired).toBeFalse();
      expect(lab(1, 2).defenseRequired).toBeTrue();
      expect(lab(1, 5).assignmentUrl).toBe('https://git.example.com/assignments/1/5');
      expect(lab(1, 20).assignmentUrl).toBe('https://git.example.com/assignments/1/20');
      // Семестр 2: номера 1–3 кратных 5 не имеют — ссылок нет.
      expect(seed.labs.filter((l) => l.semester === 2 && l.assignmentUrl !== null).length).toBe(0);
      expect(lab(2, 2).defenseRequired).toBeTrue();
      expect(lab(2, 3).defenseRequired).toBeFalse();
    });

    it('сид-сдачи в точности по макетам SCR-009/SCR-010; updated_by — преподаватель', () => {
      const seed = seedFixtures();
      expect(seed.submissions.length).toBe(4);

      const teacher = seed.users[0];
      expect(teacher.role).toBe('teacher');

      const seedSubmission = submissionOf(seed, 'student01', 1, 1);
      expect(seedSubmission.submitDate).toBe('2026-09-01');
      expect(seedSubmission.defenseDate).toBe('2026-09-11');
      expect(seedSubmission.studentId).toBe(studentByLogin(seed.users, 'student01').id);
      expect(seedSubmission.labId).toBe(labIdOf(seed, 1, 1));
      expect(submissionOf(seed, 'student01', 1, 2).submitDate).toBe('2026-09-02');
      expect(submissionOf(seed, 'student01', 1, 2).defenseDate).toBe('2026-09-12');
      expect(submissionOf(seed, 'student01', 1, 3).submitDate).toBe('2026-09-03');
      expect(submissionOf(seed, 'student01', 1, 3).defenseDate).toBeNull();
      expect(submissionOf(seed, 'student02', 1, 1).submitDate).toBe('2026-09-01');
      expect(submissionOf(seed, 'student02', 1, 1).defenseDate).toBeNull();

      for (const submission of seed.submissions) {
        expect(submission.updatedBy).toBe(teacher.id);
        // updatedAt детерминирован: ISO-метка, одинаковая во всех записях сида.
        expect(submission.updatedAt).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}/);
      }
      expect(new Set(seed.submissions.map((s) => s.updatedAt)).size).toBe(1);
    });

    it('потоки восстановления и счётчики лимитов пусты', () => {
      const seed = seedFixtures();
      expect(seed.recoveryCodes).toEqual([]);
      expect(seed.resetTokens).toEqual([]);
      expect(seed.rateLimitCounters).toEqual({
        register: [],
        loginFailures: {},
        recoveryRequests: {},
      });
    });

    it('сид проходит структурную проверку MockDb (гидратация без защитного re-seed)', () => {
      localStorage.setItem(STORAGE_KEYS.mockDb, JSON.stringify(seedFixtures()));
      const warnSpy = spyOn(console, 'warn');
      const db = new MockDb();
      const hydrated = db.read();
      expect(hydrated).toEqual(seedFixtures());
      expect(warnSpy).not.toHaveBeenCalled();
    });

    it('каждая сид-сдача ссылается на существующую работу — пустых labId нет (CR-001 ревью T-107)', () => {
      // Инвариант против тихого фолбэка labId ?? '': в сиде нет сдач-сирот,
      // resolve несуществующей пары (semester:labNumber) в seedFixtures
      // приводит к fail-fast (throw), а не к записи с пустым labId.
      const seed = seedFixtures();
      const labIds = new Set(seed.labs.map((lab) => lab.id));
      expect(seed.submissions.length).toBeGreaterThan(0);
      for (const submission of seed.submissions) {
        expect(submission.labId).withContext('labId не пустой').not.toBe('');
        expect(labIds.has(submission.labId))
          .withContext(`labId ${submission.labId} есть в таблице работ`)
          .toBeTrue();
      }
    });
  });
});
