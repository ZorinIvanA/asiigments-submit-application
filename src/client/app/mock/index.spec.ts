/**
 * Юнит-тесты агрегатора мок-слоя и APP_INITIALIZER (C-107, IF-110,
 * ADR-101/ADR-110).
 *
 * Покрытие по unit_test_requirements подзадачи T-107:
 *  - AC aggregator-registers-all: после setupMockLayer вызов любого из 25
 *    доменных методов не даёт ошибки «обработчик не зарегистрирован»;
 *  - идемпотентность: повторный setupMockLayer заменяет обработчики;
 *  - агрегатор подключает сид: первый вызов инициализирует mock.db.v1
 *    полным сидом (33/3/23/4);
 *  - APP_INITIALIZER: appConfig содержит инициализатор, который выполняет
 *    setupMockLayer (сид появляется в mock.db.v1) ровно один раз.
 */
import { APP_INITIALIZER, Injector, runInInjectionContext } from '@angular/core';
import { fakeAsync, TestBed, tick } from '@angular/core/testing';

import { appConfig } from '../core/config/app.config';
import { GroupDto, MOCK_DELAY_MS, PagedResult, STORAGE_KEYS, StudentDto } from '../shared/models';
import { MockApiClient } from './mock-api-client';
import { MockDbData, resetMockDbSeed } from './mock-db';
import { setupMockLayer } from './index';
import { seedFixtures } from './seed';

const DB_KEY = STORAGE_KEYS.mockDb;

function storedDb(): MockDbData {
  return JSON.parse(localStorage.getItem(DB_KEY) ?? 'null') as MockDbData;
}

/**
 * Полный реестр доменных методов (IF-101..IF-107) с минимально валидными
 * параметрами: ошибка «не зарегистрирован» не должна возникнуть ни для
 * одного; прочие исходы (успех, ApiError 401/404/429 от прав доступа и
 * лимитов) означают, что обработчик найден и выполнен.
 */
function allDomainMethods(seed: MockDbData): Array<[method: string, params: unknown]> {
  const lab1 = seed.labs[0];
  const group1 = seed.groups[0];
  const student01 = seed.users.find((u) => u.login === 'student01');
  return [
    ['auth.register', { fullName: 'Новенький Тест', login: 'newuser', email: 'newuser@example.com', password: 'Password#2026', repeatPassword: 'Password#2026' }],
    ['auth.login', { login: 'teacher', password: 'teacher123!' }],
    ['auth.logout', null],
    ['auth.me', null],
    ['auth.recovery.request', { email: 'student01@example.com' }],
    ['auth.recovery.confirm', { email: 'student01@example.com', code: '123456' }],
    ['auth.reset-password', { resetToken: 'missing-token', password: 'NewPass#2026', confirmPassword: 'NewPass#2026' }],
    ['labs.getList', { page: 1 }],
    ['labs.getById', { id: lab1.id }],
    ['labs.create', { semester: 3, number: 1, content: 'Новая работа', assignmentUrl: null, defenseRequired: false }],
    ['labs.update', { id: lab1.id, semester: 1, number: 1, content: lab1.content, assignmentUrl: null, defenseRequired: false }],
    ['labs.remove', { id: lab1.id }],
    ['labs.semesters', null],
    ['groups.getList', { page: 1 }],
    ['groups.create', { name: 'ИК-224' }],
    ['groups.rename', { id: group1.id, name: 'ИК-221а' }],
    ['groups.remove', { id: group1.id }],
    ['groups.getStudents', { id: group1.id, page: 1 }],
    ['students.getList', { page: 1 }],
    ['students.setGroup', { studentId: student01?.id, groupId: null }],
    ['submissions.getGrid', { groupId: group1.id, semester: 1, page: 1 }],
    ['submissions.update', { studentId: student01?.id, labId: lab1.id, submitDate: '2026-09-15', defenseDate: null }],
    ['submissions.getMy', { semester: 1 }],
    ['profile.get', null],
    ['profile.update', { fullName: 'Сидоров Семён Семёнович', email: 'teacher@example.com' }],
  ];
}

