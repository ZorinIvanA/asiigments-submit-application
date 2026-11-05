/**
 * Программируемый HTTP-бэкенд зоны grid-groups-access (FR-026, переработка
 * батча 3 с мок-слоя на HttpTestingController): двойник REST-доменов
 * students/groups/submissions над детерминированным серверным состоянием —
 * сид «1 преподаватель + 32 студента + 3 группы + 23 работы + 4 сдачи»
 * (аналог прежнего seedFixtures). Заменяет прежний мок-слой (IF-104/105/106
 * поверх localStorage), удалённый при миграции на реальные HTTP-запросы
 * (FR-026) — зона полностью самодостаточна.
 *
 * Серверное поведение воспроизводит контракты REST:
 *  - GET /auth/me — MeDto сессионного пользователя либо 401 «Не авторизован»;
 *    POST /auth/logout — 204 (сессия сброшена); POST /auth/login — 200 MeDto
 *    демо-учётки; POST /auth/refresh — всегда 401 (cookie-семантика зоны не
 *    программируется — «сессия уже истекла»);
 *  - GET /groups — name↑, studentCount вычисляется; POST/PUT/DELETE —
 *    201/200/204, 400 «Данные заполнены неверно» (errors.name), 409
 *    «Группа с таким названием уже существует» (ci), 404 «Группа не найдена»;
 *    DELETE рассыпает членов группы (groupId=null, §4.5);
 *  - GET /groups/{id}/students — страница состава (10, fullName↑ login↑);
 *  - GET /students — поиск/фильтр/сортировка/нарезка (10; ci-токены поиска
 *    применяются к ОДНОМУ полю ФИО/логин/email — правило IF-105);
 *  - PUT /students/{id}/group — 204; 404 «Студент не найден»/«Группа не найдена»;
 *  - GET /semesters, GET /labs, GET /labs/{id} — для страниц-соседей
 *    (ведомость, works) на общем дереве маршрутов;
 *  - GET /submissions — грид группы×семестра (5 студентов/страницу, total —
 *    вся группа, page — нормализована); PUT /submissions — upsert с
 *    updatedBy/updatedAt (404 «Студент не найден»/«Лабораторная не найдена»);
 *  - GET /me/submissions — сдачи сессионного студента (не student → 403);
 *  - GET /me/profile — ProfileDto сессионного пользователя.
 *
 * Роли: домены students/groups/submissions — только teacher (403 «Доступ
 * запрещён»); /me/submissions — только student. «Другой сеанс» — прямые
 * мутации состояния: mutate(fn) и вспомогательные removeGroupDirect/
 * removeLabDirect (каскад сдач — §4.3); последующие операции UI честно
 * получают 404. Запросы вне перечисленных маршрутов — ошибка сценария
 * (дыра в программировании бэкенда).
 */
import { HttpRequest } from '@angular/common/http';

import {
  GroupDto,
  LabDto,
  LABS_PAGE_SIZE,
  LabsSortDir,
  LabsSortField,
  MeDto,
  PagedResult,
  ProfileDto,
  STUDENTS_PAGE_SIZE,
  StudentDto,
  Submission,
  UserRole,
} from '../../../shared/models';

/** Тексты отказов — дословно контракты IF-004/104/105/106. */
const TEXT_UNAUTHORIZED = 'Не авторизован';
const TEXT_FORBIDDEN = 'Доступ запрещён';
const TEXT_NOT_FOUND_GROUP = 'Группа не найдена';
const TEXT_NOT_FOUND_STUDENT = 'Студент не найден';
const TEXT_NOT_FOUND_LAB = 'Лабораторная не найдена';
const TEXT_DUPLICATE_GROUP = 'Группа с таким названием уже существует';
const TEXT_WRONG_CREDENTIALS = 'Неверный логин или пароль';

/** Тексты статусов для flush (диагностика в отчётах HttpTestingController). */
const STATUS_TEXT: Record<number, string> = {
  200: 'OK',
  201: 'Created',
  204: 'No Content',
  400: 'Bad Request',
  401: 'Unauthorized',
  403: 'Forbidden',
  404: 'Not Found',
  409: 'Conflict',
};

