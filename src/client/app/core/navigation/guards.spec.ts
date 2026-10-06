/**
 * Юнит-тесты навигационных guards (C-108, контракт IF-108, FR-4.8,
 * ADR-105/ADR-106): authGuard (гость → /login, синхронный кэш,
 * однократная догрузка loadMe, битая сессия → очистка + /login),
 * roleGuard (чужая роль → /403, гость → /login), guestGuard (обе роли
 * → домашний маршрут по ROLE_HOME), recoveryStepGuard (все комбинации
 * store), homeGuard (обе роли и гость). Редиректы проверяются как UrlTree;
 * guards запускаются через TestBed.runInInjectionContext. Полный конвейер —
 * реальные MockApiClient + registerAuthHandlers на фиксированных часах,
 * задержка 500 мс сжимается fakeAsync/tick.
 */
import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  CanActivateFn,
  provideRouter,
  RouterStateSnapshot,
  UrlTree,
} from '@angular/router';

import { registerAuthHandlers } from '../../mock/auth/handlers';
import { SessionStore } from '../../mock/auth/session-store';
import { MockApiClient } from '../../mock/mock-api-client';
import {
  MockDbData,
  configureMockDbSeed,
  emptyMockDbData,
  resetMockDbSeed,
} from '../../mock/mock-db';
import { STORAGE_KEYS } from '../../shared/models';
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

const T0 = 1_758_240_000_000;

const TEACHER_ID = 'aaaaaaaa-1111-4111-8111-111111111111';
const STUDENT_ID = 'bbbbbbbb-2222-4222-8222-222222222222';
const GHOST_ID = 'ffffffff-ffff-4fff-8fff-ffffffffffff';

function makeSeed(): MockDbData {
  return {
    ...emptyMockDbData(),
    users: [
      {
        id: TEACHER_ID,
        login: 'teacher',
        email: 'teacher@example.com',
        fullName: 'Сидоров Семён Семёнович',
        role: 'teacher',
        groupId: null,
        password: 'teacher123!',
      },
      {
        id: STUDENT_ID,
        login: 'student01',
        email: 'student01@example.com',
        fullName: 'Иванов Иван Иванович 01',
        role: 'student',
        groupId: null,
        password: 'student123!',
      },
    ],
  };
}

const ROUTE = {} as ActivatedRouteSnapshot;
const STATE = { url: '/works' } as RouterStateSnapshot;

