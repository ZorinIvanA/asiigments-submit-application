/**
 * Юнит-тесты AuthService (C-101, IF-101, ADR-105/ADR-106): кэш currentUser
 * (login/register/loadMe), logout очищает сессию и кэш, синхронная
 * isAuthenticated по ключу сессии, loadMe при отсутствии/битой сессии,
 * ведение потока восстановления (RecoveryFlowStore): email/resetToken,
 * полная очистка при успехе и терминальная ветка токена. Полный конвейер
 * вызова — реальные MockApiClient + registerAuthHandlers на фиксированных
 * часах; задержка 500 мс сжимается fakeAsync/tick.
 */
import { TestBed, fakeAsync, tick } from '@angular/core/testing';

import { SessionStore } from '../../mock/auth/session-store';
import { MeDto, registerAuthHandlers } from '../../mock/auth/handlers';
import { MockApiClient } from '../../mock/mock-api-client';
import {
  MockDbData,
  configureMockDbSeed,
  emptyMockDbData,
  resetMockDbSeed,
} from '../../mock/mock-db';
import { ApiError, STORAGE_KEYS } from '../../shared/models';
import { AuthService } from './auth.service';
import { RecoveryFlowStore } from './recovery-flow-store';

const T0 = 1_758_240_000_000;
const MINUTE = 60 * 1000;

const TEACHER_ID = 'aaaaaaaa-1111-4111-8111-111111111111';
const STUDENT_ID = 'bbbbbbbb-2222-4222-8222-222222222222';
const GROUP_ID = 'cccccccc-3333-4333-8333-333333333333';

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
        groupId: GROUP_ID,
        password: 'student123!',
      },
    ],
    groups: [{ id: GROUP_ID, name: 'ИК-221', studentCount: 1 }],
  };
}

/** Строка [mock-email] последнего запроса кода (формат IF-101/§4.2). */
function lastLoggedCode(): string {
  const calls = (console.info as jasmine.Spy).calls.allArgs();
  const last = calls[calls.length - 1][0] as string;
  const match = /\[mock-email\] Код восстановления для (.+): (\d{6})$/.exec(last);
  expect(match).withContext(`формат строки: ${last}`).not.toBeNull();
  return match![2];
}

