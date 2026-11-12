/**
 * TS-173 (P0, FR-091): сервисы core — HTTP-вызовы по контрактам (метод,
 * URL с query, тело).
 *
 * given — юнит-контекст шести сервисов (auth, labs, groups, students,
 *         submissions, profile) на provideHttpClientTesting/
 *         HttpTestingController с производственным authInterceptor
 *         (withCredentials выставляется им);
 * when  — вызовы всех публичных методов сервисов; проверка ожиданий
 *         HttpTestingController;
 * then  — каждый вызов порождает ожидаемые метод+URL+тело:
 *         LabsService.getList({semester:1, page:2, sortField:'number',
 *         sortDir:'desc'}) → GET /api/v1/labs?semester=1&page=2&
 *         sortField=number&sortDir=desc; SubmissionsService.update →
 *         PUT /api/v1/submissions; auth-методы → POST /api/v1/auth/login
 *         и т.д.; все запросы с withCredentials; возвращаемые Promise-типы
 *         DTO идентичны прежним (значение промиса — прочитанный DTO).
 *
 * Реальные компоненты: шесть сервисов core + authInterceptor (C-014/C-013).
 * Конвенция URL — ADR-014: ожидаемые URL строятся из
 * TestBed.inject(API_BASE_URL), литералы префикса в spec запрещены.
 */
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
  TestRequest,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { authInterceptor } from '../../../src/client/app/core/auth-interceptor';
import { API_BASE_URL } from '../../../src/client/app/core/api-base-url';
import { AuthService } from '../../../src/client/app/core/services/auth.service';
import { GroupsService } from '../../../src/client/app/core/services/groups.service';
import { LabsService } from '../../../src/client/app/core/services/labs.service';
import { ProfileService } from '../../../src/client/app/core/services/profile.service';
import {
  StudentsService,
} from '../../../src/client/app/core/services/students.service';
import {
  SubmissionUpdateParams,
  SubmissionsService,
} from '../../../src/client/app/core/services/submissions.service';
import { SessionLifecycle } from '../../../src/client/app/core/session-lifecycle';
import {
  ConfirmRecoveryResult,
  GroupDto,
  LabDto,
  LabInput,
  LoginParams,
  MeDto,
  MySubmissionsDto,
  PagedResult,
  ProfileDto,
  RegisterParams,
  StudentDto,
  Submission,
  SubmissionsGridDto,
} from '../../../src/client/app/shared/models';

/** Заголовки HTTP-ответов бэкенда. */
function status(status: number, statusText: string): { status: number; statusText: string } {
  return { status, statusText };
}

/**
 * expectOne с обязательной проверкой withCredentials — контракт FR-091
 * «все запросы с withCredentials» (интерцептор выставляет признак).
 */
function expectApiWithCredentials(
  httpMock: HttpTestingController,
  url: string,
): TestRequest {
  const request = httpMock.expectOne(url);
  expect(request.request.withCredentials)
    .withContext('все запросы API — withCredentials: true (FR-091)')
    .toBeTrue();
  return request;
}

/** Идентификаторы фикстур — uuid-форма (IF-001). */
const LAB_ID = 'bbbbbbbb-0000-4000-8000-000000000001';
const GROUP_ID = 'b1111111-1111-4111-8111-111111111111';
const STUDENT_ID = 'cccccccc-0000-4000-8000-000000000001';

const ME: MeDto = {
  login: 'student01',
  fullName: 'Иванов Иван Иванович',
  role: 'student',
  groupName: 'А-01',
};

const LOGIN_PARAMS: LoginParams = { login: 'student01', password: 'Pa$$word123' };

const REGISTER_PARAMS: RegisterParams = {
  fullName: 'Иванов Иван Иванович',
  login: 'student01',
  email: 'student01@example.com',
  password: 'Pa$$word123',
  repeatPassword: 'Pa$$word123',
};

const LAB: LabDto = {
  id: LAB_ID,
  semester: 1,
  number: 21,
  content: 'Содержание работы',
  assignmentUrl: null,
  defenseRequired: false,
};

