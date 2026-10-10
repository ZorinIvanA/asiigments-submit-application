/**
 * Программируемый HTTP-бэкенд зоны works-notifications (FR-026): двойник
 * REST-домена работ над HttpTestingController — детерминированное
 * серверное состояние (23 работы: семестр 1 №1–20, семестр 2 №1–3; сдачи
 * demo-сида) и обработка эндпойнтов labs*, semesters и
 * POST /auth/login по контрактам IF-103/IF-007. Заменяет прежний мок-слой
 * (IF-103 поверх локального хранилища браузера) — изоляция зоны от
 * мок-каталога, FR-026.
 *
 * Серверное поведение воспроизводит контракт REST:
 *  - GET /labs — фильтр semester, двухколоночная сортировка (первичный
 *    ключ sortField/sortDir, вторичный — соседнее поле asc; дефолт
 *    semester↑,number↑), нарезка страниц pageSize=10, подпись страницы
 *    строится компонентом из total/page/pageSize;
 *  - GET /semesters — distinct семестры по возрастанию;
 *  - GET/PUT/DELETE /labs/{id} — 404 «Лабораторная не найдена» чужого id,
 *    409 «Лабораторная с таким номером уже есть в семестре» занятой пары
 *    (своя пара при update не конфликтует), DELETE удаляет записи ведомости
 *    каскадом (§4.3) и отвечает 204;
 *  - POST /labs → 201 созданная запись; POST /auth/login → 200 MeDto
 *    demo-учётки teacher/teacher123! (сессия — память AuthService, FR-092).
 *
 * «Другой сеанс» (удаление записи мимо UI) — прямая мутация состояния
 * removeLabDirect(): серверное состояние общее для всех «вкладок»,
 * последующий DELETE из UI честно отвечает 404. Точечная поломка ответа
 * (серверный 400 формы, TS-221) — failNextCreate().
 *
 * Роли/лимиты/TTL — серверное поведение бэкенд-доменов: здесь не
 * эмулируются (сценарии батча всегда идут сессией teacher), запросы вне
 * перечисленных маршрутов приводят к падению теста с читаемой ошибкой.
 */
import { HttpRequest } from '@angular/common/http';

import {
  Lab,
  LabDto,
  LABS_PAGE_SIZE,
  LabInput,
  LabsSortDir,
  LabsSortField,
  MeDto,
  PagedResult,
} from '../../../shared/models';

/** Тексты отказов — дословно контракты IF-103/IF-007. */
const TEXT_NOT_FOUND = 'Лабораторная не найдена';
const TEXT_DUPLICATE = 'Лабораторная с таким номером уже есть в семестре';
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

/** Учётная запись состояния бэкенда (демо-данные ADR-107). */
export interface WorksBackendUser {
  readonly id: string;
  readonly login: string;
  readonly fullName: string;
  readonly role: 'student' | 'teacher';
  readonly password: string;
}

/** Сдача состояния бэкенда (доменная модель Submission, §4.3). */
export interface WorksBackendSubmission {
  readonly id: string;
  readonly studentId: string;
  readonly labId: string;
  readonly submitDate: string | null;
  readonly defenseDate: string | null;
}

/** Снимок серверного состояния — «чтение БД» сценариями (аналог readDb). */
export interface WorksBackendSnapshot {
  readonly users: WorksBackendUser[];
  readonly labs: LabDto[];
  readonly submissions: WorksBackendSubmission[];
}

/** Ответ бэкенда для flush (тело — JSON-объект/массив либо null у 204). */
export interface BackendResponse {
  readonly status: number;
  readonly statusText: string;
  readonly body: object | null;
}

/** Демо-пароли (ADR-107): teacher/teacher123!, studentNN/student123!. */
const TEACHER_PASSWORD = 'teacher123!';
const STUDENT_PASSWORD = 'student123!';

/** Демо-ФИО преподавателя (ADR-107). */
const TEACHER_FULL_NAME = 'Сидоров Семён Семёнович';