describe('AuthService — домен Auth над мок-конвейером (IF-101)', () => {
  let clock: number;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    spyOn(console, 'info');
    clock = T0;
    configureMockDbSeed(() => makeSeed());
    const client = new MockApiClient();
    registerAuthHandlers(client, () => clock);
    TestBed.configureTestingModule({
      providers: [{ provide: MockApiClient, useValue: client }],
    });
  });

  afterEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    resetMockDbSeed();
  });

  function service(): AuthService {
    return TestBed.inject(AuthService);
  }

  function flow(): RecoveryFlowStore {
    return TestBed.inject(RecoveryFlowStore);
  }

  function session(): SessionStore {
    return TestBed.inject(SessionStore);
  }

  describe('вход и кэш currentUser (AC login-успех)', () => {
    it('login: MeDto с ролью, сессия установлена, currentUser обновлён', fakeAsync(() => {
      const auth = service();
      expect(auth.isAuthenticated()).toBeFalse();

      let me: MeDto | null = null;
      auth.login({ login: 'teacher', password: 'teacher123!' }).then((value) => (me = value));
      tick(500);

      expect(me!).toEqual({
        login: 'teacher',
        fullName: 'Сидоров Семён Семёнович',
        role: 'teacher',
        groupName: null,
      } satisfies MeDto);
      expect(auth.currentUser()).toEqual(me);
      expect(auth.isAuthenticated()).withContext('сессия по ключу').toBeTrue();
      expect(localStorage.getItem(STORAGE_KEYS.session)).toBe(TEACHER_ID);
    }));

    it('login: единый 401 «Неверный логин или пароль» и для неверного пароля, и для чужого логина', fakeAsync(() => {
      const auth = service();
      const collect: unknown[] = [];
      auth.login({ login: 'teacher', password: 'неверный' }).catch((e: unknown) => collect.push(e));
      tick(500);
      auth.login({ login: 'ghost', password: 'что-угодно' }).catch((e: unknown) => collect.push(e));
      tick(500);

      const expected: ApiError = { status: 401, body: { message: 'Неверный логин или пароль' } };
      expect(collect.length).toBe(2);
      expect(collect[0]).toEqual(expected);
      expect(collect[1]).toEqual(expected);
      expect(auth.currentUser()).withContext('кэш не заполняется отказом').toBeNull();
      expect(auth.isAuthenticated()).toBeFalse();
    }));

    it('register: успех возвращает student-профиль и заполняет кэш и сессию', fakeAsync(() => {
      const auth = service();
      let me: MeDto | null = null;
      auth
        .register({
          fullName: 'Петров Пётр Петрович',
          login: 'student33',
          email: 'student33@example.com',
          password: 'Password#2026',
          repeatPassword: 'Password#2026',
        })
        .then((value) => (me = value));
      tick(500);

      expect(me!.role).toBe('student');
      expect(auth.currentUser()).toEqual(me);
      expect(auth.isAuthenticated()).toBeTrue();
    }));
  });

  describe('logout / isAuthenticated / loadMe', () => {
    it('logout очищает сессию и кэш; isAuthenticated синхронен по ключу', fakeAsync(() => {
      const auth = service();
      auth.login({ login: 'teacher', password: 'teacher123!' });
      tick(500);
      expect(auth.isAuthenticated()).toBeTrue();

      auth.logout();
      tick(500);

      expect(auth.currentUser()).toBeNull();
      expect(auth.isAuthenticated()).withContext('сессия очищена').toBeFalse();
      expect(localStorage.getItem(STORAGE_KEYS.session)).toBeNull();
    }));

    it('isAuthenticated — синхронная проверка наличия ключа, без вызова мока', () => {
      session().setUserId(STUDENT_ID);
      expect(service().isAuthenticated()).toBeTrue();
    });

    it('loadMe без сессии: null, кэш пуст, лишних вызовов нет', fakeAsync(() => {
      const auth = service();
      let me: MeDto | null = null;
      auth.loadMe().then((value) => (me = value));
      tick(0);
      expect(me).toBeNull();
      expect(auth.currentUser()).toBeNull();
      expect(localStorage.getItem(STORAGE_KEYS.session)).toBeNull();
    }));

    it('loadMe по сессии: MeDto и кэш заполнены', fakeAsync(() => {
      const auth = service();
      session().setUserId(STUDENT_ID);
      let me: MeDto | null = null;
      auth.loadMe().then((value) => (me = value));
      tick(500);
      expect(me!).toEqual({
        login: 'student01',
        fullName: 'Иванов Иван Иванович 01',
        role: 'student',
        groupName: 'ИК-221',
      } satisfies MeDto);
      expect(auth.currentUser()).toEqual(me);
    }));

    it('loadMe с битой сессией (пользователя нет): null, сессия очищена, кэш пуст', fakeAsync(() => {
      const auth = service();
      session().setUserId('ffffffff-ffff-4fff-8fff-ffffffffffff');
      let me: MeDto | null = null;
      auth.loadMe().then((value) => (me = value));
      tick(500);
      expect(me).toBeNull();
      expect(session().hasUser()).withContext('битая сессия очищена').toBeFalse();
      expect(auth.currentUser()).toBeNull();
    }));
  });

  describe('поток восстановления (IF-101/IF-102)', () => {
    it('requestRecoveryCode: 200 и email запоминается в потоке — в т.ч. для незарегистрированного', fakeAsync(() => {
      const auth = service();
      auth.requestRecoveryCode('student01@example.com');
      tick(500);
      expect(flow().email()).toBe('student01@example.com');

      auth.requestRecoveryCode('ghost@example.com');
      tick(500);
      expect(flow().email()).withContext('запрос всегда 200 (§9)').toBe('ghost@example.com');
      expect(console.info).toHaveBeenCalledWith(
        jasmine.stringContaining('[mock-email] Код восстановления для ghost@example.com:'),
      );
    }));

    it('confirmRecoveryCode: код из «письма» подходит → resetToken в ответе и в потоке', fakeAsync(() => {
      const auth = service();
      auth.requestRecoveryCode('student01@example.com');
      tick(500);
      const code = lastLoggedCode();

      let result: { resetToken: string } | undefined;
      auth.confirmRecoveryCode('student01@example.com', code).then((value) => (result = value));
      tick(500);

      expect(result).toBeDefined();
      expect(result!.resetToken).toBeTruthy();
      expect(flow().resetToken()).toBe(result!.resetToken);
      expect(flow().hasResetToken()).toBeTrue();
    }));

    it('confirmRecoveryCode: неверный код → 400 «Код восстановления не подходит», поток без токена', fakeAsync(() => {
      const auth = service();
      auth.requestRecoveryCode('student01@example.com');
      tick(500);

      let rejection: unknown = null;
      auth.confirmRecoveryCode('student01@example.com', '000000').catch((e: unknown) => (rejection = e));
      tick(500);

      expect(rejection).toEqual({
        status: 400,
        body: { message: 'Код восстановления не подходит' },
      } satisfies ApiError);
      expect(flow().hasResetToken()).toBeFalse();
      expect(flow().hasEmail()).withContext('email сохраняется — можно переотправить код').toBeTrue();
    }));

    it('resetPassword: успех меняет пароль и полностью очищает поток', fakeAsync(() => {
      const auth = service();
      auth.requestRecoveryCode('student01@example.com');
      tick(500);
      let result: { resetToken: string } | undefined;
      auth.confirmRecoveryCode('student01@example.com', lastLoggedCode()).then((value) => (result = value));
      tick(500);

      auth.resetPassword(result!.resetToken, 'NewPassword#2026', 'NewPassword#2026');
      tick(500);

      expect(flow().hasEmail()).withContext('поток завершён — очищен').toBeFalse();
      expect(flow().hasResetToken()).toBeFalse();

      // Новый пароль действителен: вход с ним успешен.
      let me: MeDto | null = null;
      auth.login({ login: 'student01', password: 'NewPassword#2026' }).then((value) => (me = value));
      tick(500);
      expect(me!.role).toBe('student');
    }));

    it('resetPassword: просроченный токен → 400 «Ссылка…», в потоке остаётся только email', fakeAsync(() => {
      const auth = service();
      auth.requestRecoveryCode('student01@example.com');
      tick(500);
      let result: { resetToken: string } | undefined;
      auth.confirmRecoveryCode('student01@example.com', lastLoggedCode()).then((value) => (result = value));
      tick(500);

      clock = T0 + 16 * MINUTE; // TTL токена — 15 минут
      let rejection: unknown = null;
      auth
        .resetPassword(result!.resetToken, 'NewPassword#2026', 'NewPassword#2026')
        .catch((e: unknown) => (rejection = e));
      tick(500);

      expect(rejection).toEqual({
        status: 400,
        body: { message: 'Ссылка восстановления недействительна или истекла' },
      } satisfies ApiError);
      expect(flow().resetToken()).withContext('токен удалён из потока').toBeNull();
      expect(flow().email()).withContext('email сохраняется').toBe('student01@example.com');
    }));

    it('resetPassword: полевая ошибка пароля — поток не трогается (токен ещё жив), отказ пробрасывается', fakeAsync(() => {
      const auth = service();
      auth.requestRecoveryCode('student01@example.com');
      tick(500);
      let result: { resetToken: string } | undefined;
      auth.confirmRecoveryCode('student01@example.com', lastLoggedCode()).then((value) => (result = value));
      tick(500);

      let rejection: unknown = null;
      auth.resetPassword(result!.resetToken, 'слабый', 'слабый').catch((e: unknown) => (rejection = e));
      tick(500);

      const apiError = rejection as ApiError;
      expect(apiError.status).toBe(400);
      expect(apiError.body.message).toBe('Данные заполнены неверно');
      expect(apiError.body.errors!['password']).toBeDefined();
      expect(flow().hasResetToken()).withContext('нетерминальная ветка — токен жив').toBeTrue();
    }));
  });
});