/** Учётная запись состояния бэкенда (внутреннее представление User). */
export interface BackendUser {
  id: string;
  login: string;
  email: string;
  fullName: string;
  role: UserRole;
  groupId: string | null;
  password: string;
}

/** Группа состояния (studentCount вычисляется на чтении). */
export interface BackendGroup {
  id: string;
  name: string;
}

/** Работа состояния. */
export interface BackendLab {
  id: string;
  semester: number;
  number: number;
  content: string;
  assignmentUrl: string | null;
  defenseRequired: boolean;
}

/** Сдача состояния (updatedBy/updatedAt — контракт PUT /submissions). */
export interface BackendSubmission {
  id: string;
  studentId: string;
  labId: string;
  submitDate: string | null;
  defenseDate: string | null;
  updatedAt: string;
  updatedBy: string | null;
}

/** Живое состояние бэкенда — чтение сценариями и мутации «другого сеанса». */
export interface GridBackendState {
  users: BackendUser[];
  groups: BackendGroup[];
  labs: BackendLab[];
  submissions: BackendSubmission[];
}

/** Исходный сид (id — детерминированные uuid; ссылки в submissions по логину). */
export interface GridBackendSeed {
  users: Array<Omit<BackendUser, 'id'> & { id?: string }>;
  groups: Array<{ id?: string; name: string }>;
  labs: Array<{
    id?: string;
    semester: number;
    number: number;
    content?: string;
    assignmentUrl?: string | null;
    defenseRequired?: boolean;
  }>;
  submissions: Array<{
    studentLogin: string;
    semester: number;
    labNumber: number;
    submitDate: string | null;
    defenseDate: string | null;
  }>;
}

/** Ответ бэкенда для flush (тело — JSON-объект/массив либо null у 204). */
export interface BackendResponse {
  readonly status: number;
  readonly statusText: string;
  readonly body: object | null;
}

/** Ответ «успех» для flush. */
function ok(status: number, body: object | null): BackendResponse {
  return { status, statusText: STATUS_TEXT[status] ?? '', body };
}

/** Ответ отказа с телом {message} либо {message, errors}. */
function fail(
  status: number,
  message: string,
  errors?: Record<string, string[]>,
): BackendResponse {
  return {
    status,
    statusText: STATUS_TEXT[status] ?? '',
    body: errors === undefined ? { message } : { message, errors },
  };
}

/** Ранжирующее сравнение по русскому порядку (SortComparer ru-RU, ADR-013). */
function ruCompare(a: string, b: string): number {
  return a.localeCompare(b, 'ru');
}

/** Сид по умолчанию (аналог прежнего seedFixtures, SCR-009/010/013): */
function integrationSeed(): GridBackendSeed {
  const users: GridBackendSeed['users'] = [
    {
      login: 'teacher',
      email: 'teacher@example.com',
      fullName: 'Сидоров Семён Семёнович',
      role: 'teacher',
      groupId: null,
      password: 'teacher123!',
    },
  ];
  for (let nn = 1; nn <= 32; nn++) {
    const suffix = String(nn).padStart(2, '0');
    users.push({
      login: `student${suffix}`,
      email: `student${suffix}@example.com`,
      fullName: `Иванов Иван Иванович ${suffix}`,
      role: 'student',
      groupId: nn <= 25 ? 'ГРУППА:ИК-221' : nn <= 30 ? 'ГРУППА:ИК-222' : null,
      password: 'student123!',
    });
  }
  const labs: GridBackendSeed['labs'] = [];
  for (const [semester, lastNumber] of [
    [1, 20],
    [2, 3],
  ] as const) {
    for (let number = 1; number <= lastNumber; number++) {
      labs.push({
        semester,
        number,
        content: `Содержание лабораторной работы №${number}`,
        defenseRequired: number % 2 === 0,
      });
    }
  }
  return {
    users,
    groups: [{ name: 'ИК-221' }, { name: 'ИК-222' }, { name: 'ИК-223' }],
    labs,
    submissions: [
      { studentLogin: 'student01', semester: 1, labNumber: 1, submitDate: '2026-09-01', defenseDate: '2026-09-11' },
      { studentLogin: 'student01', semester: 1, labNumber: 2, submitDate: '2026-09-02', defenseDate: '2026-09-12' },
      { studentLogin: 'student01', semester: 1, labNumber: 3, submitDate: '2026-09-03', defenseDate: null },
      { studentLogin: 'student02', semester: 1, labNumber: 1, submitDate: '2026-09-01', defenseDate: null },
    ],
  };
}

