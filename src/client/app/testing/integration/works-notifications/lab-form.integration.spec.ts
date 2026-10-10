/**
 * Интеграционные сценарии формы лабораторной /works/new и /works/:id/edit
 * (FR-4.3, C-111/IF-103, C-110/IF-109):
 *  - TS-213: валидная форма → create, «Сохранено», редирект /works, запись в списке;
 *  - TS-214: редактирование — загрузка значений, сохранение; «Назад» — без записи;
 *  - TS-215: deep-link /works/:id/edit переживает прямую навигацию (F5);
 *  - TS-216: клиентская валидация — границы полей, баннер, запрос не уходит;
 *  - TS-217: дубликат (семестр, номер) — 409 дословно; своя пара не конфликтует;
 *  - TS-218: save-404 — запись удалена из другой вкладки во время правки (аменда 5);
 *  - TS-219: edit-load-404 — несуществующий id в маршруте (аменда 5);
 *  - TS-220: кнопка-цепочка — тултип, поле URL, добавление/изменение/очистка;
 *  - TS-221: серверный 400 — только баннер body.message (ADR-112).
 *
 * Уровень — интеграционный: LabFormPage → LabsService → HttpTestingController →
 * состояние WorksBackendStub (без мок-слоя, FR-026); уведомления — через
 * NotificationService и хосты IF-109.
 */
import { By } from '@angular/platform-browser';
import { fakeAsync } from '@angular/core/testing';
import { Tooltip } from 'primeng/tooltip';

import {
  WorksIntegrationHarness,
} from './works-integration-harness';

const LINK_TOOLTIP = 'Нажмите на эту кнопку чтобы добавить или изменить ссылку';
const NOT_FOUND_MESSAGE = 'Лабораторная не найдена';
const DUPLICATE_MESSAGE = 'Лабораторная с таким номером уже есть в семестре';
const INVALID_FORM_MESSAGE = 'Данные заполнены неверно';

