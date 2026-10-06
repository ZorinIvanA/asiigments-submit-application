/**
 * Юнит-тесты мок-БД (C-002, FR-003, data_design MockDb): ленивая
 * инициализация, сид при отсутствии ключа, точка подключения сида T-004,
 * защитный re-seed повреждённых данных, персистентность мутаций (AC
 * «Персистентность мутации»), атомарность mutate, изоляция read.
 */
import { Group, STORAGE_KEYS, User } from '../shared/models';
import {
  configureMockDbSeed,
  emptyMockDbData,
  MockDb,
  MockDbData,
  resetMockDbSeed,
} from './mock-db';

const DB_KEY = STORAGE_KEYS.mockDb;

function makeGroup(name: string): Group {
  return { id: '11111111-2222-4333-8444-555555555555', name, studentCount: 0 };
}

function makeUser(login: string): User {
  return {
    id: '0b0b0b0b-0b0b-4b0b-8b0b-0b0b0b0b0b0b',
    login,
    email: `${login}@example.com`,
    fullName: `Пользователь ${login}`,
    role: 'student',
    groupId: null,
    password: 'Password#2026',
  };
}

function stored(): MockDbData {
  return JSON.parse(localStorage.getItem(DB_KEY) ?? 'null') as MockDbData;
}

