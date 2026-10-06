/**
 * Мок-обработчики домена Labs (C-102, контракт IF-103, FR-4.3/FR-4.4,
 * ADR-104/ADR-109): CRUD лабораторных работ, getById для deep-link формы
 * редактирования и перечень семестров для селекторов ведомости.
 *
 * Контракт IF-103:
 *  - labs.getList {semester?, page, sortField?, sortDir?} →
 *    PagedResult<LabDto>: pageSize LABS_PAGE_SIZE=10; фильтр и
 *    двухколоночная сортировка применяются к полной выборке до нарезки
 *    страниц; сортировка без состояния «без сортировки» — при отсутствии
 *    параметров действует дефолт semester↑,number↑, иначе первичный ключ
 *    sortField/sortDir и вторичный — соседнее поле asc; некорректная
 *    страница (нецелое/нечисловое/< 1) нормализуется к 1 (ADR-109
 *    аддендум 1), страница правее последней — пустая items при корректном
 *    total;
 *  - labs.getById {id} → LabDto | 404 «Лабораторная не найдена» (ADR-104);
 *  - labs.create {number, semester, content, assignmentUrl, defenseRequired}
 *    → LabDto; labs.update {id, …те же поля} → LabDto; нарушение правил §8
 *    → 400 «Данные заполнены неверно» + errors {number, semester, content,
 *    assignmentUrl} — правила и тексты полей берутся из validators.ts и
 *    ERROR_TEXTS (единый источник границ); существующая пара
 *    (semester, number) → 409 «Лабораторная с таким номером уже есть в
 *    семестре», при update собственная запись не конфликтует; несуществующий
 *    id при update → 404;
 *  - labs.remove {id} → каскадно удаляет записи ведомости по labId (§4.3);
 *    несуществующий id → 404;
 *  - labs.semesters → distinct семестры по возрастанию — доступен любой
 *    роли с сессией; остальные методы — только teacher, иначе
 *    403 «Доступ запрещён»; без сессии или с битой сессией →
 *    401 «Не авторизован».
 *
 * Порядок проверок при отказе: сессия (401) → роль (403) → валидация (400)
 * → существование записи (404) → уникальность пары (409).
 *
 * Сессия читается через SessionStore домена Auth (C-101) — единственного
 * владельца ключа mock.session.userId; экземпляр не имеет состояния, все
 * операции идут в localStorage напрямую.
 */

import { FormControl, ValidatorFn } from '@angular/forms';

import { SessionStore } from '../auth/session-store';
import { Lab, LabDto, PagedResult, User } from '../../shared/models';
import {
  firstErrorText,
  labContent,
  labNumber,
  labSemester,
  labUrl,
  requiredTrim,
} from '../../shared/validation/validators';
import { newRuntimeId } from '../ids';
import { MockApiClient } from '../mock-api-client';
import { MockDb, MockDbData } from '../mock-db';
import { MockValidationError } from '../mock-error';

/** Поле первичной сортировки списка (IF-103): номер или семестр. */
export type LabsSortField = 'number' | 'semester';

/** Направление первичной сортировки (IF-103). */
export type LabsSortDir = 'asc' | 'desc';

/** Параметры labs.getList (IF-103). */
export interface LabsGetListParams {
  /** Фильтр по семестру; null/undefined — все семестры. */
  semester?: number | null;
  /**
   * Номер страницы, начиная с 1. Некорректное значение (нецелое,
   * нечисловое, < 1) нормализуется к 1 (ADR-109 аддендум 1); страница
   * правее последней — пустая items при корректном total.
   */
  page: number;
  /** Первичный ключ сортировки; по умолчанию 'semester' (дефолт semester↑,number↑). */
  sortField?: LabsSortField;
  /** Направление первичного ключа; по умолчанию 'asc'. */
  sortDir?: LabsSortDir;
}

/** Вход формы лабораторной (IF-103: create(input), update(id, input)). */
export interface LabInput {
  /** Номер работы: целое > 0 (§8). */
  number: number;
  /** Семестр: целое 1..MAX_SEMESTER (§8, validators.labSemester). */
  semester: number;
  /** Содержание работы: 1–500 символов после трима (§8). */
  content: string;
  /** Ссылка на задание (префикс http/https) либо null (§8). */
  assignmentUrl: string | null;
  /** Признак «Нужна защита». */
  defenseRequired: boolean;
}

/** Параметры labs.update: id правимой записи + поля формы. */
export interface LabsUpdateParams extends LabInput {
  id: string;
}

