/**
 * Unit-тесты страницы «Сдача работ» (T-114, SCR-009, FR-4.4):
 *  - дефолты селекторов (группа — первая по порядку мока, семестр —
 *    наименьший) и параметры первого getGrid (AC defaults);
 *  - двухуровневая шапка на N работ с парой «Сдача/Защита» у каждой,
 *    включая defenseRequired=false;
 *  - выбор/очистка даты шлют submissions.update с обеими датами пары
 *    (null = сброс), ячейка показывает дд.мм.гггг (AC set-date-upsert,
 *    clear-date);
 *  - блокировка сохраняемой ячейки на время сохранения (RSK-006);
 *  - ошибка update → notifyError якорем 'header' + перезагрузка грида;
 *  - пагинация 5 и подпись «Показать записи с X по Y из Z»;
 *  - вырожденные состояния: пустая группа, нет семестров, нет групп.
 *
 * Сервисы core заменены шпионами (unit-уровень, детерминированно,
 * без мок-задержки 500 мс); NotificationService — настоящий, с
 * MockBreakpointObserver для управления режимом показа.
 */
import { BreakpointObserver } from '@angular/cdk/layout';
import { By } from '@angular/platform-browser';
import { TestBed, ComponentFixture } from '@angular/core/testing';

import { GroupsService } from '../../../core/services/groups.service';
import { LabsService } from '../../../core/services/labs.service';
import {
  SubmissionsGridResult,
  SubmissionsService,
  SubmissionUpdateParams,
} from '../../../core/services/submissions.service';
import { MockBreakpointObserver } from '../../../../testing/mock-breakpoint-observer';
import { NotificationService } from '../../../shared/notifications/notification-service';
import { GroupDto, Submission } from '../../../shared/models';
import { SubmissionDateCell } from './submission-date-cell';
import { SubmissionsPage } from './submissions-page';

/** Дефолтный сид (AC defaults): группа ИК-221, 25 студентов, работы 1..20. */
const GROUP_221 = 'group-221';
const GROUP_222 = 'group-222';
const GROUP_223 = 'group-223';
const STUDENT_1 = 'student-1';
const LAB_1 = 'lab-1';
const LAB_6 = 'lab-6';
const LABS_COUNT = 20;

/** Группы селектора — порядок задаёт мок (name↑ без учёта регистра). */
const GROUPS: GroupDto[] = [
  { id: GROUP_221, name: 'ИК-221', studentCount: 25 },
  { id: GROUP_222, name: 'ИК-222', studentCount: 10 },
  { id: GROUP_223, name: 'ИК-223', studentCount: 0 },
];

/** Страница студентов ИК-221: 5 строк, порядок fullName↑ задаёт мок. */
function makeStudents(prefix = 'Иванов Иван Иванович '): Array<{ id: string; fullName: string }> {
  return Array.from({ length: 5 }, (_, index) => ({
    id: `student-${index + 1}`,
    fullName: `${prefix}${String(index + 1).padStart(2, '0')}`,
  }));
}

/** Работы семестра 1: номера 1..20; у лабы 3 защита не требуется. */
function makeLabs(): Array<{ id: string; number: number; defenseRequired: boolean }> {
  return Array.from({ length: LABS_COUNT }, (_, index) => ({
    id: `lab-${index + 1}`,
    number: index + 1,
    defenseRequired: index !== 2, // «Лаб 3» — defenseRequired=false
  }));
}

/** Ответ getGrid по умолчанию: первая страница, total 25 (AC defaults). */
function makeGrid(overrides: Partial<SubmissionsGridResult> = {}): SubmissionsGridResult {
  return {
    students: makeStudents(),
    labs: makeLabs(),
    submissions: [
      { studentId: STUDENT_1, labId: LAB_1, submitDate: '2026-09-01', defenseDate: '2026-09-11' },
      { studentId: STUDENT_1, labId: LAB_6, submitDate: null, defenseDate: '2026-09-25' },
    ],
    total: 25,
    page: 1,
    ...overrides,
  };
}