/**
 * Состояние и поведение REST-бэкенда доменов зоны. Экземпляр создаётся на
 * каждый сценарий (GridAccessEnv.setup) — состояние не протекает между
 * спеками. Ссылки членов групп в сидах допускают маркер 'ГРУППА:<имя>' —
 * раскрывается в uuid по порядку объявления групп.
 */
export class GridBackendStub {
  private idCounter = 0;

  /** Виртуальные часы updated_at: каждая запись продвигает метку. */
  private clock = 0;

  private readonly state: GridBackendState;

  /** Логин сессионного пользователя (null — гость: /auth/me → 401). */
  sessionLogin: string | null = null;

  /** Программируемый отказ следующего PUT /students/{id}/group. */
  private nextSetGroupFailure: { status: number; message: string } | null = null;

  constructor(seed: GridBackendSeed = integrationSeed()) {
    const groups: BackendGroup[] = seed.groups.map((group) => ({
      id: group.id ?? this.nextId(),
      name: group.name,
    }));
    const users: BackendUser[] = seed.users.map((user) => {
      const rawGroupId = user.groupId;
      const groupId =
        rawGroupId !== null && rawGroupId.startsWith('ГРУППА:')
          ? (groups.find((group) => group.name === rawGroupId.slice('ГРУППА:'.length))?.id ?? null)
          : rawGroupId;
      return {
        id: user.id ?? this.nextId(),
        login: user.login,
        email: user.email,
        fullName: user.fullName,
        role: user.role,
        groupId,
        password: user.password,
      };
    });
    const labs: BackendLab[] = seed.labs.map((lab) => ({
      id: lab.id ?? this.nextId(),
      semester: lab.semester,
      number: lab.number,
      content: lab.content ?? `Содержание лабораторной работы №${lab.number}`,
      assignmentUrl: lab.assignmentUrl ?? null,
      defenseRequired: lab.defenseRequired === true,
    }));
    const labIdByPair = new Map(labs.map((lab) => [`${lab.semester}:${lab.number}`, lab.id]));
    const submissions: BackendSubmission[] = seed.submissions.map((rule) => {
      const student = users.find((candidate) => candidate.login === rule.studentLogin);
      const labId = labIdByPair.get(`${rule.semester}:${rule.labNumber}`);
      if (student === undefined || labId === undefined) {
        throw new Error('grid-backend-stub: правило сид-сдачи невыполнимо — сид неполон');
      }
      return {
        id: this.nextId(),
        studentId: student.id,
        labId,
        submitDate: rule.submitDate,
        defenseDate: rule.defenseDate,
        updatedAt: '2026-09-01T09:00:00.000Z',
        updatedBy: null,
      };
    });
    this.state = { users, groups, labs, submissions };
  }

  /** Детерминированный uuid состояния (формат IF-001). */
  private nextId(): string {
    this.idCounter += 1;
    return `c0210000-0000-4000-8000-${String(this.idCounter).padStart(12, '0')}`;
  }

  /** Следующая метка updated_at (монотонная, строка ISO). */
  private nextUpdatedAt(): string {
    this.clock += 1;
    return `2026-09-01T10:00:00.${String(this.clock).padStart(3, '0')}Z`;
  }

  // ---------- состояние: чтение и мутации «другого сеанса» ----------