const labInput = (): LabInput => ({
  number: 21,
  semester: 1,
  content: 'Содержание работы',
  assignmentUrl: null,
  defenseRequired: false,
});

const GROUP: GroupDto = { id: GROUP_ID, name: 'А-01', studentCount: 3 };

const STUDENT: StudentDto = {
  id: STUDENT_ID,
  fullName: 'Иванов Иван Иванович',
  login: 'student01',
  email: 'student01@example.com',
  groupId: GROUP_ID,
  groupName: 'А-01',
};

const GRID: SubmissionsGridDto & { page: number } = {
  students: [{ id: STUDENT_ID, fullName: 'Иванов Иван Иванович' }],
  labs: [{ id: LAB_ID, number: 21, defenseRequired: false }],
  submissions: [
    { studentId: STUDENT_ID, labId: LAB_ID, submitDate: '2026-10-01', defenseDate: null },
  ],
  total: 1,
  page: 2,
};

const SUBMISSION: Submission = {
  id: 'dddddddd-0000-4000-8000-000000000001',
  studentId: STUDENT_ID,
  labId: LAB_ID,
  submitDate: '2026-10-01',
  defenseDate: null,
  updatedAt: '2026-10-01T10:00:00Z',
  updatedBy: null,
};

const MY_SUBMISSIONS: MySubmissionsDto = {
  hasGroup: true,
  labs: [{ id: LAB_ID, number: 21, defenseRequired: false }],
  submissions: [{ labId: LAB_ID, submitDate: '2026-10-01', defenseDate: null }],
};

const PROFILE: ProfileDto = {
  login: 'student01',
  email: 'student01@example.com',
  fullName: 'Иванов Иван Иванович',
  role: 'student',
  groupName: 'А-01',
};

