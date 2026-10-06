/**
 * Unit-тесты страницы «Лабораторные работы» /works (C-111, SCR-007, T-112):
 *  - дефолтная загрузка getList {semester: null, page: 1, semester↑} (IF-103);
 *  - AC filter-reload: выбор семестра 2 → getList {semester: 2, page: 1},
 *    возврат к «Все семестры» → {semester: null, page: 1} (FR-012);
 *  - AC sort-toggle: «Номер»/«Семестр» asc↔desc повторными кликами,
 *    третьего состояния нет (SCR-007);
 *  - пагинация: подпись «Показать записи с X по Y из Z» из PagedResult,
 *    в т.ч. последняя неполная страница (FR-011, ADR-109);
 *  - AC assignment-link: target="_blank", rel="noopener noreferrer", текст —
 *    URL без схемы, href через DomSanitizer; без ссылки — «—» (FR-011);
 *  - «Нужна защита» — read-only чекбокс (FR-011);
 *  - AC delete-confirm: диалог с текстом №N и №M дословно, «Yes»/«No»,
 *    блокировка списка (opacity + pointer-events), «Yes» → remove +
 *    «Удалено» + перезагрузка, «No» — без вызова (FR-014, ADR-111);
 *    пустая страница после удаления → предыдущая (ADR-109);
 *  - уведомления: успех «Удалено», ошибки дословно якорем 'header' (IF-109),
 *    отказ remove — баннер + перезагрузка текущей страницы без редиректа
 *    (аменда 5, IF-103 remove-404);
 *  - empty-состояние «Записей нет»; навигация «Добавить…»/«Редактировать».
 */
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Router } from '@angular/router';

import { LabsService, LabsGetListParams } from '../../../../core/services/labs.service';
import { ApiError, Lab, LabDto, PagedResult } from '../../../../shared/models';
import { NotificationService } from '../../../../shared/notifications/notification-service';
import { WorksPage } from './works-page';

class LabsServiceStub {
  getList = jasmine.createSpy<LabsService['getList']>('getList');
  getById = jasmine.createSpy<LabsService['getById']>('getById');
  create = jasmine.createSpy<LabsService['create']>('create');
  update = jasmine.createSpy<LabsService['update']>('update');
  remove = jasmine.createSpy<LabsService['remove']>('remove');
  getSemesters = jasmine.createSpy<LabsService['getSemesters']>('getSemesters');
}

class NotificationServiceStub {
  notifyError = jasmine.createSpy<NotificationService['notifyError']>('notifyError');
  notifySuccess = jasmine.createSpy<NotificationService['notifySuccess']>('notifySuccess');
}

class RouterStub {
  navigate = jasmine.createSpy<Router['navigate']>('navigate');
  navigateByUrl = jasmine.createSpy<Router['navigateByUrl']>('navigateByUrl');
}

