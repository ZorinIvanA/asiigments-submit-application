/**
 * Мок-обработчики домена Groups (C-103, контракт IF-104, US-16 §4.5, ADR-109).
 *
 * Пять методов реестра MockApiClient:
 *  - groups.getList    → GroupDto[]   (studentCount вычисляется; name↑ ci);
 *  - groups.create     → GroupDto     (валидация 1–100 → 400; дубликат ci → 409);
 *  - groups.rename     → GroupDto     (400 / 404 / 409 по IF-104);
 *  - groups.remove     → void         (студентам группы groupId=null, 404);
 *  - groups.getStudents → PagedResult<StudentDto> (pageSize 10, fullName↑, login↑).
 *
 * Обработчики — чистые синхронные функции над MockDb (ADR-003): чтение —
 * через read(), мутации — только через транзакционный mutate (отказ внутри
 * отбрасывает черновик целиком — 409/404 не оставляют следов в mock.db.v1).
 *
 * Роль: все методы teacher-only. Сессия — ключ mock.session.userId, доступ
 * только через его владельца — домен Auth (SessionStore, C-101, ADR-105):
 * отсутствие ключа или пользователя с таким id → 401 «Не авторизован»
 * (IF-101), роль отлична от teacher → 403 «Доступ запрещён» (IF-104).
 *
 * Подключение агрегатором мок-слоя: registerGroupsHandlers(client).
 */
import { GroupDto, PagedResult, StudentDto, User } from '../../shared/models';
import { ERROR_TEXTS } from '../../shared/validation/error-texts';
import { GROUPS_PAGE_SIZE } from '../../core/services/groups.service';
import { SessionStore } from '../auth/session-store';
import { MockApiClient, MockHandler } from '../mock-api-client';
import { MockDb } from '../mock-db';
import { MockValidationError } from '../mock-error';
import { newRuntimeId } from '../ids';

/** Параметры groups.create: название группы (нормализуется тримом в моке). */
export interface GroupNameParams {
  name: string;
}

/** Параметры groups.rename: идентификатор группы и новое название. */
export interface GroupRenameParams extends GroupNameParams {
  id: string;
}

/** Параметры groups.remove: идентификатор группы. */
export interface GroupIdParams {
  id: string;
}

/** Параметры groups.getStudents: идентификатор группы и номер страницы (1-based). */
export interface GroupStudentsParams {
  id: string;
  page: number;
}

/** Максимальная длина названия группы после трима (FR-023: 1–100). */
const GROUP_NAME_MAX_LENGTH = 100;

/** Тексты отказов — дословно контракты IF-104 и IF-101. */
const MESSAGE_INVALID_DATA = 'Данные заполнены неверно';
const MESSAGE_DUPLICATE_NAME = 'Группа с таким названием уже существует';
const MESSAGE_GROUP_NOT_FOUND = 'Группа не найдена';
const MESSAGE_FORBIDDEN = 'Доступ запрещён';
const MESSAGE_UNAUTHORIZED = 'Не авторизован';

/** Доступ к ключу сессии — только через владельца ключа (домен Auth, C-101). */
const sessionStore = new SessionStore();

/** Сравнение строк без учёта регистра (сортировки name↑/fullName↑/login↑). */
function compareCi(a: string, b: string): number {
  return a.localeCompare(b, 'ru', { sensitivity: 'base' });
}

/**
 * Проверка сессии и роли (IF-104/IF-101): нет ключа сессии либо пользователь
 * с таким id не найден (битая сессия) — 401 «Не авторизован»; роль не
 * teacher — 403 «Доступ запрещён».
 */
function requireTeacher(db: MockDb): void {
  const userId = sessionStore.getUserId();
  const user =
    userId === null ? undefined : db.read().users.find((candidate) => candidate.id === userId);
  if (user === undefined) {
    throw new MockValidationError(401, MESSAGE_UNAUTHORIZED);
  }
  if (user.role !== 'teacher') {
    throw new MockValidationError(403, MESSAGE_FORBIDDEN);
  }
}

/** Нормализация и валидация названия группы (IF-104: 400 + errors {name}). */
function validateGroupName(rawName: unknown): string {
  const name = typeof rawName === 'string' ? rawName.trim() : '';
  if (name.length < 1 || name.length > GROUP_NAME_MAX_LENGTH) {
    throw new MockValidationError(400, MESSAGE_INVALID_DATA, {
      name: [ERROR_TEXTS['group.name']],
    });
  }
  return name;
}

/** Число студентов группы — вычисляемое поле studentCount. */
function countStudents(users: readonly User[], groupId: string): number {
  return users.filter((user) => user.role === 'student' && user.groupId === groupId).length;
}

/** Транспортная форма группы: доменная Group + вычисленный studentCount. */
function toGroupDto(group: { id: string; name: string }, users: readonly User[]): GroupDto {
  return { id: group.id, name: group.name, studentCount: countStudents(users, group.id) };
}

