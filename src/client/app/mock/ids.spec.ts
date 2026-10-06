/**
 * Юнит-тесты идентификаторов мок-БД (ADR-013/ADR-005, CR-001): формат
 * MOCK_ID_PATTERN, повторяемость/уникальность детерминированного
 * uuid-генератора (требование: ≥240 уникальных значений), runtime-идентификаторы.
 */
import { createDeterministicUuidGenerator, MOCK_ID_PATTERN, newRuntimeId } from './ids';

describe('Идентификаторы мок-БД (ADR-013)', () => {
  describe('MOCK_ID_PATTERN — строгий uuid 8-4-4-4-12', () => {
    it('принимает корректный uuid в нижнем и верхнем регистре', () => {
      expect('0b0b0b0b-0b0b-4b0b-8b0b-0b0b0b0b0b0b').toMatch(MOCK_ID_PATTERN);
      expect('11111111-2222-4333-8444-555555555555').toMatch(MOCK_ID_PATTERN);
      expect('0B0B0B0B-0B0B-4B0B-8B0B-0B0B0B0B0B0B').toMatch(MOCK_ID_PATTERN); // /i
    });

    it('отвергает читаемые строковые id сида (CR-001: недопустимы)', () => {
      expect('group-ik-221').not.toMatch(MOCK_ID_PATTERN);
      expect('user-student01').not.toMatch(MOCK_ID_PATTERN);
      expect('none').not.toMatch(MOCK_ID_PATTERN);
    });

    it('отвергает повреждённые формы uuid', () => {
      expect('').not.toMatch(MOCK_ID_PATTERN);
      expect('0b0b0b0b-0b0b-4b0b-8b0b-0b0b0b0b0b0').not.toMatch(MOCK_ID_PATTERN); // 11 символов
      expect('0b0b0b0b-0b0b-4b0b-8b0b-0b0b0b0b0b0b0').not.toMatch(MOCK_ID_PATTERN); // 13 символов
      expect('0b0b0b0b_0b0b_4b0b_8b0b_0b0b0b0b0b0b').not.toMatch(MOCK_ID_PATTERN); // не дефисы
      expect('0b0b0b0b-0b0b-4b0b-8b0b0b0b0b0b0b0b').not.toMatch(MOCK_ID_PATTERN); // 4 группы
      expect('gb0b0b0b-0b0b-4b0b-8b0b-0b0b0b0b0b0b').not.toMatch(MOCK_ID_PATTERN); // не hex
      expect('{0b0b0b0b-0b0b-4b0b-8b0b-0b0b0b0b0b0b}').not.toMatch(MOCK_ID_PATTERN); // обёртка
      expect(' 0b0b0b0b-0b0b-4b0b-8b0b-0b0b0b0b0b0b').not.toMatch(MOCK_ID_PATTERN); // пробел
    });
  });

  describe('createDeterministicUuidGenerator — сид (ADR-013)', () => {
    it('повторяемость: один seed — идентичные последовательности (300 значений)', () => {
      const first = createDeterministicUuidGenerator(20260926);
      const second = createDeterministicUuidGenerator(20260926);
      for (let i = 0; i < 300; i++) {
        expect(first()).toBe(second());
      }
    });

    it('повторный re-seed воспроизводит те же id (FR-026: сброс → данные равны сиду)', () => {
      const before = createDeterministicUuidGenerator(42);
      const original = Array.from({ length: 50 }, () => before());
      const after = createDeterministicUuidGenerator(42);
      const restored = Array.from({ length: 50 }, () => after());
      expect(restored).toEqual(original);
    });

    it('разные seed — разные последовательности', () => {
      const first = createDeterministicUuidGenerator(1);
      const second = createDeterministicUuidGenerator(2);
      const fromFirst = Array.from({ length: 50 }, () => first());
      const fromSecond = Array.from({ length: 50 }, () => second());
      expect(fromFirst).not.toEqual(fromSecond);
    });

    it('уникальность: 300 значений подряд без повторов (требование ≥240)', () => {
      const generate = createDeterministicUuidGenerator(777);
      const values = new Set(Array.from({ length: 300 }, () => generate()));
      expect(values.size).toBe(300);
    });

    it('формат: MOCK_ID_PATTERN + версия 4 + вариант RFC 4122 у каждого значения', () => {
      const generate = createDeterministicUuidGenerator(31337);
      for (let i = 0; i < 300; i++) {
        const value = generate();
        expect(value).toMatch(MOCK_ID_PATTERN);
        // Третья группа начинается с версии 4, четвёртая — с варианта 8/9/a/b.
        expect(value[14]).toBe('4');
        expect('89ab').toContain(value[19]);
      }
    });
  });

  describe('newRuntimeId — записи runtime (ADR-005: crypto.randomUUID)', () => {
    it('возвращает uuid, соответствующий MOCK_ID_PATTERN, с битами version/variant', () => {
      const value = newRuntimeId();
      expect(value).toMatch(MOCK_ID_PATTERN);
      expect(value[14]).toBe('4');
      expect('89ab').toContain(value[19]);
    });

    it('уникальность: 100 runtime-идентификаторов без повторов', () => {
      const values = new Set(Array.from({ length: 100 }, () => newRuntimeId()));
      expect(values.size).toBe(100);
    });
  });
});
