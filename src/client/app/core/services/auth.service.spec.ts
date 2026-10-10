/**
 * Юнит-тесты AuthService (C-014, IF-007/IF-014, FR-091/FR-092) — все вызовы
 * через HttpTestingController (конвенция URL — ADR-014: ожидаемые URL
 * строятся из TestBed.inject(API_BASE_URL), литералы префикса запрещены).
 * Производственная цепочка — реальный authInterceptor (нормализация отказов
 * в ApiError, withCredentials, дедуплицированный refresh):
 *  - AC «Контракт вызовов»: POST /auth/login {login, password}; POST
 *    /auth/register (RegisterParams); GET /auth/me; POST /auth/logout;
 *    POST /auth/recovery/request|confirm; POST /auth/reset-password —
 *    метод/URL/тела по контрактам, возвращаемые типы прежние;
 *  - AC «Сессия без localStorage»: 401 от /auth/me → аноним без исключений,
 *    isAuthenticated синхронен по currentUser, localStorage-ключ сессии
 *    прошлой (моковой) реализации не читается и не пишется (FR-092);
 *  - AC «Восстановление refresh-фиксатора»: 2xx login/register →
 *    SessionLifecycle.markSessionRestored опубликован, кэш обновлён ДО
 *    публикации (синхронно с завершением вызова);
 *  - AC «Ошибки»: 409 {message} / 401 / 400 {message, errors} —
 *    реджект ApiError той же формы (баннеры страниц без изменений);
 *  - подписка sessionExpired$ сбрасывает состояние без HTTP (verify:
 *    никаких POST /auth/logout — ADR-019);
 *  - initSession: loadMe → markInitDone при любом исходе (200/401/сеть);
 *  - recovery-поток (IF-102): email/resetToken в RecoveryFlowStore,
 *    терминальная ветка «Ссылка…» удаляет только resetToken.
 */
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { authInterceptor } from '../auth-interceptor';
import { API_BASE_URL } from '../api-base-url';
import { SessionLifecycle } from '../session-lifecycle';
import { ApiError, MeDto } from '../../shared/models';
import { AuthService } from './auth.service';
import { RecoveryFlowStore } from './recovery-flow-store';

const TEACHER_ME: MeDto = {
  login: 'teacher',
  fullName: 'Сидоров Семён Семёнович',
  role: 'teacher',
  groupName: null,
};

const STUDENT_ME: MeDto = {
  login: 'student01',
  fullName: 'Иванов Иван Иванович 01',
  role: 'student',
  groupName: 'ИК-221',
};

/** Заголовки HTTP-отказов бэкенда. */
function status(status: number, statusText: string): { status: number; statusText: string } {
  return { status, statusText };
}

