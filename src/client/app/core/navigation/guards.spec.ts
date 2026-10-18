/**
 * Юнит-тесты навигационных guards (C-014, контракт IF-108, FR-4.8, FR-092):
 * authGuard (гость → /login, состояние только в памяти AuthService),
 * roleGuard (чужая роль → /403, гость → /login), guestGuard (обе роли →
 * домашний маршрут по ROLE_HOME), recoveryStepGuard (все комбинации store),
 * homeGuard (обе роли и гость). Редиректы проверяются как UrlTree; guards
 * запускаются через TestBed.runInInjectionContext. HTTP-конвейер — реальные
 * AuthService + authInterceptor поверх HttpTestingController (конвенция URL
 * — ADR-014): сессия устанавливается входом (POST /auth/login) либо
 * initSession — холодный старт «как F5» (GET /auth/me); guards сами HTTP
 * не выполняют (кэш наполняет provideAppInitializer — initSession).
 */
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  CanActivateFn,
  provideRouter,
  RouterStateSnapshot,
  UrlTree,
} from '@angular/router';

import { authInterceptor } from '../auth-interceptor';
import { API_BASE_URL } from '../api-base-url';
import { SessionLifecycle } from '../session-lifecycle';
import { MeDto, STORAGE_KEYS } from '../../shared/models';
import { AuthService } from '../services/auth.service';
import { RecoveryFlowStore } from '../services/recovery-flow-store';
import {
  ROLE_HOME,
  authGuard,
  guestGuard,
  homeGuard,
  recoveryStepGuard,
  roleGuard,
} from './guards';

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

const UNAUTHORIZED = { status: 401, statusText: 'Unauthorized' };

const ROUTE = {} as ActivatedRouteSnapshot;
const STATE = { url: '/works' } as RouterStateSnapshot;

