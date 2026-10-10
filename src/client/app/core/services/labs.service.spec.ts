/**
 * Юнит-тесты LabsService (C-014, FR-091) — все вызовы через
 * HttpTestingController (конвенция URL — ADR-014: ожидаемые URL строятся из
 * TestBed.inject(API_BASE_URL), литералы префикса запрещены).
 * Производственная цепочка — реальный authInterceptor (withCredentials,
 * нормализация отказов в ApiError, дедуплицированный refresh):
 *  - getList → GET /labs с побайтовой строкой query в порядке
 *    semester, page, sortField, sortDir; отсутствующие значения в query
 *    не попадают (semester null — «все семестры»);
 *  - getById → GET /labs/{id}; create → POST /labs (тело LabInput);
 *    update → PUT /labs/{id} (тело LabInput без дублирования id);
 *    remove → DELETE /labs/{id} → void; getSemesters → GET /semesters;
 *  - возвращаемые типы идентичны прежним (сигнатуры сервиса не менялись);
 *  - отказы 403/404/400+errors/409 — реджект ApiError той же формы
 *    (баннеры body.message и полевые errors работают без изменений);
 *  - 401 на /semesters (истекшая сессия) — после безуспешного refresh
 *    реджект ApiError {status: 401, body: {message}}.
 */
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { authInterceptor } from '../auth-interceptor';
import { API_BASE_URL } from '../api-base-url';
import {
  ApiError,
  LabDto,
  LABS_PAGE_SIZE,
  LabInput,
  PagedResult,
} from '../../shared/models';
import { LabsService } from './labs.service';

const API_LAB_ID = 'bbbbbbbb-0000-4000-8000-000000000001';

const CREATED_LAB: LabDto = {
  id: API_LAB_ID,
  semester: 1,
  number: 21,
  content: 'Содержание новой работы',
  assignmentUrl: null,
  defenseRequired: false,
};

const labInput = (overrides?: Partial<LabInput>): LabInput => ({
  number: 21,
  semester: 1,
  content: 'Содержание новой работы',
  assignmentUrl: null,
  defenseRequired: false,
  ...overrides,
});

/** Заголовки HTTP-отказов бэкенда. */
function status(status: number, statusText: string): { status: number; statusText: string } {
  return { status, statusText };
}