describe('AuthService — домен Auth поверх HttpClient (IF-007, FR-091/FR-092)', () => {
  let httpMock: HttpTestingController;
  let apiBase: string;
  let lifecycle: SessionLifecycle;
  let service: AuthService;
  let flow: RecoveryFlowStore;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
    apiBase = TestBed.inject(API_BASE_URL);
    lifecycle = TestBed.inject(SessionLifecycle);
    service = TestBed.inject(AuthService);
    flow = TestBed.inject(RecoveryFlowStore);
  });

  afterEach(() => {
    // Ни одного незакрытого/лишнего запроса (в т.ч. POST /auth/logout).
    httpMock.verify();
    localStorage.clear();
    sessionStorage.clear();
  });

  describe('контракт вызовов (AC, HttpTestingController)', () => {
    it('login: POST /auth/login с телом {login, password} и withCredentials → MeDto', async () => {
      const pending = service.login({ login: 'teacher', password: 'teacher123!' });

      const request = httpMock.expectOne(`${apiBase}/auth/login`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ login: 'teacher', password: 'teacher123!' });
      expect(request.request.withCredentials)
        .withContext('cookie-аутентификация — withCredentials (FR-091)')
        .toBeTrue();
      request.flush(TEACHER_ME);

      await expectAsync(pending).toBeResolvedTo(TEACHER_ME);
    });

    it('register: POST /auth/register с телом RegisterParams → MeDto студента', async () => {
      const params = {
        fullName: 'Петров Пётр Петрович',
        login: 'student33',
        email: 'student33@example.com',
        password: 'Password#2026',
        repeatPassword: 'Password#2026',
      };
      const pending = service.register(params);

      const request = httpMock.expectOne(`${apiBase}/auth/register`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual(params);
      request.flush(STUDENT_ME);

      await expectAsync(pending).toBeResolvedTo(STUDENT_ME);
    });

    it('logout: POST /auth/logout без тела → void, кэш сброшен', async () => {
      const login = service.login({ login: 'teacher', password: 'teacher123!' });
      httpMock.expectOne(`${apiBase}/auth/login`).flush(TEACHER_ME);
      await login;
      expect(service.isAuthenticated()).toBeTrue();

      const pending = service.logout();
      const request = httpMock.expectOne(`${apiBase}/auth/logout`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).withContext('тело logout отсутствует').toBeNull();
      request.flush(null, status(204, 'No Content'));

      await expectAsync(pending).toBeResolved();
      expect(service.currentUser()).withContext('кэш сброшен').toBeNull();
      expect(service.isAuthenticated()).toBeFalse();
    });

    it('loadMe: GET /auth/me → MeDto, кэш заполнен, isAuthenticated синхронно true', async () => {
      const pending = service.loadMe();

      const request = httpMock.expectOne(`${apiBase}/auth/me`);
      expect(request.request.method).toBe('GET');
      expect(request.request.body).toBeNull();
      request.flush(STUDENT_ME);

      await expectAsync(pending).toBeResolvedTo(STUDENT_ME);
      expect(service.currentUser()).toEqual(STUDENT_ME);
      expect(service.isAuthenticated()).withContext('синхронно по кэшу').toBeTrue();
    });

    it('requestRecoveryCode: POST /auth/recovery/request {email} → 200 {}, email в потоке', async () => {
      const pending = service.requestRecoveryCode('student01@example.com');

      const request = httpMock.expectOne(`${apiBase}/auth/recovery/request`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ email: 'student01@example.com' });
      request.flush({});

      await expectAsync(pending).toBeResolved();
      expect(flow.email()).withContext('email запоминается после запроса кода').toBe(
        'student01@example.com',
      );
    });

    it('confirmRecoveryCode: POST /auth/recovery/confirm {email, code} → {resetToken} в ответе и в потоке', async () => {
      const pending = service.confirmRecoveryCode('student01@example.com', '123456');

      const request = httpMock.expectOne(`${apiBase}/auth/recovery/confirm`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ email: 'student01@example.com', code: '123456' });
      request.flush({ resetToken: 'reset-token-1' });

      await expectAsync(pending).toBeResolvedTo({ resetToken: 'reset-token-1' });
      expect(flow.resetToken()).toBe('reset-token-1');
      expect(flow.hasResetToken()).toBeTrue();
    });

    it('resetPassword: POST /auth/reset-password {resetToken, password, confirmPassword} → успех очищает поток', async () => {
      flow.setEmail('student01@example.com');
      flow.setResetToken('reset-token-1');

      const pending = service.resetPassword('reset-token-1', 'NewPassword#2026', 'NewPassword#2026');

      const request = httpMock.expectOne(`${apiBase}/auth/reset-password`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({
        resetToken: 'reset-token-1',
        password: 'NewPassword#2026',
        confirmPassword: 'NewPassword#2026',
      });
      request.flush(null);

      await expectAsync(pending).toBeResolved();
      expect(flow.hasEmail()).withContext('поток завершён — очищен').toBeFalse();
      expect(flow.hasResetToken()).toBeFalse();
    });
  });

  describe('сессия без localStorage (AC, FR-092)', () => {
    it('loadMe при 401: аноним (null) без необработанных исключений; тихий refresh исчерпан', async () => {
      const pending = service.loadMe();

      httpMock.expectOne(`${apiBase}/auth/me`).flush(
        { message: 'Не авторизован' },
        status(401, 'Unauthorized'),
      );
      httpMock.expectOne(`${apiBase}/auth/refresh`).flush(
        { message: 'Не авторизован' },
        status(401, 'Unauthorized'),
      );

      await expectAsync(pending).toBeResolvedTo(null);
      expect(service.currentUser()).toBeNull();
      expect(service.isAuthenticated()).withContext('аноним').toBeFalse();
    });

    it('isAuthenticated синхронно отражает currentUser; ключ сессии не читается', () => {
      // Маркер прошлой (моковой) реализации в localStorage игнорируется:
      // сервис не читает НИКАКИХ ключей, поэтому проверяем произвольный
      // legacy-ключ прошлого контракта.
      localStorage.setItem('legacy.session.userId', 'aaaaaaaa-1111-4111-8111-111111111111');
      expect(service.isAuthenticated())
        .withContext('признак сессии — только память (FR-092)')
        .toBeFalse();
      expect(service.currentUser()).toBeNull();
    });

    it('ключ сессии прошлых реализаций не пишется: login заполняет только память', async () => {
      const pending = service.login({ login: 'teacher', password: 'teacher123!' });
      httpMock.expectOne(`${apiBase}/auth/login`).flush(TEACHER_ME);
      await pending;

      expect(service.isAuthenticated()).toBeTrue();
      expect(localStorage.getItem('legacy.session.userId'))
        .withContext('localStorage-маркер сессии запрещён (FR-092)')
        .toBeNull();
      expect(localStorage.length).withContext('сервис ничего не пишет в localStorage').toBe(0);
    });

    it('после logout в localStorage тоже пусто; повторный вход начинается с чистой памятью', async () => {
      const login = service.login({ login: 'teacher', password: 'teacher123!' });
      httpMock.expectOne(`${apiBase}/auth/login`).flush(TEACHER_ME);
      await login;

      const out = service.logout();
      httpMock.expectOne(`${apiBase}/auth/logout`).flush(null, status(204, 'No Content'));
      await out;

      expect(service.isAuthenticated()).toBeFalse();
      expect(localStorage.length).toBe(0);
    });
  });

  describe('восстановление refresh-фиксатора (AC, ADR-019)', () => {
    let restoredEvents: number;
    /** Снимок кэша в момент публикации sessionRestored$ (кэш ДО публикации). */
    let cacheAtRestore: MeDto | null | undefined;

    beforeEach(() => {
      restoredEvents = 0;
      cacheAtRestore = undefined;
      lifecycle.sessionRestored$.subscribe(() => {
        restoredEvents += 1;
        cacheAtRestore = service.currentUser();
      });
    });

    it('успешный login публикует markSessionRestored; кэш обновлён синхронно с публикацией', async () => {
      const pending = service.login({ login: 'teacher', password: 'teacher123!' });
      httpMock.expectOne(`${apiBase}/auth/login`).flush(TEACHER_ME);
      await pending;

      expect(restoredEvents).withContext('ровно одна публикация на успешный вход').toBe(1);
      expect(cacheAtRestore).withContext('кэш обновлён ДО публикации').toEqual(TEACHER_ME);
    });

    it('успешный register публикует markSessionRestored (автоматический вход)', async () => {
      const params = {
        fullName: 'Петров Пётр Петрович',
        login: 'student33',
        email: 'student33@example.com',
        password: 'Password#2026',
        repeatPassword: 'Password#2026',
      };
      const pending = service.register(params);
      httpMock.expectOne(`${apiBase}/auth/register`).flush(STUDENT_ME);
      await pending;

      expect(restoredEvents).toBe(1);
      expect(cacheAtRestore).toEqual(STUDENT_ME);
      expect(service.currentUser()).toEqual(STUDENT_ME);
    });

    it('неудачный login (401) публикаций не делает и кэш не заполняет', async () => {
      const pending = service.login({ login: 'teacher', password: 'неверный' });
      httpMock.expectOne(`${apiBase}/auth/login`).flush(
        { message: 'Неверный логин или пароль' },
        status(401, 'Unauthorized'),
      );

      await expectAsync(pending).toBeRejectedWith({
        status: 401,
        body: { message: 'Неверный логин или пароль' },
      });
      expect(restoredEvents).withContext('публикация только после 2xx').toBe(0);
      expect(service.currentUser()).toBeNull();
      expect(service.isAuthenticated()).toBeFalse();
    });

    it('logout публикаций sessionRestored не делает', async () => {
      const login = service.login({ login: 'teacher', password: 'teacher123!' });
      httpMock.expectOne(`${apiBase}/auth/login`).flush(TEACHER_ME);
      await login;
      expect(restoredEvents).toBe(1);

      const out = service.logout();
      httpMock.expectOne(`${apiBase}/auth/logout`).flush(null, status(204, 'No Content'));
      await out;

      expect(restoredEvents).withContext('выход — не восстановление сессии').toBe(1);
    });
  });

  describe('ошибки — нормализация в ApiError (AC)', () => {
    it('register 409 {message} → реджект ApiError {status: 409, body: {message}}', async () => {
      const pending = service.register({
        fullName: 'Петров Пётр Петрович',
        login: 'teacher',
        email: 'student33@example.com',
        password: 'Password#2026',
        repeatPassword: 'Password#2026',
      });
      httpMock.expectOne(`${apiBase}/auth/register`).flush(
        { message: 'Пользователь с таким логином уже существует' },
        status(409, 'Conflict'),
      );

      const expected: ApiError = {
        status: 409,
        body: { message: 'Пользователь с таким логином уже существует' },
      };
      await expectAsync(pending).toBeRejectedWith(expected);
    });

    it('register 400 с полевыми ошибками → ApiError {status: 400, body: {message, errors}}', async () => {
      const pending = service.register({
        fullName: 'Петров Пётр Петрович',
        login: 'student33',
        email: 'сломанный-email',
        password: 'Password#2026',
        repeatPassword: 'другой',
      });
      httpMock.expectOne(`${apiBase}/auth/register`).flush(
        {
          message: 'Данные заполнены неверно',
          errors: {
            email: ['Введите корректный email'],
            repeatPassword: ['Пароли не совпадают'],
          },
        },
        status(400, 'Bad Request'),
      );

      await expectAsync(pending).toBeRejectedWith({
        status: 400,
        body: {
          message: 'Данные заполнены неверно',
          errors: {
            email: ['Введите корректный email'],
            repeatPassword: ['Пароли не совпадают'],
          },
        },
      });
      expect(service.currentUser()).withContext('отказ кэш не заполняет').toBeNull();
    });

    it('confirmRecoveryCode 400 «Код восстановления не подходит» → ApiError, в потоке email без токена', async () => {
      flow.setEmail('student01@example.com');
      const pending = service.confirmRecoveryCode('student01@example.com', '000000');
      httpMock.expectOne(`${apiBase}/auth/recovery/confirm`).flush(
        { message: 'Код восстановления не подходит' },
        status(400, 'Bad Request'),
      );

      await expectAsync(pending).toBeRejectedWith({
        status: 400,
        body: { message: 'Код восстановления не подходит' },
      });
      expect(flow.hasEmail()).withContext('email сохраняется — можно переотправить код').toBeTrue();
      expect(flow.hasResetToken()).toBeFalse();
    });

    it('resetPassword: терминальная ветка токена (400 «Ссылка…») — удаляется только resetToken', async () => {
      flow.setEmail('student01@example.com');
      flow.setResetToken('reset-token-1');

      const pending = service.resetPassword('гнилой-токен', 'NewPassword#2026', 'NewPassword#2026');
      httpMock.expectOne(`${apiBase}/auth/reset-password`).flush(
        { message: 'Ссылка восстановления недействительна или истекла' },
        status(400, 'Bad Request'),
      );

      await expectAsync(pending).toBeRejectedWith({
        status: 400,
        body: { message: 'Ссылка восстановления недействительна или истекла' },
      });
      expect(flow.resetToken()).withContext('токен удалён из потока').toBeNull();
      expect(flow.email()).withContext('email сохраняется').toBe('student01@example.com');
    });

    it('resetPassword: полевая ошибка пароля (400 с errors) — поток не трогается (токен жив)', async () => {
      flow.setEmail('student01@example.com');
      flow.setResetToken('reset-token-1');

      const pending = service.resetPassword('reset-token-1', 'слабый', 'слабый');
      httpMock.expectOne(`${apiBase}/auth/reset-password`).flush(
        {
          message: 'Данные заполнены неверно',
          errors: { password: ['Пароль должен содержать хотя бы одну цифру'] },
        },
        status(400, 'Bad Request'),
      );

      const rejection = (await pending.then(
        () => null,
        (error: unknown) => error,
      )) as ApiError | null;
      expect(rejection).not.toBeNull();
      expect(rejection!.body.errors!['password']).toBeDefined();
      expect(flow.hasResetToken()).withContext('нетерминальная ветка — токен жив').toBeTrue();
    });

    it('запрос кода: 429 → ApiError {status: 429}, email в поток не попадает', async () => {
      const pending = service.requestRecoveryCode('student01@example.com');
      httpMock.expectOne(`${apiBase}/auth/recovery/request`).flush(
        { message: 'Слишком много попыток. Повторите позже' },
        status(429, 'Too Many Requests'),
      );

      await expectAsync(pending).toBeRejectedWith({
        status: 429,
        body: { message: 'Слишком много попыток. Повторите позже' },
      });
      expect(flow.hasEmail()).withContext('email запоминается только после успеха').toBeFalse();
    });
  });

  describe('сброс по sessionExpired$ — без HTTP (AC, ADR-019)', () => {
    it('событие сбрасывает кэш; запросов к /auth/logout нет (verify)', async () => {
      const login = service.login({ login: 'teacher', password: 'teacher123!' });
      httpMock.expectOne(`${apiBase}/auth/login`).flush(TEACHER_ME);
      await login;
      expect(service.isAuthenticated()).toBeTrue();

      // Публикатор события — интерцептор (после неудачного refresh).
      lifecycle.notifySessionExpired();

      expect(service.currentUser()).withContext('кэш сброшен подпиской').toBeNull();
      expect(service.isAuthenticated()).withContext('сессия только в памяти').toBeFalse();

      // Повторная публикация идемпотентна для сброса (IF-014).
      lifecycle.notifySessionExpired();
      expect(service.isAuthenticated()).toBeFalse();

      // afterEach-verify() подтвердит: никаких POST /auth/logout.
    });

    it('после сброса по событию повторный вход работает (кэш и публикация восстанавливаются)', async () => {
      const first = service.login({ login: 'teacher', password: 'teacher123!' });
      httpMock.expectOne(`${apiBase}/auth/login`).flush(TEACHER_ME);
      await first;
      lifecycle.notifySessionExpired();
      expect(service.isAuthenticated()).toBeFalse();

      const second = service.login({ login: 'teacher', password: 'teacher123!' });
      httpMock.expectOne(`${apiBase}/auth/login`).flush(TEACHER_ME);
      await second;

      expect(service.isAuthenticated()).toBeTrue();
      expect(service.currentUser()).toEqual(TEACHER_ME);
    });
  });

  describe('initSession — provideAppInitializer (FR-092, ADR-016)', () => {
    it('200 от /auth/me: кэш заполнен, фаза инициализации завершена markInitDone', async () => {
      expect(lifecycle.initializing).withContext('до init фаза активна').toBeTrue();

      const pending = service.initSession();
      httpMock.expectOne(`${apiBase}/auth/me`).flush(TEACHER_ME);
      await pending;

      expect(service.currentUser()).toEqual(TEACHER_ME);
      expect(service.isAuthenticated()).toBeTrue();
      expect(lifecycle.initializing).withContext('markInitDone вызван').toBeFalse();
    });

    it('401 от /auth/me: аноним без исключений, markInitDone вызван', async () => {
      const pending = service.initSession();
      httpMock.expectOne(`${apiBase}/auth/me`).flush(
        { message: 'Не авторизован' },
        status(401, 'Unauthorized'),
      );
      httpMock.expectOne(`${apiBase}/auth/refresh`).flush(
        { message: 'Не авторизован' },
        status(401, 'Unauthorized'),
      );

      await expectAsync(pending).toBeResolved();
      expect(service.currentUser()).withContext('аноним').toBeNull();
      expect(service.isAuthenticated()).toBeFalse();
      expect(lifecycle.initializing).withContext('фаза завершена и при отказе').toBeFalse();
    });

    it('сетевой отказ: initSession разрешается (анонимный старт), markInitDone вызван', async () => {
      const pending = service.initSession();
      httpMock.expectOne(`${apiBase}/auth/me`).error(new ProgressEvent('error'));

      await expectAsync(pending).toBeResolved();
      expect(service.currentUser()).toBeNull();
      expect(service.isAuthenticated()).toBeFalse();
      expect(lifecycle.initializing).withContext('фаза завершена при сетевом отказе').toBeFalse();
    });
  });

  describe('тихий refresh в ветке loadMe (C-013, IF-014)', () => {
    it('401 от /auth/me → refresh успешен → повтор me → MeDto и кэш', async () => {
      const pending = service.loadMe();

      httpMock.expectOne(`${apiBase}/auth/me`).flush(
        { message: 'Не авторизован' },
        status(401, 'Unauthorized'),
      );
      const refresh = httpMock.expectOne(`${apiBase}/auth/refresh`);
      expect(refresh.request.method).withContext('refresh выполняет интерцептор').toBe('POST');
      refresh.flush(TEACHER_ME);

      const retry = httpMock.expectOne(`${apiBase}/auth/me`);
      retry.flush(TEACHER_ME);

      await expectAsync(pending).toBeResolvedTo(TEACHER_ME);
      expect(service.isAuthenticated()).toBeTrue();
    });

    it('дедупликация refresh: два параллельных 401 → ровно один POST /auth/refresh, оба исходных повторены (IF-014)', async () => {
      const first = service.loadMe();
      const second = service.loadMe();

      // Оба исходных запроса уходят и получают 401.
      const originals = httpMock.match(`${apiBase}/auth/me`);
      expect(originals.length).withContext('два параллельных GET /auth/me').toBe(2);
      for (const request of originals) {
        request.flush({ message: 'Не авторизован' }, status(401, 'Unauthorized'));
      }

      // Дедупликация: на оба 401 — единственный refresh-вызов.
      const refresh = httpMock.expectOne(`${apiBase}/auth/refresh`);
      expect(refresh.request.method).withContext('refresh выполняет интерцептор').toBe('POST');
      refresh.flush(null, status(204, 'No Content'));

      // Каждый исходный запрос повторён ровно один раз.
      const retries = httpMock.match(`${apiBase}/auth/me`);
      expect(retries.length).withContext('повтор каждого исходного ровно один раз').toBe(2);
      retries[0].flush(TEACHER_ME);
      retries[1].flush(STUDENT_ME);

      await expectAsync(first).toBeResolvedTo(TEACHER_ME);
      await expectAsync(second).toBeResolvedTo(STUDENT_ME);
      expect(service.isAuthenticated()).withContext('сессия восстановлена refresh').toBeTrue();
    });
  });

  it('без действий сервиса HTTP-запросов нет (isAuthenticated синхронен и без сети)', () => {
    expect(service.isAuthenticated()).toBeFalse();
    expect(service.currentUser()).toBeNull();
    // afterEach-verify(): ни одного запроса.
  });
});