  /**
   * Снимок состояния для чтения сценариями (глубокая копия — прежние снимки
   * не меняются задним числом при последующих мутациях; запись — только
   * через mutate и UI-операции).
   */
  read(): GridBackendState {
    return {
      users: this.state.users.map((user) => ({ ...user })),
      groups: this.state.groups.map((group) => ({ ...group })),
      labs: this.state.labs.map((lab) => ({ ...lab })),
      submissions: this.state.submissions.map((submission) => ({ ...submission })),
    };
  }

  /** Прямая мутация состояния «из другой вкладки» (мимо UI). */
  mutate(mutation: (state: GridBackendState) => void): void {
    mutation(this.state);
  }

  /** uuid группы по названию сида; отсутствие — ошибка сценария. */
  groupIdByName(name: string): string {
    const group = this.state.groups.find((candidate) => candidate.name === name);
    if (group === undefined) {
      throw new Error(`grid-backend-stub: в сиде нет группы ${name}`);
    }
    return group.id;
  }

  /** uuid пользователя по логину сида; отсутствие — ошибка сценария. */
  userIdByLogin(login: string): string {
    const user = this.state.users.find((candidate) => candidate.login === login);
    if (user === undefined) {
      throw new Error(`grid-backend-stub: в сиде нет пользователя ${login}`);
    }
    return user.id;
  }

  /** uuid работы по паре (семестр, номер); отсутствие — ошибка сценария. */
  labId(semester: number, number: number): string {
    const lab = this.state.labs.find(
      (candidate) => candidate.semester === semester && candidate.number === number,
    );
    if (lab === undefined) {
      throw new Error(`grid-backend-stub: в сиде нет работы ${semester}:${number}`);
    }
    return lab.id;
  }

  /**
   * Удаление группы «из другой вкладки» — семантика DELETE /groups/{id}:
   * члены группы сохраняются с groupId=null.
   */
  removeGroupDirect(id: string): void {
    this.mutate((state) => {
      state.groups = state.groups.filter((group) => group.id !== id);
      for (const user of state.users) {
        if (user.groupId === id) {
          user.groupId = null;
        }
      }
    });
  }

  /** Удаление работы «из другой вкладки» — каскад сдач (§4.3). */
  removeLabDirect(id: string): void {
    this.mutate((state) => {
      state.labs = state.labs.filter((lab) => lab.id !== id);
      state.submissions = state.submissions.filter((submission) => submission.labId !== id);
    });
  }

  /**
   * Точечная поломка следующего PUT /students/{id}/group (например 404
   * «Группа не найдена» по устаревшему id — TS-359): одноразовый отказ.
   */
  failNextSetGroup(failure: { status: number; message: string }): void {
    this.nextSetGroupFailure = failure;
  }

  // ---------- маршрутизация запросов ----------

