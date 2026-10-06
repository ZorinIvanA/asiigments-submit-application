/**
 * Идентификаторы записей мок-БД (C-002, ADR-013/ADR-005, CR-001).
 *
 * Формат — строгий uuid 8-4-4-4-12 (MOCK_ID_PATTERN); читаемые строковые id
 * ('group-ik-221', 'user-student01') недопустимы. Два источника значений:
 *  - createDeterministicUuidGenerator(seed) — детерминированный поток uuid
 *    для сида (seeded-PRNG mulberry32, установлены биты version/variant):
 *    повторный re-seed воспроизводит те же id в том же порядке (FR-026,
 *    повторяемость unit-тестов);
 *  - newRuntimeId() — crypto.randomUUID() для записей, создаваемых в runtime
 *    (ADR-005); в не защищённом контексте, где randomUUID отсутствует, — uuid
 *    из crypto.getRandomValues (тот же источник непредсказуемости).
 */

/** Формат идентификаторов мок-БД — строгий uuid 8-4-4-4-12 (ADR-013). */
export const MOCK_ID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * mulberry32 — компактный детерминированный PRNG (32-битный состояние-в-ходе).
 * Возвращает беззнаковые 32-битные целые; одинаковое seed даёт одинаковую
 * последовательность.
 */
function mulberry32(seed: number): () => number {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) | 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return (t ^ (t >>> 14)) >>> 0;
  };
}

/** 32-битное слово в виде ровно восьми hex-символов. */
function hex8(value: number): string {
  return (value >>> 0).toString(16).padStart(8, '0');
}

/** Нибл → hex-цифра варианта RFC 4122 (10xx: 8/9/a/b). */
function variantDigit(nibble: number): string {
  return ((nibble & 0x3) | 0x8).toString(16);
}

/** Сборка uuid 8-4-4-4-12 из четырёх 32-битных слов случайности. */
function uuidFromWords(w1: number, w2: number, w3: number, w4: number): string {
  return [
    hex8(w1),
    hex8(w2).slice(0, 4),
    // Третья группа: старший нибл заменён на версию 4.
    '4' + hex8(w2).slice(5, 8),
    // Четвёртая группа: старший нибл приведён к варианту 10xx.
    variantDigit(w3 >>> 28) + hex8(w3).slice(1, 4),
    hex8(w3).slice(4, 8) + hex8(w4),
  ].join('-');
}

/**
 * Детерминированный генератор uuid для сида (ADR-013): один и тот же seed
 * всегда даёт одну и ту же последовательность uuid-строк, удовлетворяющих
 * MOCK_ID_PATTERN. Вызывается строго последовательно — в порядке обхода
 * сущностей сида (T-004).
 */
export function createDeterministicUuidGenerator(seed: number): () => string {
  const next = mulberry32(seed);
  return () => uuidFromWords(next(), next(), next(), next());
}

/**
 * Идентификатор runtime-записи: crypto.randomUUID() (ADR-005); резервный
 * путь для не защищённого контекста — uuid из crypto.getRandomValues.
 */
export function newRuntimeId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  bytes[6] = (bytes[6] & 0x0f) | 0x40; // версия 4
  bytes[8] = (bytes[8] & 0x3f) | 0x80; // вариант 10xx
  const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20, 32)}`;
}
