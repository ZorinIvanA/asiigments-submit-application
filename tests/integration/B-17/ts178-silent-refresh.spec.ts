/**
 * TS-178 (P0, FR-092 AC «Тихое обновление токена»): интерцептор обрабатывает
 * 401 API-запроса вне auth-семейства при просроченном access и действительном
 * refresh.
 *
 * given — access просрочен, refresh действителен; пользователь на экране
 *         (фаза инициализации завершена); выполняется API-запрос вне
 *         auth-семейства, отвечающий 401; refresh эмулируется 200;
 * when  — интерцептор обрабатывает 401;
 * then  — ровно один POST /auth/refresh (с базовым префиксом API; expectOne
 *         падает при нуле или нескольких); исходный запрос повторен один
 *         раз и завершается успехом; пользователь остаётся на экране.
 *         Ветка кейса про неудачу: при неудаче refresh — ровно одно событие
 *         недействительной сессии (sessionExpired$) и редирект /login,
 *         исходный запрос не повторяется.
 *
 * Реальные компоненты: authInterceptor (цепочка provideHttpClient) +
 * SessionLifecycle + Router через TestBed; HTTP — HttpTestingController.
 * Конвенция URL — ADR-014 (URL из TestBed.inject(API_BASE_URL)).
 */
import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { API_BASE_URL } from '../../../src/client/app/core/api-base-url';
import { authInterceptor } from '../../../src/client/app/core/auth-interceptor';
import { SessionLifecycle } from '../../../src/client/app/core/session-lifecycle';

const UNAUTHORIZED = { status: 401, statusText: 'Unauthorized' };
const BODY_401 = { message: 'Не авторизован' };

describe('TS-178: тихое обновление токена — один refresh и один повтор (FR-092)', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let router: Router;
  let lifecycle: SessionLifecycle;
  let apiBase: string;
  let sessionExpiredEvents: number;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    lifecycle = TestBed.inject(SessionLifecycle);
    apiBase = TestBed.inject(API_BASE_URL);

    sessionExpiredEvents = 0;
    lifecycle.sessionExpired$.subscribe(() => {
      sessionExpiredEvents += 1;
    });
    spyOn(router, 'navigate');
  });

  afterEach(() => {
    // Ни одного незакрытого/лишнего запроса (в т.ч. второй refresh и
    // POST /auth/logout — сброс сессии без HTTP, IF-014).
    httpMock.verify();
  });

  it('401 → ровно один POST /auth/refresh → исходный запрос повторён один раз и успешен; пользователь остаётся на экране', async () => {
    // given: пользователь на экране — фаза инициализации завершена.
    lifecycle.markInitDone();

    // API-запрос вне auth-семейства.
    const pending = firstValueFrom(http.get(`${apiBase}/labs`));

    // access просрочен — исходный запрос отвечает 401.
    const original = httpMock.expectOne(`${apiBase}/labs`);
    expect(original.request.method).toBe('GET');
    original.flush(BODY_401, UNAUTHORIZED);

    // when/then: ровно один POST /auth/refresh (expectOne падает, если их
    // ноль или больше одного) — с withCredentials (cookie-модель).
    const refresh = httpMock.expectOne(`${apiBase}/auth/refresh`);
    expect(refresh.request.method)
      .withContext('тихое обновление — POST /auth/refresh')
      .toBe('POST');
    expect(refresh.request.withCredentials).withContext('cookie на refresh').toBeTrue();
    refresh.flush(null); // refresh действителен — 200.

    // then: исходный запрос повторён ровно один раз и завершается успехом.
    const retry = httpMock.expectOne(`${apiBase}/labs`);
    expect(retry.request.method).toBe('GET');
    retry.flush({ items: [], total: 0, page: 1, pageSize: 10 });

    await expectAsync(pending).toBeResolved();
    // Пользователь остаётся на экране: навигации нет, сессия не сброшена.
    expect(router.navigate).not.toHaveBeenCalled();
    expect(sessionExpiredEvents)
      .withContext('без события недействительной сессии')
      .toBe(0);
  });

  it('неудача refresh — событие недействительной сессии и редирект /login, исходный запрос не повторяется', async () => {
    lifecycle.markInitDone();

    const pendingError: Promise<unknown> = firstValueFrom(http.get(`${apiBase}/labs`)).then(
      () => null,
      (error: unknown) => error,
    );

    // given: refresh недействителен — 401 исходного → refresh тоже 401.
    httpMock.expectOne(`${apiBase}/labs`).flush(BODY_401, UNAUTHORIZED);
    httpMock.expectOne(`${apiBase}/auth/refresh`).flush(BODY_401, UNAUTHORIZED);

    const error = (await pendingError) as HttpErrorResponse;
    // Потребитель получает нормализованный отказ исходного запроса.
    expect(error.status).withContext('нормализованный ApiError 401').toBe(401);

    // then: событие недействительной сессии (ровно одно на дедуплицированный
    // refresh, IF-014) и редирект /login (фаза инициализации завершена).
    expect(sessionExpiredEvents).withContext('одно событие sessionExpired').toBe(1);
    expect(router.navigate).withContext('редирект /login').toHaveBeenCalledWith(['/login']);

    // Повтора исходного запроса нет (незакрытый повтор не прошёл бы match).
    expect(httpMock.match(`${apiBase}/labs`).length)
      .withContext('исходный запрос не повторяется')
      .toBe(0);
  });
});