  /**
   * Обработка запроса по состоянию. Вызывается харнесом для каждого
   * перехваченного HttpTestingController-запроса; неизвестный маршрут —
   * ошибка сценария (дыра в программировании бэкенда).
   */
  handle(req: HttpRequest<unknown>, apiBase: string): BackendResponse {
    if (!req.url.startsWith(`${apiBase}/`)) {
      throw new Error(`grid-backend-stub: запрос вне API ${req.method} ${req.url}`);
    }
    const pathWithQuery = req.urlWithParams.slice(apiBase.length);
    const queryIndex = pathWithQuery.indexOf('?');
    const path = queryIndex === -1 ? pathWithQuery : pathWithQuery.slice(0, queryIndex);
    const query = new URLSearchParams(queryIndex === -1 ? '' : pathWithQuery.slice(queryIndex + 1));

    if (req.method === 'GET' && path === '/auth/me') {
      return this.me();
    }
    if (req.method === 'POST' && path === '/auth/login') {
      return this.login(req.body as { login: string; password: string });
    }
    if (req.method === 'POST' && path === '/auth/logout') {
      this.sessionLogin = null;
      return ok(204, null);
    }
    if (req.method === 'POST' && path === '/auth/refresh') {
      return fail(401, TEXT_UNAUTHORIZED);
    }

    const session = this.sessionUser();
    if (req.method === 'GET' && path === '/me/profile') {
      return session === null
        ? fail(401, TEXT_UNAUTHORIZED)
        : ok(200, this.profileOf(session));
    }
    if (req.method === 'GET' && path === '/me/submissions') {
      return this.mySubmissions(session, Number(query.get('semester')));
    }

    if (req.method === 'GET' && path === '/groups') {
      return this.teacherGate(session) ?? ok(200, this.groupDtos());
    }
    if (req.method === 'POST' && path === '/groups') {
      return this.teacherGate(session) ?? this.createGroup(req.body as { name: string });
    }
    const groupMatch = /^\/groups\/([^/]+)$/.exec(path);
    if (groupMatch !== null) {
      const id = decodeURIComponent(groupMatch[1]!);
      if (req.method === 'PUT') {
        return this.teacherGate(session) ?? this.renameGroup(id, req.body as { name: string });
      }
      if (req.method === 'DELETE') {
        return this.teacherGate(session) ?? this.deleteGroup(id);
      }
    }
    const membersMatch = /^\/groups\/([^/]+)\/students$/.exec(path);
    if (membersMatch !== null && req.method === 'GET') {
      return (
        this.teacherGate(session) ??
        this.groupStudents(decodeURIComponent(membersMatch[1]!), Number(query.get('page')))
      );
    }

    if (req.method === 'GET' && path === '/students') {
      return (
        this.teacherGate(session) ??
        this.studentsList(
          query.get('search'),
          query.get('groupId'),
          Number(query.get('page')),
        )
      );
    }
    const groupOfStudent = /^\/students\/([^/]+)\/group$/.exec(path);
    if (groupOfStudent !== null && req.method === 'PUT') {
      return (
        this.teacherGate(session) ??
        this.setGroup(decodeURIComponent(groupOfStudent[1]!), req.body as { groupId: string | null })
      );
    }

    if (req.method === 'GET' && path === '/semesters') {
      return ok(200, [...new Set(this.state.labs.map((lab) => lab.semester))].sort((a, b) => a - b));
    }
    if (req.method === 'GET' && path === '/labs') {
      return this.teacherGate(session) ?? this.labsList(query);
    }
    const labMatch = /^\/labs\/([^/]+)$/.exec(path);
    if (labMatch !== null && req.method === 'GET') {
      const lab = this.state.labs.find((candidate) => candidate.id === decodeURIComponent(labMatch[1]!));
      return lab === undefined ? fail(404, TEXT_NOT_FOUND_LAB) : ok(200, { ...lab });
    }

    if (req.method === 'GET' && path === '/submissions') {
      return (
        this.teacherGate(session) ??
        this.grid(
          query.get('groupId') ?? '',
          Number(query.get('semester')),
          Number(query.get('page')),
        )
      );
    }
    if (req.method === 'PUT' && path === '/submissions') {
      return (
        this.teacherGate(session) ??
        this.upsertSubmission(req.body as {
          studentId: string;
          labId: string;
          submitDate: string | null;
          defenseDate: string | null;
        })
      );
    }

    throw new Error(`grid-backend-stub: нет обработчика ${req.method} ${req.url}`);
  }

  // ---------- auth ----------

  private sessionUser(): BackendUser | null {
    if (this.sessionLogin === null) {
      return null;
    }
    return this.state.users.find((user) => user.login === this.sessionLogin) ?? null;
  }

  private meDtoOf(user: BackendUser): MeDto {
    return {
      login: user.login,
      fullName: user.fullName,
      role: user.role,
      groupName: user.groupId === null ? null : this.groupNameOf(user.groupId),
    };
  }

  private profileOf(user: BackendUser): ProfileDto {
    return {
      login: user.login,
      email: user.email,
      fullName: user.fullName,
      role: user.role,
      groupName: user.groupId === null ? null : this.groupNameOf(user.groupId),
    };
  }

  private groupNameOf(groupId: string): string | null {
    return this.state.groups.find((group) => group.id === groupId)?.name ?? null;
  }

  private me(): BackendResponse {
    const session = this.sessionUser();
    return session === null ? fail(401, TEXT_UNAUTHORIZED) : ok(200, this.meDtoOf(session));
  }

