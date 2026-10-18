/**
 * Юнит-тесты SubmissionsService (C-014, FR-091) — все вызовы через
 * HttpTestingController (конвенция URL — ADR-014: ожидаемые URL строятся из
 * TestBed.inject(API_BASE_URL), литералы префикса запрещены).
 * Производственная цепочка — реальный authInterceptor (withCredentials,
 * нормализация отказов в ApiError, дедуплицированный refresh):
 *  - getGrid → GET /submissions с query «groupId&semester&page» в контрактом
 *    порядке (все три параметра обязательны);
 *  - update → PUT /submissions с телом {studentId, labId, submitDate,
 *    defenseDate} — обе даты всегда передаются, null = сброс (частичной
 *    формы контракт не имеет);
 *  - getMy → GET /me/submissions?semester={n};
 *  - возвращаемые типы идентичны прежним (сигнатуры сервиса не менялись);
 *  - отказы 404/400+errors/403 — реджект ApiError той же формы (баннеры
 *    body.message и полевые errors работают без изменений);
 *  - 401 (истекшая сессия) — после безуспешного refresh реджект
 *    ApiError {status: 401, body: {message}}.
 */
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { authInterceptor } from '../auth-interceptor';
import { API_BASE_URL } from '../api-base-url';
import { ApiError, MySubmissionsDto, Submission } from '../../shared/models';
import {
  SUBMISSIONS_PAGE_SIZE,
  SubmissionsGridQuery,
  SubmissionsGridResult,
  SubmissionsService,
} from './submissions.service';

const GROUP_ID = 'a0000000-0000-4000-8000-000000000001';
const STUDENT_ID = 'b0000000-0000-4000-8000-000000000002';
const LAB_ID = 'd0000000-0000-4000-8000-000000000002';

const GRID: SubmissionsGridResult = {
  students: [{ id: STUDENT_ID, fullName: 'Фамилия А' }],
  labs: [{ id: LAB_ID, number: 1, defenseRequired: true }],
  submissions: [
    {
      studentId: STUDENT_ID,
      labId: LAB_ID,
      submitDate: '2026-09-01',
      defenseDate: null,
    },
  ],
  total: 1,
  page: 2,
};

const SAVED: Submission = {
  id: 'f0000000-0000-4000-8000-000000000001',
  studentId: STUDENT_ID,
  labId: LAB_ID,
  submitDate: '2026-09-15',
  defenseDate: null,
  updatedAt: '2026-09-27T12:00:00.000Z',
  updatedBy: 'b0000000-0000-4000-8000-000000000001',
};

const MY: MySubmissionsDto = {
  hasGroup: true,
  labs: [{ id: LAB_ID, number: 1, defenseRequired: true }],
  submissions: [{ labId: LAB_ID, submitDate: '2026-09-01', defenseDate: null }],
};

/** Заголовки HTTP-отказов бэкенда. */
function status(status: number, statusText: string): { status: number; statusText: string } {
  return { status, statusText };
}