/** Параметры labs.getById / labs.remove. */
export interface LabIdParams {
  id: string;
}

/** Размер страницы списка лабораторных (§4.3, ADR-109) — контракт IF-103. */
export const LABS_PAGE_SIZE = 10;

/** Тексты отказов — дословно IF-103 и словарь ошибок сессии (IF-101, OQ-002). */
const TEXT_UNAUTHORIZED = 'Не авторизован';
const TEXT_FORBIDDEN = 'Доступ запрещён';
const TEXT_INVALID = 'Данные заполнены неверно';
const TEXT_NOT_FOUND = 'Лабораторная не найдена';
const TEXT_DUPLICATE = 'Лабораторная с таким номером уже есть в семестре';

/** Доступ к ключу сессии — только через владельца ключа (домен Auth, C-101). */
const sessionStore = new SessionStore();

/**
 * Пользователь текущей сессии. Нет ключа или ключ указывает на удалённого
 * пользователя (битая сессия) → 401 «Не авторизован».
 */
function requireSessionUser(db: MockDb): User {
  const userId = sessionStore.getUserId();
  const user =
    userId === null ? undefined : db.read().users.find((candidate) => candidate.id === userId);
  if (user === undefined) {
    throw new MockValidationError(401, TEXT_UNAUTHORIZED);
  }
  return user;
}

/** Пользователь-преподаватель текущей сессии: иначе 403 «Доступ запрещён». */
function requireTeacher(db: MockDb): User {
  const user = requireSessionUser(db);
  if (user.role !== 'teacher') {
    throw new MockValidationError(403, TEXT_FORBIDDEN);
  }
  return user;
}

/** Текст первой ошибки поля по валидатору validators.ts; null — поле валидно. */
function fieldError(validator: ValidatorFn, value: unknown): string | null {
  return firstErrorText(validator(new FormControl(value)));
}

/**
 * Валидация входа формы лабораторной по §8 (IF-103). Правила и тексты —
 * только из validators.ts/ERROR_TEXTS (DoD подзадачи): номер — целое > 0;
 * семестр — целое 1..MAX_SEMESTER; содержание — 1–500 символов после трима;
 * ссылка — префикс http/https либо пусто. Нарушение → 400 «Данные заполнены
 * неверно» + errors по полям {number, semester, content, assignmentUrl}.
 */
function validateLabInput(input: LabInput): void {
  const errors: Record<string, string[]> = {};
  const add = (field: keyof LabInput, message: string | null): void => {
    if (message !== null) {
      (errors[field] ??= []).push(message);
    }
  };
  add('number', fieldError(requiredTrim(), input.number));
  add('number', fieldError(labNumber(), input.number));
  add('semester', fieldError(requiredTrim(), input.semester));
  add('semester', fieldError(labSemester(), input.semester));
  add('content', fieldError(requiredTrim(), input.content));
  add('content', fieldError(labContent(), input.content));
  add('assignmentUrl', fieldError(labUrl(), input.assignmentUrl ?? null));
  if (Object.keys(errors).length > 0) {
    throw new MockValidationError(400, TEXT_INVALID, errors);
  }
}

/**
 * Нормализация полей после успешной валидации — те же правила, что и у форм
 * с trimFormValues (IF-010): числовые поля — Number (валидатор допускает
 * строку из цифр), содержание и ссылка триммятся, пустая ссылка → null,
 * флаг — строго boolean.
 */
function normalizeLabInput(input: LabInput): LabInput {
  const rawUrl = input.assignmentUrl;
  const trimmedUrl = typeof rawUrl === 'string' ? rawUrl.trim() : '';
  return {
    number: Number(input.number),
    semester: Number(input.semester),
    content: String(input.content).trim(),
    assignmentUrl: trimmedUrl === '' ? null : trimmedUrl,
    defenseRequired: input.defenseRequired === true,
  };
}

function compareByField(a: Lab, b: Lab, field: LabsSortField): number {
  return field === 'semester' ? a.semester - b.semester : a.number - b.number;
}

/**
 * Двухколоночная сортировка без состояния «без сортировки» (IF-103,
 * SCR-007): первичный ключ — sortField/sortDir, вторичный — соседнее поле
 * всегда по возрастанию. При значениях по умолчанию (semester, asc) это
 * контрактный дефолт semester↑,number↑.
 */
