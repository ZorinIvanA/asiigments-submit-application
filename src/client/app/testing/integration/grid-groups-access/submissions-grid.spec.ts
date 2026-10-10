/**
 * Интеграционные спеки ведомости преподавателя /submissions (батч 3, FR-4.4,
 * SCR-009): реальные SubmissionsPage + core-сервисы на реальном HTTP-ядре
 * (HttpClient + authInterceptor) поверх программируемого
 * HttpTestingController-бэкенда GridBackendStub с сидом зоны; серверная
 * сессия teacher — serverSession (признак сессии — память AuthService,
 * FR-092).
 *
 * Покрываемые согласованные сценарии (scenarios.batch3.json, automation:
 * automated): TS-301 (дефолты), TS-302 (датапик: upsert обеих дат), TS-303
 * (факт-коррекция 2: явные очистки ×/пустой blur/Enter коммитят сброс,
 * непарсящийся текст не шлёт update и откатывается на blur; семантика
 * коммита ревью T-114 итерация 2), TS-305 (пагинация 5, подпись),
 * TS-306 (пустая группа), TS-307 (нет семестров / нет групп), TS-309 (гонки
 * смены группы/семестра/страницы), TS-310 (параллельное сохранение с
 * отказом), TS-311 (повторная установка той же даты — без дублей), TS-312
 * (формат сид-сдач), TS-314 (обновление по удалённой лабораторной: 404 +
 * перезагрузка).
 *
 * Тайминги: задержек бэкенда нет — «волны» settle() отвечают перехваченные
 * запросы и продвигают виртуальное время (fakeAsync + tick); мутации
 * «другого сеанса» — env.backend.mutate/removeLabDirect над тем же
 * состоянием бэкенда.
 */
import { ComponentFixture, fakeAsync } from '@angular/core/testing';

import { NotificationService } from '../../../shared/notifications/notification-service';
import { SubmissionsService } from '../../../core/services/submissions.service';
import { SubmissionsPage } from '../../../features/submissions/pages/submissions-page';
import {
  GridAccessEnv,
  openSelect,
  pickOption,
  qsAllIn,
  qsIn,
  selectLabelOf,
} from './integration-env';

/** Тексты и пара сид-сдач (SCR-009) — дословно сценарии TS-302/TS-303. */
const GROUP_NAMES = ['ИК-221', 'ИК-222', 'ИК-223'] as const;
const PAGE_SIZE = 5;
const TOTAL_221 = 25;

