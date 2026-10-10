/**
 * Интеграционные сценарии списка /works (FR-4.3, C-111, IF-103):
 *  - TS-201: сид-список — колонки, дефолтный порядок, первая страница, подпись;
 *  - TS-202: пагинация — страница 2 из 3 (полная страница в середине);
 *  - TS-203: фильтр семестра — опции из getSemesters, сброс страницы, «Все семестры»;
 *  - TS-204: сортировка «Номер»/«Семестр» — asc↔desc без третьего состояния;
 *  - TS-205: колонка «Задание» — ссылка у кратных 5, прочерк у остальных;
 *  - TS-206: «Нужна защита» — read-only чекбоксы;
 *  - TS-207/TS-208: диалог удаления, блокировка списка, «No»; удаление,
 *    «Удалено» и каскадное удаление сдач на сервере;
 *  - TS-209: опустошение семестра и списка (аменда 7), подпись «с 1 по 0 из 0»;
 *  - TS-210: remove-404 — баннер 'header' + перезагрузка страницы без редиректа;
 *  - TS-211: двойной клик «Yes» — ровно одно удаление;
 *  - TS-212: гонка быстрых смен фильтра — последний запрос выигрывает;
 *  - TS-234: пагинация — последняя неполная страница.
 *
 * Уровень — интеграционный: WorksPage → LabsService → HttpTestingController →
 * состояние WorksBackendStub (без мок-слоя, FR-026); режим уведомлений —
 * BreakpointObserverStub; тайминги — волны ответов бэкенда в fakeAsync.
 */
import { fakeAsync } from '@angular/core/testing';

import {
  WorksIntegrationHarness,
} from './works-integration-harness';

