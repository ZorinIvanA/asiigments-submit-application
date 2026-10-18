/**
 * Юнит-тесты authInterceptor (C-013, контракт IF-014, FR-025/FR-026) — все
 * сценарии через HttpTestingController; сброс состояния проверяется
 * подписчиком-стабом на SessionLifecycle.sessionExpired$ (AuthService не
 * используется — сопряжение только через канал, ADR-019):
 *  - AC1 «Refresh и повтор»: 401 от GET labs → POST /auth/refresh
 *    (withCredentials) → повтор исходного ровно один раз;
 *  - AC2 «Неудача refresh — сброс через канал»: исходный не повторяется,
 *    событие sessionExpired$ ровно одно, навигация /login, запросов к
 *    /auth/logout нет (verify);
 *  - AC3 «Фаза инициализации»: initializing=true — событие публикуется,
 *    навигации на /login нет (ADR-016);
 *  - AC4 «Нет цикла»: 3 параллельных 401 при стабильно 401 refresh —
 *    refresh ≤1 раза, повторные 401 новых refresh не порождают;
 *  - AC5 «Исключения»: 401 от /auth/login (и прочих auth-эндпойнтов) —
 *    refresh не вызывается;
 *  - CR-001 / ADR-021 (сброс фиксатора): неудачный refresh не «ломает»
 *    refresh навсегда — публикация sessionRestored (markSessionRestored,
 *    которую AuthService делает после успешных login/register) сбрасывает
 *    фиксатор, и следующий 401 снова инициирует refresh + повтор
 *    («повторный вход без перезагрузки страницы → тихий refresh работает»);
 *    сброс идемпотентен (двойная публикация безвредна); 2xx/409-ответы
 *    API сам по себе фиксатор НЕ сбрасывают — только канал sessionRestored$;
 *  - withCredentials на API-запросах; нормализация отказов в ApiError
 *    {status, body:{message, errors?}} (баннеры body.message, FR-025).
 *
 * Конвенция URL — ADR-014: ожидаемые URL строятся из
 * TestBed.inject(API_BASE_URL); литералы с префиксом API в spec запрещены.
 */
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { API_BASE_URL } from './api-base-url';
import { authInterceptor } from './auth-interceptor';
import { SessionLifecycle } from './session-lifecycle';