function compareLabs(a: Lab, b: Lab, sortField: LabsSortField, sortDir: LabsSortDir): number {
  const direction = sortDir === 'desc' ? -1 : 1;
  const primary = compareByField(a, b, sortField) * direction;
  if (primary !== 0) {
    return primary;
  }
  const secondary: LabsSortField = sortField === 'number' ? 'semester' : 'number';
  return compareByField(a, b, secondary);
}

/** Транспортная копия записи: узел черновика mutate наружу не отдаётся. */
function toLabDto(lab: Lab): LabDto {
  return { ...lab };
}

/**
 * 409 при существующей паре (semester, number); excludeId — id правимой
 * записи при update (своя запись конфликтом не считается).
 */
function assertNoDuplicate(
  data: MockDbData,
  semester: number,
  number: number,
  excludeId: string | null,
): void {
  const duplicate = data.labs.some(
    (lab) => lab.id !== excludeId && lab.semester === semester && lab.number === number,
  );
  if (duplicate) {
    throw new MockValidationError(409, TEXT_DUPLICATE);
  }
}

/**
 * Регистрирует обработчики labs.* в реестре клиента (IF-110: каждый домен
 * экспортирует register<Domain>Handlers). Обработчики — чистые функции над
 * MockDb (ADR-003): чтение через read(), мутации — только через mutate.
 */
export function registerLabsHandlers(client: MockApiClient): void {
  client.register<LabsGetListParams, PagedResult<LabDto>>('labs.getList', (db, params) => {
    requireTeacher(db);
    const semester = params.semester ?? null;
    const sortField: LabsSortField = params.sortField ?? 'semester';
    const sortDir: LabsSortDir = params.sortDir ?? 'asc';
    // ADR-109 аддендум 1: некорректная страница (нецелое/нечисловое/< 1)
    // нормализуется к 1 — эхо исходного значения в PagedResult запрещено.
    const requested = Number(params.page);
    const page = Number.isInteger(requested) && requested >= 1 ? requested : 1;
    const filtered = db
      .read()
      .labs.filter((lab) => semester === null || lab.semester === semester)
      .sort((a, b) => compareLabs(a, b, sortField, sortDir));
    // Страница правее последней — пустая items при корректном total (IF-103).
    const start = (page - 1) * LABS_PAGE_SIZE;
    const items = filtered.slice(start, start + LABS_PAGE_SIZE).map(toLabDto);
    return { items, total: filtered.length, page, pageSize: LABS_PAGE_SIZE };
  });

  client.register<LabIdParams, LabDto>('labs.getById', (db, params) => {
    requireTeacher(db);
    const lab = db.read().labs.find((candidate) => candidate.id === params.id);
    if (lab === undefined) {
      throw new MockValidationError(404, TEXT_NOT_FOUND);
    }
    return toLabDto(lab);
  });

  client.register<LabInput, LabDto>('labs.create', (db, params) => {
    requireTeacher(db);
    validateLabInput(params);
    const input = normalizeLabInput(params);
    return db.mutate((data) => {
      assertNoDuplicate(data, input.semester, input.number, null);
      const lab: Lab = { id: newRuntimeId(), ...input };
      data.labs.push(lab);
      return toLabDto(lab);
    });
  });

  client.register<LabsUpdateParams, LabDto>('labs.update', (db, params) => {
    requireTeacher(db);
    validateLabInput(params);
    const input = normalizeLabInput(params);
    return db.mutate((data) => {
      const lab = data.labs.find((candidate) => candidate.id === params.id);
      if (lab === undefined) {
        throw new MockValidationError(404, TEXT_NOT_FOUND);
      }
      assertNoDuplicate(data, input.semester, input.number, params.id);
      Object.assign(lab, input); // id записи сохраняется
      return toLabDto(lab);
    });
  });

  client.register<LabIdParams, void>('labs.remove', (db, params) => {
    requireTeacher(db);
    db.mutate((data) => {
      const index = data.labs.findIndex((candidate) => candidate.id === params.id);
      if (index === -1) {
        throw new MockValidationError(404, TEXT_NOT_FOUND);
      }
      data.labs.splice(index, 1);
      // Каскадное удаление записей ведомости по работе (§4.3).
      data.submissions = data.submissions.filter(
        (submission) => submission.labId !== params.id,
      );
    });
  });

  client.register<null, number[]>('labs.semesters', (db) => {
    requireSessionUser(db); // доступен любой роли с сессией (IF-103)
    const semesters = [...new Set(db.read().labs.map((lab) => lab.semester))];
    return semesters.sort((a, b) => a - b);
  });
}