/**
 * AC «Сессия в памяти» (FR-026, IF-018): пересоздание модуля — аналог
 * перезагрузки страницы. Отдельный describe с собственным жизненным циклом
 * TestBed: внутри теста модуль пересоздаётся (новый root-инжектор → новый
 * root-сервис AuthService с пустой памятью), что нельзя выразить в общем
 * describe с общим beforeEach-инжектором.
 */
describe('AuthService — пересоздание модуля, аналог перезагрузки страницы (AC «Сессия в памяти»)', () => {
  function configureTestingModule(): void {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
  }

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    TestBed.resetTestingModule();
    configureTestingModule();
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
    localStorage.clear();
    sessionStorage.clear();
  });

  it('после входа пересоздание модуля не считает сессию активной из localStorage; восстановление — через /auth/refresh интерцептора', async () => {
    // 1. «Первая жизнь» приложения: пользователь вошёл — кэш только в памяти.
    const first = TestBed.inject(AuthService);
    const apiBase = TestBed.inject(API_BASE_URL);
    const httpMock = TestBed.inject(HttpTestingController);

    const pendingLogin = first.login({ login: 'teacher', password: 'teacher123!' });
    httpMock.expectOne(`${apiBase}/auth/login`).flush(TEACHER_ME);
    await pendingLogin;
    expect(first.isAuthenticated()).withContext('вход установил сессию в памяти').toBeTrue();
    expect(localStorage.length)
      .withContext('маркер сессии в localStorage не пишется (FR-026)')
      .toBe(0);

    // 2. Пересоздание модуля (аналог F5): новый root-инжектор — память пуста.
    TestBed.resetTestingModule();
    configureTestingModule();
    const second = TestBed.inject(AuthService);
    expect(second).withContext('новый экземпляр root-сервиса').not.toBe(first);
    expect(second.isAuthenticated())
      .withContext('сессия НЕ восстанавливается из localStorage — признак только в памяти')
      .toBeFalse();
    expect(second.currentUser()).toBeNull();

    // 3. Восстановление — исключительно тихий refresh интерцептора:
    //    loadMe → 401 → POST /auth/refresh → повтор me → сессия активна.
    const restored = TestBed.inject(HttpTestingController);
    const pending = second.loadMe();
    restored.expectOne(`${apiBase}/auth/me`).flush(
      { message: 'Не авторизован' },
      status(401, 'Unauthorized'),
    );
    const refresh = restored.expectOne(`${apiBase}/auth/refresh`);
    expect(refresh.request.method).toBe('POST');
    refresh.flush(null, status(204, 'No Content'));
    restored.expectOne(`${apiBase}/auth/me`).flush(TEACHER_ME);

    await expectAsync(pending).toBeResolvedTo(TEACHER_ME);
    expect(second.isAuthenticated()).withContext('сессия восстановлена через refresh').toBeTrue();
  });
});