/** Правила сид-сдач (макеты SCR-009/SCR-010): студент NN + работа (семестр, №). */
const SEED_SUBMISSION_RULES: ReadonlyArray<{
  studentLogin: string;
  semester: number;
  labNumber: number;
  submitDate: string | null;
  defenseDate: string | null;
}> = [
  { studentLogin: 'student01', semester: 1, labNumber: 1, submitDate: '2026-09-01', defenseDate: '2026-09-11' },
  { studentLogin: 'student01', semester: 1, labNumber: 2, submitDate: '2026-09-02', defenseDate: '2026-09-12' },
  { studentLogin: 'student01', semester: 1, labNumber: 3, submitDate: '2026-09-03', defenseDate: null },
  { studentLogin: 'student02', semester: 1, labNumber: 1, submitDate: '2026-09-01', defenseDate: null },
];

/** Ответ «успех» для flush. */
function ok(status: number, body: object | null): BackendResponse {
  return { status, statusText: STATUS_TEXT[status] ?? '', body };
}

/** Ответ отказа с телом {message, errors?}. */
function fail(status: number, message: string): BackendResponse {
  return { status, statusText: STATUS_TEXT[status] ?? '', body: { message } };
}

/**
 * Состояние и поведение REST-бэкенда works-домена. Экземпляр создаётся на
 * каждый сценарий (WorksIntegrationHarness.setup) — состояние не протекает
 * между спеками.
 */
export class WorksBackendStub {
  private idCounter = 0;

  private readonly users: WorksBackendUser[] = [
    {
      id: this.nextId(),
      login: 'teacher',
      fullName: TEACHER_FULL_NAME,
      role: 'teacher',
      password: TEACHER_PASSWORD,
    },
    { id: this.nextId(), login: 'student01', fullName: 'Иванов Иван Иванович 01', role: 'student', password: STUDENT_PASSWORD },
    { id: this.nextId(), login: 'student02', fullName: 'Иванов Иван Иванович 02', role: 'student', password: STUDENT_PASSWORD },
  ];

  /** Работы: семестр 1 №1–20, семестр 2 №1–3 (сид T-107/ADR-107). */
  private readonly labs: Lab[] = [];

  private readonly submissions: WorksBackendSubmission[] = [];

  /** Программируемый отказ следующего POST /labs (серверный 400 формы). */
  private nextCreateFailure: { status: number; body: object | null } | null = null;

  constructor() {
    const labIdByKey = new Map<string, string>();
    for (const [semester, lastNumber] of [
      [1, 20],
      [2, 3],
    ] as const) {
      for (let number = 1; number <= lastNumber; number += 1) {
        const lab: Lab = {
          id: this.nextId(),
          semester,
          number,
          content: `Содержание лабораторной работы №${number}`,
          defenseRequired: number % 2 === 0,
          assignmentUrl:
            number % 5 === 0 ? `https://git.example.com/assignments/${semester}/${number}` : null,
        };
        labIdByKey.set(`${semester}:${number}`, lab.id);
        this.labs.push(lab);
      }
    }
    for (const rule of SEED_SUBMISSION_RULES) {
      const labId = labIdByKey.get(`${rule.semester}:${rule.labNumber}`);
      const student = this.users.find((candidate) => candidate.login === rule.studentLogin);
      if (labId === undefined || student === undefined) {
        throw new Error('works-backend-stub: правило сид-сдачи невыполнимо — сид неполон');
      }
      this.submissions.push({
        id: this.nextId(),
        studentId: student.id,
        labId,
        submitDate: rule.submitDate,
        defenseDate: rule.defenseDate,
      });
    }
  }

  /** Детерминированный uuid нового состояния (формат IF-001). */
  private nextId(): string {
    this.idCounter += 1;
    return `c0140000-0000-4000-8000-${String(this.idCounter).padStart(12, '0')}`;
  }

  /** Снимок состояния: пользователи, работы, сдачи. */
  snapshot(): WorksBackendSnapshot {
    return {
      users: this.users.map((user) => ({ ...user })),
      labs: this.labs.map((lab) => ({ ...lab })),
      submissions: this.submissions.map((submission) => ({ ...submission })),
    };
  }

