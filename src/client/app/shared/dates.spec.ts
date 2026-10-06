/**
 * Юнит-тесты утилит дат (ADR-007): конвертация 'YYYY-MM-DD' ↔ Date ↔
 * 'дд.мм.гггг' без UTC-сдвига.
 *
 * «Эмуляция сдвига» таймзоны: переключить часовую зону браузера из теста
 * нельзя, поэтому отсутствие сдвига проверяется инвариантами, верными при
 * ЛЮБОМ смещении окружения: (1) все даты создаются через new Date(год, месяц,
 * день) — то есть как local-даты; (2) ожидаемые строки зафиксированы
 * литералами; (3) наивный подход toISOString().slice(0, 10) сравнивается
 * напрямую — при любом ненулевом смещении он ошибается хотя бы на одной из
 * меток «локальная полночь»/«локальный поздний вечер».
 */
import { toDisplayDate, toIsoDate, toLocalDate } from './dates';

/** Матрица дат на границах месяцев и лет: [год, месяц 1–12, день, 'YYYY-MM-DD']. */
const LOCAL_DATE_MATRIX: ReadonlyArray<readonly [number, number, number, string]> =
  [
    [2026, 9, 15, '2026-09-15'],
    [2026, 1, 1, '2026-01-01'], // граница года: первое января
    [2026, 12, 31, '2026-12-31'], // граница года: тридцать первое декабря
    [2024, 2, 29, '2024-02-29'], // 29 февраля високосного года
    [2000, 2, 29, '2000-02-29'], // 29 февраля года, кратного 400
    [1999, 12, 31, '1999-12-31'], // смена тысячелетия
    [2026, 7, 4, '2026-07-04'], // однозначные месяц и день — ведущие нули
  ];