describe('WorksPage — список лабораторных /works (T-112)', () => {
  let labs: LabsServiceStub;
  let notifications: NotificationServiceStub;
  let router: RouterStub;
  let fixture: ComponentFixture<WorksPage>;
  let native: HTMLElement;
  let idSeq: number;

  /** Запись лабораторной с дефолтами, дополненная оверрайдами. */
  function makeLab(overrides: Partial<Lab> = {}): LabDto {
    idSeq += 1;
    return {
      id: `lab-${idSeq}`,
      semester: 1,
      number: idSeq,
      content: `Содержание лабораторной работы №${idSeq}`,
      assignmentUrl: null,
      defenseRequired: false,
      ...overrides,
    };
  }

  /** Записи 1..count одного семестра — сид-подобная страница. */
  function makeLabs(count: number, semester = 1): LabDto[] {
    return Array.from({ length: count }, (_, index) =>
      makeLab({ number: index + 1, semester }),
    );
  }

  function makePage(items: LabDto[], total: number, page: number): PagedResult<LabDto> {
    return { items, total, page, pageSize: 10 };
  }

  /** Типизированный промис для and.returnValues ( getList возвращает Promise ). */
  function resolved<T>(value: T): Promise<T> {
    return Promise.resolve(value);
  }

  function apiError(status: number, message: string): ApiError {
    return { status, body: { message } };
  }

  /**
   * Доводит асинхронные цепочки (getList → сигнал → CD, диалог, удаление)
   * до конца: стабы отвечают промисами-микротасками, поэтому несколько
   * опустошений очереди микрозадач + detectChanges дают детерминизм
   * (паттерн спецификаций соседних страниц).
   */
  async function settle(): Promise<void> {
    // Первый прогон CD подхватывает синхронные последствия (ngOnInit, клики),
    // whenStable дожидается стабилизации (цепочки промисов стабов, привязки
    // PrimeNG); завершающие прогоны CD рендерят данные и хост-привязки,
    // изменившиеся сигналами в предыдущем прогоне (в т.ч. writeValue CVA
    // p-checkbox, отрисованного строками p-table в этом же цикле).
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    fixture.detectChanges();
    fixture.detectChanges();
  }

  function configure(): void {
    labs = new LabsServiceStub();
    notifications = new NotificationServiceStub();
    router = new RouterStub();
    // Дефолты для тестов, которым конкретные ответы не важны: «семестров
    // нет» и пустой список вместо undefined-ответов «голых» шпионов.
    labs.getSemesters.and.resolveTo([]);
    labs.remove.and.resolveTo(undefined);
    TestBed.configureTestingModule({
      imports: [WorksPage],
      providers: [
        provideNoopAnimations(),
        { provide: LabsService, useValue: labs },
        { provide: NotificationService, useValue: notifications },
        { provide: Router, useValue: router },
      ],
    });
  }

  async function createAndSettle(): Promise<void> {
    fixture = TestBed.createComponent(WorksPage);
    native = fixture.nativeElement as HTMLElement;
    // Хост крепится в document: фокус-ловушки диалога и оверлеи
    // разрешаются только для элементов в дереве документа.
    document.body.appendChild(native);
    await settle();
  }

  function sortHeader(title: string): HTMLTableCellElement {
    const header = Array.from(native.querySelectorAll('th')).find((th) =>
      (th.textContent ?? '').trim().startsWith(title),
    );
    expect(header).withContext(`заголовок «${title}» не найден`).not.toBeNull();
    return header as HTMLTableCellElement;
  }

  function reportText(): string {
    return native.querySelector('.works-page__report')?.textContent ?? '';
  }

  function rowCells(row: HTMLTableRowElement): string[] {
    return Array.from(row.querySelectorAll('td')).map((td) => (td.textContent ?? '').trim());
  }

  function openConfirmDialog(row: HTMLTableRowElement): void {
    const trash = row.querySelector<HTMLButtonElement>('button[aria-label="Удалить"]');
    expect(trash).withContext('кнопка «Удалить» не найдена').not.toBeNull();
    trash!.click();
  }

  // Диалог телепортируется в document.body (appendTo 'body' в PrimeNG 20).
  function confirmMessage(): string {
    return document.body.querySelector('.p-confirmdialog-message')?.textContent ?? '';
  }

  function dialogButton(kind: 'accept' | 'reject'): HTMLButtonElement {
    const button = document.body.querySelector<HTMLButtonElement>(`button.p-confirmdialog-${kind}-button`);
    expect(button).withContext(`кнопка «${kind}» диалога не найдена`).not.toBeNull();
    return button!;
  }

  function listElement(): HTMLElement {
    return native.querySelector('.works-page__list')!;
  }

  beforeEach(() => {
    idSeq = 0;
    configure();
  });

  afterEach(() => {
    if (fixture) {
      document.body.removeChild(native);
      fixture.destroy();
    }
  });

  describe('первичная загрузка (дефолт IF-103)', () => {
    it('AC default: getList {semester: null, page: 1, semester asc}, шапка и «Добавить…»', async () => {
      labs.getList.and.resolveTo(makePage(makeLabs(10), 10, 1));
      labs.getSemesters.and.resolveTo([1, 2]);

      await createAndSettle();

      expect(native.querySelector('h1')!.textContent).toBe('Лабораторные работы');
      expect(native.querySelector('.works-page__head button')!.textContent).toContain('Добавить…');
      expect(labs.getList).toHaveBeenCalledWith({
        semester: null,
        page: 1,
        sortField: 'semester',
        sortDir: 'asc',
      });
      expect(labs.getSemesters).toHaveBeenCalled();
      expect(native.querySelectorAll('tbody tr').length).toBe(10);
    });

    it('опции селекта: «Все семестры» (null) + семестры с работами (labs.getSemesters)', async () => {
      labs.getList.and.resolveTo(makePage([], 0, 1));
      labs.getSemesters.and.resolveTo([1, 2, 5]);

      await createAndSettle();

      expect(fixture.componentInstance.semesterOptions()).toEqual([
        { label: 'Все семестры', value: null },
        { label: '1', value: 1 },
        { label: '2', value: 2 },
        { label: '5', value: 5 },
      ]);
      expect(native.querySelector('.p-select-label')!.textContent).toContain('Все семестры');
    });
  });

  describe('AC filter-reload: фильтр семестра', () => {
    beforeEach(() => {
      labs.getSemesters.and.resolveTo([1, 2]);
    });

    it('выбор семестра 2 → getList {semester: 2, page: 1}, таблица перезагружена', async () => {
      const semester2 = makeLab({ number: 21, semester: 2, content: 'Семестр 2, работа 21' });
      labs.getList.and.returnValues(resolved(makePage(makeLabs(10), 12, 1)), resolved(makePage([semester2], 1, 1)));

      await createAndSettle();

      (native.querySelector('p-select') as HTMLElement).click();
      await settle();
      const option = Array.from(native.querySelectorAll('.p-select-option')).find(
        (item) => (item.textContent ?? '').trim() === '2',
      );
      expect(option).withContext('опция «2» не найдена').toBeDefined();
      (option as HTMLElement).click();
      await settle();

      expect(labs.getList).toHaveBeenCalledWith({
        semester: 2,
        page: 1,
        sortField: 'semester',
        sortDir: 'asc',
      });
      expect(native.querySelector('tbody')!.textContent).toContain('Семестр 2, работа 21');
    });

    it('возврат к «Все семестры» → getList {semester: null, page: 1}', async () => {
      labs.getList.and.returnValues(
        resolved(makePage(makeLabs(10), 12, 1)),
        resolved(makePage(makeLabs(2, 2), 2, 1)),
        resolved(makePage(makeLabs(10), 12, 1)),
      );

      await createAndSettle();

      (native.querySelector('p-select') as HTMLElement).click();
      await settle();
      (Array.from(native.querySelectorAll('.p-select-option')).find(
        (item) => (item.textContent ?? '').trim() === '2',
      ) as HTMLElement).click();
      await settle();

      (native.querySelector('p-select') as HTMLElement).click();
      await settle();
      (Array.from(native.querySelectorAll('.p-select-option')).find(
        (item) => (item.textContent ?? '').trim() === 'Все семестры',
      ) as HTMLElement).click();
      await settle();

      expect(labs.getList).toHaveBeenCalledWith({
        semester: null,
        page: 1,
        sortField: 'semester',
        sortDir: 'asc',
      });
    });
  });

  describe('AC sort-toggle: сортировка в двух состояниях', () => {
    beforeEach(() => {
      labs.getList.and.resolveTo(makePage(makeLabs(3), 3, 1));
    });

    it('дефолт — «Семестр» ▲ (semester↑), клик по «Номер» → asc, повторно → desc', async () => {
      await createAndSettle();

      const semesterHeader = sortHeader('Семестр');
      expect(semesterHeader.getAttribute('aria-sort')).toBe('ascending');
      expect(semesterHeader.textContent).toContain('▲');

      const numberHeader = sortHeader('Номер');
      numberHeader.click();
      await settle();
      expect(labs.getList).toHaveBeenCalledWith({
        semester: null,
        page: 1,
        sortField: 'number',
        sortDir: 'asc',
      });
      expect(numberHeader.getAttribute('aria-sort')).toBe('ascending');

      numberHeader.click();
      await settle();
      expect(labs.getList).toHaveBeenCalledWith({
        semester: null,
        page: 1,
        sortField: 'number',
        sortDir: 'desc',
      });
      expect(numberHeader.getAttribute('aria-sort')).toBe('descending');
      expect(numberHeader.textContent).toContain('▼');
    });

    it('третьего состояния нет: третий клик по «Номер» снова asc', async () => {
      await createAndSettle();

      const numberHeader = sortHeader('Номер');
      numberHeader.click();
      await settle();
      numberHeader.click();
      await settle();
      numberHeader.click();
      await settle();

      expect(labs.getList).toHaveBeenCalledWith({
        semester: null,
        page: 1,
        sortField: 'number',
        sortDir: 'asc',
      });
    });

    it('клик по активной «Семестр» (дефолт asc) → desc; «Номер» после этого — asc', async () => {
      await createAndSettle();

      const semesterHeader = sortHeader('Семестр');
      semesterHeader.click();
      await settle();
      expect(labs.getList).toHaveBeenCalledWith({
        semester: null,
        page: 1,
        sortField: 'semester',
        sortDir: 'desc',
      });
      expect(semesterHeader.getAttribute('aria-sort')).toBe('descending');

      sortHeader('Номер').click();
      await settle();
      expect(labs.getList).toHaveBeenCalledWith({
        semester: null,
        page: 1,
        sortField: 'number',
        sortDir: 'asc',
      });
    });
  });

  describe('пагинация и подпись «Показать записи с X по Y из Z»', () => {
    it('первая полная страница: «с 1 по 10 из 12»', async () => {
      labs.getList.and.resolveTo(makePage(makeLabs(10), 12, 1));

      await createAndSettle();

      expect(reportText()).toBe('Показать записи с 1 по 10 из 12');
    });

    it('листание на последнюю неполную страницу: getList {page: 2}, «с 11 по 12 из 12»', async () => {
      const tail = [makeLab({ number: 11 }), makeLab({ number: 12 })];
      labs.getList.and.returnValues(resolved(makePage(makeLabs(10), 12, 1)), resolved(makePage(tail, 12, 2)));

      await createAndSettle();

      const pageLinks = native.querySelectorAll<HTMLButtonElement>('.p-paginator-page');
      expect(pageLinks.length).toBeGreaterThanOrEqual(2);
      pageLinks[1]!.click();
      await settle();

      expect(labs.getList).toHaveBeenCalledWith({
        semester: null,
        page: 2,
        sortField: 'semester',
        sortDir: 'asc',
      });
      expect(native.querySelectorAll('tbody tr').length).toBe(2);
      expect(reportText()).toBe('Показать записи с 11 по 12 из 12');
    });
  });

  describe('AC assignment-link: колонка «Задание»', () => {
    it('ссылка: текст без схемы, target=_blank, rel="noopener noreferrer"; без ссылки — «—»', async () => {
      const withUrl = makeLab({
        number: 5,
        assignmentUrl: 'https://git.example.com/assignments/1/5',
      });
      const withoutUrl = makeLab({ number: 6, assignmentUrl: null });
      labs.getList.and.resolveTo(makePage([withUrl, withoutUrl], 2, 1));

      await createAndSettle();

      const rows = Array.from(native.querySelectorAll<HTMLTableRowElement>('tbody tr'));
      const link = rows[0]!.querySelector<HTMLAnchorElement>('a.works-page__assignment');
      expect(link).not.toBeNull();
      expect(link!.getAttribute('href')).toBe('https://git.example.com/assignments/1/5');
      expect(link!.getAttribute('target')).toBe('_blank');
      expect(link!.getAttribute('rel')).toBe('noopener noreferrer');
      expect(link!.textContent!.trim()).toBe('git.example.com/assignments/1/5');

      const cells = rowCells(rows[1]!);
      expect(rows[1]!.querySelector('a.works-page__assignment')).toBeNull();
      expect(cells[3]).toBe('—');
    });

    it('href санитизируется DomSanitizer: javascript: нейтрализуется, текст срезает http/https', async () => {
      labs.getList.and.resolveTo(makePage(makeLabs(1), 1, 1));

      await createAndSettle();
      const component = fixture.componentInstance;

      // Опасная схема нейтрализуется префиксом «unsafe:» — исполнение невозможно.
      const unsafe = component.assignmentHref('javascript:alert(1)');
      expect(unsafe === null || unsafe.startsWith('unsafe:')).toBeTrue();
      expect(component.assignmentHref('https://ok.example.com/a')).toBe(
        'https://ok.example.com/a',
      );
      expect(component.assignmentLabel('https://ok.example.com/a')).toBe('ok.example.com/a');
      expect(component.assignmentLabel('http://ok.example.com/b')).toBe('ok.example.com/b');
    });
  });

  it('«Нужна защита» — read-only чекбокс с состоянием записи (FR-011)', async () => {
    labs.getList.and.resolveTo(
      makePage([makeLab({ number: 1, defenseRequired: true }), makeLab({ number: 2 })], 2, 1),
    );

    await createAndSettle();

    const checkboxes = native.querySelectorAll('p-checkbox');
    expect(checkboxes.length).toBe(2);
    // Признак из записи виден, но недоступен для изменения (read-only, FR-011).
    // Строки таблицы создаются в последнем прогоне CD Settle — writeValue CVA
    // чекбокса отражается в хост-привязках на следующем прогоне, поэтому
    // делаем два контрольных прогона.
    fixture.detectChanges();
    fixture.detectChanges();
    expect(native.querySelector('p-checkbox[data-p-checked="true"]')).not.toBeNull();
    expect(native.querySelector('p-checkbox[data-p-checked="false"]')).not.toBeNull();
    for (const checkbox of Array.from(checkboxes)) {
      expect(checkbox.getAttribute('data-p-disabled')).toBe('true');
    }
  });

  describe('AC delete-confirm: диалог удаления', () => {
    let target: LabDto;

    beforeEach(() => {
      target = makeLab({ number: 2, semester: 1 });
      const row = makeLab({ number: 1, semester: 1 });
      const tail = makeLab({ number: 3, semester: 1 });
      labs.getList.and.resolveTo(makePage([row, target, tail], 3, 1));
    });

    async function openDialogOnTargetRow(): Promise<HTMLTableRowElement> {
      await createAndSettle();
      const rows = Array.from(native.querySelectorAll<HTMLTableRowElement>('tbody tr'));
      const targetRow = rows.find(
        (candidate) => (candidate.querySelector('td')?.textContent ?? '').trim() === '2',
      );
      expect(targetRow).withContext('строка №2 не найдена').toBeDefined();
      openConfirmDialog(targetRow!);
      await settle();
      return targetRow!;
    }

    it('текст с №N и №M дословно, кнопки «Yes»/«No», список заблокирован', async () => {
      await openDialogOnTargetRow();

      expect(confirmMessage()).toBe('Вы точно хотите удалить лабораторную №2 в семестре №1?');
      expect(dialogButton('accept').textContent!.trim()).toBe('Yes');
      expect(dialogButton('reject').textContent!.trim()).toBe('No');

      const list = listElement();
      expect(list.classList).toContain('works-page__list--locked');
      expect(getComputedStyle(list).opacity).toBe('0.35');
      expect(getComputedStyle(list).pointerEvents).toBe('none');
    });

    it('«No» — remove не вызывается, диалог закрыт, список разблокирован', async () => {
      await openDialogOnTargetRow();

      dialogButton('reject').click();
      await settle();

      expect(labs.remove).not.toHaveBeenCalled();
      expect(confirmMessage()).toBe('');
      expect(listElement().classList).not.toContain('works-page__list--locked');
    });

    it('«Yes» — remove вызван, «Удалено», список перезагружен и разблокирован', async () => {
      const initial = makePage([makeLab({ number: 1, semester: 1 }), target, makeLab({ number: 3, semester: 1 })], 3, 1);
      const afterRemove = makePage([makeLab({ number: 1, semester: 1 }), makeLab({ number: 3, semester: 1 })], 2, 1);
      labs.getList.and.returnValues(resolved(initial), resolved(afterRemove));
      labs.remove.and.resolveTo(undefined);

      await openDialogOnTargetRow();

      dialogButton('accept').click();
      await settle();

      expect(labs.remove).toHaveBeenCalledWith(target.id);
      expect(notifications.notifySuccess).toHaveBeenCalledWith('Удалено');
      expect(notifications.notifyError).not.toHaveBeenCalled();
      expect(labs.getList).toHaveBeenCalledTimes(2);
      expect(listElement().classList).not.toContain('works-page__list--locked');
    });

    it('отказ remove (аменда 5): notifyError дословно с якорем «header», список перезагружен, редиректа нет', async () => {
      labs.remove.and.rejectWith(apiError(404, 'Лабораторная не найдена'));

      await openDialogOnTargetRow();

      dialogButton('accept').click();
      await settle();

      expect(notifications.notifyError).toHaveBeenCalledWith('Лабораторная не найдена', 'header');
      expect(notifications.notifySuccess).not.toHaveBeenCalled();
      // Аменда 5: после неудачного remove текущая страница перезагружена.
      expect(labs.getList).toHaveBeenCalledTimes(2);
      const reloadCall = labs.getList.calls.mostRecent().args[0] as LabsGetListParams;
      expect(reloadCall.page).toBe(1);
      expect(native.querySelectorAll('tbody tr').length).toBe(3);
      expect(listElement().classList).not.toContain('works-page__list--locked');
      // Редиректа нет — пользователь остаётся на /works.
      expect(router.navigate).not.toHaveBeenCalled();
      expect(router.navigateByUrl).not.toHaveBeenCalled();
    });

    it('страница опустела после удаления (page > 1) — перезагрузка предыдущей', async () => {
      const lone = makeLab({ number: 11 });
      const initial = makePage(makeLabs(10), 11, 1);
      const page2 = makePage([lone], 11, 2);
      const emptyPage2 = makePage([], 10, 2);
      const page1After = makePage(makeLabs(10), 10, 1);
      labs.getList.and.returnValues(resolved(initial), resolved(page2), resolved(emptyPage2), resolved(page1After));
      labs.remove.and.resolveTo(undefined);

      await createAndSettle();

      const pageLinks = native.querySelectorAll<HTMLButtonElement>('.p-paginator-page');
      pageLinks[1]!.click();
      await settle();
      expect(native.querySelectorAll('tbody tr').length).toBe(1);

      openConfirmDialog(native.querySelector('tbody tr') as HTMLTableRowElement);
      await settle();

      dialogButton('accept').click();
      await settle();

      expect(labs.remove).toHaveBeenCalledWith(lone.id);
      const lastCall = labs.getList.calls.mostRecent().args[0] as LabsGetListParams;
      expect(lastCall.page).toBe(1);
      expect(native.querySelectorAll('tbody tr').length).toBe(10);
      expect(reportText()).toBe('Показать записи с 1 по 10 из 10');
    });
  });

  it('CR-004: удаление последней работы семестра убирает его опцию из селекта', async () => {
    const onlyFirst = makeLab({ number: 1, semester: 1 });
    const lastOfSecond = makeLab({ number: 7, semester: 2 });
    labs.getSemesters.and.returnValues(resolved([1, 2]), resolved([1]));
    labs.getList.and.returnValues(
      resolved(makePage([onlyFirst, lastOfSecond], 2, 1)),
      resolved(makePage([onlyFirst], 1, 1)),
    );
    labs.remove.and.resolveTo(undefined);

    await createAndSettle();
    expect(fixture.componentInstance.semesterOptions().map((option) => option.value)).toEqual([
      null,
      1,
      2,
    ]);

    const rows = Array.from(native.querySelectorAll<HTMLTableRowElement>('tbody tr'));
    const secondRow = rows.find(
      (row) => (row.querySelector('td')?.textContent ?? '').trim() === '7',
    );
    expect(secondRow).withContext('строка №7 не найдена').toBeDefined();
    openConfirmDialog(secondRow!);
    await settle();
    dialogButton('accept').click();
    await settle();

    expect(labs.remove).toHaveBeenCalledWith(lastOfSecond.id);
    expect(fixture.componentInstance.semesterOptions().map((option) => option.value)).toEqual([
      null,
      1,
    ]);
    expect(labs.getSemesters).toHaveBeenCalledTimes(2);
  });

  it('аменда 7: исчезновение выбранного семестра после удаления — сброс фильтра на «Все семестры» и перезагрузка без ошибки', async () => {
    const firstSemester = makeLab({ number: 1, semester: 1 });
    const secondSemester = makeLab({ number: 5, semester: 2 });
    labs.getSemesters.and.returnValues(resolved([1, 2]), resolved([1]));
    labs.getList.and.returnValues(
      resolved(makePage(makeLabs(10), 12, 1)), // старт: все семестры
      resolved(makePage([secondSemester], 1, 1)), // фильтр «семестр 2»
      resolved(makePage([], 0, 1)), // после удаления: семестр 2 пуст
      resolved(makePage([firstSemester], 1, 1)), // сброшенный фильтр «Все семестры»
    );
    labs.remove.and.resolveTo(undefined);

    await createAndSettle();

    (native.querySelector('p-select') as HTMLElement).click();
    await settle();
    (Array.from(native.querySelectorAll('.p-select-option')).find(
      (item) => (item.textContent ?? '').trim() === '2',
    ) as HTMLElement).click();
    await settle();
    expect(labs.getList).toHaveBeenCalledWith({
      semester: 2,
      page: 1,
      sortField: 'semester',
      sortDir: 'asc',
    });

    openConfirmDialog(native.querySelector('tbody tr') as HTMLTableRowElement);
    await settle();
    dialogButton('accept').click();
    await settle();

    expect(labs.remove).toHaveBeenCalledWith(secondSemester.id);
    const lastCall = labs.getList.calls.mostRecent().args[0] as LabsGetListParams;
    expect(lastCall).toEqual({
      semester: null,
      page: 1,
      sortField: 'semester',
      sortDir: 'asc',
    });
    expect(fixture.componentInstance.semesterOptions().map((option) => option.value)).toEqual([
      null,
      1,
    ]);
    expect(notifications.notifyError).not.toHaveBeenCalled();
  });

  describe('состояния и уведомления (IF-109)', () => {
    it('пустой список — «Записей нет» и подпись «Показать записи с 1 по 0 из 0» (аменда 7, CR-003а)', async () => {
      labs.getSemesters.and.resolveTo([]);
      labs.getList.and.resolveTo(makePage([], 0, 1));

      await createAndSettle();

      expect(native.querySelector('.works-page__empty')!.textContent).toContain('Записей нет');
      expect(reportText()).toBe('Показать записи с 1 по 0 из 0');
      // Пустой getSemesters: единственная опция «Все семестры», селектор активен.
      expect(fixture.componentInstance.semesterOptions()).toEqual([
        { label: 'Все семестры', value: null },
      ]);
    });

    it('ошибка getList — notifyError дословно с якорем «header»', async () => {
      labs.getList.and.rejectWith(apiError(403, 'Доступ запрещён'));

      await createAndSettle();

      expect(notifications.notifyError).toHaveBeenCalledWith('Доступ запрещён', 'header');
      expect(notifications.notifySuccess).not.toHaveBeenCalled();
    });

    it('ошибка getSemesters — notifyError, список всё равно загружен', async () => {
      labs.getSemesters.and.rejectWith(apiError(403, 'Доступ запрещён'));
      labs.getList.and.resolveTo(makePage(makeLabs(1), 1, 1));

      await createAndSettle();

      expect(notifications.notifyError).toHaveBeenCalledWith('Доступ запрещён', 'header');
      expect(native.querySelectorAll('tbody tr').length).toBe(1);
    });
  });

  describe('навигация к форме (T-113)', () => {
    it('«Добавить…» → /works/new, «Редактировать» → /works/:id/edit', async () => {
      const first = makeLab({ number: 1 });
      labs.getList.and.resolveTo(makePage([first], 1, 1));

      await createAndSettle();

      (native.querySelector('.works-page__head button') as HTMLButtonElement).click();
      expect(router.navigateByUrl).toHaveBeenCalledWith('/works/new');

      (
        native.querySelector('button[aria-label="Редактировать"]') as HTMLButtonElement
      ).click();
      expect(router.navigate).toHaveBeenCalledWith(['/works', first.id, 'edit']);
    });

    it('иконки-кнопки имеют title-тултипы по макету SCR-007', async () => {
      labs.getList.and.resolveTo(makePage(makeLabs(1), 1, 1));

      await createAndSettle();

      expect(native.querySelector('p-button[title="Редактировать"]')).not.toBeNull();
      expect(native.querySelector('p-button[title="Удалить"]')).not.toBeNull();
    });
  });
});
