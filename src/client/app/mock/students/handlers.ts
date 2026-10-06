/**
 * Мок-обработчики домена Students (C-104, контракт IF-105, FR-4.6/US-17,
 * ADR-102/ADR-109): список студентов раздела «Доступ» и назначение группы.
 *
 *  - 'students.getList' {search?, groupId?: 'none'|uuid|null, page} →
 *    PagedResult<StudentDto>: поиск многословный (уточнение IF-105,
 *    CR-010) — запрос после трима разбивается на токены по пробелам;
 *    запись проходит, если ВСЕ токены — подстроки без учёта регистра
 *    ОДНОГО И ТОГО ЖЕ поля (ФИО ИЛИ логин ИЛИ email); порядок токенов
 *    не значим, пустой запрос — без фильтра; groupId='none' — только
 *    студенты без группы, uuid —
 *    только эта группа (несуществующий uuid — пустая выборка, 404 здесь
 *    не предусмотрен контрактом); порядок ФИО↑ затем login↑ (русская
 *    локаль-коллация) применяется к полной выборке до нарезки страниц
 *    (ADR-109) и не зависит от поиска/фильтра; страница 10
 *    (STUDENTS_PAGE_SIZE); некорректная страница (нечисловое/< 1)
 *    нормализуется к 1 (ADR-109 аддендум 1), страница правее последней —
 *    пустая items при корректном total; search длиннее 200 символов —
 *    400 «Данные заполнены неверно» + errors {search}.
 *  - 'students.setGroup' {studentId, groupId: uuid|null} — единственная
 *    точка изменения users.group_id (IF-105): uuid — включение/перевод,
 *    null — исключение из группы; несуществующий студент или группа — 404.
 *
 * Оба метода — только для роли teacher (иначе 403 «Доступ запрещён»);
 * без действующей или битой сессии — 401 «Не авторизован» (IF-101).
 * Сессия читается только через SessionStore домена Auth (C-101) —
 * единственного владельца ключа mock.session.userId (ADR-105); экземпляр
 * не имеет состояния, все операции идут в localStorage напрямую.
 * Обработчики — чистые функции над MockDb (ADR-003): чтение через read(),
 * изменение — только через транзакционный mutate.
 */

import { SessionStore } from '../auth/session-store';
import { PagedResult, StudentDto, User } from '../../shared/models';
import { MockApiClient, MockHandler } from '../mock-api-client';
import { MockDbData } from '../mock-db';
import { MockValidationError } from '../mock-error';

/**
 * Размер страницы students.getList (IF-105, ADR-109). Единый источник
 * значения для мока и StudentsService (сервис переэкспортирует константу).
 */
export const STUDENTS_PAGE_SIZE = 10;

/** Верхняя граница длины search (IF-105: «длиннее 200» — отказ 400). */
const SEARCH_MAX_LENGTH = 200;

/** Специальное значение фильтра «только студенты без группы» (IF-105). */
const GROUP_FILTER_NONE = 'none';

/** Доступ к ключу сессии — только через владельца ключа (домен Auth, C-101). */
const sessionStore = new SessionStore();

/** Русская локаль-коллация для порядков над User — единый объект на модуль. */
const RU_COLLATOR = new Intl.Collator('ru');

/** Параметры students.getList (IF-105). */
export interface StudentsGetListParams {
  /**
   * Многословный поисковый запрос (уточнение IF-105, CR-010): токены через
   * пробел, запись проходит, если каждый токен — подстрока (ci) одного и
   * того же поля (ФИО/логин/email); пусто — без поиска.
   */
  search?: string | null;
  /** 'none' — только без группы; uuid — только эта группа; null/undefined — без фильтра. */
  groupId?: 'none' | string | null;
  /** Номер страницы, 1-based (ADR-109). */
  page: number;
}

/** Параметры students.setGroup (IF-105). */
export interface StudentsSetGroupParams {
  /** uuid студента (User с ролью student). */
  studentId: string;
  /** uuid группы либо null = исключить из группы. */
  groupId: string | null;
}

/** Пользователь сессии либо null (ключ сессии читается только SessionStore — IF-101). */
function sessionUser(data: MockDbData): User | null {
  const userId = sessionStore.getUserId();
  if (userId === null) {
    return null;
  }
  return data.users.find((user) => user.id === userId) ?? null;
}

/** Авторизованный преподаватель либо отказ 401/403 (IF-105: teacher-only). */
function requireTeacher(data: MockDbData): void {
  const user = sessionUser(data);
  if (user === null) {
    throw new MockValidationError(401, 'Не авторизован');
  }
  if (user.role !== 'teacher') {
    throw new MockValidationError(403, 'Доступ запрещён');
  }
}

/** Фильтр по группе: 'none' — без группы, uuid — точная группа, иначе без фильтра. */
function matchesGroupFilter(user: User, groupId: 'none' | string | null | undefined): boolean {
  if (groupId === undefined || groupId === null) {
    return true;
  }
  if (groupId === GROUP_FILTER_NONE) {
    return user.groupId === null;
  }
  return user.groupId === groupId;
}