describe('Интеграция: список лабораторных /works (FR-4.3)', () => {
  let harness: WorksIntegrationHarness;

  beforeEach(() => {
    harness = WorksIntegrationHarness.setup();
  });

  afterEach(() => {
    harness.detach();
    WorksIntegrationHarness.restoreSeed();
    localStorage.clear();
  });

  /** Монтирование, сессия teacher и открытие /works с загруженным сидом. */
  function openWorks(): void {
    harness.attach();
    harness.loginTeacher();
    harness.navigate('/works');
    harness.flush(); // getList + getSemesters параллельно
  }

  it('TS-201: сид-список — колонки, дефолтный порядок, первая страница, подпись «с 1 по 10 из 23»', fakeAsync(() => {
    openWorks();

    expect(harness.native.querySelector('h1')!.textContent!.trim()).toBe('Лабораторные работы');
    expect(harness.native.querySelector('.works-page__head button')!.textContent).toContain(
      'Добавить…',
    );
    expect(harness.headerTitles()).toEqual([
      'Номер',
      'Содержание',
      'Семестр',
      'Задание',
      'Нужна защита',
      'Действия',
    ]);

    const rows = harness.rows();
    expect(rows.length).withContext('первая страница из 23 записей').toBe(10);
    rows.forEach((row, index) => {
      const cells = harness.rowCells(row);
      // Дефолтная сортировка semester↑ затем number↑: семестр 1, №1–10.
      expect(cells[2]).toBe('1');
      expect(Number(cells[0])).toBe(index + 1);
      expect(cells[1]).toBe(`Содержание лабораторной работы №${index + 1}`);
    });
    expect(harness.reportText()).toBe('Показать записи с 1 по 10 из 23');
  }));

  it('TS-202: пагинация — страница 2 из 3, полная страница в середине («с 11 по 20 из 23»)', fakeAsync(() => {
    openWorks();

    harness.gotoPage(2);
    harness.flush(); // getList страницы 2

    const rows = harness.rows();
    expect(rows.length).toBe(10);
    rows.forEach((row, index) => {
      const cells = harness.rowCells(row);
      expect(Number(cells[0])).toBe(11 + index);
      expect(cells[2]).toBe('1');
    });
    // Формула ADR-111: X=(2−1)·10+1=11, Y=min(20,23)=20 — 13 записей на
    // полной средней странице быть не может.
    expect(harness.reportText()).toBe('Показать записи с 11 по 20 из 23');
  }));

  it('TS-234: пагинация — последняя неполная страница («с 21 по 23 из 23»)', fakeAsync(() => {
    openWorks();

    harness.gotoPage(3);
    harness.flush(); // getList страницы 3

    const rows = harness.rows();
    expect(rows.length).toBe(3);
    rows.forEach((row, index) => {
      const cells = harness.rowCells(row);
      expect(Number(cells[0])).toBe(index + 1);
      expect(cells[2]).toBe('2');
    });
    expect(harness.reportText()).toBe('Показать записи с 21 по 23 из 23');
  }));

  it('TS-203: фильтр семестра — опции из getSemesters, сброс страницы, «Все семестры»', fakeAsync(() => {
    openWorks();
    const getList = spyOn(harness.labs, 'getList').and.callThrough();

    harness.openSemesterSelect();
    // Аменда 7: опции ровно по getSemesters — семестры 3–10 без работ отсутствуют.
    expect(harness.semesterOptionLabels()).toEqual(['Все семестры', '1', '2']);

    harness.chooseSemesterOption('1');
    harness.flush(); // getList семестра 1
    expect(getList.calls.mostRecent().args[0]).toEqual({
      semester: 1,
      page: 1, // смена фильтра сбрасывает страницу
      sortField: 'semester',
      sortDir: 'asc',
    });
    expect(harness.rows().length).toBe(10);
    expect(harness.reportText()).toBe('Показать записи с 1 по 10 из 20');

    harness.openSemesterSelect();
    harness.chooseSemesterOption('2');
    harness.flush();
    expect(getList.calls.mostRecent().args[0]).toEqual({
      semester: 2,
      page: 1,
      sortField: 'semester',
      sortDir: 'asc',
    });
    expect(harness.rows().length).toBe(3);
    expect(harness.reportText()).toBe('Показать записи с 1 по 3 из 3');

    harness.openSemesterSelect();
    harness.chooseSemesterOption('Все семестры');
    harness.flush();
    expect(getList.calls.mostRecent().args[0]).toEqual({
      semester: null,
      page: 1,
      sortField: 'semester',
      sortDir: 'asc',
    });
    expect(harness.reportText()).toBe('Показать записи с 1 по 10 из 23');
  }));

  it('TS-204: сортировка «Номер»/«Семестр» — asc↔desc без третьего состояния, страница не сбрасывается', fakeAsync(() => {
    openWorks();
    const getList = spyOn(harness.labs, 'getList').and.callThrough();

    // Дефолт semester↑,number↑ (aria-sort на «Семестр»).
    expect(harness.sortHeader('Семестр').getAttribute('aria-sort')).toBe('ascending');

    // Клик по «Номер»: asc → desc → asc (третьего состояния нет).
    harness.sortHeader('Номер').click();
    harness.flush();
    expect(getList.calls.mostRecent().args[0]).toEqual({
      semester: null,
      page: 1,
      sortField: 'number',
      sortDir: 'asc',
    });
    expect(harness.sortHeader('Номер').getAttribute('aria-sort')).toBe('ascending');

    harness.sortHeader('Номер').click();
    harness.flush();
    expect(getList.calls.mostRecent().args[0]).toEqual({
      semester: null,
      page: 1,
      sortField: 'number',
      sortDir: 'desc',
    });

    harness.sortHeader('Номер').click();
    harness.flush();
    expect(getList.calls.mostRecent().args[0]).toEqual({
      semester: null,
      page: 1,
      sortField: 'number',
      sortDir: 'asc',
    });

    // Повторные клики по «Семестр»: asc → desc → asc.
    harness.sortHeader('Семестр').click();
    harness.flush();
    expect(getList.calls.mostRecent().args[0]).toEqual({
      semester: null,
      page: 1,
      sortField: 'semester',
      sortDir: 'asc',
    });
    harness.sortHeader('Семестр').click();
    harness.flush();
    expect(getList.calls.mostRecent().args[0]).toEqual({
      semester: null,
      page: 1,
      sortField: 'semester',
      sortDir: 'desc',
    });
    expect(harness.sortHeader('Семестр').getAttribute('aria-sort')).toBe('descending');
    harness.sortHeader('Семестр').click();
    harness.flush();
    expect(getList.calls.mostRecent().args[0]).toEqual({
      semester: null,
      page: 1,
      sortField: 'semester',
      sortDir: 'asc',
    });

    // Сортировка не сбрасывает страницу (сбрасывает только фильтр, IF-103).
    harness.gotoPage(2);
    harness.flush();
    harness.sortHeader('Номер').click();
    harness.flush();
    harness.sortHeader('Номер').click();
    harness.flush();
    expect(getList.calls.mostRecent().args[0]).toEqual({
      semester: null,
      page: 2,
      sortField: 'number',
      sortDir: 'desc',
    });
    // Вторичная колонка всегда asc: связи по номеру (№3, №2 семестров 1 и 2)
    // упорядочены семестром по возрастанию.
    const pairs = harness.rows().map((row) => {
      const cells = harness.rowCells(row);
      return `${cells[0]}:${cells[2]}`;
    });
    expect(pairs).toEqual([
      '10:1',
      '9:1',
      '8:1',
      '7:1',
      '6:1',
      '5:1',
      '4:1',
      '3:1',
      '3:2',
      '2:1',
    ]);
  }));

  it('TS-205: колонка «Задание» — ссылка у кратных 5, прочерк у остальных', fakeAsync(() => {
    openWorks();

    // Страница 1 (семестр 1 №1–10): ссылки у №5 и №10.
    const rows = harness.rows();
    const linkedNumbers = rows
      .filter((row) => row.querySelector('a.works-page__assignment') !== null)
      .map((row) => harness.rowCells(row)[0]);
    expect(linkedNumbers).toEqual(['5', '10']);

    const link5 = rows[4]!.querySelector<HTMLAnchorElement>('a.works-page__assignment')!;
    // Текст = URL без схемы, href = исходный https-URL.
    expect(link5.textContent!.trim()).toBe('git.example.com/assignments/1/5');
    expect(link5.getAttribute('href')).toBe('https://git.example.com/assignments/1/5');
    expect(link5.getAttribute('target')).toBe('_blank');
    expect(link5.getAttribute('rel')).toBe('noopener noreferrer');

    // Работы без ссылки — прочерк «—», не кликабельный (без href).
    const dash = rows[0]!.querySelector('.works-page__dash');
    expect(dash!.textContent!.trim()).toBe('—');
    expect(rows[0]!.querySelector('a.works-page__assignment')).toBeNull();

    // Страница 2 (№11–20): ссылки у №15 и №20.
    harness.gotoPage(2);
    harness.flush();
    const page2 = harness.rows();
    expect(
      page2.map((row) => row.querySelector('a.works-page__assignment') !== null),
    ).toEqual([false, false, false, false, true, false, false, false, false, true]);
    const link15 = page2[4]!.querySelector<HTMLAnchorElement>('a.works-page__assignment')!;
    expect(link15.getAttribute('href')).toBe('https://git.example.com/assignments/1/15');
    expect(link15.textContent!.trim()).toBe('git.example.com/assignments/1/15');
  }));

  it('TS-206: «Нужна защита» — read-only чекбоксы: чётные отмечены, клик не меняет и не порождает запросов', fakeAsync(() => {
    openWorks();

    const rows = harness.rows();
    rows.forEach((row) => {
      const checkbox = row.querySelector('p-checkbox');
      expect(checkbox).withContext('чекбокс защиты в строке').not.toBeNull();
      const number = Number(harness.rowCells(row)[0]);
      expect(checkbox!.getAttribute('data-p-checked')).toBe(
        number % 2 === 0 ? 'true' : 'false',
      );
      expect(checkbox!.getAttribute('data-p-disabled')).toBe('true');
    });

    (rows[0]!.querySelector('p-checkbox') as HTMLElement).click(); // №1, пустой
    harness.uiSettle();
    expect(harness.rowByNumber(1).querySelector('p-checkbox')!.getAttribute('data-p-checked')).toBe(
      'false',
    );
    // Ни одного нового HTTP-запроса (read-only чекбокс, FR-011).
    expect(harness.pendingHttpRequests()).toBe(0);
  }));

  it('TS-207: диалог удаления — дословный текст с №N и №M, «Yes»/«No», блокировка списка, «No» без удаления', fakeAsync(() => {
    openWorks();

    harness.openRemoveDialog(harness.rowByNumber(2));

    expect(harness.dialogMessage()).toBe(
      'Вы точно хотите удалить лабораторную №2 в семестре №1?',
    );
    expect(harness.dialogButton('accept').textContent!.trim()).toBe('Yes');
    expect(harness.dialogButton('reject').textContent!.trim()).toBe('No');

    // Пока диалог открыт — список заблокирован (затемнение + pointer-events).
    const list = harness.native.querySelector('.works-page__list')!;
    expect(list.classList).toContain('works-page__list--locked');
    expect(getComputedStyle(list).opacity).toBe('0.35');
    expect(getComputedStyle(list).pointerEvents).toBe('none');

    // «No» закрывает диалог (скрытие PrimeNG — отложенный ~10 мс таймер):
    // remove не вызван, строка на месте.
    harness.dialogButton('reject').click();
    harness.dialogSettle();
    expect(harness.dialogMessage()).toBe('');
    expect(list.classList).not.toContain('works-page__list--locked');
    expect(harness.rowByNumber(2)).toBeDefined();
  }));

  it('TS-208: удаление — «Удалено», перезагрузка списка, каскадное удаление сдач на сервере', fakeAsync(() => {
    openWorks();
    const lab1 = harness.labId(1, 1);
    const lab2 = harness.labId(1, 2);
    const lab3 = harness.labId(1, 3);
    const student01 = harness.readDb().users.find((user) => user.login === 'student01')!.id;

    harness.openRemoveDialog(harness.rowByNumber(1));
    harness.dialogButton('accept').click();
    harness.flush(3); // remove → getList → getSemesters

    expect(harness.toastTexts()).toContain('Удалено');
    expect(harness.rows().some((row) => harness.rowCells(row)[0] === '1')).toBeFalse();
    expect(harness.reportText()).toBe('Показать записи с 1 по 10 из 22');

    // В состоянии сервера отсутствуют только submissions удалённой лабы.
    const db = harness.readDb();
    expect(db.labs.some((lab) => lab.id === lab1)).toBeFalse();
    expect(db.submissions.some((submission) => submission.labId === lab1)).toBeFalse();
    // Сдачи student01 по лабам 2 и 3 сохранены.
    expect(
      db.submissions.some(
        (submission) => submission.studentId === student01 && submission.labId === lab2,
      ),
    ).toBeTrue();
    expect(
      db.submissions.some(
        (submission) => submission.studentId === student01 && submission.labId === lab3,
      ),
    ).toBeTrue();
    harness.finalize(); // таймеры автозакрытия «Удалено»
  }));

  it('TS-209: опустошение семестра и списка — сброс фильтра, исчезновение опции, подпись «с 1 по 0 из 0»', fakeAsync(() => {
    openWorks();
    const getList = spyOn(harness.labs, 'getList').and.callThrough();

    harness.openSemesterSelect();
    harness.chooseSemesterOption('2');
    harness.flush();
    expect(harness.rows().length).toBe(3);

    // Удалить все 3 работы семестра 2 подряд.
    for (let removed = 0; removed < 3; removed += 1) {
      harness.openRemoveDialog(harness.rows()[0]!);
      harness.dialogButton('accept').click();
      // remove → getList → getSemesters; на последней — ещё перезагрузка
      // со сбросом фильтра (аменда 7).
      harness.flush(removed === 2 ? 4 : 3);
    }

    // Опция «2» исчезла, фильтр автоматически «Все семестры», без ошибки.
    expect(harness.semesterSelectLabel()).toBe('Все семестры');
    harness.openSemesterSelect();
    expect(harness.semesterOptionLabels()).toEqual(['Все семестры', '1']);
    harness.closeSemesterSelect();
    expect(getList.calls.mostRecent().args[0]).toEqual({
      semester: null,
      page: 1,
      sortField: 'semester',
      sortDir: 'asc',
    });
    expect(document.querySelectorAll('.p-toast-message-error').length).toBe(0);

    // Довести список до пустого: осталось 20 работ семестра 1.
    for (let removed = 0; removed < 20; removed += 1) {
      harness.openRemoveDialog(harness.rows()[0]!);
      harness.dialogButton('accept').click();
      harness.flush(3);
    }

    expect(harness.native.querySelector('.works-page__empty')!.textContent).toContain(
      'Записей нет',
    );
    expect(harness.reportText()).toBe('Показать записи с 1 по 0 из 0');
    harness.openSemesterSelect();
    expect(harness.semesterOptionLabels()).toEqual(['Все семестры']);
    harness.finalize(); // таймеры автозакрытия «Удалено»
  }));

  // known implementation defect (amendment 5): баннер «header» + перезагрузка
  // текущей страницы после remove-404 ожидаемы сценарием; если реализация
  // (works-page.ts, catch remove) не перезагружает список — упадут ассерты
  // ниже, и это фиксируется как дефект реализации на run-стадии.
  it('TS-210: remove-404 — баннер «Лабораторная не найдена», без редиректа, страница списка перезагружена', fakeAsync(() => {
    openWorks();
    // Шпион после начальной загрузки: считает только перезагрузки страницы.
    const getList = spyOn(harness.labs, 'getList').and.callThrough();
    const lab1 = harness.labId(1, 1);

    // Работа №1 удалена «из другой вкладки» — прямой мутацией сервера.
    harness.removeLabDirect(lab1);
    harness.flush();

    // Строка №1 ещё на экране (данные страницы устарели) — удаление по ней.
    harness.openRemoveDialog(harness.rowByNumber(1));
    harness.dialogButton('accept').click();
    harness.flush(2); // волна 1 — remove → 404; волна 2 — перезагрузка страницы (аменда 5)

    expect(harness.toastTexts()).toContain('Лабораторная не найдена');
    // Редиректа НЕТ — список активен.
    expect(harness.router.url).toBe('/works');
    expect(harness.native.querySelector('h1')!.textContent).toContain('Лабораторные работы');
    // Аменда 5: перезагрузка текущей страницы списка, строка исчезла.
    expect(getList).toHaveBeenCalledTimes(1);
    expect(harness.rows().some((row) => harness.rowCells(row)[0] === '1')).toBeFalse();
    expect(harness.reportText()).toBe('Показать записи с 1 по 10 из 22');
    harness.finalize(); // таймер автозакрытия тоста ошибки
  }));

  it('TS-211: двойной клик «Yes» — ровно одно удаление', fakeAsync(() => {
    openWorks();
    const remove = spyOn(harness.labs, 'remove').and.callThrough();

    harness.openRemoveDialog(harness.rowByNumber(1));
    harness.dialogButton('accept').click();
    harness.dialogButton('accept').click(); // второй клик до завершения первого
    harness.flush(3);

    expect(remove).toHaveBeenCalledTimes(1);
    expect(harness.toastTexts().filter((text) => text === 'Удалено').length).toBe(1);
    expect(harness.reportText()).toBe('Показать записи с 1 по 10 из 22');
    harness.finalize(); // таймер автозакрытия «Удалено»
  }));

  it('TS-212: гонка быстрых смен фильтра при задержке мока — итог соответствует последнему выбору', fakeAsync(() => {
    openWorks();

    harness.openSemesterSelect();
    harness.chooseSemesterOption('1'); // запрос А стартовал, не завершён
    harness.openSemesterSelect();
    harness.chooseSemesterOption('2'); // запрос В — до завершения ответа А

    harness.flush(2); // оба ответа: поздний ответ А отбрасывается по счётчику
    expect(harness.rows().length).toBe(3);
    harness.rows().forEach((row) => expect(harness.rowCells(row)[2]).toBe('2'));
    expect(harness.reportText()).toBe('Показать записи с 1 по 3 из 3');
    expect(harness.semesterSelectLabel()).toBe('2');
  }));
});