describe('TS-173: сервисы core — HTTP-вызовы по контрактам (FR-091)', () => {
  let httpMock: HttpTestingController;
  let apiBase: string;

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
  });

  afterEach(() => {
    // Ни одного незакрытого/лишнего запроса.
    httpMock.verify();
  });

  describe('AuthService (IF-007)', () => {
    let service: AuthService;
    let lifecycle: SessionLifecycle;

    beforeEach(() => {
      service = TestBed.inject(AuthService);
      lifecycle = TestBed.inject(SessionLifecycle);
    });

    it('login → POST /auth/login (тело LoginParams), Promise разрешается MeDto', async () => {
      const pending = service.login(LOGIN_PARAMS);

      const request = expectApiWithCredentials(httpMock, `${apiBase}/auth/login`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual(LOGIN_PARAMS);
      request.flush(ME);

      await expectAsync(pending).toBeResolvedTo(ME);
    });

    it('register → POST /auth/register (тело RegisterParams), Promise разрешается MeDto', async () => {
      const pending = service.register(REGISTER_PARAMS);

      const request = expectApiWithCredentials(httpMock, `${apiBase}/auth/register`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual(REGISTER_PARAMS);
      request.flush(ME, { status: 201, statusText: 'Created' });

      await expectAsync(pending).toBeResolvedTo(ME);
    });

    it('logout → POST /auth/logout без тела, Promise разрешается', async () => {
      const pending = service.logout();

      const request = expectApiWithCredentials(httpMock, `${apiBase}/auth/logout`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).withContext('тело logout отсутствует').toBeNull();
      request.flush(null, status(204, 'No Content'));

      await expectAsync(pending).toBeResolved();
    });

    it('loadMe → GET /auth/me, Promise разрешается MeDto', async () => {
      const pending = service.loadMe();

      const request = expectApiWithCredentials(httpMock, `${apiBase}/auth/me`);
      expect(request.request.method).toBe('GET');
      expect(request.request.body).toBeNull();
      request.flush(ME);

      await expectAsync(pending).toBeResolvedTo(ME);
    });

    it('initSession → GET /auth/me, фаза инициализации завершена (initializing=false)', async () => {
      const pending = service.initSession();

      const request = expectApiWithCredentials(httpMock, `${apiBase}/auth/me`);
      expect(request.request.method).toBe('GET');
      request.flush(ME);
      await pending;

      expect(lifecycle.initializing)
        .withContext('initSession завершает фазу инициализации markInitDone()')
        .toBeFalse();
    });

    it('requestRecoveryCode → POST /auth/recovery/request {email}', async () => {
      const pending = service.requestRecoveryCode('student01@example.com');

      const request = expectApiWithCredentials(httpMock, `${apiBase}/auth/recovery/request`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ email: 'student01@example.com' });
      request.flush(null);

      await expectAsync(pending).toBeResolved();
    });

    it('confirmRecoveryCode → POST /auth/recovery/confirm {email, code} → {resetToken}', async () => {
      const pending = service.confirmRecoveryCode('student01@example.com', '123456');

      const request = expectApiWithCredentials(httpMock, `${apiBase}/auth/recovery/confirm`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ email: 'student01@example.com', code: '123456' });
      const result: ConfirmRecoveryResult = { resetToken: 'reset-token-1' };
      request.flush(result);

      await expectAsync(pending).toBeResolvedTo(result);
    });

    it('resetPassword → POST /auth/reset-password {resetToken, password, confirmPassword}', async () => {
      const pending = service.resetPassword('reset-token-1', 'NewPa$$word1', 'NewPa$$word1');

      const request = expectApiWithCredentials(httpMock, `${apiBase}/auth/reset-password`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({
        resetToken: 'reset-token-1',
        password: 'NewPa$$word1',
        confirmPassword: 'NewPa$$word1',
      });
      request.flush(null, status(204, 'No Content'));

      await expectAsync(pending).toBeResolved();
    });

    it('isAuthenticated — синхронный метод без HTTP-вызова', () => {
      expect(service.isAuthenticated()).toBeFalse();
    });
  });

  describe('LabsService (IF-103)', () => {
    let service: LabsService;

    beforeEach(() => {
      service = TestBed.inject(LabsService);
    });

    it('getList({semester:1, page:2, sortField:number, sortDir:desc}) → GET /labs с query побайтово (AC)', async () => {
      const page: PagedResult<LabDto> = {
        items: [LAB],
        total: 11,
        page: 2,
        pageSize: 10,
      };
      const pending = service.getList({
        semester: 1,
        page: 2,
        sortField: 'number',
        sortDir: 'desc',
      });

      const request = expectApiWithCredentials(
        httpMock,
        `${apiBase}/labs?semester=1&page=2&sortField=number&sortDir=desc`,
      );
      expect(request.request.method).toBe('GET');
      expect(request.request.body).toBeNull();
      request.flush(page);

      await expectAsync(pending).toBeResolvedTo(page);
    });

    it('getById(id) → GET /labs/{id} → LabDto', async () => {
      const pending = service.getById(LAB_ID);

      const request = expectApiWithCredentials(httpMock, `${apiBase}/labs/${LAB_ID}`);
      expect(request.request.method).toBe('GET');
      request.flush(LAB);

      await expectAsync(pending).toBeResolvedTo(LAB);
    });

    it('create(input) → POST /labs (тело LabInput) → LabDto', async () => {
      const input = labInput();
      const pending = service.create(input);

      const request = expectApiWithCredentials(httpMock, `${apiBase}/labs`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual(input);
      request.flush(LAB, { status: 201, statusText: 'Created' });

      await expectAsync(pending).toBeResolvedTo(LAB);
    });

    it('update(id, input) → PUT /labs/{id} (id в пути, тело LabInput) → LabDto', async () => {
      const input = labInput();
      const pending = service.update(LAB_ID, input);

      const request = expectApiWithCredentials(httpMock, `${apiBase}/labs/${LAB_ID}`);
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).withContext('id уходит в путь, не в тело').toEqual(input);
      request.flush(LAB);

      await expectAsync(pending).toBeResolvedTo(LAB);
    });

    it('remove(id) → DELETE /labs/{id} → 204, void', async () => {
      const pending = service.remove(LAB_ID);

      const request = expectApiWithCredentials(httpMock, `${apiBase}/labs/${LAB_ID}`);
      expect(request.request.method).toBe('DELETE');
      request.flush(null, status(204, 'No Content'));

      await expectAsync(pending).toBeResolved();
    });

    it('getSemesters() → GET /semesters → number[]', async () => {
      const pending = service.getSemesters();

      const request = expectApiWithCredentials(httpMock, `${apiBase}/semesters`);
      expect(request.request.method).toBe('GET');
      request.flush([1, 3]);

      await expectAsync(pending).toBeResolvedTo([1, 3]);
    });
  });

  describe('GroupsService (IF-104)', () => {
    let service: GroupsService;

    beforeEach(() => {
      service = TestBed.inject(GroupsService);
    });

    it('getList() → GET /groups → GroupDto[]', async () => {
      const pending = service.getList();

      const request = expectApiWithCredentials(httpMock, `${apiBase}/groups`);
      expect(request.request.method).toBe('GET');
      request.flush([GROUP]);

      await expectAsync(pending).toBeResolvedTo([GROUP]);
    });

    it('create(name) → POST /groups {name} → GroupDto', async () => {
      const pending = service.create('А-01');

      const request = expectApiWithCredentials(httpMock, `${apiBase}/groups`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ name: 'А-01' });
      request.flush(GROUP, { status: 201, statusText: 'Created' });

      await expectAsync(pending).toBeResolvedTo(GROUP);
    });

    it('rename(id, name) → PUT /groups/{id} {name} → GroupDto', async () => {
      const pending = service.rename(GROUP_ID, 'Б-02');

      const request = expectApiWithCredentials(httpMock, `${apiBase}/groups/${GROUP_ID}`);
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).toEqual({ name: 'Б-02' });
      request.flush({ ...GROUP, name: 'Б-02' });

      await expectAsync(pending).toBeResolvedTo({ ...GROUP, name: 'Б-02' });
    });

    it('remove(id) → DELETE /groups/{id} → 204, void', async () => {
      const pending = service.remove(GROUP_ID);

      const request = expectApiWithCredentials(httpMock, `${apiBase}/groups/${GROUP_ID}`);
      expect(request.request.method).toBe('DELETE');
      request.flush(null, status(204, 'No Content'));

      await expectAsync(pending).toBeResolved();
    });

    it('getStudents(id, page) → GET /groups/{id}/students?page=N → PagedResult<StudentDto>', async () => {
      const page: PagedResult<StudentDto> = {
        items: [STUDENT],
        total: 3,
        page: 2,
        pageSize: 10,
      };
      const pending = service.getStudents(GROUP_ID, 2);

      const request = expectApiWithCredentials(
        httpMock,
        `${apiBase}/groups/${GROUP_ID}/students?page=2`,
      );
      expect(request.request.method).toBe('GET');
      request.flush(page);

      await expectAsync(pending).toBeResolvedTo(page);
    });
  });

  describe('StudentsService (IF-105)', () => {
    let service: StudentsService;

    beforeEach(() => {
      service = TestBed.inject(StudentsService);
    });

    it('getList({search, groupId, page}) → GET /students c query search/groupId/page', async () => {
      const page: PagedResult<StudentDto> = {
        items: [STUDENT],
        total: 3,
        page: 2,
        pageSize: 10,
      };
      const pending = service.getList({ search: 'ivanov', groupId: 'none', page: 2 });

      // Порядок параметров query контрактом не зафиксирован — проверяем
      // состав и значения query по параметрам запроса.
      const request = httpMock.expectOne((req) => req.url === `${apiBase}/students`);
      expect(request.request.method).toBe('GET');
      expect(request.request.withCredentials)
        .withContext('все запросы API — withCredentials: true (FR-091)')
        .toBeTrue();
      expect(request.request.params.get('search')).toBe('ivanov');
      expect(request.request.params.get('groupId')).toBe('none');
      expect(request.request.params.get('page')).toBe('2');
      request.flush(page);

      await expectAsync(pending).toBeResolvedTo(page);
    });

    it('setGroup(studentId, groupId) → PUT /students/{id}/group {groupId} → 204', async () => {
      const pending = service.setGroup(STUDENT_ID, GROUP_ID);

      const request = expectApiWithCredentials(
        httpMock,
        `${apiBase}/students/${STUDENT_ID}/group`,
      );
      expect(request.request.method).toBe('PUT');
      expect(request.request.body)
        .withContext('id студента — в пути, тело несёт только groupId')
        .toEqual({ groupId: GROUP_ID });
      request.flush(null, status(204, 'No Content'));

      await expectAsync(pending).toBeResolved();
    });
  });

  describe('SubmissionsService (IF-106)', () => {
    let service: SubmissionsService;

    beforeEach(() => {
      service = TestBed.inject(SubmissionsService);
    });

    it('getGrid({groupId, semester, page}) → GET /submissions c query groupId/semester/page', async () => {
      const pending = service.getGrid({ groupId: GROUP_ID, semester: 1, page: 2 });

      const request = httpMock.expectOne((req) => req.url === `${apiBase}/submissions`);
      expect(request.request.method).toBe('GET');
      expect(request.request.withCredentials)
        .withContext('все запросы API — withCredentials: true (FR-091)')
        .toBeTrue();
      expect(request.request.params.get('groupId')).toBe(GROUP_ID);
      expect(request.request.params.get('semester')).toBe('1');
      expect(request.request.params.get('page')).toBe('2');
      request.flush(GRID);

      await expectAsync(pending).toBeResolvedTo(GRID);
    });

    it('update(params) → PUT /submissions (тело SubmissionUpdateParams) → Submission (AC)', async () => {
      const params: SubmissionUpdateParams = {
        studentId: STUDENT_ID,
        labId: LAB_ID,
        submitDate: '2026-10-01',
        defenseDate: null,
      };
      const pending = service.update(params);

      const request = expectApiWithCredentials(httpMock, `${apiBase}/submissions`);
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).toEqual(params);
      request.flush(SUBMISSION);

      await expectAsync(pending).toBeResolvedTo(SUBMISSION);
    });

    it('getMy(semester) → GET /me/submissions?semester=N → MySubmissionsDto', async () => {
      const pending = service.getMy(3);

      const request = expectApiWithCredentials(
        httpMock,
        `${apiBase}/me/submissions?semester=3`,
      );
      expect(request.request.method).toBe('GET');
      request.flush(MY_SUBMISSIONS);

      await expectAsync(pending).toBeResolvedTo(MY_SUBMISSIONS);
    });
  });

  describe('ProfileService (IF-013)', () => {
    let service: ProfileService;

    beforeEach(() => {
      service = TestBed.inject(ProfileService);
    });

    it('get() → GET /me/profile → ProfileDto', async () => {
      const pending = service.get();

      const request = expectApiWithCredentials(httpMock, `${apiBase}/me/profile`);
      expect(request.request.method).toBe('GET');
      request.flush(PROFILE);

      await expectAsync(pending).toBeResolvedTo(PROFILE);
    });

    it('update({fullName, email}) → PUT /me/profile {fullName, email} → ProfileDto', async () => {
      const input = { fullName: 'Иванов Иван Иванович', email: 'ivanov@example.com' };
      const pending = service.update(input);

      const request = expectApiWithCredentials(httpMock, `${apiBase}/me/profile`);
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).toEqual(input);
      request.flush(PROFILE);

      await expectAsync(pending).toBeResolvedTo(PROFILE);
    });

    it('changePassword({currentPassword, password, confirmPassword}) → PUT /me/password → 204', async () => {
      const input = {
        currentPassword: 'OldPa$$word1',
        password: 'NewPa$$word1',
        confirmPassword: 'NewPa$$word1',
      };
      const pending = service.changePassword(input);

      const request = expectApiWithCredentials(httpMock, `${apiBase}/me/password`);
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).toEqual(input);
      request.flush(null, status(204, 'No Content'));

      await expectAsync(pending).toBeResolved();
    });
  });
});
