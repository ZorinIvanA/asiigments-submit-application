/**
 * Unit-тесты страницы «Сдача работ» студента (C-113, SCR-010, US-11):
 *  - десктоп p-table (ADR-111): «Студент» + пары «Сдача»/«Защита» каждой
 *    работы, одна строка, только свои данные, даты дд.мм.гггг, пустая —
 *    пусто;
 *  - мобильные карточки «Лаб N» (MockBreakpointObserver, <768px) и
 *    переключение вёрсток в обе стороны;
 *  - дефолтный семестр — наименьший, смена селектора → перезагрузка getMy;
 *  - индикатор загрузки p-table на время запроса (CR-003);
 *  - read-only: нулевые вызовы SubmissionsService.update, календаря нет;
 *  - студент без группы (groupName = null) — предупреждение дословно
 *    (role=status), таблицы нет, селектор активен;
 *  - аменда 3 (CR-002): пустой getSemesters → getMy НЕ вызывается,
 *    «в группе» — селектор заблокирован + «Нет семестров с работами»,
 *    «без группы» — только предупреждение (приоритет);
 *  - аменда 3 (CR-004): у выбранного семестра нет работ (labs: []) —
 *    тот же текст «Нет семестров с работами» в обеих вёрстках, селектор
 *    активен;
 *  - ошибки мока — notifyError дословно с якорем 'header' (IF-109).
 */
import { BreakpointObserver } from '@angular/cdk/layout';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AuthService, MeDto } from '../../../core/services/auth.service';
import { LabsService } from '../../../core/services/labs.service';
import { SubmissionsService } from '../../../core/services/submissions.service';
import { ApiError, MySubmissionsDto } from '../../../shared/models';
import { NotificationService } from '../../../shared/notifications/notification-service';
import { MockBreakpointObserver } from '../../../../testing/mock-breakpoint-observer';
import { MySubmissionsPage } from './my-submissions-page';

const TEXT_NO_GROUP =
  'Вы не включены в группу, доступ к сдаче лабораторных не выдан';
const TEXT_NO_SEMESTERS = 'Нет семестров с работами';

/** Студент в группе «ИТ-21» (дефолт); для ветки «без группы» — groupName null. */
const STUDENT_IN_GROUP: MeDto = {
  login: 'ivanov01',
  fullName: 'Иванов Иван Иванович 01',
  role: 'student',
  groupName: 'ИТ-21',
};

/** Сид-подобный ответ getMy: три работы, у третьей защита не проставлена. */
const MY_SUBMISSIONS: MySubmissionsDto = {
  hasGroup: true,
  labs: [
    { id: 'lab-1', number: 1, defenseRequired: true },
    { id: 'lab-2', number: 2, defenseRequired: true },
    { id: 'lab-3', number: 3, defenseRequired: false },
  ],
  submissions: [
    { labId: 'lab-1', submitDate: '2026-09-01', defenseDate: '2026-09-11' },
    { labId: 'lab-2', submitDate: '2026-09-02', defenseDate: '2026-09-12' },
    { labId: 'lab-3', submitDate: '2026-09-03', defenseDate: null },
  ],
};

/** Ответ getMy для семестра 2 (перезагрузка селектором). */
const SEMESTER_2_SUBMISSIONS: MySubmissionsDto = {
  hasGroup: true,
  labs: [{ id: 'lab-9', number: 9, defenseRequired: true }],
  submissions: [{ labId: 'lab-9', submitDate: '2026-10-05', defenseDate: null }],
};

class LabsServiceStub {
  getSemesters = jasmine.createSpy<LabsService['getSemesters']>('getSemesters');
}

class SubmissionsServiceStub {
  getMy = jasmine.createSpy<SubmissionsService['getMy']>('getMy');
  update = jasmine.createSpy<SubmissionsService['update']>('update');
}

class AuthServiceStub {
  readonly currentUser = signal<MeDto | null>(STUDENT_IN_GROUP);
}