/** Дубликат названия без учёта регистра; excludeId — сам переименовываемый. */
function findDuplicateName(
  groups: readonly { id: string; name: string }[],
  name: string,
  excludeId?: string,
): { id: string; name: string } | undefined {
  const lowered = name.toLowerCase();
  return groups.find(
    (group) => group.id !== excludeId && group.name.toLowerCase() === lowered,
  );
}

/** groups.getList: все группы, name↑ без учёта регистра, studentCount вычислен. */
const groupsGetList: MockHandler<void, GroupDto[]> = (db) => {
  requireTeacher(db);
  const data = db.read();
  return [...data.groups]
    .sort((a, b) => compareCi(a.name, b.name))
    .map((group) => toGroupDto(group, data.users));
};

/** groups.create: 400 (1–100 после трима) → 409 (дубликат ci) → создание. */
const groupsCreate: MockHandler<GroupNameParams, GroupDto> = (db, params) => {
  requireTeacher(db);
  const name = validateGroupName(params?.name);
  return db.mutate((data) => {
    if (findDuplicateName(data.groups, name) !== undefined) {
      throw new MockValidationError(409, MESSAGE_DUPLICATE_NAME);
    }
    const created = { id: newRuntimeId(), name };
    data.groups.push({ ...created, studentCount: 0 });
    return toGroupDto(created, data.users);
  });
};

/** groups.rename: 400 (1–100 после трима) → 404 → 409 (дубликат ci, кроме самой группы). */
const groupsRename: MockHandler<GroupRenameParams, GroupDto> = (db, params) => {
  requireTeacher(db);
  const id = typeof params?.id === 'string' ? params.id : '';
  const name = validateGroupName(params?.name);
  return db.mutate((data) => {
    const group = data.groups.find((candidate) => candidate.id === id);
    if (group === undefined) {
      throw new MockValidationError(404, MESSAGE_GROUP_NOT_FOUND);
    }
    if (findDuplicateName(data.groups, name, id) !== undefined) {
      throw new MockValidationError(409, MESSAGE_DUPLICATE_NAME);
    }
    group.name = name;
    return toGroupDto(group, data.users);
  });
};

/**
 * groups.remove: 404 → удаление группы; студенты группы (role=student с
 * этим groupId) сохраняются с groupId=null — §4.5 «не удаляются, теряют
 * доступ к сдаче» и попадают в фильтр «без группы» раздела «Доступ».
 */
const groupsRemove: MockHandler<GroupIdParams, void> = (db, params) => {
  requireTeacher(db);
  const id = typeof params?.id === 'string' ? params.id : '';
  db.mutate((data) => {
    const index = data.groups.findIndex((group) => group.id === id);
    if (index === -1) {
      throw new MockValidationError(404, MESSAGE_GROUP_NOT_FOUND);
    }
    for (const user of data.users) {
      if (user.groupId === id) {
        user.groupId = null;
      }
    }
    data.groups.splice(index, 1);
    return undefined;
  });
};

/** Страница 1-based; отсутствующее/нечисловое/дробное значение → 1. */
function normalizePage(value: unknown): number {
  if (typeof value === 'number' && Number.isFinite(value)) {
    return Math.max(1, Math.floor(value));
  }
  return 1;
}

/**
 * groups.getStudents: 404 → студенты группы (fullName↑, затем login↑, оба
 * без учёта регистра) на странице page размером GROUPS_PAGE_SIZE; вне
 * диапазона страниц — пустой items при корректном total. groupId и groupName
 * в составе группы константны: groupId = id группы (из user.groupId, аменда 6),
 * groupName — имя группы (IF-104).
 */
const groupsGetStudents: MockHandler<GroupStudentsParams, PagedResult<StudentDto>> = (
  db,
  params,
) => {
  requireTeacher(db);
  const data = db.read();
  const id = typeof params?.id === 'string' ? params.id : '';
  const group = data.groups.find((candidate) => candidate.id === id);
  if (group === undefined) {
    throw new MockValidationError(404, MESSAGE_GROUP_NOT_FOUND);
  }
  const students = data.users
    .filter((user) => user.role === 'student' && user.groupId === group.id)
    .sort(
      (a, b) => compareCi(a.fullName, b.fullName) || compareCi(a.login, b.login),
    );
  const page = normalizePage(params?.page);
  const start = (page - 1) * GROUPS_PAGE_SIZE;
  const items: StudentDto[] = students
    .slice(start, start + GROUPS_PAGE_SIZE)
    .map((user) => ({
      id: user.id,
      fullName: user.fullName,
      login: user.login,
      email: user.email,
      // Аменда 6: groupId из user.groupId — в составе группы это id группы.
      groupId: user.groupId,
      groupName: group.name,
    }));
  return { items, total: students.length, page, pageSize: GROUPS_PAGE_SIZE };
};

/** Подключает обработчики домена Groups к реестру мок-клиента (ADR-003). */
export function registerGroupsHandlers(client: MockApiClient): void {
  client.register('groups.getList', groupsGetList);
  client.register('groups.create', groupsCreate);
  client.register('groups.rename', groupsRename);
  client.register('groups.remove', groupsRemove);
  client.register('groups.getStudents', groupsGetStudents);
}