/**
 * Разбор поискового запроса на токены (уточнение IF-105, CR-010): трим,
 * нижний регистр, разбиение по пробелам; пустой перечень — фильтр не
 * применяется.
 */
function searchTokens(search: string): string[] {
  return search
    .trim()
    .toLowerCase()
    .split(/\s+/)
    .filter((token) => token !== '');
}

/**
 * Многословный поиск (уточнение IF-105, CR-010): запись проходит, если ВСЕ
 * токены — подстроки (ci) ОДНОГО И ТОГО ЖЕ поля (ФИО ИЛИ логин ИЛИ email);
 * порядок токенов не значим. Пустой перечень токенов — без фильтра.
 */
function matchesSearch(user: User, tokens: string[]): boolean {
  if (tokens.length === 0) {
    return true;
  }
  const loweredFields = [
    user.fullName.toLowerCase(),
    user.login.toLowerCase(),
    user.email.toLowerCase(),
  ];
  return loweredFields.some((field) => tokens.every((token) => field.includes(token)));
}

/** Порядок списка студентов: ФИО↑, затем login↑ (IF-105) — русская локаль-коллация. */
function compareFullNameLogin(a: User, b: User): number {
  const byName = RU_COLLATOR.compare(a.fullName, b.fullName);
  return byName !== 0 ? byName : RU_COLLATOR.compare(a.login, b.login);
}

/**
 * Проекция User → StudentDto (IF-105): без пароля и служебных полей;
 * groupName — имя группы по group_id (у удалённой/неизвестной группы — null).
 */
function toStudentDto(user: User, groupNames: ReadonlyMap<string, string>): StudentDto {
  return {
    id: user.id,
    fullName: user.fullName,
    login: user.login,
    email: user.email,
    // Аменда 6: groupId — ключевое значение для селекторов страницы «Доступ»
    // (восстановление по имени groupName справочником запрещено).
    groupId: user.groupId,
    groupName: user.groupId === null ? null : (groupNames.get(user.groupId) ?? null),
  };
}

/**
 * students.getList (IF-105, ADR-109): роли, поиск/фильтр и сортировка
 * применяются к полной выборке, затем нарезается запрошенная страница;
 * total — размер полной выборки после фильтра/поиска.
 */
const getListHandler: MockHandler<StudentsGetListParams, PagedResult<StudentDto>> = (db, params) => {
  const data = db.read();
  requireTeacher(data);

  const search = params.search ?? '';
  if (search.length > SEARCH_MAX_LENGTH) {
    throw new MockValidationError(400, 'Данные заполнены неверно', {
      search: ['Поиск — не более 200 символов'],
    });
  }

  const groupNames = new Map(data.groups.map((group) => [group.id, group.name]));
  const tokens = searchTokens(search);

  const matched = data.users
    .filter((user) => user.role === 'student')
    .filter((user) => matchesGroupFilter(user, params.groupId))
    .filter((user) => matchesSearch(user, tokens))
    .sort(compareFullNameLogin);

  // ADR-109 аддендум 1: некорректная страница (нецелое/нечисловое/< 1)
  // нормализуется к 1 — эхо исходного значения в PagedResult запрещено.
  const requested = Number(params.page);
  const page = Number.isInteger(requested) && requested >= 1 ? requested : 1;
  const start = (page - 1) * STUDENTS_PAGE_SIZE;

  return {
    items: matched
      .slice(start, start + STUDENTS_PAGE_SIZE)
      .map((user) => toStudentDto(user, groupNames)),
    total: matched.length,
    page,
    pageSize: STUDENTS_PAGE_SIZE,
  };
};

/**
 * students.setGroup (IF-105): включение/перевод (uuid) и исключение (null).
 * Мутация транзакционна: отказ 404 оставляет состояние и localStorage
 * нетронутыми; в качестве студента принимается только User с ролью student.
 */
const setGroupHandler: MockHandler<StudentsSetGroupParams, void> = (db, params) => {
  requireTeacher(db.read());
  db.mutate((data) => {
    const student = data.users.find((user) => user.id === params.studentId);
    if (student === undefined || student.role !== 'student') {
      throw new MockValidationError(404, 'Студент не найден');
    }
    if (params.groupId !== null && !data.groups.some((group) => group.id === params.groupId)) {
      throw new MockValidationError(404, 'Группа не найдена');
    }
    student.groupId = params.groupId;
  });
};

/**
 * Регистрирует обработчики домена Students в реестре клиента (IF-110):
 * вызывается агрегатором мок-слоя; повторный вызов заменяет обработчики.
 */
export function registerStudentsHandlers(client: MockApiClient): void {
  client.register('students.getList', getListHandler);
  client.register('students.setGroup', setGroupHandler);
}
