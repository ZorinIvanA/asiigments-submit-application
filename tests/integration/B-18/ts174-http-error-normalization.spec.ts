/**
 * TS-174 (P0, FR-091): нормализация HTTP-ошибок в ApiError.
 *
 * given — HttpTestingController эмулирует ответ 409
 *         {message:'Пользователь с таким email уже существует'}
 *         (DUPLICATE_EMAIL контракта IF-013);
 * when  — вызов метода сервиса, отвергающегося этим ответом
 *         (ProfileService.update → PUT /me/profile);
 * then  — сервис отвергается ApiError {status:409,
 *         body:{message:'Пользователь с таким email уже существует'}}
 *         — страницы показывают баннер без изменений (нормализация —
 *         normalizeHttpError из http-errors.ts в цепочке HttpClient;
 *         аутентификации в приложении нет: authInterceptor и сессии
 *         удалены, интерцептор нормализации ошибок подключён в спеке
 *         как производственная цепочка normalizeHttpError).
 *
 * Реальные компоненты: ProfileService + normalizeHttpError (http-errors).
 * Конвенция URL — ADR-014 (URL из TestBed.inject(API_BASE_URL)).
 */
import {
  HttpInterceptorFn,
  provideHttpClient,
  withInterceptors,
} from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { catchError, throwError } from 'rxjs';

import { API_BASE_URL } from '../../../src/client/app/core/api-base-url';
import { normalizeHttpError } from '../../../src/client/app/core/http-errors';
import { ProfileService } from '../../../src/client/app/core/services/profile.service';
import { ApiError } from '../../../src/client/app/shared/models';

/**
 * Интерцептор нормализации ошибок (без auth-веток): тот же
 * normalizeHttpError, что был в производственной цепочке authInterceptor;
 * идемпотентен для готовых ApiError — сервисные нормализации не ломаются.
 */
const httpErrorInterceptor: HttpInterceptorFn = (req, next) =>
  next(req).pipe(catchError((error: unknown) => throwError(() => normalizeHttpError(error))));

describe('TS-174: нормализация HTTP-ошибок в ApiError (FR-091)', () => {
  let httpMock: HttpTestingController;
  let apiBase: string;
  let service: ProfileService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([httpErrorInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
    apiBase = TestBed.inject(API_BASE_URL);
    service = TestBed.inject(ProfileService);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('ProfileService.update: 409 {message} → реджект ApiError {status:409, body:{message}}', async () => {
    // when: вызов метода сервиса, отвергающегося эмулированным 409.
    const pending = service.update({
      fullName: 'Иванов Иван Иванович',
      email: 'ivanov@example.com',
    });

    const request = httpMock.expectOne(`${apiBase}/me/profile`);
    expect(request.request.method).toBe('PUT');
    request.flush(
      { message: 'Пользователь с таким email уже существует' },
      { status: 409, statusText: 'Conflict' },
    );

    // then: сервис отвергается ApiError той же формы — баннеры страниц
    // (body.message) работают без изменений.
    await expectAsync(pending).toBeRejectedWith({
      status: 409,
      body: { message: 'Пользователь с таким email уже существует' },
    } satisfies ApiError);
  });
});
