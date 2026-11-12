/**
 * Юнит-тесты GroupsService (C-014, FR-091) — все вызовы через
 * HttpTestingController (конвенция URL — ADR-014: ожидаемые URL строятся из
 * TestBed.inject(API_BASE_URL), литералы префикса запрещены).
 * Производственная цепочка — реальный authInterceptor (withCredentials,
 * нормализация отказов в ApiError, дедуплицированный refresh):
 *  - getList → GET /groups → GroupDto[];
 *  - create → POST /groups с телом {name} → 201 GroupDto;
 *  - rename → PUT /groups/{id} с телом {name} (id в пути, не в теле);
 *  - remove → DELETE /groups/{id} → 204, void;
 *  - getStudents → GET /groups/{id}/students?page=N → PagedResult<StudentDto>;
 *  - возвращаемые типы идентичны прежним (сигнатуры сервиса не менялись);
 *  - отказы 403/400+errors/409/404 — реджект ApiError той же формы
 *    (баннеры body.message и полевые errors работают без изменений).
 */
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { authInterceptor } from '../auth-interceptor';
import { API_BASE_URL } from '../api-base-url';
import { ApiError, GroupDto, PagedResult, StudentDto } from '../../shared/models';
import { GROUPS_PAGE_SIZE, GroupsService } from './groups.service';

const GROUP_ID = 'b1111111-1111-4111-8111-111111111111';

const GROUP_221: GroupDto = { id: GROUP_ID, name: 'ИК-221', studentCount: 25 };

const STUDENT: StudentDto = {
  id: 'c0c0c0c0-0000-4000-8000-000000000001',
  fullName: 'Иванов Иван Иванович 01',
  login: 'student01',
  email: 'student01@example.com',
  groupId: GROUP_ID,
  groupName: 'ИК-221',
};

/** Заголовки HTTP-отказов бэкенда. */
function status(status: number, statusText: string): { status: number; statusText: string } {
  return { status, statusText };
}

