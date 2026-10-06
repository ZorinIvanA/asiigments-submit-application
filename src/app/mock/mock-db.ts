/**
 * Мок-БД (C-002, FR-003, ADR-012): единый JSON-объект в localStorage по ключу
 * mock.db.v1 — {users, groups, labs, submissions, recoveryCodes, resetTokens,
 * rateLimitCounters}. Сессия здесь НЕ хранится (единственное место — отдельный
 * ключ mock.session.userId, AR-003; доступ к нему — только у домена Auth).
 *
 * Правила (data_design):
 *  - чтение при первом обращении (ленивая инициализация в памяти);
 *  - отсутствие ключа → инициализация сидом;
 *  - запись JSON.stringify после каждой мутации (через mutate — см. ниже);
 *  - ошибка парсинга/структуры → защитный re-seed с консоль-предупреждением
 *    (единственная точка console.warn в мок-слое; без персональных данных —
 *    NFR-007);
 *  - индексов нет: линейные поиск/сортировка (объёмы демо — сотни записей).
 *
 * Точка подключения сида T-004 (SeedFixtures): configureMockDbSeed(...)
 * заменяет поставщика данных инициализации; до подключения используется
 * пустой сид emptyMockDbData().
 */
import { Group, Lab, STORAGE_KEYS, Submission, User } from '../shared/models';

/**
 * Код восстановления — запись recoveryCodes мок-БД (спека §4.2: 6-значный
 * код, TTL 10 минут, одноразовый, не более 5 попыток подбора на код).
 * Значение хранится как есть — то же осознанное ограничение демо-режима,
 * что и открытый пароль User (NFR-007, out_of_scope).
 */
export interface MockRecoveryCode {
  /** uuid (MOCK_ID_PATTERN). */
  id: string;
  /** uuid пользователя-владельца (email уже сопоставлен моком пользователю). */
  userId: string;
  /** 6-значный код восстановления. */
  code: string;
  /** ISO-время истечения (10 минут от создания). */
  expiresAt: string;
  /** ISO-время использования; null = код ещё не использован. */
  usedAt: string | null;
  /** Число неудачных попыток ввода (лимит 5 — §4.2). */
  attempts: number;
  /** ISO-время создания. */
  createdAt: string;
}

/**
 * Reset-токен — запись resetTokens мок-БД (спека §4.2: краткоживущий токен
 * сброса пароля, TTL 15 минут). Значение хранится как есть (демо-режим,
 * см. MockRecoveryCode).
 */
export interface MockResetToken {
  /** uuid (MOCK_ID_PATTERN). */
  id: string;
  /** uuid пользователя-владельца токена. */
  userId: string;
  /** Значение токена для сравнения при reset-password. */
  token: string;
  /** ISO-время истечения (15 минут от создания). */
  expiresAt: string;
  /** ISO-время использования; null = токен ещё не погашен. */
  usedAt: string | null;
  /** ISO-время создания. */
  createdAt: string;
}

/**
 * Счётчики лимитов частоты (data_design MockDb): метки времени всех
 * вызовов для скользящих окон RateLimiter (T-006 задаёт окна/пороги).
 */
export interface MockRateLimitCounters {
  /** Метки времени всех вызовов регистрации (§4.1: ≤5 в час). */
  register: number[];
  /** Метки неуспешных входов по логину в нижнем регистре (§4.1: ≤5 в минуту). */
  loginFailures: Record<string, number[]>;
  /** Метки запросов кода по email в нижнем регистре (§4.2: ≤3 в час). */
  recoveryRequests: Record<string, number[]>;
}

/** Полное состояние мок-БД — содержимое ключа mock.db.v1. */
export interface MockDbData {
  users: User[];
  groups: Group[];
  labs: Lab[];
  submissions: Submission[];
  recoveryCodes: MockRecoveryCode[];
  resetTokens: MockResetToken[];
  rateLimitCounters: MockRateLimitCounters;
}

/** Поставщик сида: возвращает исходное состояние мок-БД. */
export type MockDbSeedProvider = () => MockDbData;

/** Пустое состояние — сид по умолчанию до подключения SeedFixtures (T-004). */
export function emptyMockDbData(): MockDbData {
  return {
    users: [],
    groups: [],
    labs: [],
    submissions: [],
    recoveryCodes: [],
    resetTokens: [],
    rateLimitCounters: { register: [], loginFailures: {}, recoveryRequests: {} },
  };
}

let seedProvider: MockDbSeedProvider = emptyMockDbData;