  /** uuid работы по паре (семестр, номер); отсутствие — ошибка сценария. */
  labId(semester: number, number: number): string {
    const lab = this.labs.find(
      (candidate) => candidate.semester === semester && candidate.number === number,
    );
    if (lab === undefined) {
      throw new Error(`works-backend-stub: в состоянии нет работы ${semester}:${number}`);
    }
    return lab.id;
  }

  /**
   * Удаление работы «из другой вкладки» — прямая мутация серверного
   * состояния мимо UI: последующие операции UI по этому id получают 404.
   */
  removeLabDirect(id: string): void {
    const index = this.labs.findIndex((candidate) => candidate.id === id);
    if (index === -1) {
      throw new Error(`works-backend-stub: работы ${id} нет в состоянии (removeLabDirect)`);
    }
    this.labs.splice(index, 1);
    for (let i = this.submissions.length - 1; i >= 0; i -= 1) {
      if (this.submissions[i]!.labId === id) {
        this.submissions.splice(i, 1);
      }
    }
  }

  /** Серверный отказ следующего POST /labs (например 400 формы с errors). */
  failNextCreate(failure: { status: number; body: object | null }): void {
    this.nextCreateFailure = failure;
  }

  /**
   * Обработка запроса по состоянию. Вызывается харнесом для каждого
   * перехваченного HttpTestingController-запроса; неизвестный маршрут —
   * ошибка сценария (дыра в программировании бэкенда). Query читается из
   * urlWithParams: HttpRequest.url не содержит HttpParams.
   */
  handle(req: HttpRequest<unknown>, apiBase: string): BackendResponse {
    if (!req.url.startsWith(`${apiBase}/`)) {
      throw new Error(`works-backend-stub: запрос вне API ${req.method} ${req.url}`);
    }
    const pathWithQuery = req.urlWithParams.slice(apiBase.length);
    const queryIndex = pathWithQuery.indexOf('?');
    const path = queryIndex === -1 ? pathWithQuery : pathWithQuery.slice(0, queryIndex);
    const query = new URLSearchParams(queryIndex === -1 ? '' : pathWithQuery.slice(queryIndex + 1));

    if (req.method === 'POST' && path === '/auth/login') {
      return this.login(req.body as { login: string; password: string });
    }
    if (req.method === 'GET' && path === '/labs') {
      return this.getList(query);
    }
    if (req.method === 'GET' && path === '/semesters') {
      return this.getSemesters();
    }
    if (req.method === 'POST' && path === '/labs') {
      return this.create(req.body as LabInput);
    }
    const labMatch = /^\/labs\/([^/]+)$/.exec(path);
    if (labMatch !== null) {
      const id = decodeURIComponent(labMatch[1]!);
      if (req.method === 'GET') {
        return this.getById(id);
      }
      if (req.method === 'PUT') {
        return this.update(id, req.body as LabInput);
      }
      if (req.method === 'DELETE') {
        return this.remove(id);
      }
    }
    throw new Error(`works-backend-stub: нет обработчика ${req.method} ${req.url}`);
  }

  /** POST /auth/login: только demo-учётки состояния (IF-007). */
  private login(body: { login: string; password: string }): BackendResponse {
    const user = this.users.find(
      (candidate) => candidate.login === body?.login && candidate.password === body?.password,
    );
    if (user === undefined) {
      return fail(401, TEXT_WRONG_CREDENTIALS);
    }
    const me: MeDto = {
      login: user.login,
      fullName: user.fullName,
      role: user.role,
      groupName: null,
    };
    return ok(200, me);
  }