  private login(body: { login: string; password: string }): BackendResponse {
    const user = this.state.users.find(
      (candidate) => candidate.login === body?.login && candidate.password === body?.password,
    );
    if (user === undefined) {
      return fail(401, TEXT_WRONG_CREDENTIALS);
    }
    this.sessionLogin = user.login;
    return ok(200, this.meDtoOf(user));
  }

  /** Шлюз преподавателя: 403 для гостя и студента, null — пропуск. */
  private teacherGate(session: BackendUser | null): BackendResponse | null {
    return session !== null && session.role === 'teacher' ? null : fail(403, TEXT_FORBIDDEN);
  }

  // ---------- groups ----------

  private studentCountOf(groupId: string): number {
    return this.state.users.filter(
      (user) => user.role === 'student' && user.groupId === groupId,
    ).length;
  }

  private groupDtos(): GroupDto[] {
    return [...this.state.groups]
      .sort((a, b) => ruCompare(a.name, b.name))
      .map((group) => ({
        id: group.id,
        name: group.name,
        studentCount: this.studentCountOf(group.id),
      }));
  }

  private createGroup(body: { name: string }): BackendResponse {
    const name = typeof body?.name === 'string' ? body.name : '';
    if (name.trim() === '' || name.trim().length > 100) {
      return fail(400, 'Данные заполнены неверно', {
        name: ['Название группы — от 1 до 100 символов'],
      });
    }
    const duplicate = this.state.groups.some(
      (group) => group.name.trim().toLowerCase() === name.trim().toLowerCase(),
    );
    if (duplicate) {
      return fail(409, TEXT_DUPLICATE_GROUP);
    }
    const group: BackendGroup = { id: this.nextId(), name: name.trim() };
    this.state.groups.push(group);
    return ok(201, { id: group.id, name: group.name, studentCount: 0 });
  }

  private renameGroup(id: string, body: { name: string }): BackendResponse {
    const group = this.state.groups.find((candidate) => candidate.id === id);
    if (group === undefined) {
      return fail(404, TEXT_NOT_FOUND_GROUP);
    }
    const name = typeof body?.name === 'string' ? body.name : '';
    if (name.trim() === '' || name.trim().length > 100) {
      return fail(400, 'Данные заполнены неверно', {
        name: ['Название группы — от 1 до 100 символов'],
      });
    }
    const duplicate = this.state.groups.some(
      (candidate) =>
        candidate.id !== id &&
        candidate.name.trim().toLowerCase() === name.trim().toLowerCase(),
    );
    if (duplicate) {
      return fail(409, TEXT_DUPLICATE_GROUP);
    }
    group.name = name.trim();
    return ok(200, { id: group.id, name: group.name, studentCount: this.studentCountOf(id) });
  }

  private deleteGroup(id: string): BackendResponse {
    const index = this.state.groups.findIndex((candidate) => candidate.id === id);
    if (index === -1) {
      return fail(404, TEXT_NOT_FOUND_GROUP);
    }
    this.state.groups.splice(index, 1);
    for (const user of this.state.users) {
      if (user.groupId === id) {
        user.groupId = null;
      }
    }
    return ok(204, null);
  }

  private studentDtoOf(user: BackendUser, forcedGroupId?: string): StudentDto {
    const groupId = forcedGroupId ?? user.groupId;
    return {
      id: user.id,
      fullName: user.fullName,
      login: user.login,
      email: user.email,
      groupId,
      groupName: groupId === null ? null : this.groupNameOf(groupId),
    };
  }

  private groupStudents(id: string, rawPage: number): BackendResponse {
    const group = this.state.groups.find((candidate) => candidate.id === id);
    if (group === undefined) {
      return fail(404, TEXT_NOT_FOUND_GROUP);
    }
    const page = normalizePage(rawPage);
    const members = this.state.users
      .filter((user) => user.role === 'student' && user.groupId === id)
      .sort((a, b) => ruCompare(a.fullName, b.fullName) || ruCompare(a.login, b.login));
    const start = (page - 1) * STUDENTS_PAGE_SIZE;
    const items = members
      .slice(start, start + STUDENTS_PAGE_SIZE)
      .map((user) => this.studentDtoOf(user, id));
    const result: PagedResult<StudentDto> = {
      items,
      total: members.length,
      page,
      pageSize: STUDENTS_PAGE_SIZE,
    };
    return ok(200, result);
  }

