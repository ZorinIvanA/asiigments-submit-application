/**
 * Сид-фикстуры мок-БД (C-107, FR-4.1, ADR-107): детерминированное начальное
 * состояние mock.db.v1 по правилам макетов (data_design, SCR-009/SCR-010).
 *
 * Детерминизм (ADR-107): все id генерируются createDeterministicUuidGenerator
 * с фиксированным seed — повторное построение (re-seed после очистки
 * localStorage или повреждения данных) воспроизводит те же значения байт в
 * байт. Порядок обхода генератора зафиксирован: группы → преподаватель →
 * студенты 01–32 → работы (семестр 1 №1–20, затем семестр 2 №1–3) → сдачи;
 * любое изменение порядка меняет значения id и запрещено.
 *
 * Демо-учётки (единственный носитель паролей вместо Seed__-переменных
 * окружения §10 — OQ-001): teacher/teacher123!, studentNN/student123!.
 */
import { Group, Lab, Submission, User } from '../shared/models';
import { createDeterministicUuidGenerator } from './ids';
import { MockDbData } from './mock-db';

/** Seed детерминированного uuid-генератора (ADR-107). */
const SEED = 20260927;

/** Демо-группы в порядке генерации uuid (ИК-223 остаётся без студентов). */
const GROUP_NAMES = ['ИК-221', 'ИК-222', 'ИК-223'] as const;

/** Число демо-студентов (data_design: student01..student32). */
const STUDENT_COUNT = 32;

/** Работы по семестрам: [семестр, номер последней работы] (OQ-003). */
const LABS_PER_SEMESTER: ReadonlyArray<readonly [semester: number, lastNumber: number]> = [
  [1, 20],
  [2, 3],
];

/** Демо-пароль студентов (ADR-107, OQ-001). */
const STUDENT_PASSWORD = 'student123!';

/** Фиксированное время последнего изменения сид-сдач (детерминизм сида). */
const SEED_SUBMISSION_UPDATED_AT = '2026-09-11T12:00:00.000Z';

/** Правила сид-сдач (макеты SCR-009/SCR-010): студент NN, работа (семестр, №). */
interface SeedSubmissionRule {
  /** Номер демо-студента (1-based): 1 → student01. */
  studentNn: number;
  /** Семестр работы. */
  semester: number;
  /** Номер работы в семестре. */
  labNumber: number;
  /** Дата сдачи 'YYYY-MM-DD'. */
  submitDate: string | null;
  /** Дата защиты 'YYYY-MM-DD'. */
  defenseDate: string | null;
}

/** Сид-сдачи по макетам SCR-009/SCR-010 (data_design: student01 — лабы 1/2/3, student02 — лаба 1). */
const SEED_SUBMISSIONS: ReadonlyArray<SeedSubmissionRule> = [
  { studentNn: 1, semester: 1, labNumber: 1, submitDate: '2026-09-01', defenseDate: '2026-09-11' },
  { studentNn: 1, semester: 1, labNumber: 2, submitDate: '2026-09-02', defenseDate: '2026-09-12' },
  { studentNn: 1, semester: 1, labNumber: 3, submitDate: '2026-09-03', defenseDate: null },
  { studentNn: 2, semester: 1, labNumber: 1, submitDate: '2026-09-01', defenseDate: null },
];

/** Номер студента в виде ровно двух цифр (data_design: NN = 01..32). */
function studentSuffix(nn: number): string {
  return String(nn).padStart(2, '0');
}

/**
 * Строит полный сид мок-БД. Чистая функция без побочных эффектов: запись в
 * localStorage делает MockDb при инициализации ключа (mock-db.ts).
 */
export function seedFixtures(): MockDbData {
  const nextId = createDeterministicUuidGenerator(SEED);

  // 1. Группы: ИК-221, ИК-222, ИК-223 (studentCount вычисляется при выдаче,
  // в хранилище хранится 0 — data_design Group).
  const groups: Group[] = GROUP_NAMES.map((name) => ({
    id: nextId(),
    name,
    studentCount: 0,
  }));

  // 2. Преподаватель — единственная роль teacher демо-данных.
  const teacher: User = {
    id: nextId(),
    login: 'teacher',
    email: 'teacher@example.com',
    fullName: 'Сидоров Семён Семёнович',
    role: 'teacher',
    groupId: null,
    password: 'teacher123!',
  };

  // 3. Студенты 01–32: 01–25 → ИК-221, 26–30 → ИК-222, 31–32 → без группы.
  const [ik221, ik222] = groups;
  const students: User[] = Array.from({ length: STUDENT_COUNT }, (_, index) => {
    const nn = index + 1;
    const suffix = studentSuffix(nn);
    return {
      id: nextId(),
      login: `student${suffix}`,
      email: `student${suffix}@example.com`,
      fullName: `Иванов Иван Иванович ${suffix}`,
      role: 'student',
      groupId: nn <= 25 ? ik221.id : nn <= 30 ? ik222.id : null,
      password: STUDENT_PASSWORD,
    };
  });

  // 4. Работы: семестр 1 №1–20, затем семестр 2 №1–3; защита у чётных,
  // ссылка у кратных 5 (data_design Lab).
  const labs: Lab[] = [];
  const labIdByKey = new Map<string, string>();
  for (const [semester, lastNumber] of LABS_PER_SEMESTER) {
    for (let number = 1; number <= lastNumber; number++) {
      const id = nextId();
      labIdByKey.set(`${semester}:${number}`, id);
      labs.push({
        id,
        semester,
        number,
        content: `Содержание лабораторной работы №${number}`,
        defenseRequired: number % 2 === 0,
        assignmentUrl:
          number % 5 === 0 ? `https://git.example.com/assignments/${semester}/${number}` : null,
      });
    }
  }

  // 5. Сдачи по макетам SCR-009/SCR-010; updated_by — преподаватель
  // (запись введена сидом от его имени), updatedAt — фиксированная метка.
  // Полнота таблицы работ проверяется fail-fast (ревью CR-001 T-107):
  // тихий фолбэк labId ?? '' дал бы сдачу-сироту, невидимую в ведомости.
  const submissions: Submission[] = SEED_SUBMISSIONS.map((rule) => {
    const labId = labIdByKey.get(`${rule.semester}:${rule.labNumber}`);
    if (labId === undefined) {
      throw new Error(
        `seedFixtures: работы ${rule.semester}:${rule.labNumber} нет в LABS_PER_SEMESTER — ` +
          `правило сид-сдачи (studentNn=${rule.studentNn}) невыполнимо, таблица работ неполна`,
      );
    }
    return {
      id: nextId(),
      studentId: students[rule.studentNn - 1].id,
      labId,
      submitDate: rule.submitDate,
      defenseDate: rule.defenseDate,
      updatedAt: SEED_SUBMISSION_UPDATED_AT,
      updatedBy: teacher.id,
    };
  });

  // Потоки восстановления и счётчики лимитов стартуют пустыми (data_design).
  return {
    users: [teacher, ...students],
    groups,
    labs,
    submissions,
    recoveryCodes: [],
    resetTokens: [],
    rateLimitCounters: { register: [], loginFailures: {}, recoveryRequests: {} },
  };
}