describe('MySubmissionsPage', () => {
  let breakpoints: MockBreakpointObserver;
  let labs: LabsServiceStub;
  let submissions: SubmissionsServiceStub;
  let auth: AuthServiceStub;
  let notifications: NotificationService;
  let fixture: ComponentFixture<MySubmissionsPage>;
  let native: HTMLElement;

  /**
   * Доводит асинхронную цепочку reload (getSemesters → getMy) до конца:
   * стабы отвечают промисами-микротасками, поэтому несколько опустошений
   * очереди микрозадач + detectChanges дают детерминированное состояние.
   */
  async function settle(): Promise<void> {
    for (let tick = 0; tick < 6; tick++) {
      await Promise.resolve();
    }
    fixture.detectChanges();
  }

  function configure(): void {
    labs = new LabsServiceStub();
    submissions = new SubmissionsServiceStub();
    auth = new AuthServiceStub();
    TestBed.configureTestingModule({
      imports: [MySubmissionsPage],
      providers: [
        { provide: BreakpointObserver, useValue: breakpoints },
        { provide: LabsService, useValue: labs },
        { provide: SubmissionsService, useValue: submissions },
        { provide: AuthService, useValue: auth },
      ],
    });
    notifications = TestBed.inject(NotificationService);
  }

  async function createAndSettle(): Promise<void> {
    fixture = TestBed.createComponent(MySubmissionsPage);
    native = fixture.nativeElement as HTMLElement;
    await settle();
  }

  /** Значение селектора семестра + событие change (как у пользователя). */
  async function selectSemester(value: string): Promise<void> {
    const select = native.querySelector<HTMLSelectElement>(
      '#my-submissions-semester',
    )!;
    select.value = value;
    select.dispatchEvent(new Event('change'));
    await settle();
  }

  beforeEach(() => {
    breakpoints = new MockBreakpointObserver();
    configure();
  });

  describe('десктоп p-table (>=768px)', () => {
    beforeEach(() => {
      breakpoints.simulate(false);
      labs.getSemesters.and.resolveTo([1, 2]);
      submissions.getMy.and.resolveTo(MY_SUBMISSIONS);
    });

    it('AC desktop-table: одна строка, пары подколонок «Сдача»/«Защита», только свои данные', async () => {
      await createAndSettle();

      const table = native.querySelector('table');
      expect(table).not.toBeNull();
      expect(native.querySelectorAll('tbody tr').length).toBe(1);

      const headers = Array.from(
        native.querySelectorAll('thead tr')[0]!.querySelectorAll('th'),
      ).map((th) => th.textContent!.trim());
      expect(headers).toEqual(['Студент', 'Лаб 1', 'Лаб 2', 'Лаб 3']);

      const subHeaders = Array.from(
        native.querySelectorAll('thead tr')[1]!.querySelectorAll('th'),
      ).map((th) => th.textContent!.trim());
      expect(subHeaders).toEqual([
        'Сдача',
        'Защита',
        'Сдача',
        'Защита',
        'Сдача',
        'Защита',
      ]);

      const cells = Array.from(native.querySelectorAll('tbody td')).map(
        (td) => td.textContent!.trim(),
      );
      expect(cells).toEqual([
        'Иванов Иван Иванович 01',
        '01.09.2026',
        '11.09.2026',
        '02.09.2026',
        '12.09.2026',
        '03.09.2026',
        '', // защита третьей работы не проставлена — пустая ячейка без прочерка
      ]);
    });

    it('даты отображаются в формате дд.мм.гггг (toDisplayDate)', async () => {
      await createAndSettle();

      expect(native.textContent).toContain('01.09.2026');
      expect(native.textContent).not.toContain('2026-09-01');
      expect(native.textContent).not.toContain('2026-9-1');
    });

    it('селектор семестра: дефолт — наименьший из возвращённых, getMy вызван с ним', async () => {
      labs.getSemesters.and.resolveTo([4, 2, 7]);
      await createAndSettle();

      const select = native.querySelector<HTMLSelectElement>(
        '#my-submissions-semester',
      )!;
      expect(select.disabled).toBeFalse();
      expect(select.options.length).toBe(3);
      expect(labs.getSemesters).toHaveBeenCalled();
      expect(submissions.getMy).toHaveBeenCalledWith(2);
      const selected = select.options[select.selectedIndex];
      expect(selected.textContent!.trim()).toBe('2');
    });

    it('read-only: клики по ячейкам не вызывают update, календарь не открывается', async () => {
      await createAndSettle();

      const dateCell = native.querySelector('tbody td:nth-child(2)')!;
      dateCell.dispatchEvent(new Event('click'));
      await settle();

      expect(native.querySelector('p-datepicker')).toBeNull();
      expect(submissions.update).not.toHaveBeenCalled();
    });

    it('CR-003: на время перезагрузки p-table показывает индикатор загрузки', async () => {
      await createAndSettle();
      expect(native.querySelector('.p-datatable-mask')).toBeNull();

      let resolveGetMy!: (dto: MySubmissionsDto) => void;
      submissions.getMy.and.returnValue(
        new Promise<MySubmissionsDto>((resolve) => (resolveGetMy = resolve)),
      );
      await selectSemester('2');

      expect(fixture.componentInstance.loading()).toBeTrue();
      fixture.detectChanges();
      expect(native.querySelector('.p-datatable-mask')).not.toBeNull();

      resolveGetMy(SEMESTER_2_SUBMISSIONS);
      await settle();
      expect(fixture.componentInstance.loading()).toBeFalse();
      expect(native.querySelector('.p-datatable-mask')).toBeNull();
    });
  });

  describe('мобильные карточки (<768px)', () => {
    beforeEach(() => {
      breakpoints.simulate(true);
      labs.getSemesters.and.resolveTo([1, 2]);
      submissions.getMy.and.resolveTo(MY_SUBMISSIONS);
    });

    it('AC mobile-cards: карточки «Лаб N» со строками Сдача/Защита, таблицы нет', async () => {
      await createAndSettle();

      expect(native.querySelector('table')).toBeNull();
      const cards = Array.from(native.querySelectorAll('.card'));
      expect(cards.length).toBe(3);

      const titles = cards.map(
        (card) => card.querySelector('.card__title')!.textContent!.trim(),
      );
      expect(titles).toEqual(['Лаб 1', 'Лаб 2', 'Лаб 3']);

      // Компилятор шаблонов убирает пробелы между элементами, поэтому пара
      // проверяется по ячейкам «имя»/«значение», а не по слитному тексту.
      const rowsOf = (card: Element): Array<{ name: string; value: string }> =>
        Array.from(card.querySelectorAll('.card__row')).map((row) => ({
          name: row.querySelector('.card__name')!.textContent!.trim(),
          value: row.querySelector('.card__value')!.textContent!.trim(),
        }));
      expect(rowsOf(cards[0]!)).toEqual([
        { name: 'Сдача', value: '01.09.2026' },
        { name: 'Защита', value: '11.09.2026' },
      ]);
      // Пустая дата — пустое значение (без прочерка).
      expect(rowsOf(cards[2]!)).toEqual([
        { name: 'Сдача', value: '03.09.2026' },
        { name: 'Защита', value: '' },
      ]);
      // Пара «Защита» есть и у работы без defenseRequired (ASM-010).
      expect(cards[2]!.textContent).toContain('Защита');
    });

    it('переключение вёрсток через BreakpointObserver в обе стороны', async () => {
      await createAndSettle();
      expect(native.querySelectorAll('.card').length).toBe(3);

      breakpoints.simulate(false);
      fixture.detectChanges();
      expect(native.querySelector('table')).not.toBeNull();
      expect(native.querySelector('.card')).toBeNull();

      breakpoints.simulate(true);
      fixture.detectChanges();
      expect(native.querySelector('table')).toBeNull();
      expect(native.querySelectorAll('.card').length).toBe(3);
    });
  });

  describe('смена семестра', () => {
    beforeEach(() => {
      breakpoints.simulate(false);
      labs.getSemesters.and.resolveTo([1, 2]);
      submissions.getMy.and.resolveTo(MY_SUBMISSIONS);
    });

    it('перезагрузка: смена семестра → новый вызов getMy и новые данные', async () => {
      await createAndSettle();
      expect(submissions.getMy).toHaveBeenCalledWith(1);

      submissions.getMy.and.resolveTo(SEMESTER_2_SUBMISSIONS);
      await selectSemester('2');

      expect(submissions.getMy).toHaveBeenCalledWith(2);
      const cells = Array.from(native.querySelectorAll('tbody td')).map(
        (td) => td.textContent!.trim(),
      );
      expect(cells).toEqual(['Иванов Иван Иванович 01', '05.10.2026', '']);
    });

    it('read-only после смены семестра: update по-прежнему не вызывается', async () => {
      await createAndSettle();

      await selectSemester('2');

      expect(submissions.update).not.toHaveBeenCalled();
    });
  });

  describe('студент без группы (groupName = null)', () => {
    beforeEach(() => {
      auth.currentUser.set({ ...STUDENT_IN_GROUP, groupName: null });
      labs.getSemesters.and.resolveTo([1, 2]);
      submissions.getMy.and.resolveTo({ hasGroup: false, labs: [], submissions: [] });
    });

    it('AC no-group: предупреждение дословно (role=status), таблицы/карточек нет, селектор активен', async () => {
      breakpoints.simulate(false);
      await createAndSettle();

      const warning = native.querySelector('.no-group')!;
      expect(warning).not.toBeNull();
      expect(warning.getAttribute('role')).toBe('status');
      const warningText = warning.querySelector('span:not(.no-group__icon)')!;
      expect(warningText.textContent!.replace(/\s+/g, ' ').trim()).toBe(TEXT_NO_GROUP);

      expect(native.querySelector('table')).toBeNull();
      expect(native.querySelector('.card')).toBeNull();

      const select = native.querySelector<HTMLSelectElement>(
        '#my-submissions-semester',
      )!;
      expect(select.disabled).toBeFalse();
      expect(select.options.length).toBe(2);
    });

    it('предупреждение показывается и в мобильной вёрстке', async () => {
      breakpoints.simulate(true);
      await createAndSettle();

      expect(native.querySelector('.no-group')).not.toBeNull();
      expect(native.querySelectorAll('.card').length).toBe(0);
    });
  });

  describe('аменда 3: пустой getSemesters (CR-002)', () => {
    beforeEach(() => {
      breakpoints.simulate(false);
      labs.getSemesters.and.resolveTo([]);
    });

    it('студент в группе: getMy не вызывается ни разу, селектор пуст и заблокирован, «Нет семестров с работами»', async () => {
      await createAndSettle();

      expect(submissions.getMy).not.toHaveBeenCalled();
      const select = native.querySelector<HTMLSelectElement>(
        '#my-submissions-semester',
      )!;
      expect(select.disabled).toBeTrue();
      expect(select.options.length).toBe(0);
      expect(native.querySelector('.no-data')!.textContent!.trim()).toBe(
        TEXT_NO_SEMESTERS,
      );
      expect(native.querySelector('table')).toBeNull();
      expect(native.querySelector('.no-group')).toBeNull();
    });

    it('AC no-semesters-priority: без группы — только предупреждение, «Нет семестров…» не показан, getMy не вызывается', async () => {
      auth.currentUser.set({ ...STUDENT_IN_GROUP, groupName: null });
      await createAndSettle();

      expect(submissions.getMy).not.toHaveBeenCalled();
      expect(native.querySelector('.no-group')).not.toBeNull();
      expect(native.querySelector('.no-data')).toBeNull();
      expect(native.textContent).not.toContain(TEXT_NO_SEMESTERS);

      const select = native.querySelector<HTMLSelectElement>(
        '#my-submissions-semester',
      )!;
      expect(select.disabled).toBeTrue();
    });

    it('ветки приоритета переключаются подстановкой groupName в профиль', async () => {
      // Без группы — предупреждение.
      auth.currentUser.set({ ...STUDENT_IN_GROUP, groupName: null });
      await createAndSettle();
      expect(native.querySelector('.no-group')).not.toBeNull();
      expect(native.querySelector('.no-data')).toBeNull();

      // С возвращённой группой — пустое состояние вместо предупреждения.
      auth.currentUser.set({ ...STUDENT_IN_GROUP, groupName: 'ИТ-21' });
      fixture.detectChanges();
      expect(native.querySelector('.no-group')).toBeNull();
      expect(native.querySelector('.no-data')!.textContent!.trim()).toBe(
        TEXT_NO_SEMESTERS,
      );
    });
  });

  describe('аменда 3: у выбранного семестра нет работ (CR-004)', () => {
    beforeEach(() => {
      labs.getSemesters.and.resolveTo([1, 2]);
      submissions.getMy.and.resolveTo({ hasGroup: true, labs: [], submissions: [] });
    });

    it('десктоп: «Нет семестров с работами», селектор активен, таблицы нет, update отсутствует', async () => {
      breakpoints.simulate(false);
      await createAndSettle();

      expect(native.querySelector('.no-data')!.textContent!.trim()).toBe(
        TEXT_NO_SEMESTERS,
      );
      const select = native.querySelector<HTMLSelectElement>(
        '#my-submissions-semester',
      )!;
      expect(select.disabled).toBeFalse();
      expect(select.options.length).toBe(2);
      expect(native.querySelector('table')).toBeNull();
      expect(native.querySelector('.no-group')).toBeNull();
      expect(submissions.update).not.toHaveBeenCalled();
    });

    it('мобильная вёрстка: тот же текст пустого состояния', async () => {
      breakpoints.simulate(true);
      await createAndSettle();

      expect(native.querySelector('.no-data')!.textContent!.trim()).toBe(
        TEXT_NO_SEMESTERS,
      );
      expect(native.querySelectorAll('.card').length).toBe(0);
      const select = native.querySelector<HTMLSelectElement>(
        '#my-submissions-semester',
      )!;
      expect(select.disabled).toBeFalse();
    });

    it('смена семестра перезагружает экран: появившиеся работы рендерятся', async () => {
      breakpoints.simulate(false);
      await createAndSettle();
      expect(native.querySelector('.no-data')).not.toBeNull();

      submissions.getMy.and.resolveTo(MY_SUBMISSIONS);
      await selectSemester('2');

      expect(submissions.getMy).toHaveBeenCalledWith(2);
      expect(native.querySelector('.no-data')).toBeNull();
      expect(native.querySelector('table')).not.toBeNull();
    });
  });

  describe('ошибки мока (IF-109: экран без формы — якорь header)', () => {
    beforeEach(() => {
      breakpoints.simulate(false);
      spyOn(notifications, 'notifyError');
    });

    it('отказ getMy: текст дословно, якорь header', async () => {
      labs.getSemesters.and.resolveTo([1]);
      const error: ApiError = { status: 403, body: { message: 'Доступ запрещён' } };
      submissions.getMy.and.rejectWith(error);
      await createAndSettle();

      expect(notifications.notifyError).toHaveBeenCalledWith('Доступ запрещён', 'header');
      expect(native.querySelector('table')).toBeNull();
    });

    it('отказ getSemesters: текст дословно, якорь header, данные не рендерятся', async () => {
      labs.getSemesters.and.rejectWith({
        status: 401,
        body: { message: 'Не авторизован' },
      } satisfies ApiError);
      await createAndSettle();

      expect(notifications.notifyError).toHaveBeenCalledWith('Не авторизован', 'header');
      expect(native.querySelector('.no-group')).toBeNull();
      expect(native.querySelector('.no-data')).toBeNull();
    });
  });
});
