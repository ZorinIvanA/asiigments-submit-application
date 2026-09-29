/**
 * Юнит-тесты ограничителя частоты скользящим окном (C-002): границы окна
 * (метка ровно windowMs назад уже не считается), чистка устаревших меток,
 * отказ без записи метки, проверка без записи, независимость массивов,
 * валидация параметров конструктора.
 */
import { RateLimiter } from './rate-limiter';

describe('RateLimiter — скользящие окна (C-002)', () => {
  const WINDOW_MS = 60_000;
  const MAX = 5;
  const NOW = 1_000_000; // фиксированное «сейчас» — детерминизм

  describe('подсчёт внутри окна', () => {
    it('пропускает ровно max вызовов подряд, затем отказывает', () => {
      const limiter = new RateLimiter(WINDOW_MS, MAX);
      const marks: number[] = [];
      for (let i = 0; i < MAX; i++) {
        expect(limiter.tryAcquire(marks, NOW)).toBeTrue();
      }
      expect(marks.length).toBe(MAX);
      expect(limiter.tryAcquire(marks, NOW)).toBeFalse();
    });

    it('метки одного момента не вытесняют друг друга: окно не сбрасывается повторами', () => {
      const limiter = new RateLimiter(WINDOW_MS, MAX);
      const marks: number[] = [NOW, NOW, NOW, NOW, NOW];
      expect(limiter.tryAcquire(marks, NOW)).toBeFalse();
    });

    it('после выхода меток из окна доступ возобновляется', () => {
      const limiter = new RateLimiter(WINDOW_MS, MAX);
      const marks: number[] = Array.from({ length: MAX }, (_, i) => NOW - 1000 + i);
      expect(limiter.tryAcquire(marks, NOW)).toBeFalse();
      // Все пять меток покидают окно через WINDOW_MS после самой старой.
      expect(limiter.tryAcquire(marks, NOW + WINDOW_MS)).toBeTrue();
    });
  });

  describe('границы окна', () => {
    it('метка ровно windowMs назад уже НЕ считается (окно строго новее границы)', () => {
      const limiter = new RateLimiter(WINDOW_MS, 1);
      const marks: number[] = [NOW - WINDOW_MS];
      expect(limiter.tryAcquire(marks, NOW)).toBeTrue(); // старая выпала, метка записана
    });

    it('метка на 1 мс новее границы всё ещё считается', () => {
      const limiter = new RateLimiter(WINDOW_MS, 1);
      const marks: number[] = [NOW - WINDOW_MS + 1];
      expect(limiter.tryAcquire(marks, NOW)).toBeFalse();
    });

    it('смежные окна: пять меток в конце предыдущего окна блокируют его границу', () => {
      const limiter = new RateLimiter(WINDOW_MS, MAX);
      const marks: number[] = Array.from({ length: MAX }, (_, i) => NOW - WINDOW_MS + 1 + i);
      // Все метки строго новее NOW - WINDOW_MS — окно всё ещё полно.
      expect(limiter.tryAcquire(marks, NOW)).toBeFalse();
      // Через 1 мс старейшая метка выходит из окна — место освобождается.
      expect(limiter.tryAcquire(marks, NOW + 1)).toBeTrue();
    });
  });

  describe('чистка устаревших меток', () => {
    it('устаревшие метки удаляются из массива in place, свежие сохраняются', () => {
      const limiter = new RateLimiter(WINDOW_MS, MAX);
      const marks: number[] = [NOW - WINDOW_MS - 5, NOW - WINDOW_MS, NOW - 5000, NOW - 1];
      expect(limiter.tryAcquire(marks, NOW)).toBeTrue();
      expect(marks).toEqual([NOW - 5000, NOW - 1, NOW]);
    });

    it('wouldAcquire тоже вычищает устаревшие метки', () => {
      const limiter = new RateLimiter(WINDOW_MS, MAX);
      const marks: number[] = [NOW - WINDOW_MS - 100, NOW - 10];
      expect(limiter.wouldAcquire(marks, NOW)).toBeTrue();
      expect(marks).toEqual([NOW - 10]);
    });

    it('пустой массив и массив из одних устаревших меток обрабатываются корректно', () => {
      const limiter = new RateLimiter(WINDOW_MS, 1);
      const empty: number[] = [];
      expect(limiter.tryAcquire(empty, NOW)).toBeTrue();
      const stale: number[] = [0, 1, 2];
      expect(limiter.tryAcquire(stale, NOW)).toBeTrue();
      expect(stale).toEqual([NOW]);
    });
  });

  describe('семантика записи', () => {
    it('отказ не дописывает метку: заблокированные вызовы не продлевают окно', () => {
      const limiter = new RateLimiter(WINDOW_MS, MAX);
      const marks: number[] = Array.from({ length: MAX }, (_, i) => NOW - 100 + i);
      for (let i = 0; i < 10; i++) {
        expect(limiter.tryAcquire(marks, NOW + i)).toBeFalse();
      }
      expect(marks.length).toBe(MAX);
    });

    it('wouldAcquire проверяет без записи метки', () => {
      const limiter = new RateLimiter(WINDOW_MS, MAX);
      const marks: number[] = [];
      for (let i = 0; i < MAX; i++) {
        expect(limiter.wouldAcquire(marks, NOW)).toBeTrue();
      }
      expect(marks).toEqual([]); // ни одной метки не записано
      // Проверка без записи не наполняет счётчик: на пустом массиве
      // wouldAcquire проходит повторно (контракт «проверяет без записи»,
      // ревью T-119: прежнее ожидание false противоречило реализации —
      // отказ был бы только у записывающего tryAcquire).
      expect(limiter.wouldAcquire(marks, NOW)).toBeTrue();
      // Противофаза «would»: как только метки реально записаны tryAcquire'ом
      // до MAX, проверка на полном окне даёт отказ — и тоже без записи.
      for (let i = 0; i < MAX; i++) {
        expect(limiter.tryAcquire(marks, NOW)).toBeTrue();
      }
      expect(limiter.wouldAcquire(marks, NOW)).toBeFalse();
      expect(marks.length).withContext('wouldAcquire и здесь не пишет').toBe(MAX);
    });

    it('массив-ключи независимы: переполнение одного не блокирует другой', () => {
      const limiter = new RateLimiter(WINDOW_MS, MAX);
      const perKey: Record<string, number[]> = {
        'student01': Array.from({ length: MAX }, (_, i) => NOW - 50 + i),
        'teacher': [],
      };
      expect(limiter.tryAcquire(perKey['student01'], NOW)).toBeFalse();
      expect(limiter.tryAcquire(perKey['teacher'], NOW)).toBeTrue();
    });
  });

  describe('параметры конструктора', () => {
    it('windowMs <= 0 или не число → RangeError', () => {
      expect(() => new RateLimiter(0, 5)).toThrowError(RangeError);
      expect(() => new RateLimiter(-100, 5)).toThrowError(RangeError);
      expect(() => new RateLimiter(Number.NaN, 5)).toThrowError(RangeError);
      expect(() => new RateLimiter(Number.POSITIVE_INFINITY, 5)).toThrowError(RangeError);
    });

    it('max < 1 или не целое → RangeError', () => {
      expect(() => new RateLimiter(WINDOW_MS, 0)).toThrowError(RangeError);
      expect(() => new RateLimiter(WINDOW_MS, 2.5)).toThrowError(RangeError);
    });

    it('корректные параметры принимаются (граница max = 1)', () => {
      expect(() => new RateLimiter(1, 1)).not.toThrow();
    });
  });
});
