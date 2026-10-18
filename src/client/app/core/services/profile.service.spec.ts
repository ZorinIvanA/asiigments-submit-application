/**
 * Юнит-тесты ProfileService (C-014, IF-013, FR-091) — все вызовы через
 * HttpTestingController (конвенция URL — ADR-014: ожидаемые URL строятся из
 * TestBed.inject(API_BASE_URL), литералы префикса запрещены).
 * Производственная цепочка — реальный authInterceptor (нормализация отказов
 * в ApiError, withCredentials):
 *  - get() → GET /me/profile → ProfileDto;
 *  - update() → PUT /me/profile {fullName, email} → обновлённый ProfileDto;
 *  - changePassword() → PUT /me/password {currentPassword, password,
 *    confirmPassword} → 204, void;
 *  - возвращаемые типы идентичны прежним; отказы 400 {message, errors} /
 *    409 {message} — реджект ApiError той же формы (баннеры body.message и
 *    полевые errors работают без изменений).
 */
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { authInterceptor } from '../auth-interceptor';
import { API_BASE_URL } from '../api-base-url';
import { ProfileDto } from '../../shared/models';
import { ProfileChangePasswordInput, ProfileService, ProfileUpdateInput } from './profile.service';

const STUDENT_PROFILE: ProfileDto = {
  login: 'student01',
  email: 'student01@example.com',
  fullName: 'Иванов Иван Иванович 01',
  role: 'student',
  groupName: 'ИК-221',
};

describe('ProfileService — профиль поверх HttpClient (IF-013, FR-091)', () => {
  let httpMock: HttpTestingController;
  let apiBase: string;
  let service: ProfileService;

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
    service = TestBed.inject(ProfileService);
  });

  afterEach(() => {
    // Ни одного незакрытого/лишнего запроса.
    httpMock.verify();
  });

  it('get(): GET /me/profile → ProfileDto', async () => {
    const pending = service.get();

    const request = httpMock.expectOne(`${apiBase}/me/profile`);
    expect(request.request.method).toBe('GET');
    expect(request.request.body).withContext('у GET тела нет').toBeNull();
    expect(request.request.withCredentials)
      .withContext('cookie-аутентификация — withCredentials (FR-091)')
      .toBeTrue();
    request.flush(STUDENT_PROFILE);

    await expectAsync(pending).toBeResolvedTo(STUDENT_PROFILE);
  });

  it('update(): PUT /me/profile с телом {fullName, email} → обновлённый ProfileDto', async () => {
    const input: ProfileUpdateInput = { fullName: 'Петров Пётр Петрович', email: 'petr@example.com' };
    const updated: ProfileDto = {
      login: 'student01',
      email: input.email,
      fullName: input.fullName,
      role: 'student',
      groupName: 'ИК-221',
    };
    const pending = service.update(input);

    const request = httpMock.expectOne(`${apiBase}/me/profile`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual(input);
    request.flush(updated);

    await expectAsync(pending).toBeResolvedTo(updated);
  });

  it('changePassword(): PUT /me/password с телом {currentPassword, password, confirmPassword} → void (204)', async () => {
    const input: ProfileChangePasswordInput = {
      currentPassword: 'Student#2026',
      password: 'NewPass#2027',
      confirmPassword: 'NewPass#2027',
    };
    const pending = service.changePassword(input);

    const request = httpMock.expectOne(`${apiBase}/me/password`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual(input);
    request.flush(null, { status: 204, statusText: 'No Content' });

    await expectAsync(pending).toBeResolved();
  });

  it('отказ 409 {message} → реджект ApiError {status: 409, body: {message}} (AC)', async () => {
    const pending = service.update({ fullName: 'Иванов Иван Иванович 01', email: 'TEACHER@example.com' });
    httpMock.expectOne(`${apiBase}/me/profile`).flush(
      { message: 'Пользователь с таким email уже существует' },
      { status: 409, statusText: 'Conflict' },
    );

    await expectAsync(pending).toBeRejectedWith({
      status: 409,
      body: { message: 'Пользователь с таким email уже существует' },
    });
  });

  it('отказ 400 «Неверный текущий пароль» (без errors) → ApiError с дословным message', async () => {
    const pending = service.changePassword({
      currentPassword: 'не-текущий',
      password: 'NewPass#2027',
      confirmPassword: 'NewPass#2027',
    });
    httpMock.expectOne(`${apiBase}/me/password`).flush(
      { message: 'Неверный текущий пароль' },
      { status: 400, statusText: 'Bad Request' },
    );

    await expectAsync(pending).toBeRejectedWith({
      status: 400,
      body: { message: 'Неверный текущий пароль' },
    });
  });

  it('отказ 400 «Данные заполнены неверно» с errors → ApiError переносит errors по полям', async () => {
    const pending = service.changePassword({
      currentPassword: 'Student#2026',
      password: 'abcdefgh',
      confirmPassword: 'другой',
    });
    httpMock.expectOne(`${apiBase}/me/password`).flush(
      {
        message: 'Данные заполнены неверно',
        errors: {
          password: ['Пароль должен содержать хотя бы одну цифру'],
          confirmPassword: ['Пароли не совпадают'],
        },
      },
      { status: 400, statusText: 'Bad Request' },
    );

    await expectAsync(pending).toBeRejectedWith({
      status: 400,
      body: {
        message: 'Данные заполнены неверно',
        errors: {
          password: ['Пароль должен содержать хотя бы одну цифру'],
          confirmPassword: ['Пароли не совпадают'],
        },
      },
    });
  });
});