describe('Утилиты дат (ADR-007)', () => {
  describe('toDisplayDate — формат отображения дд.мм.гггг', () => {
    it('AC «Формат отображения»: 2026-09-15 → 15.09.2026', () => {
      expect(toDisplayDate('2026-09-15')).toBe('15.09.2026');
    });

    it('дополняет день и месяц нулями: 2026-09-05 → 05.09.2026', () => {
      expect(toDisplayDate('2026-09-05')).toBe('05.09.2026');
    });

    it('границы месяца и года: 31.12 и 01.01', () => {
      expect(toDisplayDate('2026-12-31')).toBe('31.12.2026');
      expect(toDisplayDate('2026-01-01')).toBe('01.01.2026');
    });

    it('29 февраля високосного года валидно', () => {
      expect(toDisplayDate('2024-02-29')).toBe('29.02.2024');
    });

    it('null → null (пустая ячейка ведомости, без прочерка — ISS-102)', () => {
      expect(toDisplayDate(null)).toBeNull();
    });

    it('строки нестрогого формата → null', () => {
      expect(toDisplayDate('2026-9-15')).toBeNull(); // месяц без ведущего нуля
      expect(toDisplayDate('2026-09-5')).toBeNull(); // день без ведущего нуля
      expect(toDisplayDate('26-09-15')).toBeNull(); // двузначный год
      expect(toDisplayDate('2026/09/15')).toBeNull(); // не дефисы
      expect(toDisplayDate('15.09.2026')).toBeNull(); // уже формат отображения
      expect(toDisplayDate('2026-09-15T00:00:00.000Z')).toBeNull(); // не дата-время
      expect(toDisplayDate(' 2026-09-15')).toBeNull(); // пробелы не тримаются
      expect(toDisplayDate('')).toBeNull(); // пустая строка
    });

    it('календарно невозможные даты → null', () => {
      expect(toDisplayDate('2026-02-29')).toBeNull(); // невисокосный год
      expect(toDisplayDate('2026-02-30')).toBeNull();
      expect(toDisplayDate('2026-04-31')).toBeNull();
      expect(toDisplayDate('2026-13-01')).toBeNull(); // месяц 13
      expect(toDisplayDate('2026-00-10')).toBeNull(); // месяц 0
      expect(toDisplayDate('2026-09-00')).toBeNull(); // день 0
      expect(toDisplayDate('2026-09-32')).toBeNull(); // день 32
    });
  });

  describe('toIsoDate — сериализация local-даты в контракт без UTC-сдвига', () => {
    it('AC «Сериализация без сдвига»: Date(2026, 8, 15) → 2026-09-15', () => {
      // Месяц в конструкторе Date отсчитывается с нуля: 8 = сентябрь.
      expect(toIsoDate(new Date(2026, 8, 15))).toBe('2026-09-15');
    });

    it('матрица дат на границах месяцев/лет — верна при любой таймзоне окружения', () => {
      // Ожидания зафиксированы литералами, а даты создаются как local:
      // если бы утилита использовала toISOString, в таймзоне UTC+N
      // эти проверки упали бы (день сдвинулся бы назад).
      for (const [year, month, day, expected] of LOCAL_DATE_MATRIX) {
        expect(toIsoDate(new Date(year, month - 1, day))).toBe(expected);
      }
    });

    it('время суток не влияет на день: полночь, полдень и 23:59:59.999 дают одну дату', () => {
      expect(toIsoDate(new Date(2026, 8, 15, 0, 0, 0, 0))).toBe('2026-09-15');
      expect(toIsoDate(new Date(2026, 8, 15, 12, 30, 0, 0))).toBe('2026-09-15');
      expect(toIsoDate(new Date(2026, 8, 15, 23, 59, 59, 999))).toBe('2026-09-15');
    });

    it('не повторяет ошибку наивного toISOString: при ненулевом смещении окружения день не сдвигается (UTC+N и UTC-N)', () => {
      const midnight = new Date(2026, 8, 15, 0, 0, 0, 0); // локальная полночь
      const lateEvening = new Date(2026, 8, 15, 23, 0, 0, 0); // локальный вечер
      const naiveIsoDay = (date: Date): string => date.toISOString().slice(0, 10);

      if (midnight.getTimezoneOffset() !== 0) {
        // Эмуляция сдвига: при смещении UTC+N наивный подход теряет день у
        // полуночи, при UTC-N — у позднего вечера; хотя бы одна метка
        // обязана «пострадать» в любом ненулевом смещении.
        const naiveKeepsBothDays =
          naiveIsoDay(midnight) === '2026-09-15' &&
          naiveIsoDay(lateEvening) === '2026-09-15';
        expect(naiveKeepsBothDays).toBe(false);
      }

      // Наша утилита верна независимо от направления смещения.
      expect(toIsoDate(midnight)).toBe('2026-09-15');
      expect(toIsoDate(lateEvening)).toBe('2026-09-15');
    });

    it('дополняет месяц и день нулями: Date(2026, 8, 5) → 2026-09-05', () => {
      expect(toIsoDate(new Date(2026, 8, 5))).toBe('2026-09-05');
      expect(toIsoDate(new Date(2027, 0, 3))).toBe('2027-01-03');
    });

    it('двузначные годы не превращаются в 19xx (ловушка new Date(year < 100))', () => {
      // new Date(50, 5, 1) без защиты даёт 1950 год — утилита обязана
      // сохранить год 50 и четырёхзначную запись 0050.
      const firstCenturyDate = new Date(0, 0, 1);
      firstCenturyDate.setFullYear(50, 5, 1);
      expect(toIsoDate(firstCenturyDate)).toBe('0050-06-01');
    });
  });

  describe('toLocalDate — разбор контрактной строки в local-дату', () => {
    it('2026-09-15 → локальная полночь 15 сентября 2026', () => {
      const parsed = toLocalDate('2026-09-15');
      expect(parsed).toEqual(new Date(2026, 8, 15));
      expect(parsed?.getFullYear()).toBe(2026);
      expect(parsed?.getMonth()).toBe(8); // сентябрь: месяцы с нуля
      expect(parsed?.getDate()).toBe(15);
      expect(parsed?.getHours()).toBe(0);
      expect(parsed?.getMinutes()).toBe(0);
    });

    it('null → null', () => {
      expect(toLocalDate(null)).toBeNull();
    });

    it('строки нестрогого формата и невозможные даты → null', () => {
      expect(toLocalDate('2026-02-29')).toBeNull();
      expect(toLocalDate('2026-02-30')).toBeNull();
      expect(toLocalDate('2026-13-01')).toBeNull();
      expect(toLocalDate('2026-9-15')).toBeNull();
      expect(toLocalDate('15.09.2026')).toBeNull();
      expect(toLocalDate('')).toBeNull();
    });

    it('двузначный год разбирается как год нашей эры, а не 19xx', () => {
      const parsed = toLocalDate('0050-06-01');
      expect(parsed?.getFullYear()).toBe(50);
      expect(parsed?.getMonth()).toBe(5);
      expect(parsed?.getDate()).toBe(1);
    });
  });

  describe('круглые преобразования', () => {
    it('строка → Date → та же строка (матрица границ)', () => {
      for (const [year, month, day, expected] of LOCAL_DATE_MATRIX) {
        const roundTrip = toIsoDate(toLocalDate(expected)!);
        expect(roundTrip).toBe(expected);
        expect(toLocalDate(expected)).toEqual(new Date(year, month - 1, day));
      }
    });

    it('Date → строка → Date: то же мгновение (локальная полночь)', () => {
      for (const [year, month, day] of LOCAL_DATE_MATRIX) {
        const original = new Date(year, month - 1, day);
        const roundTrip = toLocalDate(toIsoDate(original));
        expect(roundTrip?.getTime()).toBe(original.getTime());
      }
    });

    it('строка → формат отображения согласован с компонентами local-даты', () => {
      expect(toDisplayDate(toIsoDate(new Date(2026, 8, 15)))).toBe('15.09.2026');
      expect(toDisplayDate(toIsoDate(new Date(2026, 0, 1)))).toBe('01.01.2026');
      expect(toDisplayDate(toIsoDate(new Date(2026, 11, 31)))).toBe('31.12.2026');
    });

    it('null проходит сквозь разбор без значения: сброс даты остаётся null', () => {
      expect(toLocalDate(toDisplayDate(null) ?? null)).toBeNull();
    });
  });
});