function makeSubmission(params: SubmissionUpdateParams): Submission {
  return {
    id: 'submission-1',
    studentId: params.studentId,
    labId: params.labId,
    submitDate: params.submitDate,
    defenseDate: params.defenseDate,
    updatedAt: '2026-09-15T10:00:00.000Z',
    updatedBy: 'teacher-1',
  };
}

describe('SubmissionsPage (ведомость преподавателя, SCR-009)', () => {
  let groupsServiceSpy: jasmine.SpyObj<GroupsService>;
  let labsServiceSpy: jasmine.SpyObj<LabsService>;
  let submissionsServiceSpy: jasmine.SpyObj<SubmissionsService>;
  let breakpoints: MockBreakpointObserver;
  let notifications: NotificationService | null;
  let nextGrid: SubmissionsGridResult;

  beforeEach(() => {
    notifications = null;
    nextGrid = makeGrid();
    breakpoints = new MockBreakpointObserver();
    groupsServiceSpy = jasmine.createSpyObj<GroupsService>('GroupsService', ['getList']);
    labsServiceSpy = jasmine.createSpyObj<LabsService>('LabsService', ['getSemesters']);
    submissionsServiceSpy = jasmine.createSpyObj<SubmissionsService>('SubmissionsService', [
      'getGrid',
      'update',
    ]);
    groupsServiceSpy.getList.and.resolveTo(GROUPS);
    labsServiceSpy.getSemesters.and.resolveTo([1, 2]);
    submissionsServiceSpy.getGrid.and.callFake(() => Promise.resolve(nextGrid));
    submissionsServiceSpy.update.and.callFake((params: SubmissionUpdateParams) =>
      Promise.resolve(makeSubmission(params)),
    );

    TestBed.configureTestingModule({
      imports: [SubmissionsPage],
      providers: [
        { provide: BreakpointObserver, useValue: breakpoints },
        { provide: GroupsService, useValue: groupsServiceSpy },
        { provide: LabsService, useValue: labsServiceSpy },
        { provide: SubmissionsService, useValue: submissionsServiceSpy },
      ],
    });
  });

  afterEach(() => {
    // Гасим возможный незавершённый таймер автозакрытия уведомления.
    notifications?.dismissMobile();
    notifications = null;
  });

  /**
   * Доводит асинхронные цепочки (селекторы → getGrid → сигналы → CD) до
   * конца: стабы отвечают промисами-микротасками, поэтому несколько
   * опустошений очереди микрозадач + detectChanges дают детерминизм
   * (паттерн спецификаций соседних страниц). Второй detectChanges нужен
   * для перерисовки внутренних состояний p-select/p-datepicker
   * (ngModel/value приходят с запаздыванием на один проход CD).
   */
  async function settle(fixture: ComponentFixture<SubmissionsPage>): Promise<void> {
    fixture.detectChanges();
    for (let tick = 0; tick < 8; tick++) {
      await Promise.resolve();
    }
    fixture.detectChanges();
    // Внутренние состояния p-select/p-datepicker (label, disabled, input
    // value) доезжают с запаздыванием на один-два прохода CD после записи
    // ngModel — даём несколько циклов «микрозадача + CD» с запасом.
    for (let round = 0; round < 3; round++) {
      await Promise.resolve();
      fixture.detectChanges();
    }
  }

  /** Создаёт страницу и дожидается завершения цепочек промисов init. */
  async function createPage(): Promise<ComponentFixture<SubmissionsPage>> {
    const fixture = TestBed.createComponent(SubmissionsPage);
    notifications = TestBed.inject(NotificationService);
    await settle(fixture); // ngOnInit → initSelectors → getGrid → рендер
    return fixture;
  }

  /** Первый p-select (группа) / второй (семестр). */
  function selects(root: HTMLElement): HTMLElement[] {
    return Array.from(root.querySelectorAll('p-select'));
  }

  function selectLabel(root: HTMLElement, index: number): string {
    return selects(root)[index]?.querySelector('.p-select-label')?.textContent?.trim() ?? '';
  }

  /** Строка tbody по номеру (1-based). */
  function bodyRow(root: HTMLElement, row: number): HTMLTableRowElement {
    return root.querySelector(`tbody tr:nth-child(${row})`) as HTMLTableRowElement;
  }

  /**
   * input ячейки даты: td 1 — студент, у лабы k сдача — td 2k, защита — 2k+1.
   */
  function cellInput(root: HTMLElement, row: number, labNumber: number, field: 'submit' | 'defense'): HTMLInputElement {
    const cell = field === 'submit' ? 2 * labNumber : 2 * labNumber + 1;
    return bodyRow(root, row).querySelector(
      `td:nth-child(${cell}) input`,
    ) as HTMLInputElement;
  }

  function rangeCaption(root: HTMLElement): string {
    return (
      root.querySelector('[data-test="range-caption"]')?.textContent?.trim() ?? ''
    );
  }

  /**
   * Пользовательский ввод даты в ячейку (CR-003): keydown будит парсер
   * пикера (isKeydown), input несёт текст — события идут в DOM реальной
   * ячейки, а не через вызовы методов страницы.
   */
  function typeCellDate(
    root: HTMLElement,
    row: number,
    labNumber: number,
    field: 'submit' | 'defense',
    text: string,
  ): void {
    const input = cellInput(root, row, labNumber, field);
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    input.value = text;
    input.dispatchEvent(new Event('input'));
  }

  /** Пользовательский blur ячейки (коммит очистки / возврат к источнику). */
  function blurCellDate(
    root: HTMLElement,
    row: number,
    labNumber: number,
    field: 'submit' | 'defense',
  ): void {
    cellInput(root, row, labNumber, field).dispatchEvent(new FocusEvent('blur'));
  }

  it('defaults: группа — первая по порядку мока (ИК-221), семестр — наименьший (1), getGrid страницы 1; 5 студентов, работы 1..20', async () => {
    const fixture = await createPage();
    const root: HTMLElement = fixture.nativeElement;

    expect(submissionsServiceSpy.getGrid).toHaveBeenCalledTimes(1);
    expect(submissionsServiceSpy.getGrid).toHaveBeenCalledWith({
      groupId: GROUP_221,
      semester: 1,
      page: 1,
    });

    expect(selectLabel(root, 0)).toBe('ИК-221');
    expect(selectLabel(root, 1)).toBe('1');

    const rows = root.querySelectorAll('tbody tr');
    expect(rows.length).withContext('страница студентов — 5 строк').toBe(5);

    const headerLabs = Array.from(root.querySelectorAll('thead tr:first-child th')).map(
      (th) => th.textContent?.trim(),
    );
    expect(headerLabs[0]).toBe('Студент');
    expect(headerLabs.length).toBe(1 + LABS_COUNT);
    expect(headerLabs[1]).toBe('Лаб 1');
    expect(headerLabs[LABS_COUNT]).toBe('Лаб 20');

    expect(bodyRow(root, 1).querySelector('td')?.textContent?.trim()).toBe(
      'Иванов Иван Иванович 01',
    );
  });

  it('двухуровневая шапка: «Лаб N» colspan=2 → пара «Сдача/Защита» у КАЖДОЙ работы, включая defenseRequired=false', async () => {
    const fixture = await createPage();
    const root: HTMLElement = fixture.nativeElement;

    const headerRows = root.querySelectorAll('thead tr');
    expect(headerRows.length).withContext('двухуровневая шапка').toBe(2);

    const groupHeaders = Array.from(
      root.querySelectorAll('thead tr:first-child th[colspan="2"]'),
    );
    expect(groupHeaders.length).toBe(LABS_COUNT);
    expect(groupHeaders[2]?.textContent?.trim())
      .withContext('лаба 3 — defenseRequired=false, колонка всё равно есть')
      .toBe('Лаб 3');
    expect(groupHeaders[2]?.getAttribute('colspan')).toBe('2');

    const subHeaders = Array.from(
      (headerRows[1] as HTMLTableRowElement).querySelectorAll('th'),
    ).map((th) => th.textContent?.trim());
    expect(subHeaders.length).toBe(LABS_COUNT * 2);
    expect(subHeaders.filter((text) => text === 'Сдача').length).toBe(LABS_COUNT);
    expect(subHeaders.filter((text) => text === 'Защита').length).toBe(LABS_COUNT);
    expect(subHeaders.slice(4, 6)).toEqual(['Сдача', 'Защита']); // пара лабы 3
  });

  it('формат даты дд.мм.гггг; пустая дата — пустая ячейка без прочерка', async () => {
    const fixture = await createPage();
    const root: HTMLElement = fixture.nativeElement;

    expect(cellInput(root, 1, 1, 'submit').value).toBe('01.09.2026');
    expect(cellInput(root, 1, 1, 'defense').value).toBe('11.09.2026');
    // «Сдача» лабы 6 первого студента пуста (сид) — пустая ячейка, без «—».
    expect(cellInput(root, 1, 6, 'submit').value).toBe('');
    expect(bodyRow(root, 1).textContent).not.toContain('—');
  });

  it('VBUG-003: 200 ячеек — изолированные OnPush-компоненты с примитивным значением, Date тождественно стабилен между проходами CD', async () => {
    const fixture = await createPage();
    const page = fixture.componentInstance;

    const cells = fixture.debugElement.queryAll(By.directive(SubmissionDateCell));
    expect(cells.length)
      .withContext('5 студентов × 20 работ × 2 колонки')
      .toBe(200);

    // Страница передаёт в ячейку только примитив-строку (без создания объектов).
    expect(page.recordDate(STUDENT_1, LAB_1, 'submitDate')).toBe('2026-09-01');

    // Кэш Date внутри ячейки не пересоздаётся на проходах CD без смены value —
    // [ngModel] p-datepicker не получает новый объект, writeValue-шторма нет.
    // (CR-004: protected-член читается без прод-хука для тестов.)
    const cell = cells[0].componentInstance as SubmissionDateCell;
    const dateModel = (
      cell as unknown as { dateModel: () => Date | null }
    ).dateModel.bind(cell);
    const before = dateModel();
    fixture.detectChanges();
    fixture.detectChanges();
    expect(dateModel()).toBe(before);
  });

  it('CR-001: промежуточные неудачные парсинги не шлют update и не затирают ввод; финальный валидный ввод коммитится один раз', async () => {
    const fixture = await createPage();
    const root: HTMLElement = fixture.nativeElement;

    typeCellDate(root, 1, 1, 'submit', '1');
    await settle(fixture);
    expect(submissionsServiceSpy.update)
      .withContext('промежуточный парсинг не порождает серверный вызов')
      .not.toHaveBeenCalled();
    expect(cellInput(root, 1, 1, 'submit').value)
      .withContext('недопечатанный текст не затирается во время набора')
      .toBe('1');

    typeCellDate(root, 1, 1, 'submit', '15.');
    await settle(fixture);
    expect(submissionsServiceSpy.update).not.toHaveBeenCalled();

    typeCellDate(root, 1, 1, 'submit', '15.09.2026');
    await settle(fixture);
    expect(submissionsServiceSpy.update).toHaveBeenCalledTimes(1);
    expect(submissionsServiceSpy.update).toHaveBeenCalledWith({
      studentId: STUDENT_1,
      labId: LAB_1,
      submitDate: '2026-09-15',
      defenseDate: '2026-09-11',
    });
    expect(cellInput(root, 1, 1, 'submit').value).toBe('15.09.2026');
  });

  it('CR-001/CR-002: после ошибки сохранения ячейки возвращаются к серверной истине — и в непустой, и в пустой', async () => {
    const fixture = await createPage();
    const page = fixture.componentInstance;
    const root: HTMLElement = fixture.nativeElement;

    submissionsServiceSpy.update.and.returnValue(
      Promise.reject({ status: 400, body: { message: 'Данные заполнены неверно' } }),
    );

    // Непустая ячейка: '01.09.2026' → '15.09.2026' → 400 → возврат '01.09.2026'.
    typeCellDate(root, 1, 1, 'submit', '15.09.2026');
    await settle(fixture);
    expect(cellInput(root, 1, 1, 'submit').value)
      .withContext('серверная истина восстановлена в непустой ячейке')
      .toBe('01.09.2026');

    // Пустая ячейка (лаба 6): null → '15.09.2026' → 400 → фантом стёрт:
    // ссылочно равный null в [ngModel] не пишется в контрол, сброс делает
    // effect по ревизии (writeValue(null) через viewChild).
    typeCellDate(root, 1, 6, 'submit', '15.09.2026');
    await settle(fixture);
    expect(page.cellRevision(STUDENT_1, LAB_1, 'submitDate')).toBe(1);
    expect(page.cellRevision(STUDENT_1, LAB_6, 'submitDate')).toBe(1);
    expect(cellInput(root, 1, 6, 'submit').value)
      .withContext('фантомная дата в пустой ячейке стёрта (CR-002)')
      .toBe('');
    expect(cellInput(root, 1, 6, 'defense').value).toBe('25.09.2026');
  });

  it('set-date-upsert: ввод 15.09.2026 в пустую «Сдачу» лабы 6 шлёт update с ОБЕИМИ датами, ячейка показывает 15.09.2026', async () => {
    const fixture = await createPage();
    const root: HTMLElement = fixture.nativeElement;

    typeCellDate(root, 1, 6, 'submit', '15.09.2026');
    await settle(fixture);

    expect(submissionsServiceSpy.update).toHaveBeenCalledTimes(1);
    expect(submissionsServiceSpy.update).toHaveBeenCalledWith({
      studentId: STUDENT_1,
      labId: LAB_6,
      submitDate: '2026-09-15',
      defenseDate: '2026-09-25', // вторая дата пары — прежнее значение
    });
    expect(cellInput(root, 1, 6, 'submit').value).toBe('15.09.2026');
  });

  it('clear-date: очистка «Защиты» пустым blur шлёт defenseDate:null и прежний submitDate', async () => {
    const fixture = await createPage();
    const root: HTMLElement = fixture.nativeElement;

    cellInput(root, 1, 1, 'defense').value = '';
    blurCellDate(root, 1, 1, 'defense');
    await settle(fixture);

    expect(submissionsServiceSpy.update).toHaveBeenCalledTimes(1);
    expect(submissionsServiceSpy.update).toHaveBeenCalledWith({
      studentId: STUDENT_1,
      labId: LAB_1,
      submitDate: '2026-09-01',
      defenseDate: null,
    });
    expect(cellInput(root, 1, 1, 'defense').value)
      .withContext('сброшенная дата — пустая ячейка')
      .toBe('');
    expect(cellInput(root, 1, 1, 'submit').value)
      .withContext('сдача не тронута')
      .toBe('01.09.2026');
  });

  it('clear-date: кнопка × коммитит сброс (пользовательский путь)', async () => {
    const fixture = await createPage();
    const root: HTMLElement = fixture.nativeElement;

    const icon = bodyRow(root, 1).querySelector(
      'td:nth-child(3) .p-datepicker-clear-icon',
    ) as HTMLElement | null;
    expect(icon).withContext('кнопка × видима у заполненной ячейки').not.toBeNull();
    icon!.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    await settle(fixture);

    expect(submissionsServiceSpy.update).toHaveBeenCalledWith({
      studentId: STUDENT_1,
      labId: LAB_1,
      submitDate: '2026-09-01',
      defenseDate: null,
    });
    expect(cellInput(root, 1, 1, 'defense').value).toBe('');
  });

  it('блокировка ячейки: на время сохранения disabled только у своей ячейки, после — разблокирована', async () => {
    const fixture = await createPage();
    const page = fixture.componentInstance;
    const root: HTMLElement = fixture.nativeElement;

    let releaseSave!: (submission: Submission) => void;
    submissionsServiceSpy.update.and.returnValue(
      new Promise<Submission>((resolve) => {
        releaseSave = resolve;
      }),
    );

    typeCellDate(root, 1, 6, 'submit', '15.09.2026');
    await settle(fixture); // [disabled] доходит до DOM p-datepicker за несколько проходов CD

    expect(page.isCellSaving(STUDENT_1, LAB_6, 'submitDate')).toBeTrue();
    // Заблокированный p-datepicker отключает свой внутренний input.
    const savingInput = bodyRow(root, 1).querySelector(
      'td:nth-child(12) input',
    ) as HTMLInputElement;
    expect(savingInput.disabled)
      .withContext('сохраняемая ячейка заблокирована')
      .toBeTrue();
    const otherInput = bodyRow(root, 1).querySelector(
      'td:nth-child(2) input',
    ) as HTMLInputElement;
    expect(otherInput.disabled)
      .withContext('чужие ячейки доступны (RSK-006)')
      .toBeFalse();

    releaseSave(makeSubmission({
      studentId: STUDENT_1,
      labId: LAB_6,
      submitDate: '2026-09-15',
      defenseDate: '2026-09-25',
    }));
    await settle(fixture);

    expect(page.isCellSaving(STUDENT_1, LAB_6, 'submitDate')).toBeFalse();
    expect(savingInput.disabled).withContext('ячейка разблокирована').toBeFalse();
    expect(savingInput.value).withContext('коммит применён').toBe('15.09.2026');
  });

  it('повторное изменение сохраняемой ячейки игнорируется (guard)', async () => {
    const fixture = await createPage();
    const root: HTMLElement = fixture.nativeElement;

    let releaseSave!: (submission: Submission) => void;
    submissionsServiceSpy.update.and.returnValue(
      new Promise<Submission>((resolve) => {
        releaseSave = resolve;
      }),
    );

    typeCellDate(root, 1, 6, 'submit', '15.09.2026');
    typeCellDate(root, 1, 6, 'submit', '20.09.2026'); // во время сохранения

    expect(submissionsServiceSpy.update).toHaveBeenCalledTimes(1);

    releaseSave(makeSubmission({
      studentId: STUDENT_1,
      labId: LAB_6,
      submitDate: '2026-09-15',
      defenseDate: '2026-09-25',
    }));
    await settle(fixture);
  });

  it('ошибка update: notifyError дословно якорем "header" + перезагрузка грида, ячейка разблокирована', async () => {
    const fixture = await createPage();
    const page = fixture.componentInstance;
    const root: HTMLElement = fixture.nativeElement;
    const callsBefore = submissionsServiceSpy.getGrid.calls.count();

    submissionsServiceSpy.update.and.returnValue(
      Promise.reject({ status: 400, body: { message: 'Данные заполнены неверно' } }),
    );
    typeCellDate(root, 1, 6, 'submit', '15.09.2026');
    await settle(fixture);

    expect(notifications?.desktopMessage()).withContext('десктоп: Toast справа вверху').toEqual({
      severity: 'error',
      text: 'Данные заполнены неверно',
    });
    expect(submissionsServiceSpy.getGrid.calls.count())
      .withContext('сервер — истина: грид перезагружен')
      .toBe(callsBefore + 1);
    expect(submissionsServiceSpy.getGrid.calls.mostRecent().args[0]).toEqual({
      groupId: GROUP_221,
      semester: 1,
      page: 1,
    });
    expect(page.isCellSaving(STUDENT_1, LAB_6, 'submitDate')).toBeFalse();
  });

  it('мобильная ширина: ошибка уходит под шапку (якорь header, formId=null)', async () => {
    breakpoints.simulate(true); // <768px — до создания страницы/сервиса уведомлений
    const fixture = await createPage();
    const root: HTMLElement = fixture.nativeElement;

    submissionsServiceSpy.update.and.returnValue(
      Promise.reject({ status: 404, body: { message: 'Лабораторная не найдена' } }),
    );
    typeCellDate(root, 1, 6, 'submit', '15.09.2026');
    await settle(fixture);

    expect(notifications?.mobileMessage()).toEqual({
      severity: 'error',
      text: 'Лабораторная не найдена',
      formId: null, // 'header' — экран без формы (IF-109)
    });
    expect(notifications?.desktopMessage()).toBeNull();
  });

  it('пагинация: подпись «Показать записи с 1 по 5 из 25», листание шлёт page=2 и обновляет подпись', async () => {
    const fixture = await createPage();
    const page = fixture.componentInstance;
    const root: HTMLElement = fixture.nativeElement;

    expect(rangeCaption(root)).toBe('Показать записи с 1 по 5 из 25');
    expect(submissionsServiceSpy.getGrid).toHaveBeenCalledWith({
      groupId: GROUP_221,
      semester: 1,
      page: 1,
    });

    nextGrid = makeGrid({
      page: 2,
      students: makeStudents().map((student) => ({
        id: `${student.id}-p2`,
        fullName: student.fullName,
      })),
      submissions: [],
    });
    page.onPageChange({ page: 1, first: 5, rows: 5, pageCount: 5 }); // 0-based: страница 2
    await settle(fixture);

    expect(submissionsServiceSpy.getGrid).toHaveBeenCalledWith({
      groupId: GROUP_221,
      semester: 1,
      page: 2,
    });
    expect(rangeCaption(root)).toBe('Показать записи с 6 по 10 из 25');
  });

  it('смена группы: page сбрасывается на 1, грид с новой группой', async () => {
    const fixture = await createPage();
    const page = fixture.componentInstance;

    page.onPageChange({ page: 1, first: 5, rows: 5, pageCount: 5 }); // 0-based: страница 2
    await settle(fixture);

    page.onGroupChange(GROUP_222);
    await settle(fixture);

    expect(submissionsServiceSpy.getGrid).toHaveBeenCalledWith({
      groupId: GROUP_222,
      semester: 1,
      page: 1,
    });
    expect(page.selectedGroupId()).toBe(GROUP_222);
  });

  it('смена семестра: page сбрасывается на 1, грид с новым семестром', async () => {
    const fixture = await createPage();
    const page = fixture.componentInstance;

    page.onSemesterChange(2);
    await settle(fixture);

    expect(submissionsServiceSpy.getGrid).toHaveBeenCalledWith({
      groupId: GROUP_221,
      semester: 2,
      page: 1,
    });
  });

  it('empty-group (ИК-223): строка «В группе нет студентов» и подпись «Показать записи с 1 по 0 из 0»', async () => {
    const fixture = await createPage();
    const page = fixture.componentInstance;
    const root: HTMLElement = fixture.nativeElement;

    nextGrid = makeGrid({ students: [], submissions: [], total: 0 });
    page.onGroupChange(GROUP_223);
    await settle(fixture);

    const emptyRow = root.querySelector('[data-test="empty-group-row"]');
    expect(emptyRow?.textContent?.trim()).toBe('В группе нет студентов');
    expect(emptyRow?.getAttribute('colspan'))
      .withContext('строка растягивается на все колонки: 1 + 20×2')
      .toBe(String(1 + LABS_COUNT * 2));
    expect(rangeCaption(root)).toBe('Показать записи с 1 по 0 из 0');
  });

  it('no-groups: селектор групп disabled, подпись «Нет групп», элемент семестра скрыт, таблица скрыта', async () => {
    groupsServiceSpy.getList.and.resolveTo([]);
    const fixture = await createPage();
    const root: HTMLElement = fixture.nativeElement;

    expect(selects(root)[0].classList.contains('p-disabled'))
      .withContext('селектор группы заблокирован')
      .toBeTrue();
    expect(root.querySelector('[data-test="no-groups-caption"]')?.textContent?.trim()).toBe(
      'Нет групп',
    );
    // Макет SCR-009 (ревью CR-002 T-114): в состоянии «Нет групп» в тулбаре
    // остаётся только селектор группы, элемент семестра не рендерится.
    expect(selects(root).length).withContext('селектор семестра скрыт').toBe(1);
    expect(root.textContent).not.toContain('Семестр');
    expect(root.querySelector('[data-test="no-semesters-caption"]')).toBeNull();
    expect(root.querySelector('p-table')).withContext('таблица не отображается').toBeNull();
    expect(submissionsServiceSpy.getGrid).not.toHaveBeenCalled();
  });

  it('loading: на время перезагрузки грида таблица в состоянии загрузки (ревью CR-001 T-114)', async () => {
    const fixture = await createPage();
    const page = fixture.componentInstance;
    const root: HTMLElement = fixture.nativeElement;
    expect(page.loading()).withContext('после первичной загрузки — false').toBeFalse();

    let releaseGrid!: (grid: SubmissionsGridResult) => void;
    submissionsServiceSpy.getGrid.and.returnValue(
      new Promise<SubmissionsGridResult>((resolve) => (releaseGrid = resolve)),
    );
    page.onPageChange({ page: 1, first: 5, rows: 5, pageCount: 5 }); // 0-based: страница 2
    fixture.detectChanges();

    expect(page.loading()).toBeTrue();
    expect(root.querySelector('.p-datatable-mask'))
      .withContext('маска загрузки p-table видима')
      .not.toBeNull();

    releaseGrid(makeGrid({ page: 2 }));
    await settle(fixture);

    expect(page.loading()).withContext('после ответа — false').toBeFalse();
    expect(root.querySelector('.p-datatable-mask')).withContext('маска скрыта').toBeNull();
  });

  it('no-semesters: селектор семестра disabled, подпись «Нет семестров с работами», таблица скрыта', async () => {
    labsServiceSpy.getSemesters.and.resolveTo([]);
    const fixture = await createPage();
    const root: HTMLElement = fixture.nativeElement;

    expect(selectLabel(root, 0)).withContext('группы при этом доступны').toBe('ИК-221');
    expect(selects(root)[0].classList.contains('p-disabled')).toBeFalse();
    expect(selects(root)[1].classList.contains('p-disabled'))
      .withContext('селектор семестра заблокирован')
      .toBeTrue();
    expect(
      root.querySelector('[data-test="no-semesters-caption"]')?.textContent?.trim(),
    ).toBe('Нет семестров с работами');
    expect(root.querySelector('p-table')).withContext('таблица не отображается').toBeNull();
    expect(submissionsServiceSpy.getGrid).not.toHaveBeenCalled();
  });

  it('ошибка загрузки селекторов: notifyError якорем "header", таблица скрыта', async () => {
    groupsServiceSpy.getList.and.returnValue(
      Promise.reject({ status: 403, body: { message: 'Доступ запрещён' } }),
    );
    const fixture = await createPage();
    const root: HTMLElement = fixture.nativeElement;

    expect(notifications?.desktopMessage()).toEqual({
      severity: 'error',
      text: 'Доступ запрещён',
    });
    expect(root.querySelector('p-table')).toBeNull();
    expect(submissionsServiceSpy.getGrid).not.toHaveBeenCalled();
  });
});