/**
 * Подключает поставщика сида (точка входа T-004/SeedFixtures): вызывается
 * один раз при сборке мок-слоя до первого обращения к мок-БД. Дальнейшие
 * инициализации ключа mock.db.v1 (отсутствие ключа, повреждённые данные,
 * очистка localStorage) берут данные от нового поставщика.
 */
export function configureMockDbSeed(seed: MockDbSeedProvider): void {
  seedProvider = seed;
}

/** Возвращает сид по умолчанию (пустое состояние) — для изоляции тестов. */
export function resetMockDbSeed(): void {
  seedProvider = emptyMockDbData;
}

/** Проверка структуры распарсенного значения перед гидратацией. */
function isMockDbData(value: unknown): value is MockDbData {
  if (typeof value !== 'object' || value === null) {
    return false;
  }
  const v = value as Record<string, unknown>;
  return (
    Array.isArray(v['users']) &&
    Array.isArray(v['groups']) &&
    Array.isArray(v['labs']) &&
    Array.isArray(v['submissions']) &&
    Array.isArray(v['recoveryCodes']) &&
    Array.isArray(v['resetTokens']) &&
    isRateLimitCounters(v['rateLimitCounters'])
  );
}

function isRateLimitCounters(value: unknown): value is MockRateLimitCounters {
  if (typeof value !== 'object' || value === null) {
    return false;
  }
  const v = value as Record<string, unknown>;
  return (
    Array.isArray(v['register']) &&
    isNumberArrayRecord(v['loginFailures']) &&
    isNumberArrayRecord(v['recoveryRequests'])
  );
}

function isNumberArrayRecord(value: unknown): boolean {
  if (typeof value !== 'object' || value === null) {
    return false;
  }
  return Object.values(value).every(
    (marks) => Array.isArray(marks) && marks.every((m) => typeof m === 'number'),
  );
}

/**
 * Хранилище мок-БД: ленивая инициализация из localStorage, персистентность
 * после каждой мутации, защитный re-seed повреждённых данных.
 *
 * Гарантии API:
 *  - read() возвращает изолированный снимок (клон): обработчики — чистые
 *    функции (ADR-003), всё, что должно сохраниться, делается через mutate;
 *  - mutate() транзакционен: функция работает над черновиком, при успехе
 *    черновик становится новым состоянием и сериализуется (IF-001: «мок-БД
 *    сериализуется после каждой успешной мутации»), при выбросе — черновик
 *    отбрасывается целиком, состояние и localStorage не меняются.
 */
export class MockDb {
  /** Кэш в памяти; null = ещё не инициализирована (ленивость). */
  private inMemory: MockDbData | null = null;

  /** Снимок состояния для чтения — клон; мутации только через mutate. */
  read(): MockDbData {
    return structuredClone(this.load());
  }

  /**
   * Транзакционная мутация с сериализацией. Возвращаемое значение — результат
   * функции; наружу из функции следует возвращать транспортные DTO, а не узлы
   * черновика (они станут живым состоянием).
   */
  mutate<T>(mutation: (data: MockDbData) => T): T {
    const draft = structuredClone(this.load());
    const result = mutation(draft);
    this.inMemory = draft;
    localStorage.setItem(STORAGE_KEYS.mockDb, JSON.stringify(draft));
    return result;
  }

  /** Сбрасывает кэш в памяти: следующее обращение перечитает localStorage. */
  reset(): void {
    this.inMemory = null;
  }

  /** Ленивая инициализация: память → localStorage → сид (с записью ключа). */
  private load(): MockDbData {
    if (this.inMemory !== null) {
      return this.inMemory;
    }
    const raw = localStorage.getItem(STORAGE_KEYS.mockDb);
    if (raw !== null) {
      try {
        const parsed: unknown = JSON.parse(raw);
        if (isMockDbData(parsed)) {
          this.inMemory = parsed;
          return this.inMemory;
        }
        throw new Error('неожиданная структура');
      } catch {
        // Защитный re-seed (data_design): повреждённые данные не читаются —
        // консоль-предупреждение без персональных данных (NFR-007) и откат
        // к сиду. Единственная точка console.warn мок-слоя.
        console.warn(
          `[mock-db] Ключ ${STORAGE_KEYS.mockDb} повреждён и не читается — данные восстановлены из сида.`,
        );
      }
    }
    this.inMemory = seedProvider();
    localStorage.setItem(STORAGE_KEYS.mockDb, JSON.stringify(this.inMemory));
    return this.inMemory;
  }
}