describe('MockDb — хранилище mock.db.v1 (C-002, FR-003)', () => {
  beforeEach(() => {
    localStorage.clear();
    resetMockDbSeed();
  });

  afterEach(() => {
    localStorage.clear();
    resetMockDbSeed();
  });

  describe('ленивая инициализация', () => {
    it('конструктор не пишет в localStorage; первое чтение инициализирует ключ', () => {
      const db = new MockDb();
      expect(localStorage.getItem(DB_KEY)).toBeNull(); // обращения к хранилищу ещё не было
      const data = db.read();
      expect(data).toEqual(emptyMockDbData());
      expect(localStorage.getItem(DB_KEY)).toBe(JSON.stringify(emptyMockDbData()));
    });

    it('второе чтение не перечитывает localStorage: правки ключа игнорируются до reset', () => {
      const db = new MockDb();
      expect(db.read().users).toEqual([]);
      localStorage.setItem(DB_KEY, JSON.stringify({ ...emptyMockDbData(), users: [makeUser('hack')] }));
      expect(db.read().users).toEqual([]); // память уже инициализирована
      db.reset();
      expect(db.read().users.length).toBe(1); // после сброса кэша — перечитано
    });
  });

  describe('инициализация сидом', () => {
    it('отсутствие ключа → пустой сид по умолчанию (до подключения T-004)', () => {
      const db = new MockDb();
      expect(db.read()).toEqual(emptyMockDbData());
      expect(stored()).toEqual(emptyMockDbData());
    });

    it('точка подключения SeedFixtures (T-004): configureMockDbSeed задаёт данные инициализации', () => {
      const seedData: MockDbData = {
        ...emptyMockDbData(),
        users: [makeUser('student01')],
        groups: [makeGroup('ИК-221')],
      };
      configureMockDbSeed(() => structuredClone(seedData));
      const db = new MockDb();
      expect(db.read().users).toEqual([makeUser('student01')]);
      expect(db.read().groups).toEqual([makeGroup('ИК-221')]);
      expect(stored().groups[0]?.name).toBe('ИК-221');
    });

    it('сид вызывается ровно один раз на жизненный цикл кэша', () => {
      let calls = 0;
      configureMockDbSeed(() => {
        calls++;
        return emptyMockDbData();
      });
      const db = new MockDb();
      db.read();
      db.read();
      db.mutate((data) => data.groups.push(makeGroup('ИК-222')));
      expect(calls).toBe(1);
    });
  });

  describe('защитный re-seed повреждённых данных', () => {
    it('битый JSON → предупреждение в консоль и откат к сиду с перезаписью ключа', () => {
      spyOn(console, 'warn');
      localStorage.setItem(DB_KEY, '{это не json');
      const db = new MockDb();
      expect(db.read()).toEqual(emptyMockDbData());
      expect(console.warn).toHaveBeenCalledTimes(1);
      expect(String((console.warn as jasmine.Spy).calls.argsFor(0)[0])).toContain(DB_KEY);
      expect(stored()).toEqual(emptyMockDbData()); // повреждённое значение заменено
    });

    it('JSON без ожидаемой структуры → тот же защитный путь', () => {
      spyOn(console, 'warn');
      for (const broken of ['null', '"строка"', '[]', '{"users":"не массив"}', '{"users":[],"groups":[]}']) {
        localStorage.clear();
        localStorage.setItem(DB_KEY, broken);
        const db = new MockDb();
        expect(db.read()).toEqual(emptyMockDbData());
      }
      expect(console.warn).toHaveBeenCalledTimes(5);
    });

    it('re-seed берёт данные подключённого сида, а не только пустые', () => {
      configureMockDbSeed(() => ({ ...emptyMockDbData(), groups: [makeGroup('ИК-941')] }));
      spyOn(console, 'warn');
      localStorage.setItem(DB_KEY, '{{{');
      expect(new MockDb().read().groups[0]?.name).toBe('ИК-941');
      expect(stored().groups[0]?.name).toBe('ИК-941');
    });
  });

  describe('валидное сохранённое состояние', () => {
    it('читается как есть: без предупреждения и без перезаписи ключа', () => {
      const persisted = JSON.stringify({ ...emptyMockDbData(), users: [makeUser('student07')] });
      localStorage.setItem(DB_KEY, persisted);
      spyOn(console, 'warn');
      const db = new MockDb();
      expect(db.read().users[0]?.login).toBe('student07');
      expect(console.warn).not.toHaveBeenCalled();
      expect(localStorage.getItem(DB_KEY)).toBe(persisted); // ключ не тронут
    });
  });

  describe('мутации (AC «Персистентность мутации»)', () => {
    it('изменение сериализуется в localStorage после каждого mutate', () => {
      const db = new MockDb();
      db.mutate((data) => data.groups.push(makeGroup('ИК-224')));
      expect(stored().groups.map((g) => g.name)).toEqual(['ИК-224']); // AC: чтение ключа содержит изменение

      db.mutate((data) => data.groups.push(makeGroup('ИК-225')));
      expect(stored().groups.map((g) => g.name)).toEqual(['ИК-224', 'ИК-225']);
    });

    it('мутации по разным коллекциям сериализуются одним JSON-объектом (ADR-012)', () => {
      const db = new MockDb();
      db.mutate((data) => data.users.push(makeUser('student01')));
      db.mutate((data) => data.rateLimitCounters.loginFailures['student01'] = [12345]);
      const snapshot = stored();
      expect(snapshot.users.length).toBe(1);
      expect(snapshot.rateLimitCounters.loginFailures['student01']).toEqual([12345]);
    });

    it('mutate возвращает результат функции', () => {
      const db = new MockDb();
      const created = db.mutate((data) => {
        const group = makeGroup('ИК-226');
        data.groups.push(group);
        return group.name;
      });
      expect(created).toBe('ИК-226');
    });

    it('атомарность: выброс внутри mutate не меняет ни память, ни localStorage', () => {
      const db = new MockDb();
      db.mutate((data) => data.groups.push(makeGroup('ИК-227')));
      const before = localStorage.getItem(DB_KEY);
      expect(() =>
        db.mutate((data) => {
          data.groups.push(makeGroup('мусор'));
          data.users.push(makeUser('мусор'));
          throw new Error('отказ обработчика');
        }),
      ).toThrowError('отказ обработчика');
      expect(db.read().groups.map((g) => g.name)).toEqual(['ИК-227']); // память не тронута
      expect(localStorage.getItem(DB_KEY)).toBe(before); // ключ не перезаписан
    });
  });

  describe('изоляция снимков', () => {
    it('read() возвращает клон: правка результата не влияет на память и хранилище', () => {
      const db = new MockDb();
      const snapshot = db.read();
      snapshot.users.push(makeUser('ghost'));
      snapshot.groups.push(makeGroup('ghost'));
      const before = localStorage.getItem(DB_KEY);
      expect(db.read().users).toEqual([]);
      expect(db.read().groups).toEqual([]);
      expect(localStorage.getItem(DB_KEY)).toBe(before);
    });

    it('новый экземпляр после правки ключа читает обновлённые данные (гидратация при старте)', () => {
      const first = new MockDb();
      first.mutate((data) => data.groups.push(makeGroup('ИК-228')));
      const second = new MockDb(); // например, после перезагрузки страницы
      expect(second.read().groups.map((g) => g.name)).toEqual(['ИК-228']);
    });
  });
});
