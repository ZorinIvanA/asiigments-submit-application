/**
 * Юнит-тесты StudentsService (C-014, FR-091) — все вызовы через
 * HttpTestingController (конвенция URL — ADR-014: ожидаемые URL строятся из
 * TestBed.inject(API_BASE_URL), литералы префикса запрещены).
 * Производственная цепочка — реальный authInterceptor (withCredentials,
 * нормализация отказов в ApiError, дедуплицированный refresh):
 *  - getList → GET /students с query в порядке search, groupId, page;
 *    отсутствующие значения (search пустой/пробельный, groupId null) в
 *    query не попадают; многословный search уходит URL-кодированным;
 *  - setGroup → PUT /students/{id}/group с телом {groupId} (uuid или null),
 *    идентификатор студента — в пути;
 *  - возвращаемые типы идентичны прежним (сигнатуры сервиса не менялись);
 *  - отказы 403/400+errors/404 — реджект ApiError той же формы (баннеры
 *    body.message и полевые errors работают без изменений);
 *  - 401 (истекшая сессия) — после безуспешного refresh реджект ApiError
 *    {status: 401, body: {message}}.
 */
import { HttpParams, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { authInterceptor } from '../auth-interceptor';
import { API_BASE_URL } from '../api-base-url';
import {
  ApiError,
  PagedResult,
  STUDENTS_PAGE_SIZE,
  StudentDto,
} from '../../shared/models';
import { StudentsService } from './students.service';

const STUDENT_ID = 'cccccccc-0000-4000-8000-000000000001';
const GROUP_ID = 'b1111111-1111-4111-8111-111111111111';
const UNKNOWN_STUDENT_ID = 'ddddddd4-0000-4000-8000-000000000004';

const STUDENT: StudentDto = {
  id: STUDENT_ID,
  fullName: 'Иванов Иван Иванович 01',
  login: 'student01',
  email: 'student01@example.com',
  groupId: null,
  groupName: null,
};

/** Заголовки HTTP-отказов бэкенда. */
function status(status: number, statusText: string): { status: number; statusText: string } {
  return { status, statusText };
}

describe('StudentsService — домен студентов поверх HttpClient (FR-091)', () => {
  let httpMock: HttpTestingController;
  let apiBase: string;
  let service: StudentsService;

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
    service = TestBed.inject(StudentsService);
  });

  afterEach(() => {
    // Ни одного незакрытого/лишнего запроса.
    httpMock.verify();
  });

  describe('контракт вызовов (AC, HttpTestingController)', () => {
    it('getList: query побайтово «groupId=none&page=2» — порядок groupId, page; page всегда в query', async () => {
      const page: PagedResult<StudentDto> = {
        items: [STUDENT],
        total: 3,
        page: 2,
        pageSize: STUDENTS_PAGE_SIZE,
      };
      const pending = service.getList({ search: null, groupId: 'none', page: 2 });

      const request = httpMock.expectOne(`${apiBase}/students?groupId=none&page=2`);
      expect(request.request.method).toBe('GET');
      expect(request.request.body).withContext('у GET тела нет').toBeNull();
      expect(request.request.withCredentials)
        .withContext('cookie-аутентификация — withCredentials (FR-091)')
        .toBeTrue();
      request.flush(page);

      await expectAsync(pending).toBeResolvedTo(page);
    });

    it('getList: многословный search уходит URL-кодированным, значения декодируются обратно', async () => {
      const pending = service.getList({ search: 'иванов 01', groupId: 'none', page: 1 });

      const request = httpMock.expectOne(
        `${apiBase}/students?${new HttpParams()
          .set('search', 'иванов 01')
          .set('groupId', 'none')
          .set('page', 1)
          .toString()}`,
      );
      expect(request.request.params.get('search'))
        .withContext('search — исходное значение (кодирование/декодирование взаимно)')
        .toBe('иванов 01');
      expect(request.request.params.get('groupId')).toBe('none');
      expect(request.request.params.get('page')).toBe('1');
      // Пробел многословного запроса не попадает в URL «как есть» —
      // только в закодированной форме (стандартный энкодер HttpParams).
      expect(request.request.urlWithParams).not.toContain('иванов 01');
      request.flush({ items: [], total: 0, page: 1, pageSize: STUDENTS_PAGE_SIZE });

      await expectAsync(pending).toBeResolved();
    });

    it('getList без фильтров: search пустой и groupId null в query не попадают', async () => {
      const pending = service.getList({ search: '', groupId: null, page: 1 });

      const request = httpMock.expectOne(`${apiBase}/students?page=1`);
      expect(request.request.method).toBe('GET');
      request.flush({ items: [], total: 0, page: 1, pageSize: STUDENTS_PAGE_SIZE });

      await expectAsync(pending).toBeResolvedTo({ items: [], total: 0, page: 1, pageSize: STUDENTS_PAGE_SIZE });
    });

    it('getList: search из одних пробелов — «без поиска», в query не попадает', async () => {
      const pending = service.getList({ search: '   ', page: 3 });

      const request = httpMock.expectOne(`${apiBase}/students?page=3`);
      expect(request.request.params.get('search')).toBeNull();
      request.flush({ items: [], total: 0, page: 3, pageSize: STUDENTS_PAGE_SIZE });

      await expectAsync(pending).toBeResolved();
    });

    it('getList: фильтр по uuid группы уходит как есть, порядок search, groupId, page', async () => {
      const pending = service.getList({ search: 'иванов', groupId: GROUP_ID, page: 1 });

      const request = httpMock.expectOne(
        `${apiBase}/students?${new HttpParams()
          .set('search', 'иванов')
          .set('groupId', GROUP_ID)
          .set('page', 1)
          .toString()}`,
      );
      expect(request.request.params.get('search')).toBe('иванов');
      expect(request.request.params.get('groupId')).toBe(GROUP_ID);
      request.flush({ items: [], total: 0, page: 1, pageSize: STUDENTS_PAGE_SIZE });

      await expectAsync(pending).toBeResolved();
    });

    it('setGroup: PUT /students/{id}/group с телом {groupId: uuid} → 204, void', async () => {
      const pending = service.setGroup(STUDENT_ID, GROUP_ID);

      const request = httpMock.expectOne(`${apiBase}/students/${STUDENT_ID}/group`);
      expect(request.request.method).toBe('PUT');
      expect(request.request.body)
        .withContext('идентификатор студента — в пути; тело — ровно {groupId}')
        .toEqual({ groupId: GROUP_ID });
      request.flush(null, status(204, 'No Content'));

      await expectAsync(pending).toBeResolved();
    });

    it('setGroup null: тело {groupId: null} — исключение из группы → 204', async () => {
      const pending = service.setGroup(STUDENT_ID, null);

      const request = httpMock.expectOne(`${apiBase}/students/${STUDENT_ID}/group`);
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).withContext('null уходит в теле, не в query').toEqual({ groupId: null });
      request.flush(null, status(204, 'No Content'));

      await expectAsync(pending).toBeResolved();
    });

    it('константа контракта: STUDENTS_PAGE_SIZE=10 (реэкспорт из shared/models)', () => {
      expect(STUDENTS_PAGE_SIZE).toBe(10);
    });
  });

  describe('нормализация отказов (ApiError, баннеры страниц без изменений)', () => {
    it('getList: 400 «Данные заполнены неверно» с errors {search} → ApiError переносит errors по полям', async () => {
      const pending = service.getList({ search: 'x'.repeat(201), page: 1 });
      const errors = { search: ['Поиск — не более 200 символов'] };
      httpMock.expectOne(`${apiBase}/students?search=${'x'.repeat(201)}&page=1`).flush(
        { message: 'Данные заполнены неверно', errors },
        status(400, 'Bad Request'),
      );

      await expectAsync(pending).toBeRejectedWith({
        status: 400,
        body: { message: 'Данные заполнены неверно', errors },
      } satisfies ApiError);
    });

    it('getList: 403 «Доступ запрещён» студенту → ApiError {status, body:{message}}', async () => {
      const pending = service.getList({ page: 1 });
      httpMock.expectOne(`${apiBase}/students?page=1`).flush(
        { message: 'Доступ запрещён' },
        status(403, 'Forbidden'),
      );

      await expectAsync(pending).toBeRejectedWith({
        status: 403,
        body: { message: 'Доступ запрещён' },
      } satisfies ApiError);
    });

    it('setGroup: 404 «Студент не найден» → ApiError с дословным message', async () => {
      const pending = service.setGroup(UNKNOWN_STUDENT_ID, GROUP_ID);
      httpMock.expectOne(`${apiBase}/students/${UNKNOWN_STUDENT_ID}/group`).flush(
        { message: 'Студент не найден' },
        status(404, 'Not Found'),
      );

      await expectAsync(pending).toBeRejectedWith({
        status: 404,
        body: { message: 'Студент не найден' },
      } satisfies ApiError);
    });

    it('setGroup: 404 «Группа не найдена» → ApiError', async () => {
      const pending = service.setGroup(STUDENT_ID, UNKNOWN_STUDENT_ID);
      httpMock.expectOne(`${apiBase}/students/${STUDENT_ID}/group`).flush(
        { message: 'Группа не найдена' },
        status(404, 'Not Found'),
      );

      await expectAsync(pending).toBeRejectedWith({
        status: 404,
        body: { message: 'Группа не найдена' },
      } satisfies ApiError);
    });

    it('getList: 401 (сессия истекла) — refresh безуспешен, реджект ApiError 401 «Не авторизован»', async () => {
      // Интерцептор в фазе инициализации не навигирует; spy страхует цепочку
      // Router от необработанных отказов (навигация — не зона сервиса).
      spyOn(TestBed.inject(Router), 'navigate');

      const pending = service.getList({ page: 1 });
      httpMock.expectOne(`${apiBase}/students?page=1`).flush(
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