describe('SubmissionsService — домен сдач поверх HttpClient (FR-091)', () => {
  let httpMock: HttpTestingController;
  let apiBase: string;
  let service: SubmissionsService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
    apiBase = TestBed.inject(API_BASE_URL);
    service = TestBed.inject(SubmissionsService);
  });

  afterEach(() => {
    // Ни одного незакрытого/лишнего запроса.
    httpMock.verify();
  });

  describe('контракт вызовов (AC, HttpTestingController)', () => {
    it('getGrid: GET /submissions с query «groupId&semester&page» в контрактом порядке', async () => {
      const query: SubmissionsGridQuery = { groupId: GROUP_ID, semester: 1, page: 2 };
      const pending = service.getGrid(query);

      const request = httpMock.expectOne(`${apiBase}/submissions?groupId=${GROUP_ID}&semester=1&page=2`);
      expect(request.request.method).toBe('GET');
      expect(request.request.body).withContext('у GET тела нет').toBeNull();
      expect(request.request.withCredentials)
        .withContext('cookie-аутентификация — withCredentials (FR-091)')
        .toBeTrue();
      request.flush(GRID);

      await expectAsync(pending).toBeResolvedTo(GRID);
    });

    it('getGrid: query соответствует выборке — другие groupId/semester/page попадают в строку дословно', async () => {
      const otherGroupId = 'a9999999-9999-4999-8999-999999999999';
      const pending = service.getGrid({ groupId: otherGroupId, semester: 10, page: 3 });

      const request = httpMock.expectOne(
        `${apiBase}/submissions?groupId=${otherGroupId}&semester=10&page=3`,
      );
      request.flush({ ...GRID, page: 3 });

      await expectAsync(pending).toBeResolved();
    });

    it('update (AC «Контракт update»): PUT /submissions с телом {studentId, labId, submitDate, defenseDate}', async () => {
      const params = {
        studentId: STUDENT_ID,
        labId: LAB_ID,
        submitDate: '2026-09-15',
        defenseDate: '2026-09-22',
      };
      const pending = service.update(params);

      const request = httpMock.expectOne(`${apiBase}/submissions`);
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).withContext('тело — обе даты целиком').toEqual(params);
      request.flush({ ...SAVED, defenseDate: '2026-09-22' });

      await expectAsync(pending).toBeResolvedTo({ ...SAVED, defenseDate: '2026-09-22' });
    });

    it('update (сброс): null-дата присутствует в теле ключом со значением null — обе даты всегда передаются', async () => {
      const pending = service.update({
        studentId: STUDENT_ID,
        labId: LAB_ID,
        submitDate: null,
        defenseDate: '2026-09-22',
      });

      const request = httpMock.expectOne(`${apiBase}/submissions`);
      expect(request.request.method).toBe('PUT');
      const body = request.request.body as Record<string, unknown>;
      expect(Object.keys(body).sort()).withContext('ровно 4 поля контракта').toEqual([
        'defenseDate',
        'labId',
        'studentId',
        'submitDate',
      ]);
      expect(body['submitDate'])
        .withContext('сброс — null в теле, а не отсутствие поля')
        .toBeNull();
      request.flush({ ...SAVED, submitDate: null });

      await expectAsync(pending).toBeResolvedTo({ ...SAVED, submitDate: null });
    });

    it('getMy: GET /me/submissions?semester={n} → MySubmissionsDto', async () => {
      const pending = service.getMy(3);

      const request = httpMock.expectOne(`${apiBase}/me/submissions?semester=3`);
      expect(request.request.method).toBe('GET');
      expect(request.request.body).toBeNull();
      expect(request.request.withCredentials).toBeTrue();
      request.flush(MY);

      await expectAsync(pending).toBeResolvedTo(MY);
    });

    it('getMy: semester в query — дословное значение параметра', async () => {
      const pending = service.getMy(10);

      httpMock.expectOne(`${apiBase}/me/submissions?semester=10`).flush({ hasGroup: false, labs: [], submissions: [] });

      await expectAsync(pending).toBeResolvedTo({ hasGroup: false, labs: [], submissions: [] });
    });

    it('константа контракта: SUBMISSIONS_PAGE_SIZE=5 (§4.4, ADR-109)', () => {
      expect(SUBMISSIONS_PAGE_SIZE).toBe(5);
    });
  });

  describe('нормализация отказов (ApiError, баннеры страниц без изменений)', () => {
    it('getGrid: 404 «Группа не найдена» → ApiError {status, body:{message}}', async () => {
      const pending = service.getGrid({ groupId: GROUP_ID, semester: 1, page: 1 });
      httpMock
        .expectOne(`${apiBase}/submissions?groupId=${GROUP_ID}&semester=1&page=1`)
        .flush({ message: 'Группа не найдена' }, status(404, 'Not Found'));

      await expectAsync(pending).toBeRejectedWith({
        status: 404,
        body: { message: 'Группа не найдена' },
      } satisfies ApiError);
    });

    it('getGrid: 403 «Доступ запрещён» не-teacher → ApiError', async () => {
      const pending = service.getGrid({ groupId: GROUP_ID, semester: 1, page: 1 });
      httpMock
        .expectOne(`${apiBase}/submissions?groupId=${GROUP_ID}&semester=1&page=1`)
        .flush({ message: 'Доступ запрещён' }, status(403, 'Forbidden'));

      await expectAsync(pending).toBeRejectedWith({
        status: 403,
        body: { message: 'Доступ запрещён' },
      } satisfies ApiError);
    });

    it('update: 400 «Данные заполнены неверно» с errors {submitDate} → ApiError переносит errors по полям', async () => {
      const pending = service.update({
        studentId: STUDENT_ID,
        labId: LAB_ID,
        submitDate: '15.09.2026',
        defenseDate: null,
      });
      const errors = { submitDate: ['Неконтрактный формат даты'] };
      httpMock.expectOne(`${apiBase}/submissions`).flush(
        { message: 'Данные заполнены неверно', errors },
        status(400, 'Bad Request'),
      );

      await expectAsync(pending).toBeRejectedWith({
        status: 400,
        body: { message: 'Данные заполнены неверно', errors },
      } satisfies ApiError);
    });

    it('update: 404 «Студент не найден» → ApiError с дословным message', async () => {
      const pending = service.update({
        studentId: STUDENT_ID,
        labId: LAB_ID,
        submitDate: '2026-09-15',
        defenseDate: null,
      });
      httpMock
        .expectOne(`${apiBase}/submissions`)
        .flush({ message: 'Студент не найден' }, status(404, 'Not Found'));

      await expectAsync(pending).toBeRejectedWith({
        status: 404,
        body: { message: 'Студент не найден' },
      } satisfies ApiError);
    });

    it('update: 404 «Лабораторная не найдена» → ApiError с дословным message', async () => {
      const pending = service.update({
        studentId: STUDENT_ID,
        labId: LAB_ID,
        submitDate: null,
        defenseDate: null,
      });
      httpMock
        .expectOne(`${apiBase}/submissions`)
        .flush({ message: 'Лабораторная не найдена' }, status(404, 'Not Found'));

      await expectAsync(pending).toBeRejectedWith({
        status: 404,
        body: { message: 'Лабораторная не найдена' },
      } satisfies ApiError);
    });

    it('getMy: 403 «Доступ запрещён» teacher-у → ApiError', async () => {
      const pending = service.getMy(1);
      httpMock
        .expectOne(`${apiBase}/me/submissions?semester=1`)
        .flush({ message: 'Доступ запрещён' }, status(403, 'Forbidden'));

      await expectAsync(pending).toBeRejectedWith({
        status: 403,
        body: { message: 'Доступ запрещён' },
      } satisfies ApiError);
    });

    it('getMy: 401 (сессия истекла) — refresh безуспешен, реджект ApiError 401 «Не авторизован»', async () => {
      // Интерцептор в фазе инициализации не навигирует; spy страхует цепочку
      // Router от необработанных отказов (навигация — не зона сервиса).
      spyOn(TestBed.inject(Router), 'navigate');

      const pending = service.getMy(1);
      httpMock.expectOne(`${apiBase}/me/submissions?semester=1`).flush(
        { message: 'Не авторизован' },
        status(401, 'Unauthorized'),
      );
      httpMock.expectOne(`${apiBase}/auth/refresh`).flush(
        { message: 'Не авторизован' },
        status(401, 'Unauthorized'),
      );

      await expectAsync(pending).toBeRejectedWith({
        status: 401,
        body: { message: 'Не авторизован' },
      } satisfies ApiError);
    });
  });
});