describe('Навигационные guards (IF-108)', () => {
  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    spyOn(console, 'error');
    configureMockDbSeed(() => makeSeed());
    const client = new MockApiClient();
    registerAuthHandlers(client, () => T0);
    TestBed.configureTestingModule({
      providers: [{ provide: MockApiClient, useValue: client }, provideRouter([])],
    });
  });

  afterEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    resetMockDbSeed();
  });

  function auth(): AuthService {
    return TestBed.inject(AuthService);
  }

  function session(): SessionStore {
    return TestBed.inject(SessionStore);
  }

  function flow(): RecoveryFlowStore {
    return TestBed.inject(RecoveryFlowStore);
  }

  function call(guard: CanActivateFn): Promise<boolean | UrlTree> {
    const pending = TestBed.runInInjectionContext(() => guard(ROUTE, STATE));
    return Promise.resolve(pending) as Promise<boolean | UrlTree>;
  }

  /** Один прогон guard'а с дожиданием мок-задержки (500 мс). */
  function run(guard: CanActivateFn): boolean | UrlTree {
    let result!: boolean | UrlTree;
    fakeAsync(() => {
      call(guard).then((value) => (result = value));
      tick(500);
    })();
    return result;
  }

  /** Редирект по IF-108 проверяется как UrlTree с точным путём. */
  function expectRedirect(result: boolean | UrlTree, path: string): void {
    expect(result instanceof UrlTree).withContext(`UrlTree ${path}`).toBeTrue();
    expect((result as UrlTree).toString()).toBe(path);
  }

  /** Сессия «после F5»: ключ есть, кэш currentUser пуст. */
  function setSession(userId: string): void {
    session().setUserId(userId);
    expect(auth().isAuthenticated()).toBeTrue();
    expect(auth().currentUser()).toBeNull();
  }

  /** Вход через полный конвейер — заполняет сессию и кэш currentUser. */
  function primeCache(login: string, password: string): void {
    fakeAsync(() => {
      auth().login({ login, password });
      tick(500);
    })();
  }

  describe('authGuard (AC auth-guard-guest)', () => {
    it('без сессии → UrlTree /login, без вызова мока', () => {
      const spy = spyOn(auth(), 'loadMe').and.callThrough();
      expectRedirect(run(authGuard), '/login');
      expect(spy).not.toHaveBeenCalled();
      expect(console.error).not.toHaveBeenCalled();
    });

    it('сессия + заполненный кэш → true синхронно, loadMe не нужен', () => {
      primeCache('teacher', 'teacher123!');
      const spy = spyOn(auth(), 'loadMe').and.callThrough();
      expect(run(authGuard)).toBeTrue();
      expect(spy).not.toHaveBeenCalled();
    });

    it('сессия после F5 → однократная догрузка loadMe, дальше синхронно', fakeAsync(() => {
      setSession(TEACHER_ID);
      const spy = spyOn(auth(), 'loadMe').and.callThrough();

      let first: boolean | UrlTree | undefined;
      call(authGuard).then((value) => (first = value));
      tick(500);

      let second: boolean | UrlTree | undefined;
      call(authGuard).then((value) => (second = value));
      tick(0);

      expect(first).toBeTrue();
      expect(second).withContext('кэш currentUser заполнен').toBeTrue();
      expect(spy).toHaveBeenCalledTimes(1);
    }));

    it('битая сессия (пользователь удалён) → очистка сессии и UrlTree /login', () => {
      setSession(GHOST_ID);
      expectRedirect(run(authGuard), '/login');
      expect(session().hasUser()).withContext('битая сессия очищена').toBeFalse();
      expect(localStorage.getItem(STORAGE_KEYS.session)).toBeNull();
      expect(auth().currentUser()).toBeNull();
    });
  });

  describe('roleGuard (AC role-guard-403)', () => {
    it('сессия студента, roleGuard("teacher") → UrlTree /403, loadMe один раз', fakeAsync(() => {
      setSession(STUDENT_ID);
      const spy = spyOn(auth(), 'loadMe').and.callThrough();

      let result: boolean | UrlTree | undefined;
      call(roleGuard('teacher')).then((value) => (result = value));
      tick(500);

      expectRedirect(result!, '/403');
      expect(spy).withContext('догрузка me однократна (ADR-105)').toHaveBeenCalledTimes(1);
    }));

    it('своя роль → true (сессия после F5, одна догрузка)', fakeAsync(() => {
      setSession(TEACHER_ID);
      const spy = spyOn(auth(), 'loadMe').and.callThrough();

      let result: boolean | UrlTree | undefined;
      call(roleGuard('teacher')).then((value) => (result = value));
      tick(500);

      expect(result).toBeTrue();
      expect(spy).toHaveBeenCalledTimes(1);
    }));

    it('список ролей: student входит в roleGuard("teacher", "student")', fakeAsync(() => {
      setSession(STUDENT_ID);
      let result: boolean | UrlTree | undefined;
      call(roleGuard('teacher', 'student')).then((value) => (result = value));
      tick(500);
      expect(result).toBeTrue();
    }));

    it('без сессии → UrlTree /login (ветка authGuard, а не /403)', () => {
      expectRedirect(run(roleGuard('teacher')), '/login');
    });

    it('чужая роль при заполненном кэше → UrlTree /403 без догрузки', () => {
      primeCache('student01', 'student123!');
      const spy = spyOn(auth(), 'loadMe').and.callThrough();
      expectRedirect(run(roleGuard('teacher')), '/403');
      expect(spy).not.toHaveBeenCalled();
    });
  });

  describe('guestGuard', () => {
    it('сессия преподавателя → UrlTree /works (ROLE_HOME.teacher)', () => {
      setSession(TEACHER_ID);
      expectRedirect(run(guestGuard), ROLE_HOME.teacher);
    });

    it('сессия студента → UrlTree /my-submissions (ROLE_HOME.student)', () => {
      setSession(STUDENT_ID);
      expectRedirect(run(guestGuard), ROLE_HOME.student);
    });

    it('гость → вход разрешён (true)', () => {
      expect(run(guestGuard)).toBeTrue();
    });

    it('битая сессия → гость допущен, сессия очищена', () => {
      setSession(GHOST_ID);
      expect(run(guestGuard)).toBeTrue();
      expect(session().hasUser()).withContext('битая сессия очищена').toBeFalse();
    });
  });

  describe('recoveryStepGuard (AC recovery-guard-restart)', () => {
    it('пустой store: /recovery/code и /reset-password → /recovery без ошибок консоли', () => {
      expect(flow().hasEmail()).toBeFalse();
      expect(flow().hasResetToken()).toBeFalse();
      expectRedirect(run(recoveryStepGuard('code')), '/recovery');
      expectRedirect(run(recoveryStepGuard('reset')), '/recovery');
      expect(console.error).not.toHaveBeenCalled();
    });

    it('шаг "code": есть email → true; есть email и токен → true', () => {
      flow().setEmail('student01@example.com');
      expect(run(recoveryStepGuard('code'))).toBeTrue();
      flow().setResetToken('reset-token-1');
      expect(run(recoveryStepGuard('code'))).withContext('email остаётся').toBeTrue();
    });

    it('шаг "reset": только email → /recovery; email и токен → true', () => {
      flow().setEmail('student01@example.com');
      expectRedirect(run(recoveryStepGuard('reset')), '/recovery');
      flow().setResetToken('reset-token-1');
      expect(run(recoveryStepGuard('reset'))).toBeTrue();
    });

    it('deep-link после F5: store восстановлен из sessionStorage → шаг пройден', () => {
      sessionStorage.setItem(
        STORAGE_KEYS.recoveryFlow,
        JSON.stringify({ email: 'student01@example.com', resetToken: null }),
      );
      // Новый экземпляр TestBed-инжектора читает sessionStorage при создании.
      expect(flow().hasEmail()).toBeTrue();
      expect(run(recoveryStepGuard('code'))).toBeTrue();
      expectRedirect(run(recoveryStepGuard('reset')), '/recovery');
    });
  });

  describe('homeGuard', () => {
    it('гость → UrlTree /login', () => {
      expectRedirect(run(homeGuard), '/login');
    });

    it('сессия преподавателя → UrlTree /works', () => {
      setSession(TEACHER_ID);
      expectRedirect(run(homeGuard), ROLE_HOME.teacher);
    });

    it('сессия студента → UrlTree /my-submissions', () => {
      setSession(STUDENT_ID);
      expectRedirect(run(homeGuard), ROLE_HOME.student);
    });

    it('битая сессия → UrlTree /login и очистка сессии', () => {
      setSession(GHOST_ID);
      expectRedirect(run(homeGuard), '/login');
      expect(session().hasUser()).toBeFalse();
    });
  });
});
