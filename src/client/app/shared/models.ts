/**
 * Общие модели DTO и константы приложения — единый словарь типов для
 * сервисов core и страниц (контракт IF-011, компонент C-010 «Общий UI-кит»).
 *
 * Поля интерфейсов воспроизводят доменную модель спеки 3.2 (раздел
 * domain_model) и выходные схемы раздела interfaces дословно. Правила:
 *  - идентификаторы всех сущностей — строки в uuid-формате (IF-001);
 *  - даты сдач (submitDate/defenseDate) — строки 'YYYY-MM-DD' либо null;
 *    конвертация в Date — только внутри контролов p-datepicker, отображение —
 *    только через утилиты shared/dates (формат дд.мм.гггг, без UTC-сдвига,
 *    ADR-007);
 *  - поля, отсутствующие в ответе (например, groupName), помечены «?».
 */

/** Роль учётной записи: студент или преподаватель (доменная модель User). */
export type UserRole = 'student' | 'teacher';

/** Пользователь — элемент users серверного состояния (доменная модель User). */
export interface User {
  /** uuid. */
  id: string;
  /** 1–100 символов после трима, без пробелов; уникален без учёта регистра. */
  login: string;
  /** Формат логин@домен.зона, 1–254 символа; уникален без учёта регистра. */
  email: string;
  /** ФИО, 1–200 символов после трима. */
  fullName: string;
  role: UserRole;
  /** Группа студента (uuid) либо null = без группы; у преподавателя — null. */
  groupId: string | null;
  /** Вычисляемое имя группы для отображения; null/отсутствует, если группы нет. */
  groupName?: string | null;
  /**
   * Пароль. Служебное поле состояния серверного сида; наружу не отдаётся
   * ни одним ответом API и не логируется.
   */
  password: string;
}

/** Группа — элемент groups серверного состояния + вычисляемое число студентов. */
export interface Group {
  /** uuid. */
  id: string;
  /** 1–100 символов после трима; уникально без учёта регистра. */
  name: string;
  /** Вычисляемое: число студентов группы (>= 0). */
  studentCount: number;
}

/** Лабораторная работа — элемент labs мок-БД (доменная модель Lab). */
export interface Lab {
  /** uuid. */
  id: string;
  /** Номер семестра курса: целое 1..MaxSemester (по умолчанию 10). */
  semester: number;
  /** Номер работы: целое > 0; пара (semester, number) уникальна. */
  number: number;
  /** Содержание работы, 1–500 символов после трима. */
  content: string;
  /** Ссылка на задание (http/https) либо null = ссылки нет. */
  assignmentUrl: string | null;
  /** Признак «Нужна защита». */
  defenseRequired: boolean;
}

/** Сдача — элемент submissions мок-БД (доменная модель Submission). */
export interface Submission {
  /** uuid. */
  id: string;
  /** uuid студента (User с role = student). */
  studentId: string;
  /** uuid работы; при удалении Lab запись удаляется каскадно. */
  labId: string;
  /** Дата сдачи 'YYYY-MM-DD' либо null = не сдано. */
  submitDate: string | null;
  /** Дата защиты 'YYYY-MM-DD' либо null = защита не проставлена. */
  defenseDate: string | null;
  /** Время последнего изменения записи моком (ISO-8601). */
  updatedAt: string;
  /** uuid преподавателя, последним изменившего запись; null = не менялась после сида. */
  updatedBy: string | null;
}

/**
 * Лабораторная в ответах API (LabsService.getList/getById/create/update):
 * состав полей транспортной формы совпадает с доменной Lab — спека 3.2
 * различия не вводит, поэтому наследование фиксирует тождество.
 */
export interface LabDto extends Lab {}

/**
 * Группа в ответе GroupsService.getList (GET списка групп):
 * транспортная форма совпадает с доменной Group.
 */
export interface GroupDto extends Group {}

/**
 * Строка списка студентов раздела «Доступ» (StudentsService.getList,
 * GET списка студентов) — проекция User без пароля и служебных полей.
 */