  /**
   * GET /labs: фильтр/сортировка/нарезка применяются ко всей выборке
   * (IF-103); отсутствующие значения query — дефолты контракта.
   */
  private getList(query: URLSearchParams): BackendResponse {
    const semesterRaw = query.get('semester');
    const semester = semesterRaw === null ? null : Number(semesterRaw);
    const requestedPage = Number(query.get('page'));
    const page = Number.isInteger(requestedPage) && requestedPage >= 1 ? requestedPage : 1;
    const sortField: LabsSortField = query.get('sortField') === 'number' ? 'number' : 'semester';
    const sortDir: LabsSortDir = query.get('sortDir') === 'desc' ? 'desc' : 'asc';

    const direction = sortDir === 'desc' ? -1 : 1;
    const compare = (a: Lab, b: Lab): number => {
      const primary =
        (sortField === 'semester' ? a.semester - b.semester : a.number - b.number) * direction;
      if (primary !== 0) {
        return primary;
      }
      return sortField === 'number' ? a.semester - b.semester : a.number - b.number;
    };
    const filtered = this.labs
      .filter((lab) => semester === null || lab.semester === semester)
      .sort(compare);
    const start = (page - 1) * LABS_PAGE_SIZE;
    const items: LabDto[] = filtered.slice(start, start + LABS_PAGE_SIZE).map((lab) => ({ ...lab }));
    const result: PagedResult<LabDto> = {
      items,
      total: filtered.length,
      page,
      pageSize: LABS_PAGE_SIZE,
    };
    return ok(200, result);
  }

  /** GET /semesters: distinct семестры работ по возрастанию (§4.4). */
  private getSemesters(): BackendResponse {
    const semesters = [...new Set(this.labs.map((lab) => lab.semester))];
    return ok(200, semesters.sort((a, b) => a - b));
  }

  /** GET /labs/{id}. */
  private getById(id: string): BackendResponse {
    const lab = this.labs.find((candidate) => candidate.id === id);
    return lab === undefined ? fail(404, TEXT_NOT_FOUND) : ok(200, { ...lab });
  }

  /** Занята ли пара (semester, number); excludeId — своя запись update. */
  private hasDuplicate(semester: number, number: number, excludeId: string | null): boolean {
    return this.labs.some(
      (lab) => lab.id !== excludeId && lab.semester === semester && lab.number === number,
    );
  }

  /** POST /labs: программный отказ → 409 за пару → 201 созданная запись. */
  private create(input: LabInput): BackendResponse {
    if (this.nextCreateFailure !== null) {
      const failure = this.nextCreateFailure;
      this.nextCreateFailure = null;
      return { status: failure.status, statusText: STATUS_TEXT[failure.status] ?? '', body: failure.body };
    }
    if (this.hasDuplicate(Number(input.semester), Number(input.number), null)) {
      return fail(409, TEXT_DUPLICATE);
    }
    const lab: Lab = {
      id: this.nextId(),
      semester: Number(input.semester),
      number: Number(input.number),
      content: String(input.content),
      assignmentUrl: input.assignmentUrl ?? null,
      defenseRequired: input.defenseRequired === true,
    };
    this.labs.push(lab);
    return ok(201, { ...lab });
  }

  /** PUT /labs/{id}: 404 → 409 (кроме своей записи) → 200 обновлённая. */
  private update(id: string, input: LabInput): BackendResponse {
    const lab = this.labs.find((candidate) => candidate.id === id);
    if (lab === undefined) {
      return fail(404, TEXT_NOT_FOUND);
    }
    if (this.hasDuplicate(Number(input.semester), Number(input.number), id)) {
      return fail(409, TEXT_DUPLICATE);
    }
    const updated: Lab = {
      ...lab,
      semester: Number(input.semester),
      number: Number(input.number),
      content: String(input.content),
      assignmentUrl: input.assignmentUrl ?? null,
      defenseRequired: input.defenseRequired === true,
    };
    Object.assign(lab, updated);
    return ok(200, { ...lab });
  }

  /** DELETE /labs/{id}: 404 → 204 с каскадным удалением сдач (§4.3). */
  private remove(id: string): BackendResponse {
    const index = this.labs.findIndex((candidate) => candidate.id === id);
    if (index === -1) {
      return fail(404, TEXT_NOT_FOUND);
    }
    this.labs.splice(index, 1);
    for (let i = this.submissions.length - 1; i >= 0; i -= 1) {
      if (this.submissions[i]!.labId === id) {
        this.submissions.splice(i, 1);
      }
    }
    return ok(204, null);
  }
}
