/**
 * Мок-обработчики домена Submissions (C-105, контракт IF-106, FR-4.4):
 *  - submissions.getGrid — ведомость группы×семестр для преподавателя
 *    (US-10): пагинация по студентам по 5 на страницу (ADR-109), работы
 *    семестра по number↑, записи сдач только для пар текущей страницы;
 *    в ответе page — нормализованный номер страницы (аддендум 1 к ADR-109);
 *  - submissions.update — upsert записи сдач по паре (studentId, labId)
 *    с фиксацией updated_by/updated_at (US-10);
 *  - submissions.getMy — сдачи текущего студента по семестру с признаком
 *    hasGroup (US-11).
 *
 * Обработчики — чистые синхронные функции над MockDb (ADR-003): чтение —
 * через изолированный снимок read(), мутации — только через транзакционный
 * mutate() (отказ внутри mutate не меняет ни память, ни localStorage).
 * Подключение к реестру — registerSubmissionsHandlers(client).
 *
 * Сессия и роли (IF-101): сессия читается через SessionStore домена Auth
 * (C-101) — единственного владельца ключа mock.session.userId; экземпляр
 * не имеет состояния, все операции идут в localStorage напрямую. Отсутствие
 * сессии либо пользователь не найден → 401 «Не авторизован»; getGrid/update —
 * только teacher, getMy — только student, иначе 403 «Доступ запрещён».
 */
import { SessionStore } from '../auth/session-store';
import { MockApiClient } from '../mock-api-client';
import { MockDb } from '../mock-db';
import { MockValidationError } from '../mock-error';
import { newRuntimeId } from '../ids';
import {
  Lab,
  MySubmissionsDto,
  Submission,
  User,
  UserRole,
} from '../../shared/models';
import { toLocalDate } from '../../shared/dates';
import {
  SUBMISSIONS_PAGE_SIZE,
  SubmissionsGridResult,
} from '../../core/services/submissions.service';

/** Параметры submissions.getGrid (IF-106): группа × семестр × страница. */
export interface SubmissionsGetGridParams {
  /** uuid группы. */
  groupId: string;
  /** Номер семестра, чьи работы образуют колонки ведомости. */
  semester: number;
  /** Номер страницы студентов, 1-based (ADR-109). */
  page: number;
}

/** Параметры submissions.update (IF-106): обе даты передаются целиком. */
export interface SubmissionsUpdateParams {
  /** uuid студента (User с role = student). */
  studentId: string;
  /** uuid лабораторной работы. */
  labId: string;
  /** Дата сдачи 'YYYY-MM-DD' либо null = сброс. */
  submitDate: string | null;
  /** Дата защиты 'YYYY-MM-DD' либо null = сброс. */
  defenseDate: string | null;
}

/** Параметры submissions.getMy (IF-106). */
export interface SubmissionsGetMyParams {
  /** Номер семестра, чьи работы и сдачи возвращаются. */
  semester: number;
}

/** Тексты отказов — дословно IF-106 и словарь ошибок сессии (IF-101, OQ-002). */
const TEXT_UNAUTHORIZED = 'Не авторизован';
const TEXT_FORBIDDEN = 'Доступ запрещён';

/** Доступ к ключу сессии — только через владельца ключа (домен Auth, C-101). */
const sessionStore = new SessionStore();

/**
 * Пользователь текущей сессии (IF-101). Нет ключа или ключ указывает на
 * удалённого пользователя (битая сессия) → 401 «Не авторизован»; сам ключ
 * здесь не чистится — владелец ключа — домен Auth.
 */
function requireSessionUser(users: User[]): User {
  const userId = sessionStore.getUserId();
  if (userId === null) {
    throw new MockValidationError(401, TEXT_UNAUTHORIZED);
  }
  const user = users.find((candidate) => candidate.id === userId);
  if (user === undefined) {
    throw new MockValidationError(401, TEXT_UNAUTHORIZED);
  }
  return user;
}

/** Проверка роли сессии: несовпадение → 403 «Доступ запрещён» (IF-106). */
function requireRole(user: User, role: UserRole): void {
  if (user.role !== role) {
    throw new MockValidationError(403, TEXT_FORBIDDEN);
  }
}

