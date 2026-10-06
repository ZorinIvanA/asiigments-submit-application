/**
 * Ограничитель частоты скользящим окном (C-002) — базовый примитив для
 * анти-спам правил спеки §4.1–4.2: регистрация ≤5/час, неуспешные входы
 * ≤5/мин на логин, запросы кода восстановления ≤3/час на email. Сами окна
 * и пороги задают доменные обработчики (T-006); ядро мок-слоя только
 * считает метки времени и не знает бизнес-правил.
 *
 * Метки хранятся в мок-БД (MockDbData.rateLimitCounters) как простые
 * number[] (метки Date.now): окно — это метки, попавшие в последние
 * windowMs; устаревшие метки вычищаются при каждой проверке, поэтому массивы
 * не растут бесконечно. Момент «сейчас» передаётся параметром (по умолчанию
 * Date.now) — детерминизм в unit-тестах.
 */

/** Скользящее окно по меткам времени. */
export class RateLimiter {
  constructor(
    /** Размер окна в миллисекундах (строго > 0). */
    readonly windowMs: number,
    /** Максимум учитываемых меток в окне (целое >= 1). */
    readonly max: number,
  ) {
    if (!Number.isFinite(windowMs) || windowMs <= 0) {
      throw new RangeError(`RateLimiter: windowMs должно быть > 0, получено ${windowMs}`);
    }
    if (!Number.isInteger(max) || max < 1) {
      throw new RangeError(`RateLimiter: max должно быть целым >= 1, получено ${max}`);
    }
  }

  /**
   * Проверяет лимит и при прохождении сразу дописывает метку now в массив
   * marks (in place). Отказ метку НЕ дописывает: заблокированные вызовы
   * не продлевают окно. Массив меняется только мутацией его содержимого,
   * поэтому вызывающий код сохраняет ссылку (удобно держать метки в мок-БД).
   */
  tryAcquire(marks: number[], now: number = Date.now()): boolean {
    this.prune(marks, now);
    if (marks.length >= this.max) {
      return false;
    }
    marks.push(now);
    return true;
  }

  /** Проверяет лимит без записи метки — для правил «считать все вызовы». */
  wouldAcquire(marks: number[], now: number = Date.now()): boolean {
    this.prune(marks, now);
    return marks.length < this.max;
  }

  /**
   * Вычищает устаревшие метки: метка ровно windowMs назад уже НЕ попадает
   * в окно (считаются только метки строго новее границы).
   */
  private prune(marks: number[], now: number): void {
    const threshold = now - this.windowMs;
    for (let i = marks.length - 1; i >= 0; i--) {
      if (marks[i] <= threshold) {
        marks.splice(i, 1);
      }
    }
  }
}
