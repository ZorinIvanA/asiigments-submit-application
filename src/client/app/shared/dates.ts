/**
 * Утилиты дат (ADR-007): конвертация 'YYYY-MM-DD' ↔ Date ↔ 'дд.мм.гггг'
 * без UTC-сдвига.
 *
 * Строка 'YYYY-MM-DD' — формат хранения/передачи (глоссарий спеки),
 * 'дд.мм.гггг' — формат отображения. Конвертация в Date нужна только
 * контроловым значениям p-datepicker (ведомость FR-015); компоненты
 * local-даты собираются вручную (getFullYear/getMonth/getDate + padStart),
 * без toISOString: он переводит мгновение в UTC и в таймзоне UTC+N
 * возвращает предыдущий день.
 */

/** Строгий вид контрактной строки даты: ровно 4-2-2 цифры через дефис. */
const ISO_DATE_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/;

/** Компоненты даты без времени: месяц — человеческий, 1..12. */
interface DateComponents {
  year: number;
  month: number;
  day: number;
}

/** Дополняет число нулями слева до нужной длины (7 → '07'). */
function pad(value: number, length: number): string {
  return String(value).padStart(length, '0');
}

/**
 * Собирает local-дату из компонентов. Прямой new Date(year, ...) для годов
 * 0–99 даёт 1900+year (ловушка конструктора), поэтому год устанавливается
 * через setFullYear, который обрабатывает любые четырёхзначные годы.
 */
function makeLocalDate(components: DateComponents): Date {
  const date = new Date(0, 0, 1);
  date.setFullYear(components.year, components.month - 1, components.day);
  return date;
}

/**
 * Разбирает 'YYYY-MM-DD' и проверяет календарную корректность.
 * Возвращает null для строк нестрогого формата ('2026-9-15', '15.09.2026',
 * дата-время) и невозможных дат (30 февраля, 31 апреля, 29 февраля
 * невисокосного года): Date молча нормализует переполнение, поэтому
 * валидна только дата, компоненты которой после сборки совпали с исходными.
 */
function parseIsoDate(value: string): DateComponents | null {
  const match = ISO_DATE_PATTERN.exec(value);
  if (match === null) {
    return null;
  }
  const components: DateComponents = {
    year: Number(match[1]),
    month: Number(match[2]),
    day: Number(match[3]),
  };
  const probe = makeLocalDate(components);
  if (
    probe.getFullYear() !== components.year ||
    probe.getMonth() !== components.month - 1 ||
    probe.getDate() !== components.day
  ) {
    return null;
  }
  return components;
}

/**
 * Date → 'YYYY-MM-DD': сериализация local-даты в контракт хранения/передачи.
 * День берётся из local-компонентов, поэтому результат не зависит от
 * таймзоны окружения и времени суток внутри даты.
 */
export function toIsoDate(date: Date): string {
  return `${pad(date.getFullYear(), 4)}-${pad(date.getMonth() + 1, 2)}-${pad(
    date.getDate(),
    2,
  )}`;
}

/**
 * 'YYYY-MM-DD' → Date (локальная полночь) — для установки значения
 * p-datepicker. null либо неконтрактная строка → null (пустая ячейка).
 */
export function toLocalDate(value: string | null): Date | null {
  if (value === null) {
    return null;
  }
  const components = parseIsoDate(value);
  return components === null ? null : makeLocalDate(components);
}

/**
 * 'YYYY-MM-DD' → 'дд.мм.гггг' — формат отображения (глоссарий спеки).
 * null либо неконтрактная строка → null: пустая дата остаётся пустой
 * ячейкой без прочерка (ISS-102).
 */
export function toDisplayDate(value: string | null): string | null {
  if (value === null) {
    return null;
  }
  const components = parseIsoDate(value);
  return components === null
    ? null
    : `${pad(components.day, 2)}.${pad(components.month, 2)}.${pad(
        components.year,
        4,
      )}`;
}