export interface StudentDto {
  id: string;
  fullName: string;
  login: string;
  email: string;
  /** Идентификатор текущей группы студента (uuid) либо null = без группы (аменда 6). */
  groupId: string | null;
  /** Имя текущей группы студента либо null = без группы. */
  groupName: string | null;
}

/**
 * Ведомость группы по семестру — ответ SubmissionsService.getGrid
 * (GET ведомости группы).
 */
export interface SubmissionsGridDto {
  /** Страница студентов группы (выборка упорядочена по fullName, затем по login). */
  students: Array<{ id: string; fullName: string }>;
  /** Работы выбранного семестра, упорядочены по number по возрастанию. */
  labs: Array<{ id: string; number: number; defenseRequired: boolean }>;
  /** Записи сдач для пар студент-работа текущей страницы. */
  submissions: Array<{
    studentId: string;
    labId: string;
    submitDate: string | null;
    defenseDate: string | null;
  }>;
  /** Общее число студентов группы (для подписи пагинации). */
  total: number;
}

/**
 * Сдачи текущего пользователя по семестру — ответ
 * SubmissionsService.getMy (GET /me/submissions; в анонимной консоли
 * сервер всегда возвращает пустое состояние без группы).
 */
export interface MySubmissionsDto {
  /** false = пользователь не включён в группу; при false массивы ниже пустые. */
  hasGroup: boolean;
  /** Работы семестра, упорядочены по number по возрастанию. */
  labs: Array<{ id: string; number: number; defenseRequired: boolean }>;
  /** Сдачи только текущего пользователя. */
  submissions: Array<{
    labId: string;
    submitDate: string | null;
    defenseDate: string | null;
  }>;
}

/**
 * Профиль текущего пользователя — ответ ProfileService.get/update
 * (GET/PUT /me/profile; в анонимной консоли это всегда сид-преподаватель).
 */
export interface ProfileDto {
  login: string;
  email: string;
  fullName: string;
  role: UserRole;
  /** Имя группы студента; null/отсутствует, если группы нет или роль teacher. */
  groupName?: string | null;
}

/**
 * Ошибка HTTP-вызова — нормализованный ответ с телом ошибки
 * ({status, body: {message, errors?}}).
 */
export interface ApiError {
  /** HTTP-код отказа: 400/403/404/409/500. */
  status: number;
  body: {
    /** Текст ошибки; показывается пользователю дословно. */
    message: string;
    /** Ошибки по полям — только для 400 «Данные заполнены неверно». */
    errors?: Record<string, string[]>;
  };
}

/**
 * Страница списка с ленивой пагинацией (ADR-009: фильтр/сортировка применяет
 * мок ко всей выборке до нарезки страниц; подпись «Показать записи с X по Y
 * из Z» строится из total/page/pageSize — FR-011/FR-012).
 */
export interface PagedResult<T> {
  items: T[];
  /** Размер полной выборки после фильтра/сортировки. */
  total: number;
  page: number;
  pageSize: number;
}

/**
 * Верхняя граница номера семестра курса (спека §4.3: выпадающий список
 * 1…Labs__MaxSemester; правило валидации — labSemester в
 * shared/validation/validators: целое 1–10 включительно). Служит для
 * построения перечня семестров в формах и селекторах экранов.
 */
export const MAX_SEMESTER = 10;

/*
 * Параметры контрактов сервисов core (IF-103/IF-105) — единый словарь DTO:
 * сервисы core и страницы импортируют отсюда.
 */

/** Поле первичной сортировки списка лабораторных (IF-103): номер или семестр. */
export type LabsSortField = 'number' | 'semester';

/** Направление первичной сортировки списка лабораторных (IF-103). */
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

/** Размер страницы списка лабораторных (§4.3, ADR-109) — контракт IF-103. */
export const LABS_PAGE_SIZE = 10;

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

/** Размер страницы students.getList (IF-105, ADR-109) — контракт IF-105. */
export const STUDENTS_PAGE_SIZE = 10;
