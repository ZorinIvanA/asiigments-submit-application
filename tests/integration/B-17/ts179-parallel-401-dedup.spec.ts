/**
 * TS-179 (P1, FR-092): параллельные 401 — дедуплицированный одиночный
 * refresh («однократный дедуплицированный POST /auth/refresh»).
 *
 * given — access просрочен; параллельно уходят 3 API-запроса вне
 *         auth-семейства, все получают 401; refresh успешен;
 * when  — интерцептор обрабатывает три 401;
 * then  — ровно один POST /auth/refresh (базовый префикс API; дедупликация;
 *         expectOne падает при нуле или нескольких запросах); каждый из
 *         трёх запросов повторён по одному разу и доводит данные до
 *         потребителя; сессия не сбрасывается, навигации нет.
 *
 * Реальные компоненты: authInterceptor + RefreshCoordinator + SessionLifecycle.
 * Конвенция URL — ADR-014 (URL из TestBed.inject(API_BASE_URL)).
 */
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { firstValueFrom, forkJoin } from 'rxjs';

import { API_BASE_URL } from '../../../src/client/app/core/api-base-url';
import { authInterceptor } from '../../../src/client/app/core/auth-interceptor';
import { SessionLifecycle } from '../../../src/client/app/core/session-lifecycle';

const UNAUTHORIZED = { status: 401, statusText: 'Unauthorized' };
const BODY_401 = { message: 'Не авторизован' };

describe('TS-179: параллельные 401 — дедуплицированный одиночный refresh (FR-092)', () => {
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
    // Пользователь на экране: фаза инициализации завершена.
    lifecycle.markInitDone();
    spyOn(router, 'navigate');
  });

  afterEach(() => {
    // Все повторы закрыты, лишних запросов (второй refresh, /auth/logout) нет.
    httpMock.verify();
  });

  it('три параллельных 401 → ровно один POST /auth/refresh, каждый запрос повторён по одному разу', async () => {
    // given: три параллельных API-запроса вне auth-семейства.
    const pending = firstValueFrom(
      forkJoin([
        http.get(`${apiBase}/labs`),
        http.get(`${apiBase}/groups`),
        http.get(`${apiBase}/students`),
      ]),
    );

    // Все три исходных запроса уходят и отвечают 401 (access просрочен).
    for (const path of ['/labs', '/groups', '/students']) {
      const request = httpMock.expectOne(`${apiBase}${path}`);
      expect(request.request.method).toBe('GET');
      request.flush(BODY_401, UNAUTHORIZED);
    }

    // when/then: дедупликация — ровно один refresh на все три 401
    // (expectOne бросает ошибку при нуле или нескольких совпадениях).
    const refresh = httpMock.expectOne(`${apiBase}/auth/refresh`);
    expect(refresh.request.method)
      .withContext('один общий POST /auth/refresh на три 401')
      .toBe('POST');
    expect(refresh.request.withCredentials).withContext('cookie на refresh').toBeTrue();
    refresh.flush(null); // refresh успешен.

    // then: каждый из трёх запросов повторён по одному разу и успешен.
    for (const path of ['/labs', '/groups', '/students']) {
      const retry = httpMock.expectOne(`${apiBase}${path}`);
      expect(retry.request.method).toBe('GET');
      retry.flush({ items: [], total: 0, page: 1, pageSize: 10 });
    }

    const results = await pending;
    expect(results.length).withContext('все три запроса доведены до потребителей').toBe(3);
    // Сессия не сбрасывалась, навигации нет.
    expect(sessionExpiredEvents).withContext('без события недействительной сессии').toBe(0);
    expect(router.navigate).not.toHaveBeenCalled();
  });
});