/** Сравнение строк по правилам русского языка — детерминированный порядок. */
const RU_COLLATOR = new Intl.Collator('ru');

/** Порядок студентов ведомости: fullName↑, затем login↑ (SubmissionsGridDto). */
function byFullNameThenLogin(a: User, b: User): number {
  const byName = RU_COLLATOR.compare(a.fullName, b.fullName);
  return byName !== 0 ? byName : RU_COLLATOR.compare(a.login, b.login);
}

/** Порядок работ: number по возрастанию (SubmissionsGridDto/MySubmissionsDto). */
function byLabNumber(a: Lab, b: Lab): number {
  return a.number - b.number;
}

/** Текст полевой ошибки неконтрактной даты (словаря ERROR_TEXTS для дат нет —
 * клиентские контролы p-datepicker физически не производят таких значений,
 * поэтому 400 здесь — защитная проверка контракта мока). */
const CONTRACT_DATE_ERROR = 'Дата должна быть строкой в формате ГГГГ-ММ-ДД';

/**
 * Контрактная дата сдач — 'YYYY-MM-DD' либо null (IF-011): строка разбирается
 * общим парсером shared/dates (строгий формат 4-2-2 + календарная
 * корректность, ADR-007). Нарушение → 400 «Данные заполнены неверно»
 * с ошибкой по полю (§6 спеки).
 */
function requireContractDate(field: 'submitDate' | 'defenseDate', value: unknown): void {
  if (value === null) {
    return;
  }
  if (typeof value !== 'string' || toLocalDate(value) === null) {
    const errors: Record<string, string[]> = { [field]: [CONTRACT_DATE_ERROR] };
    throw new MockValidationError(400, 'Данные заполнены неверно', errors);
  }
}

/** Проекция студента в строку ведомости (без login/password и пр.). */
function toGridStudent(user: User): { id: string; fullName: string } {
  return { id: user.id, fullName: user.fullName };
}

/** Проекция работы в колонку ведомости / экрана сдач. */
function toGridLab(lab: Lab): { id: string; number: number; defenseRequired: boolean } {
  return { id: lab.id, number: lab.number, defenseRequired: lab.defenseRequired };
}

/**
 * submissions.getGrid — ведомость группы×семестр (US-10). Полная выборка
 * студентов группы сортируется (fullName↑, login↑) ДО нарезки страницы
 * (ADR-109); total — полное число студентов группы. Записи сдач отбираются
 * только для пар студент-работа текущей страницы и работ выбранного семестра.
 * ADR-109 аддендум 1: некорректная страница (нецелое/нечисловое/< 1)
 * нормализуется к 1; в ответе page — нормализованное значение, эхо исходного
 * некорректного page запрещено. Страница правее последней — пустые students
 * при корректном total.
 */
export function handleSubmissionsGetGrid(
  db: MockDb,
  params: SubmissionsGetGridParams,
): SubmissionsGridResult {
  const data = db.read();
  requireRole(requireSessionUser(data.users), 'teacher');

  const group = data.groups.find((candidate) => candidate.id === params.groupId);
  if (group === undefined) {
    throw new MockValidationError(404, 'Группа не найдена');
  }

  const students = data.users
    .filter((candidate) => candidate.role === 'student' && candidate.groupId === group.id)
    .sort(byFullNameThenLogin);
  const labs = data.labs.filter((lab) => lab.semester === params.semester).sort(byLabNumber);

  // ADR-109 аддендум 1: нормализация страницы по образцу labs.getList.
  const requested = Number(params.page);
  const page = Number.isInteger(requested) && requested >= 1 ? requested : 1;
  const pageStudents = students.slice(
    (page - 1) * SUBMISSIONS_PAGE_SIZE,
    page * SUBMISSIONS_PAGE_SIZE,
  );
  const pageStudentIds = new Set(pageStudents.map((student) => student.id));
  const semesterLabIds = new Set(labs.map((lab) => lab.id));

  return {
    students: pageStudents.map(toGridStudent),
    labs: labs.map(toGridLab),
    submissions: data.submissions
      .filter(
        (submission) =>
          pageStudentIds.has(submission.studentId) && semesterLabIds.has(submission.labId),
      )
      .map((submission) => ({
        studentId: submission.studentId,
        labId: submission.labId,
        submitDate: submission.submitDate,
        defenseDate: submission.defenseDate,
      })),
    total: students.length,
    page,
  };
}