  // ---------- students ----------

  private studentsList(
    search: string | null,
    groupFilter: string | null,
    rawPage: number,
  ): BackendResponse {
    const page = normalizePage(rawPage);
    const tokens =
      search === null
        ? []
        : search
            .split(/\s+/)
            .map((token) => token.toLowerCase())
            .filter((token) => token !== '');
    const matched = this.state.users.filter((user) => {
      if (user.role !== 'student') {
        return false;
      }
      if (groupFilter === 'none' && user.groupId !== null) {
        return false;
      }
      if (groupFilter !== null && groupFilter !== 'none' && user.groupId !== groupFilter) {
        return false;
      }
      if (tokens.length === 0) {
        return true;
      }
      // Многословный поиск (уточнение IF-105): все токены — подстроки (ci)
      // ОДНОГО и того же поля ФИО/логин/email.
      const fields = [user.fullName, user.login, user.email];
      return fields.some((field) =>
        tokens.every((token) => field.toLowerCase().includes(token)),
      );
    });
    const ordered = matched.sort(
      (a, b) => ruCompare(a.fullName, b.fullName) || ruCompare(a.login, b.login),
    );
    const start = (page - 1) * STUDENTS_PAGE_SIZE;
    const items = ordered
      .slice(start, start + STUDENTS_PAGE_SIZE)
      .map((user) => this.studentDtoOf(user));
    const result: PagedResult<StudentDto> = {
      items,
      total: ordered.length,
      page,
      pageSize: STUDENTS_PAGE_SIZE,
    };
    return ok(200, result);
  }

  private setGroup(studentId: string, body: { groupId: string | null }): BackendResponse {
    if (this.nextSetGroupFailure !== null) {
      const failure = this.nextSetGroupFailure;
      this.nextSetGroupFailure = null;
      return fail(failure.status, failure.message);
    }
    const student = this.state.users.find(
      (candidate) => candidate.id === studentId && candidate.role === 'student',
    );
    if (student === undefined) {
      return fail(404, TEXT_NOT_FOUND_STUDENT);
    }
    const groupId = body?.groupId ?? null;
    if (
      groupId !== null &&
      !this.state.groups.some((group) => group.id === groupId)
    ) {
      return fail(404, TEXT_NOT_FOUND_GROUP);
    }
    student.groupId = groupId;
    return ok(204, null);
  }

  // ---------- labs (для страниц-соседей на общем дереве маршрутов) ----------

  private labsList(query: URLSearchParams): BackendResponse {
    const semesterRaw = query.get('semester');
    const semester = semesterRaw === null ? null : Number(semesterRaw);
    const page = normalizePage(Number(query.get('page')));
    const sortField: LabsSortField = query.get('sortField') === 'number' ? 'number' : 'semester';
    const sortDir: LabsSortDir = query.get('sortDir') === 'desc' ? 'desc' : 'asc';
    const direction = sortDir === 'desc' ? -1 : 1;
    const compare = (a: BackendLab, b: BackendLab): number => {
      const primary =
        (sortField === 'semester' ? a.semester - b.semester : a.number - b.number) * direction;
      return primary !== 0
        ? primary
        : sortField === 'number'
          ? a.semester - b.semester
          : a.number - b.number;
    };
    const filtered = this.state.labs
      .filter((lab) => semester === null || lab.semester === semester)
      .sort(compare);
    const start = (page - 1) * LABS_PAGE_SIZE;
    const items: LabDto[] = filtered.slice(start, start + LABS_PAGE_SIZE).map((lab) => ({ ...lab }));
    const result: PagedResult<LabDto> = { items, total: filtered.length, page, pageSize: LABS_PAGE_SIZE };
    return ok(200, result);
  }

  // ---------- submissions ----------