describe('authInterceptor — контракт IF-014 (FR-025/FR-026)', () => {
  const UNAUTHORIZED = { status: 401, statusText: 'Unauthorized' };
  const API_401_BODY = { message: 'Не авторизован' };

  let http: HttpClient;
  let httpMock: HttpTestingController;
  let router: Router;
  let lifecycle: SessionLifecycle;
  let apiBase: string;
  /** Подписчик-стаб канала сброса сессии (вместо AuthService, ADR-019). */
  let sessionExpiredEvents: number;

  /** Отвечает 401 с телом «Не авторизован» на единственный pending-запрос. */
  function flushUnauthorized(relativePath: string): void {
    httpMock
      .expectOne(`${apiBase}${relativePath}`)
      .flush(API_401_BODY, UNAUTHORIZED);
  }

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
    // Ни одного незакрытого/лишнего запроса (в т.ч. POST /auth/logout).
    httpMock.verify();
  });

  it('AC1: 401 от GET labs → POST /auth/refresh с withCredentials, затем повтор исходного ровно один раз', async () => {
    lifecycle.markInitDone();

    const pending = firstValueFrom(http.get(`${apiBase}/labs`));

    // Исходный запрос выполняется с withCredentials (cookie-аутентификация).
    const original = httpMock.expectOne(`${apiBase}/labs`);
    expect(original.request.method).toBe('GET');
    expect(original.request.withCredentials).withContext('withCredentials на API-запросе').toBeTrue();
    original.flush(API_401_BODY, UNAUTHORIZED);

    // Дедуплицируемый refresh: POST с withCredentials.
    const refresh = httpMock.expectOne(`${apiBase}/auth/refresh`);
    expect(refresh.request.method).toBe('POST');
    expect(refresh.request.withCredentials).withContext('withCredentials на refresh').toBeTrue();
    refresh.flush(null);

    // Повтор исходного запроса ровно один раз; данные доходят потребителю.
    const retry = httpMock.expectOne(`${apiBase}/labs`);
    expect(retry.request.withCredentials).toBeTrue();
    retry.flush({ items: [], total: 0, page: 1, pageSize: 10 });

    await expectAsync(pending).toBeResolved();
    expect(sessionExpiredEvents).withContext('успешный refresh без сброса сессии').toBe(0);
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('AC2: неудачный refresh — исходный не повторяется, событие sessionExpired$ одно, навигация /login, без /auth/logout', async () => {
    lifecycle.markInitDone();

    const pending = firstValueFrom(http.get(`${apiBase}/labs`));
    flushUnauthorized('/labs');

    httpMock.expectOne(`${apiBase}/auth/refresh`).flush(API_401_BODY, UNAUTHORIZED);

    // Потребитель получает нормализованный ApiError исходного отказа.
    await expectAsync(pending).toBeRejectedWith({ status: 401, body: API_401_BODY });

    // Сброс состояния сессии — ровно одно событие канала (подписчик-стаб).
    expect(sessionExpiredEvents).toBe(1);
    // Редирект на /login вне фазы инициализации.
    expect(router.navigate).toHaveBeenCalledWith(['/login']);
    // verify() (afterEach) гарантирует: повторов исходного нет и запросов к
    // /auth/logout нет — сброс состояния без HTTP-вызовов (ADR-019).
  });

  it('AC3: фаза инициализации (initializing=true) — событие публикуется, навигации на /login нет (ADR-016)', async () => {
    expect(lifecycle.initializing).withContext('markInitDone не вызван').toBeTrue();

    const pending = firstValueFrom(http.get(`${apiBase}/labs`));
    flushUnauthorized('/labs');
    httpMock.expectOne(`${apiBase}/auth/refresh`).flush(API_401_BODY, UNAUTHORIZED);

    await expectAsync(pending).toBeRejectedWith({ status: 401, body: API_401_BODY });

    expect(sessionExpiredEvents).withContext('событие сброса публикуется и в init-фазе').toBe(1);
    expect(router.navigate).withContext('навигация подавлена в фазе инициализации').not.toHaveBeenCalled();
  });

  it('AC4: 3 параллельных 401 при стабильно 401 refresh — refresh ровно один, повторные 401 новых refresh не порождают', async () => {
    lifecycle.markInitDone();

    const pendings = [0, 1, 2].map(() => firstValueFrom(http.get(`${apiBase}/labs`)));

    // Три параллельных исходных запроса, каждый получает 401.
    const originals = httpMock.match(`${apiBase}/labs`);
    expect(originals.length).toBe(3);
    originals.forEach((request) => request.flush(API_401_BODY, UNAUTHORIZED));

    // На все три — один дедуплицированный refresh; он стабильно 401.
    const refresh = httpMock.expectOne(`${apiBase}/auth/refresh`);
    expect(refresh.request.method).toBe('POST');
    refresh.flush(API_401_BODY, UNAUTHORIZED);

    for (const pending of pendings) {
      await expectAsync(pending).toBeRejectedWith({ status: 401, body: API_401_BODY });
    }
    expect(sessionExpiredEvents).withContext('одно событие на дедуплицированный refresh').toBe(1);

    // Повторный 401 после неудачного refresh НЕ создаёт новый refresh-вызов.
    const later = firstValueFrom(http.get(`${apiBase}/labs`));
    flushUnauthorized('/labs');
    await expectAsync(later).toBeRejectedWith({ status: 401, body: API_401_BODY });
    expect(sessionExpiredEvents).toBe(1);
    // afterEach-verify() подтвердит отсутствие бесконечных повторов.
  });

  it('AC4: 3 параллельных 401 при успешном refresh — один refresh, каждый исходный повторён ровно один раз', async () => {
    lifecycle.markInitDone();

    const pendings = [0, 1, 2].map(() => firstValueFrom(http.get(`${apiBase}/labs`)));

    const originals = httpMock.match(`${apiBase}/labs`);
    expect(originals.length).toBe(3);
    originals.forEach((request) => request.flush(API_401_BODY, UNAUTHORIZED));

    // Ровно один refresh на все параллельные 401 (SHOULD FR-026).
    const refresh = httpMock.expectOne(`${apiBase}/auth/refresh`);
    refresh.flush(null);

    const retries = httpMock.match(`${apiBase}/labs`);
    expect(retries.length).withContext('каждый исходный повторён ровно один раз').toBe(3);
    retries.forEach((request) => request.flush({ items: [], total: 0, page: 1, pageSize: 10 }));

    for (const pending of pendings) {
      await expectAsync(pending).toBeResolved();
    }
    expect(sessionExpiredEvents).toBe(0);
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('AC5: 401 от /auth/login не триггерит refresh — отказ нормализован в ApiError для баннера', async () => {
    lifecycle.markInitDone();

    const pending = firstValueFrom(
      http.post(`${apiBase}/auth/login`, { login: 'teacher', password: 'wrong' }),
    );

    const login = httpMock.expectOne(`${apiBase}/auth/login`);
    expect(login.request.method).toBe('POST');
    login.flush({ message: 'Неверный логин или пароль' }, UNAUTHORIZED);

    await expectAsync(pending).toBeRejectedWith({
      status: 401,
      body: { message: 'Неверный логин или пароль' },
    });
    expect(sessionExpiredEvents).toBe(0);
    expect(router.navigate).not.toHaveBeenCalled();
    // verify() (afterEach): POST /auth/refresh не выполнялся.
  });

  it('остальные auth-эндпойнты (register, refresh, recovery/*, reset-password) исключены из refresh', async () => {
    lifecycle.markInitDone();

    const excludedPaths = [
      '/auth/register',
      '/auth/refresh',
      '/auth/recovery/request',
      '/auth/recovery/confirm',
      '/auth/reset-password',
    ];
    for (const path of excludedPaths) {
      const pending = firstValueFrom(http.post(`${apiBase}${path}`, {}));
      httpMock
        .expectOne(`${apiBase}${path}`)
        .flush(API_401_BODY, UNAUTHORIZED);
      await expectAsync(pending).toBeRejectedWith({ status: 401, body: API_401_BODY });
    }

    expect(sessionExpiredEvents).toBe(0);
    // verify() (afterEach): ни одного POST /auth/refresh.
  });

  it('повтор после успешного refresh не зацикливается: 401 повтора не вызывает новый refresh', async () => {
    lifecycle.markInitDone();

    const pending = firstValueFrom(http.get(`${apiBase}/labs`));
    flushUnauthorized('/labs');
    httpMock.expectOne(`${apiBase}/auth/refresh`).flush(null);

    // Повтор исходного снова получает 401 — второй попытки не будет.
    const retry = httpMock.expectOne(`${apiBase}/labs`);
    retry.flush(API_401_BODY, UNAUTHORIZED);

    await expectAsync(pending).toBeRejectedWith({ status: 401, body: API_401_BODY });
    expect(sessionExpiredEvents).withContext('refresh сам успешен — сброса нет').toBe(0);
    // verify() (afterEach): ни нового refresh, ни второго повтора.
  });

  it('запрос вне API_BASE_URL проходит без withCredentials и не перехватывается', async () => {
    lifecycle.markInitDone();

    const pending = firstValueFrom(http.get('/assets/data.json'));

    const request = httpMock.expectOne('/assets/data.json');
    expect(request.request.withCredentials).toBeFalse();
    request.flush('ok');

    await expectAsync(pending).toBeResolved();
    expect(sessionExpiredEvents).toBe(0);
  });

  it('409 с {message, errors} нормализуется в ApiError — баннер body.message работает (FR-025)', async () => {
    lifecycle.markInitDone();

    const pending = firstValueFrom(http.post(`${apiBase}/labs`, {}));
    httpMock.expectOne(`${apiBase}/labs`).flush(
      {
        message: 'Лабораторная с таким номером уже есть в семестре',
        errors: { number: ['Номер уже занят'] },
      },
      { status: 409, statusText: 'Conflict' },
    );

    await expectAsync(pending).toBeRejectedWith({
      status: 409,
      body: {
        message: 'Лабораторная с таким номером уже есть в семестре',
        errors: { number: ['Номер уже занят'] },
      },
    });
  });

  it('сетевой отказ (нет ответа) нормализуется в ApiError со статусом вне транспорта', async () => {
    lifecycle.markInitDone();

    const pending = firstValueFrom(http.get(`${apiBase}/labs`));
    httpMock.expectOne(`${apiBase}/labs`).error(new ProgressEvent('error'));

    await expectAsync(pending).toBeRejectedWith({
      status: 0,
      body: { message: 'Неизвестная ошибка' },
    });
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('тело ошибки не по форме (строка) — запасное сообщение, статус сохранён', async () => {
    lifecycle.markInitDone();

    const pending = firstValueFrom(http.get(`${apiBase}/labs`));
    httpMock
      .expectOne(`${apiBase}/labs`)
      .flush('Internal Server Error', { status: 500, statusText: 'Server Error' });

    await expectAsync(pending).toBeRejectedWith({
      status: 500,
      body: { message: 'Неизвестная ошибка' },
    });
  });

  it('CR-001 (ADR-021): публикация sessionRestored сбрасывает фиксатор — новый 401 снова инициирует refresh и повтор исходного', async () => {
    lifecycle.markInitDone();

    // 1. Взводим фиксатор: 401 → refresh 401 — событие и редирект.
    const first = firstValueFrom(http.get(`${apiBase}/labs`));
    flushUnauthorized('/labs');
    httpMock.expectOne(`${apiBase}/auth/refresh`).flush(API_401_BODY, UNAUTHORIZED);
    await expectAsync(first).toBeRejectedWith({ status: 401, body: API_401_BODY });
    expect(sessionExpiredEvents).withContext('одно событие на неудачный refresh').toBe(1);
    expect(router.navigate).toHaveBeenCalledWith(['/login']);

    // 2. Повторный 401 при взведённом фиксаторе — без refresh-вызова.
    const second = firstValueFrom(http.get(`${apiBase}/labs`));
    flushUnauthorized('/labs');
    await expectAsync(second).toBeRejectedWith({ status: 401, body: API_401_BODY });
    expect(httpMock.match(`${apiBase}/auth/refresh`).length)
      .withContext('фиксатор: нового refresh-вызова нет')
      .toBe(0);

    // 3. AuthService после успешного входа публикует sessionRestored
    //    (вызов подписчика-стаба вместо AuthService, ADR-021) — фиксатор
    //    сброшен.
    lifecycle.markSessionRestored();

    // 4. Новый 401 — интерцептор СНОВА выполняет POST /auth/refresh и
    //    повтор исходного запроса ровно один раз.
    const third = firstValueFrom(http.get(`${apiBase}/labs`));
    flushUnauthorized('/labs');
    const refresh = httpMock.expectOne(`${apiBase}/auth/refresh`);
    expect(refresh.request.method).toBe('POST');
    refresh.flush(null);
    const retry = httpMock.expectOne(`${apiBase}/labs`);
    retry.flush({ items: [], total: 0, page: 1, pageSize: 10 });
    await expectAsync(third).toBeResolved();

    // Цикла нет: одна новая серия 401 — один refresh; событий сброса сессии
    // по-прежнему одно (успешный refresh событие не публикует).
    expect(sessionExpiredEvents).withContext('успешный refresh без сброса сессии').toBe(1);
  });

  it('CR-001: повторная публикация sessionRestored безопасна — сброс фиксатора идемпотентен (IF-014)', async () => {
    lifecycle.markInitDone();

    // Взводим фиксатор: 401 → refresh 401.
    const first = firstValueFrom(http.get(`${apiBase}/labs`));
    flushUnauthorized('/labs');
    httpMock.expectOne(`${apiBase}/auth/refresh`).flush(API_401_BODY, UNAUTHORIZED);
    await expectAsync(first).toBeRejectedWith({ status: 401, body: API_401_BODY });

    // Двойная публикация восстановления (повторный вход) — безвредна.
    lifecycle.markSessionRestored();
    lifecycle.markSessionRestored();

    // Следующий 401 — ровно ОДИН refresh (не по числу публикаций), успех —
    // ровно один повтор исходного.
    const second = firstValueFrom(http.get(`${apiBase}/labs`));
    flushUnauthorized('/labs');
    httpMock.expectOne(`${apiBase}/auth/refresh`).flush(null);
    const retry = httpMock.expectOne(`${apiBase}/labs`);
    retry.flush({ items: [], total: 0, page: 1, pageSize: 10 });
    await expectAsync(second).toBeResolved();
    expect(sessionExpiredEvents).withContext('повторных событий нет').toBe(1);
  });

  it('CR-001: фиксатор сбрасывается ТОЛЬКО каналом sessionRestored$ — 2xx и 409-ответы фиксатор не снимают', async () => {
    lifecycle.markInitDone();

    // Взводим фиксатор: 401 → refresh 401.
    const first = firstValueFrom(http.get(`${apiBase}/labs`));
    flushUnauthorized('/labs');
    httpMock.expectOne(`${apiBase}/auth/refresh`).flush(API_401_BODY, UNAUTHORIZED);
    await expectAsync(first).toBeRejectedWith({ status: 401, body: API_401_BODY });

    // Успешный (2xx) API-ответ сам по себе фиксатор НЕ сбрасывает: сброс —
    // только явный канал markSessionRestored (ADR-021 отверг наблюдение
    // статусов/URL чужих ответов).
    const ok = firstValueFrom(http.get(`${apiBase}/auth/me`));
    httpMock.expectOne(`${apiBase}/auth/me`).flush({ id: 'u-1', login: 'teacher' });
    await expectAsync(ok).toBeResolved();

    // 409 тоже не сбрасывает — отказ нормализуется в ApiError потребителю.
    const conflict = firstValueFrom(http.post(`${apiBase}/labs`, {}));
    httpMock.expectOne(`${apiBase}/labs`).flush(
      { message: 'Конфликт' },
      { status: 409, statusText: 'Conflict' },
    );
    await expectAsync(conflict).toBeRejectedWith({ status: 409, body: { message: 'Конфликт' } });

    // Последующий 401 — без refresh-вызова (фиксатор всё ещё взведён).
    const later = firstValueFrom(http.get(`${apiBase}/labs`));
    flushUnauthorized('/labs');
    await expectAsync(later).toBeRejectedWith({ status: 401, body: API_401_BODY });
    expect(httpMock.match(`${apiBase}/auth/refresh`).length)
      .withContext('второго refresh-вызова нет')
      .toBe(0);
    expect(sessionExpiredEvents).withContext('повторных событий нет').toBe(1);
  });

  it('CR-001: анонимный холодный старт (refresh 401 на /auth/me) → вход с sessionRestored → истечение access — refresh снова работает', async () => {
    // 1. Холодный старт анонима (фаза init, markInitDone не вызван):
    //    GET /auth/me → 401 → refresh → 401 — фиксатор взводится, событие
    //    сброса публикуется, навигации нет (ADR-016).
    const me = firstValueFrom(http.get(`${apiBase}/auth/me`));
    flushUnauthorized('/auth/me');
    httpMock.expectOne(`${apiBase}/auth/refresh`).flush(API_401_BODY, UNAUTHORIZED);
    await expectAsync(me).toBeRejectedWith({ status: 401, body: API_401_BODY });
    expect(sessionExpiredEvents).toBe(1);
    expect(router.navigate).withContext('фаза init — навигация подавлена').not.toHaveBeenCalled();

    // 2. Инициализация завершена, пользователь входит: 2xx login, после
    //    которого AuthService публикует markSessionRestored — фиксатор
    //    сброшен (ADR-021).
    lifecycle.markInitDone();
    const login = firstValueFrom(http.post(`${apiBase}/auth/login`, {}));
    const loginReq = httpMock.expectOne(`${apiBase}/auth/login`);
    expect(loginReq.request.method).toBe('POST');
    loginReq.flush({ id: 'u-1', login: 'teacher', role: 'teacher' });
    await expectAsync(login).toBeResolved();
    lifecycle.markSessionRestored();

    // 3. Истечение access: 401 → refresh ОБЯЗАН быть отправлен (фиксатор
    //    сброшен), успех — повтор исходного ровно один раз.
    const pending = firstValueFrom(http.get(`${apiBase}/labs`));
    flushUnauthorized('/labs');
    const refresh = httpMock.expectOne(`${apiBase}/auth/refresh`);
    expect(refresh.request.method).toBe('POST');
    refresh.flush(null);
    const retry = httpMock.expectOne(`${apiBase}/labs`);
    retry.flush({ items: [], total: 0, page: 1, pageSize: 10 });

    await expectAsync(pending).toBeResolved();
    expect(sessionExpiredEvents).withContext('успешный refresh без сброса сессии').toBe(1);
    expect(router.navigate).not.toHaveBeenCalled();
  });
});
