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
 *         — страницы показывают баннер без изменений
 *         (существующий http-errors.ts в производственной цепочке
 *         authInterceptor).
 *
 * Реальные компоненты: ProfileService + authInterceptor + http-errors.
 * Конвенция URL — ADR-014 (URL из TestBed.inject(API_BASE_URL)).
 */
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { authInterceptor } from '../../../src/client/app/core/auth-interceptor';
import { API_BASE_URL } from '../../../src/client/app/core/api-base-url';
import { ProfileService } from '../../../src/client/app/core/services/profile.service';
import { ApiError } from '../../../src/client/app/shared/models';

describe('TS-174: нормализация HTTP-ошибок в ApiError (FR-091)', () => {
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