describe('Навигационные guards — сессия в памяти AuthService (IF-108, FR-092)', () => {
  let httpMock: HttpTestingController;
  let apiBase: string;
  let lifecycle: SessionLifecycle;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    spyOn(console, 'error');
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
  });

  afterEach(() => {
    // Ни одного незакрытого/лишнего запроса: guards не выполняют HTTP.
    httpMock.verify();
    localStorage.clear();
    sessionStorage.clear();
  });

  function auth(): AuthService {
    return TestBed.inject(AuthService);
  }

  function flow(): RecoveryFlowStore {
    return TestBed.inject(RecoveryFlowStore);
  }

  /** Guards синхронны: кэш наполняет initSession до первого решения. */
  function call(guard: CanActivateFn): boolean | UrlTree {
    return TestBed.runInInjectionContext(() => guard(ROUTE, STATE)) as boolean | UrlTree;
  }

  /** Редирект по IF-108 проверяется как UrlTree с точным путём. */
  function expectRedirect(result: boolean | UrlTree, path: string): void {
    expect(result instanceof UrlTree).withContext(`UrlTree ${path}`).toBeTrue();
    expect((result as UrlTree).toString()).toBe(path);
  }

  /**
   * Холодный старт «как F5» с действующими cookie: initSession →
   * GET /auth/me → 200 (то же делает provideAppInitializer, FR-092).
   */
  async function coldStartWithSession(me: MeDto): Promise<void> {
    const pending = auth().initSession();
    httpMock.expectOne(`${apiBase}/auth/me`).flush(me);
    await expectAsync(pending).toBeResolved();
    expect(auth().isAuthenticated()).withContext('кэш заполнен до guard').toBeTrue();
  }

  /** Вход через полный конвейер (POST /auth/login) — кэш currentUser. */
  async function loginAs(login: string, password: string, me: MeDto): Promise<void> {
    const pending = auth().login({ login, password });
    httpMock.expectOne(`${apiBase}/auth/login`).flush(me);
    await expectAsync(pending).toBeResolvedTo(me);
  }

  describe('authGuard (AC auth-guard-guest)', () => {
    it('без сессии → UrlTree /login, без HTTP-вызовов', () => {
      const spy = spyOn(auth(), 'loadMe').and.callThrough();
      expectRedirect(call(authGuard), '/login');
      expect(spy).withContext('guards не выполняют HTTP — кэш наполняет initSession').not.toHaveBeenCalled();
      expect(console.error).not.toHaveBeenCalled();
    });

    it('холодный старт с сессией (initSession 200) → true', async () => {
      await coldStartWithSession(TEACHER_ME);
      expect(call(authGuard)).toBeTrue();
    });

    it('сессия после входа (login 2xx) → true', async () => {
      await loginAs('teacher', 'teacher123!', TEACHER_ME);
      expect(call(authGuard)).toBeTrue();
    });

    it('битая сессия (initSession: me 401, refresh 401) → UrlTree /login, кэш пуст', async () => {
      const pending = auth().initSession();
      httpMock.expectOne(`${apiBase}/auth/me`).flush({ message: 'Не авторизован' }, UNAUTHORIZED);
      httpMock
        .expectOne(`${apiBase}/auth/refresh`)
        .flush({ message: 'Не авторизован' }, UNAUTHORIZED);
      await pending;

      expectRedirect(call(authGuard), '/login');
      expect(auth().currentUser()).withContext('аноним').toBeNull();
      expect(console.error).not.toHaveBeenCalled();
    });

    it('маркер сессии в localStorage игнорируется — признак сессии только в памяти (FR-092)', () => {
      // Ключ прошлого (мокового) контракта: экспорт ключа сессии удалён из
      // shared/models (FR-026(4)); spec проверяет, что произвольный legacy-
      // ключ прошлого контракта игнорируется в памяти сервиса.
      localStorage.setItem('legacy.session.userId', 'aaaaaaaa-1111-4111-8111-111111111111');
      expectRedirect(call(authGuard), '/login');
      expect(localStorage.getItem('legacy.session.userId'))
        .withContext('ключ не читается и не удаляется guard\'ом')
        .toBe('aaaaaaaa-1111-4111-8111-111111111111');
    });
  });

  describe('roleGuard (AC role-guard-403)', () => {
    it('сессия студента (initSession), roleGuard("teacher") → UrlTree /403', async () => {
      await coldStartWithSession(STUDENT_ME);
      expectRedirect(call(roleGuard('teacher')), '/403');
    });

    it('своя роль → true (холодный старт с сессией преподавателя)', async () => {
      await coldStartWithSession(TEACHER_ME);
      expect(call(roleGuard('teacher'))).toBeTrue();
    });

    it('список ролей: student входит в roleGuard("teacher", "student")', async () => {
      await coldStartWithSession(STUDENT_ME);
      expect(call(roleGuard('teacher', 'student'))).toBeTrue();
    });

    it('без сессии → UrlTree /login (ветка authGuard, а не /403)', () => {
      expectRedirect(call(roleGuard('teacher')), '/login');
    });

    it('чужая роль после входа → UrlTree /403 без единого HTTP-вызова guard\'а', async () => {
      await loginAs('student01', 'student123!', STUDENT_ME);
      const spy = spyOn(auth(), 'loadMe').and.callThrough();
      expectRedirect(call(roleGuard('teacher')), '/403');
      expect(spy).not.toHaveBeenCalled();
    });
  });

  describe('guestGuard', () => {
    it('сессия преподавателя → UrlTree /works (ROLE_HOME.teacher)', async () => {
      await coldStartWithSession(TEACHER_ME);
      expectRedirect(call(guestGuard), ROLE_HOME.teacher);
    });

    it('сессия студента → UrlTree /my-submissions (ROLE_HOME.student)', async () => {
      await coldStartWithSession(STUDENT_ME);
      expectRedirect(call(guestGuard), ROLE_HOME.student);
    });

    it('гость → вход разрешён (true)', () => {
      expect(call(guestGuard)).toBeTrue();
    });

    it('битая сессия (initSession 401) → гость допущен', async () => {
      const pending = auth().initSession();
      httpMock.expectOne(`${apiBase}/auth/me`).flush({ message: 'Не авторизован' }, UNAUTHORIZED);
      httpMock
        .expectOne(`${apiBase}/auth/refresh`)
        .flush({ message: 'Не авторизован' }, UNAUTHORIZED);
      await pending;

      expect(call(guestGuard)).toBeTrue();
    });
  });

  describe('recoveryStepGuard (AC recovery-guard-restart)', () => {
    it('пустой store: /recovery/code и /reset-password → /recovery без ошибок консоли', () => {
      expect(flow().hasEmail()).toBeFalse();
      expect(flow().hasResetToken()).toBeFalse();
      expectRedirect(call(recoveryStepGuard('code')), '/recovery');
      expectRedirect(call(recoveryStepGuard('reset')), '/recovery');
      expect(console.error).not.toHaveBeenCalled();
    });

    it('шаг "code": есть email → true; есть email и токен → true', () => {
      flow().setEmail('student01@example.com');
      expect(call(recoveryStepGuard('code'))).toBeTrue();
      flow().setResetToken('reset-token-1');
      expect(call(recoveryStepGuard('code'))).withContext('email остаётся').toBeTrue();
    });

    it('шаг "reset": только email → /recovery; email и токен → true', () => {
      flow().setEmail('student01@example.com');
      expectRedirect(call(recoveryStepGuard('reset')), '/recovery');
      flow().setResetToken('reset-token-1');
      expect(call(recoveryStepGuard('reset'))).toBeTrue();
    });

    it('deep-link после F5: store восстановлен из sessionStorage → шаг пройден', () => {
      sessionStorage.setItem(
        STORAGE_KEYS.recoveryFlow,
        JSON.stringify({ email: 'student01@example.com', resetToken: null }),
      );
      // Новый экземпляр TestBed-инжектора читает sessionStorage при создании.
      expect(flow().hasEmail()).toBeTrue();
      expect(call(recoveryStepGuard('code'))).toBeTrue();
      expectRedirect(call(recoveryStepGuard('reset')), '/recovery');
    });
  });

  describe('homeGuard', () => {
    it('гость → UrlTree /login', () => {
      expectRedirect(call(homeGuard), '/login');
    });

    it('сессия преподавателя → UrlTree /works', async () => {
      await coldStartWithSession(TEACHER_ME);
      expectRedirect(call(homeGuard), ROLE_HOME.teacher);
    });

    it('сессия студента → UrlTree /my-submissions', async () => {
      await coldStartWithSession(STUDENT_ME);
      expectRedirect(call(homeGuard), ROLE_HOME.student);
    });

    it('битая сессия (initSession 401) → UrlTree /login', async () => {
      const pending = auth().initSession();
      httpMock.expectOne(`${apiBase}/auth/me`).flush({ message: 'Не авторизован' }, UNAUTHORIZED);
      httpMock
        .expectOne(`${apiBase}/auth/refresh`)
        .flush({ message: 'Не авторизован' }, UNAUTHORIZED);
      await pending;

      expectRedirect(call(homeGuard), '/login');
    });
  });

  describe('сброс сессии по событию sessionExpired$ (FR-092, ADR-019)', () => {
    it('после события guard видит гостя → /login', async () => {
      await loginAs('teacher', 'teacher123!', TEACHER_ME);
      expect(call(authGuard)).toBeTrue();

      // Публикатор события — интерцептор (неудачный refresh).
      lifecycle.notifySessionExpired();

      expectRedirect(call(authGuard), '/login');
      expect(auth().isAuthenticated()).toBeFalse();
    });
  });
});