describe('GroupsService — домен групп поверх HttpClient (FR-091)', () => {
  let httpMock: HttpTestingController;
  let apiBase: string;
  let service: GroupsService;

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
    service = TestBed.inject(GroupsService);
  });

  afterEach(() => {
    // Ни одного незакрытого/лишнего запроса.
    httpMock.verify();
  });

  describe('контракт вызовов (AC, HttpTestingController)', () => {
    it('getList: GET /groups → GroupDto[] с вычисленным studentCount', async () => {
      const pending = service.getList();

      const request = httpMock.expectOne(`${apiBase}/groups`);
      expect(request.request.method).toBe('GET');
      expect(request.request.body).withContext('у GET тела нет').toBeNull();
      expect(request.request.withCredentials)
        .withContext('cookie-аутентификация — withCredentials (FR-091)')
        .toBeTrue();
      request.flush([GROUP_221]);

      await expectAsync(pending).toBeResolvedTo([GROUP_221]);
    });

    it('create: POST /groups с телом {name} → 201 GroupDto', async () => {
      const pending = service.create('ИК-224');

      const request = httpMock.expectOne(`${apiBase}/groups`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).withContext('тело — ровно {name}').toEqual({ name: 'ИК-224' });
      const created: GroupDto = { id: GROUP_ID, name: 'ИК-224', studentCount: 0 };
      request.flush(created, { status: 201, statusText: 'Created' });

      await expectAsync(pending).toBeResolvedTo(created);
    });

    it('rename: PUT /groups/{id} — id в пути, тело ровно {name} → GroupDto', async () => {
      const pending = service.rename(GROUP_ID, 'ИК-222');

      const request = httpMock.expectOne(`${apiBase}/groups/${GROUP_ID}`);
      expect(request.request.method).toBe('PUT');
      expect(request.request.body)
        .withContext('id уходит в путь, не в тело')
        .toEqual({ name: 'ИК-222' });
      const renamed: GroupDto = { id: GROUP_ID, name: 'ИК-222', studentCount: 25 };
      request.flush(renamed);

      await expectAsync(pending).toBeResolvedTo(renamed);
    });

    it('remove: DELETE /groups/{id} → 204, void', async () => {
      const pending = service.remove(GROUP_ID);

      const request = httpMock.expectOne(`${apiBase}/groups/${GROUP_ID}`);
      expect(request.request.method).toBe('DELETE');
      expect(request.request.body).toBeNull();
      request.flush(null, status(204, 'No Content'));

      await expectAsync(pending).toBeResolved();
    });

    it('getStudents: GET /groups/{id}/students?page=2 → PagedResult<StudentDto>', async () => {
      const page: PagedResult<StudentDto> = {
        items: [STUDENT],
        total: 12,
        page: 2,
        pageSize: GROUPS_PAGE_SIZE,
      };
      const pending = service.getStudents(GROUP_ID, 2);

      const request = httpMock.expectOne(`${apiBase}/groups/${GROUP_ID}/students?page=2`);
      expect(request.request.method).toBe('GET');
      expect(request.request.body).toBeNull();
      request.flush(page);

      await expectAsync(pending).toBeResolvedTo(page);
    });

    it('константа контракта: GROUPS_PAGE_SIZE=10 (ADR-109)', () => {
      expect(GROUPS_PAGE_SIZE).toBe(10);
    });
  });

  describe('нормализация отказов (ApiError, баннеры страниц без изменений)', () => {
    it('getList: 403 «Доступ запрещён» студенту → ApiError {status, body:{message}}', async () => {
      const pending = service.getList();
      httpMock.expectOne(`${apiBase}/groups`).flush(
        { message: 'Доступ запрещён' },
        status(403, 'Forbidden'),
      );

      await expectAsync(pending).toBeRejectedWith({
        status: 403,
        body: { message: 'Доступ запрещён' },
      } satisfies ApiError);
    });

    it('create: 400 «Данные заполнены неверно» с errors {name} → ApiError переносит errors по полям', async () => {
      const pending = service.create('   ');
      const errors = { name: ['Название группы должно быть от 1 до 100 символов'] };
      httpMock.expectOne(`${apiBase}/groups`).flush(
        { message: 'Данные заполнены неверно', errors },
        status(400, 'Bad Request'),
      );

      await expectAsync(pending).toBeRejectedWith({
        status: 400,
        body: { message: 'Данные заполнены неверно', errors },
      } satisfies ApiError);
    });

    it('create: 409 «Группа с таким названием уже существует» → ApiError с дословным message', async () => {
      const pending = service.create('ик-221');
      httpMock.expectOne(`${apiBase}/groups`).flush(
        { message: 'Группа с таким названием уже существует' },
        status(409, 'Conflict'),
      );

      await expectAsync(pending).toBeRejectedWith({
        status: 409,
        body: { message: 'Группа с таким названием уже существует' },
      } satisfies ApiError);
    });

    it('rename: 404 «Группа не найдена» → ApiError', async () => {
      const pending = service.rename(GROUP_ID, 'ИК-222');
      httpMock.expectOne(`${apiBase}/groups/${GROUP_ID}`).flush(
        { message: 'Группа не найдена' },
        status(404, 'Not Found'),
      );

      await expectAsync(pending).toBeRejectedWith({
        status: 404,
        body: { message: 'Группа не найдена' },
      } satisfies ApiError);
    });

    it('remove: 404 «Группа не найдена» → ApiError', async () => {
      const pending = service.remove(GROUP_ID);
      httpMock.expectOne(`${apiBase}/groups/${GROUP_ID}`).flush(
        { message: 'Группа не найдена' },
        status(404, 'Not Found'),
      );

      await expectAsync(pending).toBeRejectedWith({
        status: 404,
        body: { message: 'Группа не найдена' },
      } satisfies ApiError);
    });

    it('getStudents: 404 «Группа не найдена» → ApiError', async () => {
      const pending = service.getStudents(GROUP_ID, 1);
      httpMock.expectOne(`${apiBase}/groups/${GROUP_ID}/students?page=1`).flush(
        { message: 'Группа не найдена' },
        status(404, 'Not Found'),
      );

      await expectAsync(pending).toBeRejectedWith({
        status: 404,
        body: { message: 'Группа не найдена' },
      } satisfies ApiError);
    });
  });
});
