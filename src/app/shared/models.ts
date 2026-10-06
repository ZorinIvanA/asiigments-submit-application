/**
 * Общие модели DTO и константы приложения — единый словарь типов для сервисов
 * core, мок-слоя и страниц (контракт IF-011, компонент C-010 «Общий UI-кит»).
 *
 * Поля интерфейсов воспроизводят доменную модель спеки 3.2 (раздел
 * domain_model) и выходные схемы раздела interfaces дословно. Правила:
 *  - идентификаторы всех сущностей — строки в uuid-формате (IF-001,
 *    MOCK_ID_PATTERN из ADR-013);
 *  - даты сдач (submitDate/defenseDate) — строки 'YYYY-MM-DD' либо null;
 *    конвертация в Date — только внутри контролов p-datepicker, отображение —
 *    только через утилиты shared/dates (формат дд.мм.гггг, без UTC-сдвига,
 *    ADR-007);
 *  - поля, отсутствующие в ответе (например, groupName), помечены «?».
 */

/** Роль учётной записи: студент или преподаватель (доменная модель User). */
export type UserRole = 'student' | 'teacher';

/** Пользователь — элемент users мок-БД (доменная модель User). */
export interface User {
  /** uuid, генерируется моком. */
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
   * Пароль. Хранится в мок-БД открыто — осознанное ограничение демо-режима
   * (out_of_scope, NFR-007); наружу не отдаётся ни одним ответом мока
   * и не логируется.
   */
  password: string;
}

/** Группа — элемент groups мок-БД + вычисляемое число студентов. */
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
 * Лабораторная в ответах мока (LabsService.getList/getById/create/update):
 * состав полей транспортной формы совпадает с доменной Lab — спека 3.2
 * различия не вводит, поэтому наследование фиксирует тождество.
 */
export interface LabDto extends Lab {}

/**
 * Группа в ответе GroupsService.getList (мок GET /api/v1/groups):
 * транспортная форма совпадает с доменной Group.
 */
export interface GroupDto extends Group {}

/**
 * Строка списка студентов раздела «Доступ» (StudentsService.getList,
 * мок GET /api/v1/students) — проекция User без пароля и служебных полей.
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
 * (мок GET /api/v1/submissions).
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
 * Сдачи текущего студента по семестру — ответ SubmissionsService.getMy
 * (мок GET /api/v1/me/submissions).
 */
export interface MySubmissionsDto {
  /** false = студент не включён в группу; при false массивы ниже пустые. */
  hasGroup: boolean;
  /** Работы семестра, упорядочены по number по возрастанию. */
  labs: Array<{ id: string; number: number; defenseRequired: boolean }>;
  /** Сдачи только текущего студента (без studentId — он известен из сессии). */
  submissions: Array<{
    labId: string;
    submitDate: string | null;
    defenseDate: string | null;
  }>;
}

/**
 * Текущий пользователь — ответ auth.register/auth.login/auth.me (контракт
 * IF-101). Тип живёт в общем словаре DTO (ревью CR-001 T-101): нужен и
 * мок-обработчикам (домен Auth), и сервисам core со страницами; мок-слой
 * реэкспортирует его как часть контракта IF-101.
 */
export interface MeDto {
  login: string;
  fullName: string;
  role: UserRole;
  /** Имя группы студента; null = без группы или роль teacher. */
  groupName?: string | null;
}

/**
 * Профиль текущего пользователя — ответ ProfileService.get/update
 * (мок GET/PUT /api/v1/me/profile).
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
 * Ошибка мок-вызова — эмуляция HTTP-ответа с телом ошибки
 * (FR-003: {status, body: {message, errors?}}).
 */
export interface ApiError {
  /** Эмулируемый HTTP-код: 400/401/403/404/409/429. */
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
 * Ключи хранилищ браузера (FR-003, ADR-012): значения зафиксированы спекой,
 * изменение молча отбрасывает сохранённые данные. mock.db.v1 и
 * mock.session.userId — localStorage; recovery.flow.v1 — sessionStorage.
 */
export const STORAGE_KEYS = {
  mockDb: 'mock.db.v1',
  session: 'mock.session.userId',
  recoveryFlow: 'recovery.flow.v1',
} as const;

/** Задержка мок-вызовов в миллисекундах — имитация бэкенда (FR-002/FR-003). */
export const MOCK_DELAY_MS = 500;

/**
 * Верхняя граница номера семестра курса (спека §4.3: выпадающий список
 * 1…Labs__MaxSemester; правило валидации — labSemester в
 * shared/validation/validators: целое 1–10 включительно). Служит для
 * построения перечня семестров в формах и селекторах экранов.
 */
export const MAX_SEMESTER = 10;