describe('Интеграция: форма лабораторной (FR-4.3)', () => {
  let harness: WorksIntegrationHarness;

  beforeEach(() => {
    harness = WorksIntegrationHarness.setup();
  });

  afterEach(() => {
    harness.detach();
    WorksIntegrationHarness.restoreSeed();
    localStorage.clear();
  });

  /** Монтирование, сессия teacher, открытие формы создания. */
  function openNewForm(): void {
    harness.attach();
    harness.loginTeacher();
    harness.navigate('/works/new');
  }

  /** Заполнение полей формы минимально валидными значениями. */
  function fillValidForm(number: string, semester: string, content: string): void {
    harness.setInput('#lab-number', number);
    harness.openSemesterSelect();
    harness.chooseSemesterOption(semester);
    harness.setInput('#lab-content', content);
  }

  it('TS-213: создание работы — create вызван, «Сохранено», редирект /works, запись в списке', fakeAsync(() => {
    openNewForm();
    const create = spyOn(harness.labs, 'create').and.callThrough();

    fillValidForm('21', '1', 'Новая работа');
    harness.native.querySelector<HTMLInputElement>('.lab-form__field--checkbox input')!.click();
    harness.uiSettle();
    harness.buttonByLabel('Сохранить').click();

    harness.flush(); // create
    harness.flush(); // редирект /works + загрузка списка

    expect(create).toHaveBeenCalledTimes(1);
    expect(create).toHaveBeenCalledWith({
      number: 21,
      semester: 1,
      content: 'Новая работа',
      assignmentUrl: null,
      defenseRequired: true,
    });
    expect(harness.toastTexts()).toContain('Сохранено');
    expect(harness.router.url).toBe('/works');

    // Запись №21 в списке (страница 3 из 24 записей) с отмеченным
    // disabled-чекбоксом защиты.
    harness.gotoPage(3);
    harness.flush();
    const checkbox = harness.rowByNumber(21).querySelector('p-checkbox')!;
    expect(checkbox.getAttribute('data-p-checked')).toBe('true');
    expect(checkbox.getAttribute('data-p-disabled')).toBe('true');
    harness.finalize(); // таймер автозакрытия «Сохранено»
  }));

  it('TS-214: редактирование — загрузка значений, сохранение; «Назад» — без записи', fakeAsync(() => {
    harness.attach();
    harness.loginTeacher();
    const lab1 = harness.labId(1, 1);
    const update = spyOn(harness.labs, 'update').and.callThrough();

    // Форма заполнена значениями записи.
    harness.navigate(`/works/${lab1}/edit`);
    harness.flush(); // getById
    expect(harness.inputValue('#lab-number')).toBe('1');
    expect(harness.inputValue('#lab-content')).toBe('Содержание лабораторной работы №1');

    harness.setInput('#lab-content', 'Обновлённое содержание №1');
    harness.buttonByLabel('Сохранить').click();
    harness.flush(); // update
    harness.flush(); // редирект + загрузка /works

    expect(update).toHaveBeenCalledTimes(1);
    expect(update).toHaveBeenCalledWith(lab1, {
      number: 1,
      semester: 1,
      content: 'Обновлённое содержание №1',
      assignmentUrl: null,
      defenseRequired: false,
    });
    expect(harness.toastTexts()).toContain('Сохранено');
    expect(harness.router.url).toBe('/works');
    expect(
      harness.rows().some((row) => harness.rowCells(row)[1] === 'Обновлённое содержание №1'),
    ).toBeTrue();

    // Отдельно: изменить поле → «Назад» — update не вызван, данные исходные.
    harness.navigate(`/works/${lab1}/edit`);
    harness.flush();
    harness.setInput('#lab-content', 'Черновик, который не должен сохраниться');
    harness.buttonByLabel('Назад').click();
    harness.flush(); // загрузка /works
    expect(harness.router.url).toBe('/works');

    harness.navigate(`/works/${lab1}/edit`);
    harness.flush();
    expect(update).toHaveBeenCalledTimes(1); // второго вызова не было
    expect(harness.inputValue('#lab-content')).toBe('Обновлённое содержание №1');
    harness.finalize(); // таймер автозакрытия «Сохранено»
  }));

  it('TS-215: deep-link /works/:id/edit переживает прямую навигацию (F5)', fakeAsync(() => {
    harness.attach();
    harness.loginTeacher();
    const lab5 = harness.labId(1, 5);

    harness.navigate(`/works/${lab5}/edit`);
    harness.flush(); // getById

    // Редиректа нет, форма заполнена значениями записи (§2 SPA fallback + ADR-104).
    expect(harness.router.url).toBe(`/works/${lab5}/edit`);
    expect(harness.native.querySelector('form#lab-form')).not.toBeNull();
    expect(harness.inputValue('#lab-number')).toBe('5');
    expect(harness.inputValue('#lab-content')).toBe('Содержание лабораторной работы №5');
    expect(harness.inputValue('#lab-url')).toBe('https://git.example.com/assignments/1/5');
  }));

  it('TS-216: клиентская валидация — границы полей, баннер, запрос не уходит; позитивные границы', fakeAsync(() => {
    openNewForm();
    const create = spyOn(harness.labs, 'create').and.callThrough();
    const update = spyOn(harness.labs, 'update').and.callThrough();

    // Номер 0.
    harness.setInput('#lab-number', '0');
    harness.openSemesterSelect();
    harness.chooseSemesterOption('1');
    harness.setInput('#lab-content', 'Содержание');
    harness.buttonByLabel('Сохранить').click();
    harness.uiSettle();
    expect(harness.toastTexts()).toContain(INVALID_FORM_MESSAGE);
    expect(harness.fieldErrorText('lab-number')).toBe('Номер должен быть положительным числом');
    expect(create).not.toHaveBeenCalled();

    // Номер −1.
    harness.setInput('#lab-number', '-1');
    harness.buttonByLabel('Сохранить').click();
    harness.uiSettle();
    expect(harness.fieldErrorText('lab-number')).toBe('Номер должен быть положительным числом');
    expect(create).not.toHaveBeenCalled();

    // Номер из одних пробелов.
    harness.setInput('#lab-number', '   ');
    harness.buttonByLabel('Сохранить').click();
    harness.uiSettle();
    expect(harness.fieldErrorText('lab-number')).toBe('Заполните поле');
    expect(create).not.toHaveBeenCalled();

    // Содержание пустое (при валидном номере).
    harness.setInput('#lab-number', '3');
    harness.setInput('#lab-content', '   ');
    harness.buttonByLabel('Сохранить').click();
    harness.uiSettle();
    expect(harness.fieldErrorText('lab-content')).toBe('Заполните поле');
    expect(create).not.toHaveBeenCalled();

    // Содержание 501 символ.
    harness.setInput('#lab-content', 'х'.repeat(501));
    harness.buttonByLabel('Сохранить').click();
    harness.uiSettle();
    expect(harness.fieldErrorText('lab-content')).toBe('Содержание — от 1 до 500 символов');
    expect(create).not.toHaveBeenCalled();

    // Ссылка с недопустимой схемой.
    harness.chainButton().click();
    harness.uiSettle();
    harness.setInput('#lab-url', 'ftp://example.com/file');
    harness.buttonByLabel('Сохранить').click();
    harness.uiSettle();
    expect(harness.toastTexts()).toContain(INVALID_FORM_MESSAGE);
    expect(harness.fieldErrorText('lab-url')).toBe(
      'Ссылка должна начинаться с http:// или https://',
    );
    expect(create).not.toHaveBeenCalled();
    expect(update).not.toHaveBeenCalled();

    // Позитивные границы: содержание ровно 500, семестр 10 (MAX_SEMESTER),
    // пустое поле ссылки → null (не ошибка).
    harness.setInput('#lab-url', '');
    harness.openSemesterSelect();
    harness.chooseSemesterOption('10');
    harness.setInput('#lab-content', 'х'.repeat(500));
    harness.buttonByLabel('Сохранить').click();
    harness.flush(); // create

    expect(create).toHaveBeenCalledTimes(1);
    expect(create.calls.mostRecent().args[0]).toEqual({
      number: 3,
      semester: 10,
      content: 'х'.repeat(500),
      assignmentUrl: null,
      defenseRequired: false,
    });
    expect(harness.toastTexts()).toContain('Сохранено');
    harness.finalize(); // таймер автозакрытия «Сохранено» + незавершённая волна /works
  }));

  it('TS-217: дубликат (семестр, номер) — 409 дословно, редиректа нет; своя пара не конфликтует', fakeAsync(() => {
    openNewForm();
    const create = spyOn(harness.labs, 'create').and.callThrough();

    fillValidForm('1', '1', 'Дубль пары');
    harness.buttonByLabel('Сохранить').click();
    harness.flush(); // create → 409

    expect(harness.toastTexts()).toContain(DUPLICATE_MESSAGE);
    expect(harness.router.url).toBe('/works/new'); // редиректа нет
    expect(
      harness.readDb().labs.filter((lab) => lab.semester === 1 && lab.number === 1).length,
    ).toBe(1); // дубль в состоянии сервера не появился

    // Редактирование записи №1 с неизменённой парой — конфликта нет.
    const lab1 = harness.labId(1, 1);
    harness.navigate(`/works/${lab1}/edit`);
    harness.flush(); // getById
    const update = spyOn(harness.labs, 'update').and.callThrough();
    harness.buttonByLabel('Сохранить').click();
    harness.flush(); // update
    harness.flush(); // редирект + загрузка /works

    expect(update).toHaveBeenCalledTimes(1);
    expect(harness.toastTexts()).toContain('Сохранено');
    harness.finalize(); // таймер автозакрытия «Сохранено»
  }));

  it('TS-218: save-404 — запись удалена из другой вкладки во время редактирования (аменда 5)', fakeAsync(() => {
    harness.breakpoints.simulate(true); // mobile: якорь 'header' наблюдаем inline-баннером
    harness.attach();
    harness.loginTeacher();
    const lab1 = harness.labId(1, 1);
    const update = spyOn(harness.labs, 'update').and.callThrough();

    harness.navigate(`/works/${lab1}/edit`);
    harness.flush(); // getById

    // Запись удалена извне — прямой мутацией серверного состояния
    // (эмуляция второй вкладки).
    harness.removeLabDirect(lab1);
    harness.flush();

    harness.buttonByLabel('Сохранить').click();
    harness.flush(); // update → 404

    expect(update).toHaveBeenCalledTimes(1);
    // Якорь 'header' (НЕ под формой) + редирект /works.
    expect(harness.headerBannerTexts()).toContain(NOT_FOUND_MESSAGE);
    expect(harness.formAnchorBannerTexts()).not.toContain(NOT_FOUND_MESSAGE);
    expect(harness.router.url).toBe('/works');
    expect(harness.native.querySelector('form#lab-form')).toBeNull();

    // Безопасный повтор прерванной операции: создание новой работы работает.
    harness.navigate('/works/new');
    fillValidForm('30', '1', 'Повтор после прерывания');
    harness.buttonByLabel('Сохранить').click();
    harness.flush(); // create
    harness.flush(); // редирект + загрузка /works

    expect(harness.headerBannerTexts()).toContain('Сохранено'); // успех — под шапкой (аменда 2)
    expect(harness.readDb().labs.some((lab) => lab.number === 30)).toBeTrue();
    harness.finalize(); // таймер автозакрытия мобильного баннера
  }));

  it('TS-219: edit-load-404 — несуществующий id в маршруте: баннер, редирект, форма не показана', fakeAsync(() => {
    harness.breakpoints.simulate(true); // mobile: якорь 'header' наблюдаем inline-баннером
    harness.attach();
    harness.loginTeacher();

    harness.navigate('/works/00000000-0000-0000-0000-000000000000/edit');
    harness.flush(); // getById → 404
    harness.flush(); // редирект + загрузка /works

    expect(harness.headerBannerTexts()).toContain(NOT_FOUND_MESSAGE);
    expect(harness.router.url).toBe('/works');
    expect(harness.native.querySelector('form#lab-form')).toBeNull();
    expect(harness.native.querySelector('h1')!.textContent).toContain('Лабораторные работы');
    harness.finalize(); // таймер автозакрытия мобильного баннера
  }));

  it('TS-220: кнопка-цепочка — тултип, поле URL, добавление/изменение/очистка ссылки', fakeAsync(() => {
    openNewForm();

    // Тултип дословно, поле URL скрыто до клика.
    const chain = harness.debugElement.query(
      By.css('button[aria-label="Ссылка на задание"]'),
    )!;
    const tooltip = chain.injector.get(Tooltip, null);
    expect(tooltip).withContext('директива Tooltip на кнопке-цепочке').not.toBeNull();
    expect(tooltip!.getOption('tooltipLabel')).toBe(LINK_TOOLTIP);
    expect(harness.native.querySelector('#lab-url')).toBeNull();

    harness.chainButton().click();
    harness.uiSettle();
    expect(harness.native.querySelector('#lab-url')).not.toBeNull();

    // Сохранение с заполненной ссылкой.
    fillValidForm('21', '1', 'Работа со ссылкой');
    harness.setInput('#lab-url', 'https://example.com/task21');
    harness.buttonByLabel('Сохранить').click();
    harness.flush(); // create
    harness.flush(); // редирект + загрузка /works
    expect(harness.router.url).toBe('/works');

    // Сохранённая ссылка видна в списке (запись №21 — страница 3).
    harness.gotoPage(3);
    harness.flush();
    const link = harness
      .rowByNumber(21)!
      .querySelector<HTMLAnchorElement>('a.works-page__assignment')!;
    expect(link.getAttribute('href')).toBe('https://example.com/task21');
    expect(link.textContent!.trim()).toBe('example.com/task21');
    const lab21 = harness.labId(1, 21);

    // Повторное редактирование: поле содержит текущее значение.
    harness.navigate(`/works/${lab21}/edit`);
    harness.flush();
    expect(harness.inputValue('#lab-url')).toBe('https://example.com/task21');

    // Очищенное поле сохраняется как null, в списке — прочерк «—».
    harness.setInput('#lab-url', '');
    harness.buttonByLabel('Сохранить').click();
    harness.flush(); // update
    harness.flush(); // редирект + загрузка /works
    expect(harness.readDb().labs.find((lab) => lab.id === lab21)!.assignmentUrl).toBeNull();

    harness.gotoPage(3);
    harness.flush();
    expect(harness.rowByNumber(21).querySelector('.works-page__dash')!.textContent!.trim()).toBe(
      '—',
    );
    harness.finalize(); // таймер автозакрытия «Сохранено»
  }));

  it('TS-221: серверный 400 (оборонительная ветка) — только баннер body.message, полевые ошибки скрыты', fakeAsync(() => {
    openNewForm();
    // Серверный отказ следующего create программируется бэкендом зоны —
    // отказ проходит настоящий HTTP-конвейер (интерцептор → ApiError).
    harness.backend.failNextCreate({
      status: 400,
      body: {
        message: INVALID_FORM_MESSAGE,
        errors: { number: ['Номер должен быть положительным числом'] },
      },
    });
    const loggers = [
      spyOn(console, 'warn'),
      spyOn(console, 'info'),
      spyOn(console, 'log'),
      spyOn(console, 'error'),
    ];

    fillValidForm('2', '1', 'Содержание');
    harness.buttonByLabel('Сохранить').click();
    harness.flush(); // create → 400

    // Показан ТОЛЬКО баннер с дословным body.message (ADR-112).
    expect(harness.lastToastText()).toBe(INVALID_FORM_MESSAGE);
    expect(harness.native.textContent).not.toContain('Номер должен быть положительным числом');
    // Ошибки из body.errors не логируются: сканируем JSON всех аргументов
    // всех вызовов console.* — ловит и строку, и переданный объект ошибки.
    const logged = loggers
      .flatMap((logger) => logger.calls.allArgs())
      .map((args) => JSON.stringify(args) ?? '');
    expect(
      logged.some((entry) => entry.includes('Номер должен быть положительным числом')),
    )
      .withContext('полевые ошибки из body.errors не должны попадать в console')
      .toBeFalse();
    // Редиректа нет.
    expect(harness.router.url).toBe('/works/new');
    harness.finalize(); // таймер автозакрытия тоста ошибки
  }));
});