describe('setupMockLayer — агрегатор мок-слоя (C-107, IF-110)', () => {
  let client: MockApiClient;

  beforeEach(() => {
    localStorage.clear();
    client = new MockApiClient();
  });

  afterEach(() => {
    localStorage.clear();
    // setupMockLayer подключает глобальный поставщик сида; возвращаем
    // дефолт, чтобы не заражать другие спеки прогона (convenции mock-db.spec).
    resetMockDbSeed();
  });

  describe('AC aggregator-registers-all: реестр полон', () => {
    it('ни один из 25 доменных методов не отвечает «обработчик не зарегистрирован»', fakeAsync(() => {
      setupMockLayer(client);
      const methods = allDomainMethods(seedFixtures());
      expect(methods.length).toBe(25);

      for (const [method, params] of methods) {
        let failure: unknown = null;
        client.call(method, params).catch((error: unknown) => {
          failure = error;
        });
        tick(MOCK_DELAY_MS);
        const message = failure instanceof Error ? failure.message : '';
        expect(message).withContext(`метод ${method}`).not.toContain('не зарегистрирован');
      }
    }));

    it('setupMockLayer без клиента вне инжекционного контекста падает сразу', () => {
      expect(() => setupMockLayer()).toThrow();
    });
  });

  describe('идемпотентность и подключение сида', () => {
    /**
     * Входит демо-преподавателем: labs.* требуют сессию teacher (IF-103),
     * без неё обработчик отклоняется до первого обращения к БД.
     */
    function loginTeacher(): unknown {
      let failure: unknown = null;
      client.call('auth.login', { login: 'teacher', password: 'teacher123!' }).then(
        () => undefined,
        (error: unknown) => (failure = error),
      );
      tick(MOCK_DELAY_MS);
      return failure;
    }

    it('повторный setupMockLayer заменяет обработчики — вызовы продолжают работать', fakeAsync(() => {
      setupMockLayer(client);
      setupMockLayer(client);
      expect(loginTeacher()).toBeNull();

      let semesters: number[] | undefined;
      let failure: unknown = null;
      client.call<number[]>('labs.semesters', null).then(
        (value) => (semesters = value),
        (error: unknown) => (failure = error),
      );
      tick(MOCK_DELAY_MS);

      expect(failure).toBeNull();
      // Сид применён: работы существуют ровно в двух семестрах.
      expect(semesters).toEqual([1, 2]);
    }));

    it('первый вызов через клиент инициализирует mock.db.v1 полным сидом', fakeAsync(() => {
      expect(localStorage.getItem(DB_KEY)).toBeNull();
      setupMockLayer(client);
      expect(loginTeacher()).toBeNull();

      let failure: unknown = null;
      client.call<number[]>('labs.semesters', null).then(
        () => undefined,
        (error: unknown) => (failure = error),
      );
      tick(MOCK_DELAY_MS);
      expect(failure).toBeNull();

      const db = storedDb();
      expect(db.users.length).toBe(33);
      expect(db.groups.length).toBe(3);
      expect(db.labs.length).toBe(23);
      expect(db.submissions.length).toBe(4);
      expect(db.recoveryCodes).toEqual([]);
      expect(db.resetTokens).toEqual([]);
    }));

    it('AC seed-counts: через groups.getList + students.getList — 25/5/0, всего 32, без группы 2', fakeAsync(() => {
      setupMockLayer(client);
      expect(loginTeacher()).toBeNull();

      let groups: GroupDto[] | undefined;
      let groupsFailure: unknown = null;
      client.call<GroupDto[]>('groups.getList', null).then(
        (value) => (groups = value),
        (error: unknown) => (groupsFailure = error),
      );
      tick(MOCK_DELAY_MS);
      expect(groupsFailure).toBeNull();
      expect(groups?.map((g) => [g.name, g.studentCount])).toEqual([
        ['ИК-221', 25],
        ['ИК-222', 5],
        ['ИК-223', 0],
      ]);

      let allStudents: PagedResult<StudentDto> | undefined;
      let allFailure: unknown = null;
      client.call<PagedResult<StudentDto>>('students.getList', { page: 1 }).then(
        (value) => (allStudents = value),
        (error: unknown) => (allFailure = error),
      );
      tick(MOCK_DELAY_MS);
      expect(allFailure).toBeNull();
      expect(allStudents?.total).toBe(32);

      let withoutGroup: PagedResult<StudentDto> | undefined;
      client
        .call<PagedResult<StudentDto>>('students.getList', { groupId: 'none', page: 1 })
        .then((value) => (withoutGroup = value), () => undefined);
      tick(MOCK_DELAY_MS);
      expect(withoutGroup?.total).toBe(2);
    }));
  });

  describe('APP_INITIALIZER (ADR-110: единственная правка app.config.ts)', () => {
    /**
     * Запускает все APP_INITIALIZER провайдеров, как делает
     * ApplicationInitStatus: каждый — один раз, в инжекционном контексте.
     * (Точное число инициализаторов не проверяем: providePrimeNG добавляет
     * свой собственный — это внутреннее дело сторонних провайдеров.)
     */
    function runAppInitializers(): number {
      const initializers = TestBed.inject(APP_INITIALIZER, []);
      expect(initializers.length).withContext('есть APP_INITIALIZER').toBeGreaterThan(0);
      const injector = TestBed.inject(Injector);
      runInInjectionContext(injector, () => {
        for (const initializer of initializers) {
          initializer();
        }
      });
      return initializers.length;
    }

    it('инициализаторы appConfig выполняют setupMockLayer — root-клиент получает сид и обработчики', fakeAsync(() => {
      TestBed.configureTestingModule({ providers: appConfig.providers });
      expect(localStorage.getItem(DB_KEY)).toBeNull();

      runAppInitializers();

      // setupMockLayer только подключает сид и обработчики; запись ключа
      // происходит при первом обращении к БД — через root-клиента (тот же
      // экземпляр, что у сервисов core). Демо-вход подтверждает сид-учётку,
      // labs.semesters — подключённый обработчик домена Labs.
      const client = TestBed.inject(MockApiClient);
      let loginFailure: unknown = null;
      client
        .call('auth.login', { login: 'teacher', password: 'teacher123!' })
        .then(() => undefined, (error: unknown) => (loginFailure = error));
      tick(MOCK_DELAY_MS);
      expect(loginFailure).toBeNull();

      let semesters: number[] | undefined;
      client
        .call<number[]>('labs.semesters', null)
        .then((value) => (semesters = value), () => undefined);
      tick(MOCK_DELAY_MS);

      expect(semesters).toEqual([1, 2]);
      const db = storedDb();
      expect(db.users.length).toBe(33);
      expect(db.labs.length).toBe(23);
    }));
  });
});