describe('SubmissionsPage — интеграция с реальным HTTP-ядром (батч 3, FR-4.4)', () => {
  let env: GridAccessEnv;
  let notifications: NotificationService;
  let submissionsService: SubmissionsService;
  let fixture: ComponentFixture<SubmissionsPage>;

  /** Идентификаторы сида (детерминированные uuid). */
  let teacherId: string;
  let student01: string;
  let student02: string;
  let ik221: string;
  let ik222: string;
  let ik223: string;
  let lab1: string;
  let lab2: string;
  let lab6: string;

  beforeEach(() => {
    env = GridAccessEnv.setup();
    notifications = env.notifications;
    submissionsService = env.inject(SubmissionsService);
    teacherId = env.backend.userIdByLogin('teacher');
    student01 = env.backend.userIdByLogin('student01');
    student02 = env.backend.userIdByLogin('student02');
    ik221 = env.backend.groupIdByName('ИК-221');
    ik222 = env.backend.groupIdByName('ИК-222');
    ik223 = env.backend.groupIdByName('ИК-223');
    lab1 = env.backend.labId(1, 1);
    lab2 = env.backend.labId(1, 2);
    lab6 = env.backend.labId(1, 6);
    env.serverSession('teacher');
  });

  afterEach(() => {
    notifications.dismissMobile();
    fixture?.destroy();
    env.stop();
  });

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  /** Создаёт страницу и доводит init (селекторы волна 1 + грид волна 2). */
  function createPage(): void {
    fixture = env.mount(SubmissionsPage);
    env.settle(fixture, 2);
  }

  /** Строка tbody по номеру (1-based). */
  function bodyRow(row: number): HTMLTableRowElement {
    return root().querySelector(`tbody tr:nth-child(${row})`) as HTMLTableRowElement;
  }

  /**
   * input ячейки даты: td 1 — студент, у лабы k сдача — td 2k, защита — 2k+1
   * (структура двухуровневой шапки SCR-009). Годится для статичных гридов;
   * после удаления лабы колонки сдвигаются — используйте cellInputByHeader.
   */
  function cellInput(row: number, labNumber: number, field: 'submit' | 'defense'): HTMLInputElement {
    const cell = field === 'submit' ? 2 * labNumber : 2 * labNumber + 1;
    return bodyRow(row).querySelector(`td:nth-child(${cell}) input`) as HTMLInputElement;
  }

  /**
   * input ячейки по ТЕКУЩЕЙ колонке «Лаб N» из thead (CR-006): после
   * перезагрузки грида без удалённой лабы колонки сдвигаются, поэтому
   * позиция ячейки вычисляется из фактической шапки, а не из номера лабы.
   */
  function cellInputByHeader(
    row: number,
    labNumber: number,
    field: 'submit' | 'defense',
  ): HTMLInputElement {
    const headers = Array.from(root().querySelectorAll('thead tr:first-child th'));
    const headerIndex = headers.findIndex((th) => th.textContent?.trim() === `Лаб ${labNumber}`);
    if (headerIndex === -1) {
      throw new Error(`submissions-grid.spec: колонки «Лаб ${labNumber}» нет в текущем гриде`);
    }
    const cell = field === 'submit' ? 2 * headerIndex : 2 * headerIndex + 1;
    return bodyRow(row).querySelector(`td:nth-child(${cell}) input`) as HTMLInputElement;
  }

  /**
   * Пользовательский ввод текста в ячейку (паттерн typeCellDate зоны T-114):
   * keydown будит парсер пикера (isKeydown), input несёт текст — события
   * идут в DOM реальной ячейки, а не через вызовы методов страницы.
   */
  function typeCell(
    row: number,
    labNumber: number,
    field: 'submit' | 'defense',
    text: string,
  ): void {
    const input = cellInput(row, labNumber, field);
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    input.value = text;
    input.dispatchEvent(new Event('input'));
  }

  /** Пользовательский blur ячейки (коммит явной очистки / возврат к источнику). */
  function blurCell(row: number, labNumber: number, field: 'submit' | 'defense'): void {
    cellInput(row, labNumber, field).dispatchEvent(new FocusEvent('blur'));
  }

  /** Клик по кнопке × ячейки (явная очистка, пользовательский путь). */
  function clickCellClearIcon(
    row: number,
    labNumber: number,
    field: 'submit' | 'defense',
  ): void {
    const cell = field === 'submit' ? 2 * labNumber : 2 * labNumber + 1;
    const icon = bodyRow(row).querySelector(
      `td:nth-child(${cell}) .p-datepicker-clear-icon`,
    ) as HTMLElement | null;
    if (icon === null) {
      throw new Error('submissions-grid.spec: кнопка × не видима (у пустой ячейки её нет)');
    }
    icon.dispatchEvent(new MouseEvent('click', { bubbles: true }));
  }

  function rangeCaption(): string {
    return qsIn(fixture, '[data-test="range-caption"]')?.textContent?.trim() ?? '';
  }

  /** Верхние заголовки «Лаб N» (th colspan=2) в порядке отображения. */
  function labGroupHeaders(): string[] {
    return Array.from(root().querySelectorAll('thead tr:first-child th[colspan="2"]')).map(
      (th) => th.textContent?.trim() ?? '',
    );
  }

  /** Кнопка пагинатора по классу PrimeNG (стрелки) или тексту (номера). */
  function paginatorButton(kind: 'next' | 'page', label?: string): HTMLButtonElement {
    const selector = kind === 'next' ? '.p-paginator-next' : '.p-paginator-page';
    const found = qsAllIn(fixture, selector).find(
      (candidate) => label === undefined || candidate.textContent?.trim() === label,
    );
    if (found === undefined) {
      throw new Error(`submissions-grid.spec: нет кнопки пагинатора ${kind} ${label ?? ''}`);
    }
    return found as HTMLButtonElement;
  }

  /** Смена группы/семестра через настоящие p-select тулбара. */
  function selectToolbarOption(index: number, label: string): void {
    const selects = Array.from(root().querySelectorAll('p-select'));
    openSelect(fixture, selects[index]!);
    pickOption(fixture, label);
  }

  it('TS-301: дефолты — группа ИК-221 (name↑), семестр 1, грид стр.1: 5 студентов, лабы 1..20, двухуровневая шапка с парой «Сдача/Защита» у каждой, подпись «с 1 по 5 из 25»', fakeAsync(() => {
    createPage();

    // Селекторы: первая группа по name↑, наименьший семестр из [1, 2].
    const selects = Array.from(root().querySelectorAll('p-select'));
    expect(selects.length).toBe(2);
    expect(selectLabelOf(selects[0]!)).toBe('ИК-221');
    expect(selectLabelOf(selects[1]!)).toBe('1');

    // Страница 1: 5 студентов по ФИО↑.
    const rows = root().querySelectorAll('tbody tr');
    expect(rows.length).withContext('5 студентов на странице').toBe(PAGE_SIZE);
    expect(bodyRow(1).querySelector('td')?.textContent?.trim()).toBe('Иванов Иван Иванович 01');

    // Колонки — лабы 1..20 по number↑.
    const labs = labGroupHeaders();
    expect(labs.length).toBe(20);
    expect(labs[0]).toBe('Лаб 1');
    expect(labs[19]).toBe('Лаб 20');

    // Шапка двухуровневая: у КАЖДОЙ работы пара «Сдача»/«Защита», включая
    // defenseRequired=false (нечётные номера сида).
    const subHeaders = Array.from(
      (root().querySelectorAll('thead tr')[1] as HTMLTableRowElement).querySelectorAll('th'),
    ).map((th) => th.textContent?.trim());
    expect(subHeaders.filter((text) => text === 'Сдача').length).toBe(20);
    expect(subHeaders.filter((text) => text === 'Защита').length).toBe(20);
    for (let index = 1; index <= 20; index++) {
      expect(subHeaders[(index - 1) * 2]).withContext(`пара лабы ${index}`).toBe('Сдача');
      expect(subHeaders[(index - 1) * 2 + 1]).toBe('Защита');
    }

    expect(rangeCaption()).toBe('Показать записи с 1 по 5 из 25');
  }));

  it('TS-302: выбор 15.09.2026 шлёт update с обеими датами пары, ячейка 15.09.2026; перезагрузка и листание страниц значение сохраняют; в состоянии бэкенда updated_by — преподаватель', fakeAsync(() => {
    createPage();
    expect(cellInput(1, 6, 'submit').value).withContext('сид: сдача лабы 6 пуста').toBe('');

    // Выбор даты в датапике ячейки (dateFormat дд.мм.гггг).
    fixture.componentInstance.onCellDateChange(student01, lab6, 'submitDate', new Date(2026, 8, 15));
    env.settle(fixture, 1);

    // upsert: обе даты пары, вторая — актуальное значение (была пуста).
    const saved = env.backend
      .read()
      .submissions.find(
        (candidate) => candidate.studentId === student01 && candidate.labId === lab6,
      );
    expect(saved?.submitDate).toBe('2026-09-15');
    expect(saved?.defenseDate).toBeNull();
    expect(saved?.updatedBy).withContext('updated_by — id преподавателя сессии').toBe(teacherId);

    expect(cellInput(1, 6, 'submit').value).toBe('15.09.2026');

    // Перезагрузка страницы: сервер — истина, значение восстановлено бэкендом.
    fixture.destroy();
    createPage();
    expect(cellInput(1, 6, 'submit').value).toBe('15.09.2026');

    // Листание страниц туда-обратно: значение сохранено.
    paginatorButton('next').click();
    env.settle(fixture, 1);
    paginatorButton('page', '1').click();
    env.settle(fixture, 1);
    expect(cellInput(1, 6, 'submit').value).toBe('15.09.2026');
  }));

  /**
   * TS-303 (факт-коррекция 2, семантика коммита ревью T-114 итерация 2 /
   * IF-106): коммитятся только валидный распарсенный результат и ЯВНЫЕ
   * очистки — кнопка ×, пустой blur, Enter в пустом поле; каждый шлёт update
   * {submitDate:'2026-09-01', defenseDate:null}, ячейка пустая без прочерка,
   * результат персистентен. Непарсящийся текст ('abc', «99.99.9999» —
   * переполнение года parseDate отбрасывает) НЕ порождает серверного вызова
   * (null от неудачного парсинга — не актуальное значение): при blur поле
   * откатывается к прежнему значению сервера, запись в бэкенде не менялась.
   * Регресс CR-001: промежуточные парсинги ('1', '15.') — ноль update,
   * недопечатанный текст не затирается. Пользовательские пути — через DOM
   * реальной ячейки (паттерн typeCellDate/blurCellDate зоны T-114).
   */
  it('TS-303: явные очистки (×, пустой blur, Enter в пустом) коммитят {submitDate, defenseDate:null}; непарсящийся текст не шлёт update и откатывается на blur', fakeAsync(() => {
    createPage();
    const updateSpy = spyOn(submissionsService, 'update').and.callThrough();

    const defenseRecord = (): { submitDate: string | null; defenseDate: string | null } | undefined =>
      env.backend
        .read()
        .submissions.find(
          (candidate) => candidate.studentId === student01 && candidate.labId === lab1,
        );
    const expectCleared = (calls: number): void => {
      expect(updateSpy.calls.count()).toBe(calls);
      expect(updateSpy.calls.mostRecent().args[0]).toEqual({
        studentId: student01,
        labId: lab1,
        submitDate: '2026-09-01',
        defenseDate: null,
      });
      expect(cellInput(1, 1, 'defense').value).withContext('ячейка пустая').toBe('');
      expect(root().querySelector('tbody')?.textContent).not.toContain('—');
      expect(defenseRecord()?.submitDate).withContext('сдача не тронута').toBe('2026-09-01');
      expect(defenseRecord()?.defenseDate).toBeNull();
    };
    /** Возврат защите сид-значения: альтернативные пути — из того же given. */
    const resetDefense = (): void => {
      fixture.componentInstance.onCellDateChange(student01, lab1, 'defenseDate', new Date(2026, 8, 11));
      env.settle(fixture, 1);
      expect(defenseRecord()?.defenseDate).toBe('2026-09-11');
    };

    // Регресс CR-001: промежуточные неудачные парсинги не шлют update
    // и не затирают недопечатанный текст.
    typeCell(1, 1, 'defense', '1');
    env.settle(fixture, 1);
    expect(updateSpy.calls.count()).withContext('«1» — ноль update').toBe(0);
    expect(cellInput(1, 1, 'defense').value).withContext('ввод не затёрт').toBe('1');

    typeCell(1, 1, 'defense', '15.');
    env.settle(fixture, 1);
    expect(updateSpy.calls.count()).withContext('«15.» — ноль update').toBe(0);
    expect(cellInput(1, 1, 'defense').value).toBe('15.');

    blurCell(1, 1, 'defense');
    env.settle(fixture, 1);
    expect(updateSpy.calls.count()).withContext('blur после промежуточных — ноль update').toBe(0);
    expect(cellInput(1, 1, 'defense').value)
      .withContext('поле откатилось к прежнему значению сервера')
      .toBe('11.09.2026');
    expect(defenseRecord()?.defenseDate).toBe('2026-09-11');

    // Непарсящийся текст «abc»: серверного вызова нет; blur откатывает поле.
    typeCell(1, 1, 'defense', 'abc');
    env.settle(fixture, 1);
    expect(updateSpy.calls.count()).withContext('«abc» — ноль update').toBe(0);
    expect(cellInput(1, 1, 'defense').value).toBe('abc');
    expect(defenseRecord()?.defenseDate).withContext('запись не менялась').toBe('2026-09-11');

    blurCell(1, 1, 'defense');
    env.settle(fixture, 1);
    expect(updateSpy.calls.count()).toBe(0);
    expect(cellInput(1, 1, 'defense').value)
      .withContext('откат к прежнему значению сервера')
      .toBe('11.09.2026');

    // «99.99.9999» — тоже непарсящийся (переполнение года отбрасывается
    // parseDate): ноль update, blur возвращает 11.09.2026.
    typeCell(1, 1, 'defense', '99.99.9999');
    env.settle(fixture, 1);
    expect(updateSpy.calls.count()).toBe(0);
    blurCell(1, 1, 'defense');
    env.settle(fixture, 1);
    expect(updateSpy.calls.count()).toBe(0);
    expect(cellInput(1, 1, 'defense').value).toBe('11.09.2026');
    expect(defenseRecord()?.defenseDate).toBe('2026-09-11');

    // Явная очистка 1: кнопка × коммитит сброс.
    clickCellClearIcon(1, 1, 'defense');
    env.settle(fixture, 1);
    expectCleared(1);

    // Явная очистка 2: пустой blur (resetDefense увеличил счётчик до 2).
    resetDefense();
    cellInput(1, 1, 'defense').value = '';
    blurCell(1, 1, 'defense');
    env.settle(fixture, 1);
    expectCleared(3);

    // Явная очистка 3: Enter в пустом поле (после resetDefense счётчик 4).
    resetDefense();
    cellInput(1, 1, 'defense').value = '';
    cellInput(1, 1, 'defense').dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }),
    );
    env.settle(fixture, 1);
    expectCleared(5);

    // Персистентность: перезагрузка страницы читает сброс из бэкенда.
    fixture.destroy();
    createPage();
    expect(cellInput(1, 1, 'defense').value).withContext('защита пуста после перезагрузки').toBe('');
    expect(cellInput(1, 1, 'submit').value).withContext('сдача не тронута').toBe('01.09.2026');
  }));

  it('TS-305: пагинация по 5 — стрелка и номера страниц, стр.5 «с 21 по 25 из 25», возврат на 1; порядок ФИО↑ на всех страницах', fakeAsync(() => {
    createPage();

    const expectedNames = Array.from({ length: TOTAL_221 }, (_, index) =>
      `Иванов Иван Иванович ${String(index + 1).padStart(2, '0')}`,
    );

    function rowNames(): string[] {
      return Array.from(root().querySelectorAll('tbody tr td:first-child')).map((cell) =>
        cell.textContent?.trim() ?? '',
      );
    }

    expect(rangeCaption()).toBe('Показать записи с 1 по 5 из 25');

    // Стрелка: страница 2.
    paginatorButton('next').click();
    env.settle(fixture, 1);
    expect(rangeCaption()).toBe('Показать записи с 6 по 10 из 25');
    expect(rowNames()).toEqual(expectedNames.slice(5, 10));

    // Номера страниц: страница 5 и возврат на 1.
    paginatorButton('page', '5').click();
    env.settle(fixture, 1);
    expect(rangeCaption()).toBe('Показать записи с 21 по 25 из 25');
    expect(rowNames()).toEqual(expectedNames.slice(20, 25));
    expect(rowNames().length).toBe(5);

    paginatorButton('page', '1').click();
    env.settle(fixture, 1);
    expect(rangeCaption()).toBe('Показать записи с 1 по 5 из 25');
    expect(rowNames()).toEqual(expectedNames.slice(0, 5));
  }));

  it('TS-306: пустая группа ИК-223 — строка «В группе нет студентов» и подпись «Показать записи с 1 по 0 из 0» (аменда 7)', fakeAsync(() => {
    createPage();

    selectToolbarOption(0, 'ИК-223');
    env.settle(fixture, 1);

    const emptyRow = qsIn(fixture, '[data-test="empty-group-row"]');
    expect(emptyRow?.textContent?.trim()).toBe('В группе нет студентов');
    expect(emptyRow?.getAttribute('colspan')).withContext('1 + 20×2 колонки').toBe('41');
    expect(rangeCaption()).withContext('аменда 7: подпись выводится всегда').toBe(
      'Показать записи с 1 по 0 из 0',
    );
  }));

  it('TS-307: вырожденные состояния — без работ «Нет семестров с работами», без групп «Нет групп»; таблица скрыта, ошибочных баннеров нет', fakeAsync(() => {
    // Сценарий А: удалены все работы (каскадно со сдачами) — прямой мутацией
    // состояния бэкенда (другой сеанс).
    env.backend.mutate((state) => {
      state.labs = [];
      state.submissions = [];
    });
    createPage();

    const selectsA = Array.from(root().querySelectorAll('p-select'));
    expect(selectsA[1]?.classList.contains('p-disabled'))
      .withContext('селектор семестра пуст/disabled')
      .toBeTrue();
    expect(qsIn(fixture, '[data-test="no-semesters-caption"]')?.textContent?.trim()).toBe(
      'Нет семестров с работами',
    );
    expect(root().querySelector('p-table')).withContext('таблица скрыта').toBeNull();
    expect(notifications.desktopMessage()).withContext('ошибочных баннеров нет').toBeNull();
    expect(notifications.mobileMessage()).toBeNull();

    fixture.destroy();

    // Сценарий Б: удалены все группы.
    env.backend.mutate((state) => {
      state.groups = [];
    });
    createPage();

    const selectsB = Array.from(root().querySelectorAll('p-select'));
    expect(selectsB.length).withContext('селектор семестра скрыт целиком').toBe(1);
    expect(selectsB[0]?.classList.contains('p-disabled'))
      .withContext('селектор групп пуст/disabled')
      .toBeTrue();
    expect(qsIn(fixture, '[data-test="no-groups-caption"]')?.textContent?.trim()).toBe('Нет групп');
    expect(root().querySelector('p-table')).withContext('таблица скрыта').toBeNull();
    expect(notifications.desktopMessage()).withContext('ошибочных баннеров нет').toBeNull();
    expect(notifications.mobileMessage()).toBeNull();
  }));

  it('TS-309: гонки смены группы/семестра/страницы — отрисован только грид последнего запроса (ИК-222, сем.2, стр.2), устаревшие ответы отброшены', fakeAsync(() => {
    createPage();

    // Три очень быстрых смены подряд (до завершения предыдущих запросов).
    selectToolbarOption(0, 'ИК-222'); // группа → ИК-222
    selectToolbarOption(1, '2'); // семестр → 2
    paginatorButton('next').click(); // страница → 2
    env.settle(fixture, 1);

    // Селекторы согласованы с последним положением.
    const selects = Array.from(root().querySelectorAll('p-select'));
    expect(selectLabelOf(selects[0]!)).toBe('ИК-222');
    expect(selectLabelOf(selects[1]!)).toBe('2');

    // Грид последнего запроса: ИК-222 (5 студентов), семестр 2 (лабы 1..3),
    // страница 2 — студентов нет; подпись построена формулой ADR-109 по
    // запрошенной странице последнего запроса (стр.2, total 5).
    expect(labGroupHeaders()).toEqual(['Лаб 1', 'Лаб 2', 'Лаб 3']);
    expect(rangeCaption()).toBe('Показать записи с 6 по 5 из 5');
    const rowTexts = Array.from(root().querySelectorAll('tbody tr')).map((row) =>
      row.textContent?.trim() ?? '',
    );
    expect(
      rowTexts.some((text) => text.includes('Иванов Иван Иванович')),
    ).withContext('данные других групп/семестров/страниц не мелькают').toBeFalse();

    // Итог согласован: записи сдач семестра 1 в этом гриде не видны.
    expect(root().querySelector('tbody')?.textContent).not.toContain('01.09.2026');
  }));

  it('TS-310: параллельное сохранение двух ячеек с отказом второй — первая сохранена, баннер «Лабораторная не найдена», грид перезагружен, обе ячейки разблокированы', fakeAsync(() => {
    createPage();

    // Эмуляция другого сеанса: лабораторная второй пары удаляется до сохранения.
    env.backend.removeLabDirect(lab2);

    // Почти одновременное сохранение: первая ячейка (student01, лаб 6) и
    // вторая (student02, лаб 2) без ожидания завершения первой.
    fixture.componentInstance.onCellDateChange(student01, lab6, 'submitDate', new Date(2026, 8, 15));
    fixture.componentInstance.onCellDateChange(student02, lab2, 'submitDate', new Date(2026, 8, 16));
    env.settle(fixture, 2);

    // Первая дата сохранена и видна; в бэкенде запись с updated_by преподавателя.
    expect(cellInputByHeader(1, 6, 'submit').value).toBe('15.09.2026');
    const saved = env.backend
      .read()
      .submissions.find(
        (candidate) => candidate.studentId === student01 && candidate.labId === lab6,
      );
    expect(saved?.submitDate).toBe('2026-09-15');
    expect(saved?.updatedBy).toBe(teacherId);

    // Вторая: баннер «Лабораторная не найдена» якорем 'header'.
    expect(notifications.desktopMessage()).toEqual({
      severity: 'error',
      text: 'Лабораторная не найдена',
    });
    env.drainNotifications();

    // Грид перезагружен (сервер — истина): колонки лабы 2 больше нет.
    expect(labGroupHeaders()).not.toContain('Лаб 2');
    expect(labGroupHeaders().length).toBe(19);

    // Обе ячейки разблокированы после завершения (disabled внутреннего input).
    expect(fixture.componentInstance.isCellSaving(student01, lab6, 'submitDate')).toBeFalse();
    expect(fixture.componentInstance.isCellSaving(student02, lab2, 'submitDate')).toBeFalse();
    expect(cellInputByHeader(1, 6, 'submit').disabled).toBeFalse();
  }));

  it('TS-311: повторная установка той же даты — upsert: ровно одна запись пары, updated_by преподаватель, updated_at обновился, в гриде единственное значение', fakeAsync(() => {
    createPage();

    fixture.componentInstance.onCellDateChange(student01, lab6, 'submitDate', new Date(2026, 8, 15));
    env.settle(fixture, 1);
    const first = env.backend
      .read()
      .submissions.find(
        (candidate) => candidate.studentId === student01 && candidate.labId === lab6,
      );
    expect(first).toBeDefined();

    // Повторный выбор той же даты.
    fixture.componentInstance.onCellDateChange(student01, lab6, 'submitDate', new Date(2026, 8, 15));
    env.settle(fixture, 1);

    const records = env.backend
      .read()
      .submissions.filter(
        (candidate) => candidate.studentId === student01 && candidate.labId === lab6,
      );
    expect(records.length).withContext('дубликат не создан').toBe(1);
    expect(records[0]!.updatedBy).toBe(teacherId);
    expect(records[0]!.updatedAt > first!.updatedAt)
      .withContext('updated_at обновился (виртуальные часы волн)')
      .toBeTrue();

    const valueInputs = Array.from(root().querySelectorAll('tbody tr:nth-child(1) input')).filter(
      (input) => (input as HTMLInputElement).value === '15.09.2026',
    );
    expect(valueInputs.length).withContext('в гриде единственное значение').toBe(1);
  }));

  it('TS-312: сид-сдачи в ведомости — дд.мм.гггг, пустая дата пустая ячейка без прочерка (student01 лабы 1–3, student02 лаба 1)', fakeAsync(() => {
    createPage();

    // student01: лаб1 01.09/11.09, лаб2 02.09/12.09, лаб3 03.09/пусто.
    expect(cellInput(1, 1, 'submit').value).toBe('01.09.2026');
    expect(cellInput(1, 1, 'defense').value).toBe('11.09.2026');
    expect(cellInput(1, 2, 'submit').value).toBe('02.09.2026');
    expect(cellInput(1, 2, 'defense').value).toBe('12.09.2026');
    expect(cellInput(1, 3, 'submit').value).toBe('03.09.2026');
    expect(cellInput(1, 3, 'defense').value).withContext('пустая дата — пустая ячейка').toBe('');

    // student02: лаб1 01.09/пусто.
    expect(bodyRow(2).querySelector('td')?.textContent?.trim()).toBe('Иванов Иван Иванович 02');
    expect(cellInput(2, 1, 'submit').value).toBe('01.09.2026');
    expect(cellInput(2, 1, 'defense').value).toBe('');

    expect(root().querySelector('tbody')?.textContent).not.toContain('—');
  }));

  it('TS-314: выбор даты по удалённой лабораторной — баннер «Лабораторная не найдена» якорем header, грид перезагружен, блокировка снята', fakeAsync(() => {
    createPage();

    // Другой сеанс удаляет лабораторную открытой пары.
    env.backend.removeLabDirect(lab6);

    fixture.componentInstance.onCellDateChange(student01, lab6, 'submitDate', new Date(2026, 8, 15));
    env.settle(fixture, 2);

    expect(notifications.desktopMessage()).toEqual({
      severity: 'error',
      text: 'Лабораторная не найдена',
    });
    env.drainNotifications();

    // Грид перезагружен (сервер — истина): колонки лабы 6 нет, ячейки
    // отражают фактическое состояние, блокировка снята.
    expect(labGroupHeaders()).not.toContain('Лаб 6');
    expect(labGroupHeaders().length).toBe(19);
    expect(fixture.componentInstance.isCellSaving(student01, lab6, 'submitDate')).toBeFalse();
    expect(cellInput(1, 1, 'submit').disabled).withContext('ячейки разблокированы').toBeFalse();
    // Запись пары в состоянии бэкенда не появилась.
    expect(
      env.backend
        .read()
        .submissions.some(
          (candidate) => candidate.studentId === student01 && candidate.labId === lab6,
        ),
    ).toBeFalse();
  }));

  // Константа GROUP_NAMES используется для самопроверки сид-набора групп.
  it('сид соответствует сценарию: группы ИК-221(25)/ИК-222(5)/ИК-223(0), 4 сид-сдачи', () => {
    const state = env.backend.read();
    const counts = GROUP_NAMES.map((name) => ({
      name,
      students: state.users.filter((user) => {
        const group = state.groups.find((candidate) => candidate.name === name);
        return user.role === 'student' && user.groupId === group?.id;
      }).length,
    }));
    expect(counts.map((entry) => entry.students)).toEqual([25, 5, 0]);
    expect(state.submissions.length).withContext('сид-сдачи: 4 записи по SCR-009/010').toBe(4);
  });
});
