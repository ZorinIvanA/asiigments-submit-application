/**
 * Интеграционные спеки «Сдача работ» студента /my-submissions (батч 3, FR-4.4
 * US-11 + FR-4.10/NFR-4.10, SCR-010): реальные MySubmissionsPage + core-
 * сервисы + мок-слой поверх сида seedFixtures; признак «в группе» — через
 * настоящий кэш профиля AuthService (аменда 3: loadMe, как это делают guards).
 *
 * Сценарии (automation: automated): TS-320 (десктоп-таблица read-only),
 * TS-321 (изоляция чужих сдач), TS-322 (предупреждение без группы дословно),
 * TS-323 (смена семестра перезагружает), TS-324 (нет семестров — getMy не
 * вызывается), TS-325 (без группы и без семестров — только предупреждение),
 * TS-326 (семестр опустел между запросами), TS-327 (смена членства другим
 * сеансом), TS-380 (граница 768px: карточки/таблица, предупреждение в обеих
 * вёрстках — NFR-4.10, MockBreakpointObserver).
 */
import { BreakpointObserver } from '@angular/cdk/layout';
import { ComponentFixture, fakeAsync, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';

import { MockBreakpointObserver } from '../../../../testing/mock-breakpoint-observer';
import { AuthService } from '../../../core/services/auth.service';
import { SubmissionsService } from '../../../core/services/submissions.service';
import { NotificationService } from '../../../shared/notifications/notification-service';
import {
  MySubmissionsPage,
  TEXT_NO_GROUP,
  TEXT_NO_SEMESTERS,
} from '../../../features/my-submissions/pages/my-submissions-page';
import {
  drainNotifications,
  groupIdOf,
  hydrateSeed,
  installMockLayer,
  labIdOf,
  mutateMockDb,
  resetZoneEnvAfterSpec,
  settle,
  switchSession,
  userIdOf,
} from './integration-env';

describe('MySubmissionsPage — интеграция с реальным мок-слоем (батч 3, FR-4.4/FR-4.10)', () => {
  let breakpoints: MockBreakpointObserver;
  let notifications: NotificationService;
  let auth: AuthService;
  let submissions: SubmissionsService;
  let fixture: ComponentFixture<MySubmissionsPage>;

  let teacherId: string;
  let ik221: string;
  let lab1: string;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    breakpoints = new MockBreakpointObserver();
    TestBed.configureTestingModule({
      providers: [
        { provide: BreakpointObserver, useValue: breakpoints },
        provideNoopAnimations(),
      ],
    });
    notifications = TestBed.inject(NotificationService);
    auth = TestBed.inject(AuthService);
    submissions = TestBed.inject(SubmissionsService);
    installMockLayer();

    const db = hydrateSeed();
    teacherId = userIdOf(db, 'teacher');
    ik221 = groupIdOf(db, 'ИК-221');
    lab1 = labIdOf(db, 1, 1);
  });

  afterEach(() => {
    notifications.dismissMobile();
    fixture?.destroy();
    resetZoneEnvAfterSpec();
  });

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  /**
   * Открывает экран под сессией студента, как это делает реальный переход:
   * guard догружает профиль (loadMe → auth.me, один мок-вызов), затем
   * страница — getSemesters + getMy (по 500 мс).
   */
  function openMySubmissions(login: string): void {
    switchSession(userIdOf(hydrateSeed(), login));
    void auth.loadMe();
    fixture = TestBed.createComponent(MySubmissionsPage);
    settle(fixture, 600); // loadMe (500)
    fixture.detectChanges();
    settle(fixture, 1100); // getSemesters (500) + getMy (500)
  }

  /** Нативный select семестра страницы. */
  function semesterSelect(): HTMLSelectElement {
    return root().querySelector('#my-submissions-semester') as HTMLSelectElement;
  }

  /** Ячейка даты строки таблицы: td 0 — ФИО, лаба k: 2k−1 сдача, 2k защита. */
  function cellText(row: number, labNumber: number, field: 'submit' | 'defense'): string {
    const cell = field === 'submit' ? 2 * labNumber - 1 : 2 * labNumber;
    const td = root().querySelectorAll('tbody tr')[row]?.querySelectorAll('td')[cell];
    return td?.textContent?.trim() ?? '';
  }

  it('TS-320: десктоп-таблица своей строки, строго read-only; семестры [1,2], выбран 1; чужих данных нет', fakeAsync(() => {
    const updateSpy = spyOn(submissions, 'update').and.callThrough();
    openMySubmissions('student01');

    // Селектор семестра: только семестры с работами, выбран наименьший.
    const options = Array.from(semesterSelect().options).map((option) => option.text.trim());
    expect(options).toEqual(['1', '2']);
    expect(semesterSelect().value).toBe('1');

    // Ровно одна строка — student01, сид-даты дд.мм.гггг, лаб 3 защита пуста.
    const rows = root().querySelectorAll('tbody tr');
    expect(rows.length).withContext('ровно одна строка').toBe(1);
    expect(cellText(0, 1, 'submit')).toBe('01.09.2026');
    expect(cellText(0, 1, 'defense')).toBe('11.09.2026');
    expect(cellText(0, 2, 'submit')).toBe('02.09.2026');
    expect(cellText(0, 2, 'defense')).toBe('12.09.2026');
    expect(cellText(0, 3, 'submit')).toBe('03.09.2026');
    expect(cellText(0, 3, 'defense')).toBe('');

    // Read-only: календаря в ячейках нет, клик по ячейке не открывает его,
    // вызовов submissions.update нет.
    expect(root().querySelector('p-datepicker')).withContext('нет календарей').toBeNull();
    expect(root().querySelector('input')).withContext('нет input-контролов').toBeNull();
    root().querySelectorAll('tbody td')[1]?.dispatchEvent(new Event('click'));
    flushMicrotasks();
    expect(updateSpy).not.toHaveBeenCalled();

    // Чужих строк и данных нет.
    expect(root().textContent).not.toContain('Иванов Иван Иванович 02');
    expect(root().textContent).not.toContain('student02');
  }));

  it('TS-321: изоляция — студент02 видит только свою лабу 1; сдач student01 нет ни в DOM, ни в ответе getMy', fakeAsync(() => {
    const getMySpy = spyOn(submissions, 'getMy').and.callThrough();
    openMySubmissions('student02');

    // Своя строка: лаб1 01.09.2026/пусто, остальные пары пустые.
    expect(cellText(0, 1, 'submit')).toBe('01.09.2026');
    expect(cellText(0, 1, 'defense')).toBe('');
    expect(cellText(0, 2, 'submit')).toBe('');
    expect(cellText(0, 3, 'submit')).toBe('');

    // Дат student01 в DOM нет.
    expect(root().textContent).not.toContain('11.09.2026');
    expect(root().textContent).not.toContain('12.09.2026');
    expect(root().textContent).not.toContain('02.09.2026');
    expect(root().textContent).not.toContain('03.09.2026');

    // И в ответе getMy — только собственные сдачи (запрошен семестр 1).
    expect(getMySpy.calls.mostRecent().args[0]).toBe(1);
    let response!: Awaited<ReturnType<SubmissionsService['getMy']>>;
    void submissions.getMy(1).then((result) => (response = result));
    settle(fixture, 600);
    expect(response.submissions.length).toBe(1);
    expect(response.submissions[0]?.labId).toBe(lab1);
  }));

  it('TS-322: студент без группы — дословное предупреждение вместо таблицы, селектор семестра активен, данных ведомости не отдаётся', fakeAsync(() => {
    const getMySpy = spyOn(submissions, 'getMy').and.callThrough();
    openMySubmissions('student31');

    const warning = root().querySelector('.no-group');
    expect(warning?.getAttribute('role')).toBe('status');
    expect(warning?.textContent?.trim()).toContain(TEXT_NO_GROUP);
    expect(root().querySelector('p-table')).withContext('таблицы нет').toBeNull();

    // Селектор семестра остаётся активным (работы в системе есть).
    expect(semesterSelect().disabled).toBeFalse();

    // Никаких данных ведомости не отдаётся (контракт getMy, IF-106).
    expect(getMySpy).toHaveBeenCalledTimes(1);
    let response!: Awaited<ReturnType<SubmissionsService['getMy']>>;
    void submissions.getMy(1).then((result) => (response = result));
    settle(fixture, 600);
    expect(response).toEqual({ hasGroup: false, labs: [], submissions: [] });
  }));

  it('TS-323: смена семестра перезагружает таблицу: семестр 2 — лабы 1..3 без дат, возврат на 1 восстанавливает сид-даты', fakeAsync(() => {
    openMySubmissions('student01');

    semesterSelect().value = '2';
    semesterSelect().dispatchEvent(new Event('change'));
    settle(fixture, 600);

    // Семестр 2: те же номера работ 1..3, все пары пустые (сид-сдач нет).
    expect(cellText(0, 1, 'submit')).toBe('');
    expect(cellText(0, 2, 'submit')).toBe('');
    expect(cellText(0, 3, 'submit')).toBe('');
    expect(cellText(0, 1, 'defense')).toBe('');

    semesterSelect().value = '1';
    semesterSelect().dispatchEvent(new Event('change'));
    settle(fixture, 600);

    expect(cellText(0, 1, 'submit')).toBe('01.09.2026');
    expect(cellText(0, 2, 'submit')).toBe('02.09.2026');
    expect(cellText(0, 3, 'submit')).toBe('03.09.2026');
  }));

  it('TS-324: студент в группе, работы удалены — getMy не вызван ни разу, селектор пуст/disabled, «Нет семестров с работами», без баннеров (аменда 3)', fakeAsync(() => {
    const getMySpy = spyOn(submissions, 'getMy').and.callThrough();
    mutateMockDb((data) => {
      data.labs = [];
      data.submissions = [];
    });

    openMySubmissions('student01');

    expect(getMySpy).withContext('вызов getMy с семестром вне 1..10 запрещён').not.toHaveBeenCalled();
    expect(semesterSelect().disabled).withContext('селектор пуст/disabled').toBeTrue();
    expect(root().textContent).toContain(TEXT_NO_SEMESTERS);
    expect(notifications.desktopMessage()).withContext('ошибочных баннеров нет').toBeNull();
    expect(notifications.mobileMessage()).toBeNull();
  }));

  it('TS-325: без группы И без семестров — только предупреждение, «Нет семестров с работами» не дублируется (аменда 3)', fakeAsync(() => {
    mutateMockDb((data) => {
      data.labs = [];
      data.submissions = [];
    });

    openMySubmissions('student31');

    expect(root().querySelector('.no-group')?.textContent).toContain(TEXT_NO_GROUP);
    expect(root().textContent).not.toContain(TEXT_NO_SEMESTERS);
  }));

  it('TS-326: выбранный семестр опустел во время сессии — «Нет семестров с работами» в обеих вёрстках, селектор активен, семестр 1 перезагружается с данными', fakeAsync(() => {
    openMySubmissions('student01');

    // Каскадное удаление работ семестра 2 между запросами (другой сеанс).
    mutateMockDb((data) => {
      data.labs = data.labs.filter((lab) => lab.semester !== 2);
      data.submissions = data.submissions.filter(
        (candidate) => data.labs.some((lab) => lab.id === candidate.labId),
      );
    });

    semesterSelect().value = '2';
    semesterSelect().dispatchEvent(new Event('change'));
    settle(fixture, 600);

    expect(root().textContent).withContext('текст пустого состояния').toContain(TEXT_NO_SEMESTERS);
    expect(semesterSelect().disabled).withContext('селектор активен').toBeFalse();
    expect(root().querySelector('p-table')).withContext('вместо таблицы — текст').toBeNull();
    expect(notifications.desktopMessage()).withContext('ошибочного баннера нет').toBeNull();
    drainNotifications();

    // Переключение на семестр 1 перезагружает экран с данными.
    semesterSelect().value = '1';
    semesterSelect().dispatchEvent(new Event('change'));
    settle(fixture, 600);
    expect(cellText(0, 1, 'submit')).toBe('01.09.2026');
  }));

  it('TS-327: исключение другим сеансом подхватывается после обновления профиля; в ведомости ИК-221 студента больше нет (total 24)', fakeAsync(() => {
    openMySubmissions('student02');
    expect(root().querySelector('p-table')).withContext('до исключения — таблица').not.toBeNull();

    // Другой сеанс исключает студента из группы прямой правкой mock.db.v1.
    mutateMockDb((data) => {
      const student = data.users.find((user) => user.login === 'student02');
      if (student !== undefined) {
        student.groupId = null;
      }
    });

    // До перезагрузки профиля — состояние по кэшу (аменда 3).
    fixture.detectChanges();
    expect(root().querySelector('.no-group')).withContext('кэш профиля: без изменений').toBeNull();
    expect(root().querySelector('p-table')).not.toBeNull();

    // Перезагрузка приложения: свежий auth.me обновляет кэш профиля.
    void auth.loadMe();
    settle(fixture, 600);
    fixture.detectChanges();

    expect(root().querySelector('.no-group')?.textContent).toContain(TEXT_NO_GROUP);
    expect(root().querySelector('p-table')).withContext('предупреждение вместо таблицы').toBeNull();

    // Ведомость ИК-221: total 24, студента 02 нет ни на одной странице.
    switchSession(teacherId);
    const seen: string[] = [];
    for (let page = 1; page <= 5; page++) {
      let grid!: Awaited<ReturnType<SubmissionsService['getGrid']>>;
      void submissions.getGrid({ groupId: ik221, semester: 1, page }).then((result) => (grid = result));
      settle(fixture, 600);
      expect(grid.total).toBe(24);
      seen.push(...grid.students.map((student) => student.fullName));
    }
    expect(seen).not.toContain('Иванов Иван Иванович 02');
    expect(seen).toContain('Иванов Иван Иванович 01');
  }));

  it('TS-380 (NFR-4.10): 767px — карточки «Лаб N» без таблицы; 768px — десктопная таблица; предупреждение student31 корректно в обеих вёрстках', fakeAsync(() => {
    // Мобильная ширина (767px) до создания страницы.
    breakpoints.simulate(true);
    openMySubmissions('student01');

    // Карточки «Лаб N» со строками «Сдача»/«Защита», даты дд.мм.гггг.
    // Семестр 1 = 20 работ сида, поэтому 20 карточек (CR-001); даты — только
    // у лаб 1–3 (сид-сдачи), у остальных пары пустые.
    const cards = root().querySelectorAll('.card');
    expect(cards.length).withContext('карточка на каждую работу семестра').toBe(20);
    const cardTitle = (index: number): string =>
      cards[index]?.querySelector('.card__title')?.textContent?.trim() ?? '';
    expect(cardTitle(0)).toBe('Лаб 1');
    expect(cardTitle(1)).toBe('Лаб 2');
    expect(cardTitle(2)).toBe('Лаб 3');
    expect(cardTitle(19)).toBe('Лаб 20');
    const cardValue = (card: number, row: number): string =>
      cards[card]?.querySelectorAll('.card__row')[row]?.querySelector('.card__value')?.textContent?.trim() ?? '';
    expect(cardValue(0, 0)).toBe('01.09.2026');
    expect(cardValue(0, 1)).toBe('11.09.2026');
    expect(cardValue(1, 0)).toBe('02.09.2026');
    expect(cardValue(1, 1)).toBe('12.09.2026');
    expect(cardValue(2, 0)).toBe('03.09.2026');
    expect(cardValue(2, 1)).withContext('пустое значение пусто, без прочерка').toBe('');
    for (let index = 3; index < 20; index++) {
      expect(cardValue(index, 0)).withContext(`Лаб ${index + 1}: сдача пуста`).toBe('');
      expect(cardValue(index, 1)).withContext(`Лаб ${index + 1}: защита пуста`).toBe('');
    }
    expect(root().querySelector('p-table')).withContext('таблицы нет').toBeNull();

    // 768px — десктопная таблица одной строкой.
    breakpoints.simulate(false);
    fixture.detectChanges();
    expect(root().querySelector('p-table')).not.toBeNull();
    expect(root().querySelectorAll('tbody tr').length).toBe(1);
    expect(cellText(0, 1, 'submit')).toBe('01.09.2026');
    expect(root().querySelectorAll('.card').length).withContext('карточек нет').toBe(0);

    // student31: предупреждение корректно в обеих вёрстках.
    fixture.destroy();
    breakpoints.simulate(true);
    openMySubmissions('student31');
    expect(root().querySelector('.no-group')?.textContent).toContain(TEXT_NO_GROUP);
    expect(root().querySelector('.card')).toBeNull();

    breakpoints.simulate(false);
    fixture.detectChanges();
    expect(root().querySelector('.no-group')?.textContent).toContain(TEXT_NO_GROUP);
    expect(root().querySelector('p-table')).withContext('таблицы у студента без группы нет').toBeNull();
  }));

  /** Прогон микрозадач без CD (вспомогательный для кликов вне потока данных). */
  function flushMicrotasks(): void {
    settle(fixture, 0);
  }
});