  private grid(groupId: string, semester: number, rawPage: number): BackendResponse {
    const group = this.state.groups.find((candidate) => candidate.id === groupId);
    if (group === undefined) {
      return fail(404, TEXT_NOT_FOUND_GROUP);
    }
    const page = normalizePage(rawPage);
    const members = this.state.users
      .filter((user) => user.role === 'student' && user.groupId === groupId)
      .sort((a, b) => ruCompare(a.fullName, b.fullName) || ruCompare(a.login, b.login));
    const labs = this.state.labs
      .filter((lab) => lab.semester === semester)
      .sort((a, b) => a.number - b.number);
    const start = (page - 1) * 5;
    const pageStudents = members.slice(start, start + 5);
    const pageStudentIds = new Set(pageStudents.map((student) => student.id));
    const labIds = new Set(labs.map((lab) => lab.id));
    const submissions = this.state.submissions
      .filter(
        (submission) =>
          pageStudentIds.has(submission.studentId) && labIds.has(submission.labId),
      )
      .map((submission) => ({
        studentId: submission.studentId,
        labId: submission.labId,
        submitDate: submission.submitDate,
        defenseDate: submission.defenseDate,
      }));
    return ok(200, {
      students: pageStudents.map((student) => ({ id: student.id, fullName: student.fullName })),
      labs: labs.map((lab) => ({ id: lab.id, number: lab.number, defenseRequired: lab.defenseRequired })),
      submissions,
      total: members.length,
      page,
    });
  }

  private upsertSubmission(body: {
    studentId: string;
    labId: string;
    submitDate: string | null;
    defenseDate: string | null;
  }): BackendResponse {
    const student = this.state.users.find((candidate) => candidate.id === body?.studentId);
    if (student === undefined) {
      return fail(404, TEXT_NOT_FOUND_STUDENT);
    }
    const lab = this.state.labs.find((candidate) => candidate.id === body?.labId);
    if (lab === undefined) {
      return fail(404, TEXT_NOT_FOUND_LAB);
    }
    const teacher = this.sessionUser();
    const existing = this.state.submissions.find(
      (candidate) => candidate.studentId === body.studentId && candidate.labId === body.labId,
    );
    if (existing === undefined) {
      const created: BackendSubmission = {
        id: this.nextId(),
        studentId: body.studentId,
        labId: body.labId,
        submitDate: body.submitDate,
        defenseDate: body.defenseDate,
        updatedAt: this.nextUpdatedAt(),
        updatedBy: teacher === null ? null : teacher.id,
      };
      this.state.submissions.push(created);
      return ok(200, { ...created });
    }
    existing.submitDate = body.submitDate;
    existing.defenseDate = body.defenseDate;
    existing.updatedAt = this.nextUpdatedAt();
    existing.updatedBy = teacher === null ? null : teacher.id;
    const saved: Submission = { ...existing };
    return ok(200, saved);
  }

  private mySubmissions(session: BackendUser | null, semester: number): BackendResponse {
    if (session === null || session.role !== 'student') {
      return fail(403, TEXT_FORBIDDEN);
    }
    if (session.groupId === null) {
      return ok(200, { hasGroup: false, labs: [], submissions: [] });
    }
    const labs = this.state.labs
      .filter((lab) => lab.semester === semester)
      .sort((a, b) => a.number - b.number);
    const labIds = new Set(labs.map((lab) => lab.id));
    const submissions = this.state.submissions
      .filter(
        (submission) =>
          submission.studentId === session.id && labIds.has(submission.labId),
      )
      .map((submission) => ({
        labId: submission.labId,
        submitDate: submission.submitDate,
        defenseDate: submission.defenseDate,
      }));
    return ok(200, {
      hasGroup: true,
      labs: labs.map((lab) => ({ id: lab.id, number: lab.number, defenseRequired: lab.defenseRequired })),
      submissions,
    });
  }
}

/** Нормализация номера страницы (ADR-109 аддендум 1: некорректное → 1). */
function normalizePage(rawPage: number): number {
  return Number.isInteger(rawPage) && rawPage >= 1 ? rawPage : 1;
}