describe('LabsService — домен лабораторных поверх HttpClient (FR-091)', () => {
  let httpMock: HttpTestingController;
  let apiBase: string;
  let service: LabsService;

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
    service = TestBed.inject(LabsService);
  });

  afterEach(() => {
    // Ни одного незакрытого/лишнего запроса.
    httpMock.verify();
  });

  describe('контракт вызовов (AC, HttpTestingController)', () => {
    it('getList: query побайтово «semester=1&page=2&sortField=number&sortDir=desc» (AC «Query точно»)', async () => {
      const page: PagedResult<LabDto> = {
        items: [CREATED_LAB],
        total: 11,
        page: 2,
        pageSize: LABS_PAGE_SIZE,
      };
      const pending = service.getList({ semester: 1, page: 2, sortField: 'number', sortDir: 'desc' });

      const request = httpMock.expectOne(
        `${apiBase}/labs?semester=1&page=2&sortField=number&sortDir=desc`,
      );
      expect(request.request.method).toBe('GET');
      expect(request.request.body).withContext('у GET тела нет').toBeNull();
      expect(request.request.withCredentials)
        .withContext('cookie-аутентификация — withCredentials (FR-091)')
        .toBeTrue();
      request.flush(page);

      await expectAsync(pending).toBeResolvedTo(page);
    });

    it('getList без фильтра и сортировки: semester null и неопределённые sort* в query не попадают', async () => {
      const pending = service.getList({ semester: null, page: 1 });

      const request = httpMock.expectOne(`${apiBase}/labs?page=1`);
      expect(request.request.method).toBe('GET');
      request.flush({ items: [], total: 0, page: 1, pageSize: LABS_PAGE_SIZE });

      await expectAsync(pending).toBeResolvedTo({ items: [], total: 0, page: 1, pageSize: LABS_PAGE_SIZE });
    });

    it('getById: GET /labs/{id} → LabDto', async () => {
      const pending = service.getById(API_LAB_ID);

      const request = httpMock.expectOne(`${apiBase}/labs/${API_LAB_ID}`);
      expect(request.request.method).toBe('GET');
      expect(request.request.body).toBeNull();
      request.flush(CREATED_LAB);

      await expectAsync(pending).toBeResolvedTo(CREATED_LAB);
    });

    it('create: POST /labs с телом LabInput → 201 LabDto', async () => {
      const input = labInput({ assignmentUrl: 'https://git.example.com/a' });
      const pending = service.create(input);

      const request = httpMock.expectOne(`${apiBase}/labs`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual(input);
      request.flush(CREATED_LAB, { status: 201, statusText: 'Created' });

      await expectAsync(pending).toBeResolvedTo(CREATED_LAB);
    });

    it('update: PUT /labs/{id} — id в пути, тело без дублирования id → LabDto', async () => {
      const input = labInput({ number: 7, semester: 3, content: 'Изменено', defenseRequired: true });
      const pending = service.update(API_LAB_ID, input);

      const request = httpMock.expectOne(`${apiBase}/labs/${API_LAB_ID}`);
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).withContext('id уходит в путь, не в тело').toEqual(input);
      request.flush({ ...CREATED_LAB, ...input });

      await expectAsync(pending).toBeResolvedTo({ ...CREATED_LAB, ...input });
    });

    it('remove: DELETE /labs/{id} → 204, void', async () => {
      const pending = service.remove(API_LAB_ID);

      const request = httpMock.expectOne(`${apiBase}/labs/${API_LAB_ID}`);
      expect(request.request.method).toBe('DELETE');
      expect(request.request.body).toBeNull();
      request.flush(null, status(204, 'No Content'));

      await expectAsync(pending).toBeResolved();
    });

    it('getSemesters: GET /semesters → число семестров по возрастанию', async () => {
      const pending = service.getSemesters();

      const request = httpMock.expectOne(`${apiBase}/semesters`);
      expect(request.request.method).toBe('GET');
      expect(request.request.body).toBeNull();
      request.flush([1, 3]);

      await expectAsync(pending).toBeResolvedTo([1, 3]);
    });

    it('константа контракта: LABS_PAGE_SIZE=10 (реэкспорт из нового места, T-018)', () => {
      expect(LABS_PAGE_SIZE).toBe(10);
    });
  });

  describe('нормализация отказов (ApiError, баннеры страниц без изменений)', () => {
    it('getList: 403 «Доступ запрещён» студенту → ApiError {status, body:{message}}', async () => {
      const pending = service.getList({ semester: 1, page: 1 });
      httpMock.expectOne(`${apiBase}/labs?semester=1&page=1`).flush(
        { message: 'Доступ запрещён' },
        status(403, 'Forbidden'),
      );

      await expectAsync(pending).toBeRejectedWith({
        status: 403,
        body: { message: 'Доступ запрещён' },
      } satisfies ApiError);
    });

    it('getById: 404 «Лабораторная не найдена» → ApiError с дословным message', async () => {
      const pending = service.getById(API_LAB_ID);
      httpMock.expectOne(`${apiBase}/labs/${API_LAB_ID}`).flush(
        { message: 'Лабораторная не найдена' },
        status(404, 'Not Found'),
      );

      await expectAsync(pending).toBeRejectedWith({
        status: 404,
        body: { message: 'Лабораторная не найдена' },
      } satisfies ApiError);
    });

    it('create: 400 «Данные заполнены неверно» с errors → ApiError переносит errors по полям', async () => {
      const pending = service.create(labInput({ number: 0, semester: 11, assignmentUrl: 'ftp://x' }));
      const errors = {
        number: ['Номер должен быть положительным целым числом'],
        semester: ['Семестр должен быть целым числом от 1 до 10'],
        assignmentUrl: ['Ссылка должна начинаться с http:// или https://'],
      };
      httpMock.expectOne(`${apiBase}/labs`).flush(
        { message: 'Данные заполнены неверно', errors },
        status(400, 'Bad Request'),
      );

      await expectAsync(pending).toBeRejectedWith({
        status: 400,
        body: { message: 'Данные заполнены неверно', errors },
      } satisfies ApiError);
    });

    it('update: 409 «Лабораторная с таким номером уже есть в семестре» → ApiError', async () => {
      const pending = service.update(API_LAB_ID, labInput({ number: 3 }));
      httpMock.expectOne(`${apiBase}/labs/${API_LAB_ID}`).flush(
        { message: 'Лабораторная с таким номером уже есть в семестре' },
        status(409, 'Conflict'),
      );

      await expectAsync(pending).toBeRejectedWith({
        status: 409,
        body: { message: 'Лабораторная с таким номером уже есть в семестре' },
      } satisfies ApiError);
    });

    it('remove: 404 «Лабораторная не найдена» → ApiError', async () => {
      const pending = service.remove(API_LAB_ID);
      httpMock.expectOne(`${apiBase}/labs/${API_LAB_ID}`).flush(
        { message: 'Лабораторная не найдена' },
        status(404, 'Not Found'),
      );

      await expectAsync(pending).toBeRejectedWith({
        status: 404,
        body: { message: 'Лабораторная не найдена' },
      } satisfies ApiError);
    });

    it('getSemesters: 401 (сессия истекла) — refresh безуспешен, реджект ApiError 401 «Не авторизован»', async () => {
      // Интерцептор в фазе инициализации не навигирует; spy страхует цепочку
      // Router от необработанных отказов (навигация — не зона сервиса).
      spyOn(TestBed.inject(Router), 'navigate');

      const pending = service.getSemesters();
      httpMock.expectOne(`${apiBase}/semesters`).flush(
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