/**
 * submissions.update — проставление/сброс дат (US-10): upsert по паре
 * (studentId, labId) — клиент всегда передаёт актуальные значения ОБЕИХ дат
 * (null = сброс), частичной записи нет (гарантия IF-106). Мок пишет
 * updated_by (преподаватель сессии) и updated_at (now ISO) при каждой
 * мутации (data_design submissions). Возвращает транспортную копию записи
 * (не узел черновика — см. документацию MockDb.mutate).
 */
export function handleSubmissionsUpdate(
  db: MockDb,
  params: SubmissionsUpdateParams,
): Submission {
  return db.mutate((data) => {
    const teacher = requireSessionUser(data.users);
    requireRole(teacher, 'teacher');

    // Форма payload'а проверяется до разрешения сущностей (400 раньше 404).
    requireContractDate('submitDate', params.submitDate);
    requireContractDate('defenseDate', params.defenseDate);

    const student = data.users.find(
      (candidate) => candidate.id === params.studentId && candidate.role === 'student',
    );
    if (student === undefined) {
      throw new MockValidationError(404, 'Студент не найден');
    }
    const lab = data.labs.find((candidate) => candidate.id === params.labId);
    if (lab === undefined) {
      throw new MockValidationError(404, 'Лабораторная не найдена');
    }

    const now = new Date().toISOString();
    const existing = data.submissions.find(
      (submission) =>
        submission.studentId === params.studentId && submission.labId === params.labId,
    );
    if (existing === undefined) {
      const created: Submission = {
        id: newRuntimeId(),
        studentId: params.studentId,
        labId: params.labId,
        submitDate: params.submitDate,
        defenseDate: params.defenseDate,
        updatedAt: now,
        updatedBy: teacher.id,
      };
      data.submissions.push(created);
      return { ...created };
    }
    existing.submitDate = params.submitDate;
    existing.defenseDate = params.defenseDate;
    existing.updatedAt = now;
    existing.updatedBy = teacher.id;
    return { ...existing };
  });
}

/**
 * submissions.getMy — сдачи текущего студента по семестру (US-11). Студент
 * без группы получает hasGroup=false и пустые массивы (§4.4 — вместо
 * таблицы предупреждение, данные работ не отдаются); студент группы —
 * работы семестра (number↑) и только собственные сдачи (чужие данные
 * не отдаются, §4.4).
 */
export function handleSubmissionsGetMy(
  db: MockDb,
  params: SubmissionsGetMyParams,
): MySubmissionsDto {
  const data = db.read();
  const student = requireSessionUser(data.users);
  requireRole(student, 'student');

  if (student.groupId === null) {
    return { hasGroup: false, labs: [], submissions: [] };
  }

  const labs = data.labs.filter((lab) => lab.semester === params.semester).sort(byLabNumber);
  const semesterLabIds = new Set(labs.map((lab) => lab.id));

  return {
    hasGroup: true,
    labs: labs.map(toGridLab),
    submissions: data.submissions
      .filter(
        (submission) =>
          submission.studentId === student.id && semesterLabIds.has(submission.labId),
      )
      .map((submission) => ({
        labId: submission.labId,
        submitDate: submission.submitDate,
        defenseDate: submission.defenseDate,
      })),
  };
}

/**
 * Подключает обработчики домена Submissions к реестру мок-клиента
 * (точка сборки мок-слоя); повторная регистрация заменяет обработчик.
 */
export function registerSubmissionsHandlers(client: MockApiClient): void {
  client.register('submissions.getGrid', handleSubmissionsGetGrid);
  client.register('submissions.update', handleSubmissionsUpdate);
  client.register('submissions.getMy', handleSubmissionsGetMy);
}
