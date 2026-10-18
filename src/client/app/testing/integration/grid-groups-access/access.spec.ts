/**
 * Интеграционные спеки раздела «Доступ» /access и шва «Доступ → Ведомость»
 * (батч 3, FR-4.6, SCR-013): реальные AccessPage/SubmissionsPage + core-
 * сервисы на реальном HTTP-ядре (HttpClient + authInterceptor) поверх
 * программируемого HttpTestingController-бэкенда GridBackendStub с сидом
 * зоны; серверная сессия teacher — serverSession/loginAs (признак сессии —
 * память AuthService, FR-092).
 *
 * Сценарии (automation: automated): TS-313 (шов: студент без группы невидим,
 * включение добавляет в ведомость), TS-350 (таблица, порядок, пагинация 10),
 * TS-351 (ci-поиск по ФИО/логину/email), TS-352 (maxlength 200 и пустой
 * результат), TS-353 (фильтр «Без группы» и комбинация с поиском), TS-354
 * (включение немедленно, без диалога и success-тостов — аменда 6), TS-355
 * (перевод в другую группу), TS-356 (исключение Yes/No с откатом селектора),
 * TS-357 (согласованность groupId из двух источников — аменда 6), TS-358
 * (дебаунс 300 мс и гонка запросов), TS-359 (отказ setGroup: баннер и
 * перезагрузка строки).
 */
import { ComponentFixture, fakeAsync, tick } from '@angular/core/testing';

import { AuthService } from '../../../core/services/auth.service';
import { GroupsService } from '../../../core/services/groups.service';
import { StudentsService } from '../../../core/services/students.service';
import { SubmissionsService } from '../../../core/services/submissions.service';
import { NotificationService } from '../../../shared/notifications/notification-service';
import { AccessPage } from '../../../features/access/pages/access-page/access-page';
import { SubmissionsPage } from '../../../features/submissions/pages/submissions-page';
import { MySubmissionsPage, TEXT_NO_GROUP } from '../../../features/my-submissions/pages/my-submissions-page';
import {
  GridAccessEnv,
  openSelect,
  pickOption,
  qsAllIn,
  qsIn,
  selectLabelOf,
  selectModelValueOf,
} from './integration-env';

describe('AccessPage — интеграция с реальным HTTP-ядром (батч 3, FR-4.6)', () => {
  let env: GridAccessEnv;
  let notifications: NotificationService;
  let groupsService: GroupsService;
  let studentsService: StudentsService;
  let submissionsService: SubmissionsService;
  let auth: AuthService;
  let fixture: ComponentFixture<AccessPage | SubmissionsPage | MySubmissionsPage>;

  let student01: string;
  let student26: string;
  let student31: string;
  let ik221: string;
  let ik222: string;

  beforeEach(() => {
    env = GridAccessEnv.setup();
    notifications = env.notifications;
    groupsService = env.inject(GroupsService);
    studentsService = env.inject(StudentsService);
    submissionsService = env.inject(SubmissionsService);
    auth = env.auth;
    student01 = env.backend.userIdByLogin('student01');
    student26 = env.backend.userIdByLogin('student26');
    student31 = env.backend.userIdByLogin('student31');
    ik221 = env.backend.groupIdByName('ИК-221');
    ik222 = env.backend.groupIdByName('ИК-222');
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

  /** Открытие /access: справочник групп (волна 1) → первая страница списка (волна 2). */
  function createAccessPage(): void {
    fixture = env.mount(AccessPage);
    env.settle(fixture, 2);
  }

  /** Ведомость: селекторы (волна 1) + грид (волна 2). */
  function createSubmissionsPage(): void {
    fixture = env.mount(SubmissionsPage);
    env.settle(fixture, 2);
  }

  function tableRowFullNames(): string[] {
    return Array.from(root().querySelectorAll('tbody tr td:first-child')).map(
      (cell) => cell.textContent?.trim() ?? '',
    );
  }

  function caption(): string {
    return qsIn(fixture, '.access__caption')?.textContent?.trim() ?? '';
  }

  /** Клик по кнопке пагинации страницы («‹», номер, «›»). */
  function clickPageButton(label: string): void {
    const button = qsAllIn(fixture, '.access__page').find(
      (candidate) => candidate.textContent?.trim() === label,
    ) as HTMLButtonElement | undefined;
    if (button === undefined) {
      throw new Error(`access.spec: нет кнопки страницы ${label}`);
    }
    button.click();
    fixture.detectChanges();
  }

  function typeSearch(value: string): void {
    const input = root().querySelector<HTMLInputElement>('input[type="search"]')!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function clickNoGroupFilter(): void {
    root().querySelector<HTMLInputElement>('input[type="checkbox"]')!.click();
    fixture.detectChanges();
  }

  function rowSelect(index: number): HTMLElement {
    const row = qsAllIn(fixture, 'tbody tr')[index]!;
    return row.querySelector('p-select')!;
  }

  function rowSelectLabel(index: number): string {
    return selectLabelOf(rowSelect(index));
  }

  function rowModelValue(index: number): unknown {
    const row = qsAllIn(fixture, 'tbody tr')[index]!;
    return selectModelValueOf(fixture as ComponentFixture<AccessPage>, row as HTMLTableRowElement);
  }

  function rowConfirmButton(kind: 'accept' | 'reject'): HTMLButtonElement {
    const found = qsIn(
      fixture,
      `button.p-confirmdialog-${kind}-button`,
    ) as HTMLButtonElement | null;
    if (found === null) {
      throw new Error(`access.spec: нет кнопки подтверждения ${kind}`);
    }
    return found;
  }

  it('TS-313: шов «Доступ → Ведомость» — student31 без группы невидим в гридах; включение в ИК-222 добавляет его в ведомость с пустыми датами', fakeAsync(() => {
    // До включения: ИК-222 — 5 студентов, student31 не попадает ни в один грид.
    let grid222!: Awaited<ReturnType<SubmissionsService['getGrid']>>;
    void submissionsService.getGrid({ groupId: ik222, semester: 1, page: 1 }).then((result) => (grid222 = result));
    env.settleRequests(1);
    expect(grid222.total).toBe(5);
    expect(grid222.students.some((student) => student.fullName === 'Иванов Иван Иванович 31')).toBeFalse();

    // Включение student31 в ИК-222 селектором строки /access (страница 4).
    createAccessPage();
    clickPageButton('4');
    env.settle(fixture, 1);
    expect(tableRowFullNames()).toEqual(['Иванов Иван Иванович 31', 'Иванов Иван Иванович 32']);
    openSelect(fixture, rowSelect(0));
    pickOption(fixture, 'ИК-222');
    env.settle(fixture, 2); // setGroup + перезагрузка страницы
    fixture.destroy();

    // Ведомость ИК-222: total 6, student31 в гриде с пустыми датами.
    createSubmissionsPage();
    const selects = Array.from(root().querySelectorAll('p-select'));
    openSelect(fixture, selects[0]!);
    pickOption(fixture, 'ИК-222');
    env.settle(fixture, 1);
    expect(qsIn(fixture, '[data-test="range-caption"]')?.textContent?.trim()).toBe(
      'Показать записи с 1 по 5 из 6',
    );
    clickPageButtonSubmissions('2');
    env.settle(fixture, 1);
    expect(tableRowFullNames()).toEqual(['Иванов Иван Иванович 31']);
    const submitInput = root().querySelector('tbody tr td:nth-child(2) input') as HTMLInputElement;
    expect(submitInput.value).withContext('записей сдач у него нет — ячейка пуста').toBe('');

    // ИК-221 остаётся 25; записей сдач student31 в состоянии бэкенда нет.
    let grid221!: Awaited<ReturnType<SubmissionsService['getGrid']>>;
    void submissionsService.getGrid({ groupId: ik221, semester: 1, page: 1 }).then((result) => (grid221 = result));
    env.settleRequests(1);
    expect(grid221.total).toBe(25);
    expect(
      env.backend.read().submissions.some((candidate) => candidate.studentId === student31),
    ).withContext('записи сдач отсутствуют до проставления дат').toBeFalse();
  }));

  /** Кнопка страницы ведущего p-paginator ведомости (стр. 2). */
  function clickPageButtonSubmissions(label: string): void {
    const button = qsAllIn(fixture, '.p-paginator-page').find(
      (candidate) => candidate.textContent?.trim() === label,
    ) as HTMLButtonElement | undefined;
    button!.click();
    fixture.detectChanges();
  }

  it('TS-350: таблица студентов — колонки, порядок ФИО↑, пагинация 10 (стр.4 — 2 записи), подпись «с 31 по 32 из 32», значения колонки «Группа»', fakeAsync(() => {
    createAccessPage();

    const headers = Array.from(root().querySelectorAll('thead th')).map((th) => th.textContent?.trim());
    expect(headers).toEqual(['ФИО', 'Логин', 'Email', 'Группа']);

    const expected = Array.from({ length: 32 }, (_, index) =>
      `Иванов Иван Иванович ${String(index + 1).padStart(2, '0')}`,
    );
    expect(tableRowFullNames()).toEqual(expected.slice(0, 10));
    expect(caption()).toBe('Показать записи с 1 по 10 из 32');

    clickPageButton('2');
    env.settle(fixture, 1);
    expect(tableRowFullNames()).toEqual(expected.slice(10, 20));

    clickPageButton('3');
    env.settle(fixture, 1);
    expect(tableRowFullNames()).toEqual(expected.slice(20, 30));
    // Граница групп на странице 3: 21–25 ИК-221, 26–30 ИК-222.
    expect(rowSelectLabel(0)).toBe('ИК-221');
    expect(rowSelectLabel(5)).toBe('ИК-222');

    clickPageButton('4');
    env.settle(fixture, 1);
    expect(tableRowFullNames()).toEqual(expected.slice(30, 32));
    expect(rowSelectLabel(0)).withContext('student31 — «Без группы»').toBe('Без группы');
    expect(rowSelectLabel(1)).toBe('Без группы');
    expect(caption()).toBe('Показать записи с 31 по 32 из 32');
  }));

  it('TS-351: поиск без учёта регистра — «STUDENT31» и «иванов иванович 07» по одной записи, «иванов 05» — 0; после каждого поиска page=1, порядок не меняется', fakeAsync(() => {
    const getListSpy = spyOn(studentsService, 'getList').and.callThrough();
    createAccessPage();

    typeSearch('STUDENT31');
    env.settle(fixture, 1); // дебаунс 300 + ответ бэкенда
    expect(getListSpy.calls.mostRecent().args[0]).toEqual({ search: 'STUDENT31', groupId: null, page: 1 });
    expect(tableRowFullNames()).toEqual(['Иванов Иван Иванович 31']);
    expect(caption()).toBe('Показать записи с 1 по 1 из 1');

    typeSearch('иванов иванович 07');
    env.settle(fixture, 1);
    expect(getListSpy.calls.mostRecent().args[0]).toEqual({
      search: 'иванов иванович 07',
      groupId: null,
      page: 1,
    });
    expect(tableRowFullNames()).toEqual(['Иванов Иван Иванович 07']);

    typeSearch('иванов student1');
    env.settle(fixture, 1);
    expect(getListSpy.calls.mostRecent().args[0]).toEqual({
      search: 'иванов student1',
      groupId: null,
      page: 1,
    });
    // per-field AND: нет записи, у которой ОДНО поле содержит оба токена.
    expect(tableRowFullNames()).toEqual(['Записей нет']);
    expect(caption()).withContext('аменда 7').toBe('Показать записи с 1 по 0 из 0');
  }));

  it('TS-352: maxlength 200 — строка 250 символов обрезана, запрос с 200-символьной строкой без совпадений даёт пустое состояние и подпись «с 1 по 0 из 0»', fakeAsync(() => {
    const getListSpy = spyOn(studentsService, 'getList').and.callThrough();
    createAccessPage();

    const input = root().querySelector<HTMLInputElement>('input[type="search"]')!;
    expect(input.maxLength).withContext('maxlength=200 в поле').toBe(200);

    // Ввод «пользователем»: DOM обрезает значение до maxlength.
    const pasted = 'я'.repeat(250);
    input.value = pasted.slice(0, input.maxLength);
    input.dispatchEvent(new Event('input'));
    env.settle(fixture, 1);
    expect(input.value.length).withContext('поле обрезано до 200').toBe(200);
    expect(getListSpy.calls.mostRecent().args[0]).toEqual({
      search: pasted.slice(0, 200),
      groupId: null,
      page: 1,
    });
    expect(tableRowFullNames()).toEqual(['Записей нет']);
    expect(caption()).toBe('Показать записи с 1 по 0 из 0');
  }));

  it('TS-353: фильтр «Без группы» — только 31/32; с поиском «student3» — пересечение; снятие возвращает полный список; каждая смена сбрасывает на page=1', fakeAsync(() => {
    const getListSpy = spyOn(studentsService, 'getList').and.callThrough();
    createAccessPage();

    // Уводим список со страницы 1, чтобы проверить сброс.
    clickPageButton('2');
    env.settle(fixture, 1);

    clickNoGroupFilter();
    env.settle(fixture, 1);
    expect(getListSpy.calls.mostRecent().args[0]).toEqual({ search: '', groupId: 'none', page: 1 });
    expect(tableRowFullNames()).toEqual(['Иванов Иван Иванович 31', 'Иванов Иван Иванович 32']);

    typeSearch('student3');
    env.settle(fixture, 1);
    expect(getListSpy.calls.mostRecent().args[0]).toEqual({
      search: 'student3',
      groupId: 'none',
      page: 1,
    });
    expect(tableRowFullNames()).toEqual(['Иванов Иван Иванович 31', 'Иванов Иван Иванович 32']);

    clickNoGroupFilter();
    env.settle(fixture, 1);
    expect(getListSpy.calls.mostRecent().args[0]).toEqual({
      search: 'student3',
      groupId: null,
      page: 1,
    });
    // Снятие фильтра — полный список (буквально сценарию): очищаем поиск
    // кнопкой очистки (с сохранённым «student3» остались бы только 30–32,
    // CR-008) и проверяем страницу 1 полного списка.
    root().querySelector<HTMLButtonElement>('button[aria-label="Очистить поиск"]')!.click();
    env.settle(fixture, 1); // дебаунс 300 + ответ бэкенда
    expect(getListSpy.calls.mostRecent().args[0]).toEqual({
      search: '',
      groupId: null,
      page: 1,
    });
    expect(tableRowFullNames().length).withContext('страница 1 полного списка').toBe(10);
    expect(caption()).toBe('Показать записи с 1 по 10 из 32');
  }));

  it('TS-354: включение в группу — немедленный setGroup без диалога, блокировка селектора и спиннер; после — «ИК-221» в строке, без success-уведомлений, счётчик 26, студент в ведомости и с таблицей /my-submissions', fakeAsync(() => {
    const setGroupSpy = spyOn(studentsService, 'setGroup').and.callThrough();
    createAccessPage();
    clickPageButton('4');
    env.settle(fixture, 1);

    openSelect(fixture, rowSelect(0)); // student31, «Без группы»
    pickOption(fixture, 'ИК-221');

    // Немедленно: без диалога, селектор заблокирован, спиннер в подписи.
    expect(setGroupSpy).toHaveBeenCalledWith(student31, ik221);
    expect(qsIn(fixture, '.p-confirmdialog')).withContext('без диалога').toBeNull();
    expect(rowSelect(0).classList).toContain('p-disabled');
    expect(qsIn(fixture, '.access__spinner')).withContext('спиннер в подписи').not.toBeNull();

    env.settle(fixture, 2); // setGroup + перезагрузка страницы списка

    expect(rowSelect(0).classList).not.toContain('p-disabled');
    expect(qsIn(fixture, '.access__spinner')).toBeNull();
    expect(rowSelectLabel(0)).toBe('ИК-221');

    // Аменда 6: успехи setGroup — БЕЗ уведомлений, фидбек — обновлённая строка.
    expect(notifications.desktopMessage()).toBeNull();
    expect(notifications.mobileMessage()).toBeNull();

    // Счётчик ИК-221 = 26; студент появился в ведомости ИК-221.
    let list!: Awaited<ReturnType<GroupsService['getList']>>;
    void groupsService.getList().then((result) => (list = result));
    env.settleRequests(1);
    expect(list.find((group) => group.id === ik221)?.studentCount).toBe(26);

    let grid!: Awaited<ReturnType<SubmissionsService['getGrid']>>;
    void submissionsService.getGrid({ groupId: ik221, semester: 1, page: 6 }).then((result) => (grid = result));
    env.settleRequests(1);
    expect(grid.students.map((student) => student.fullName)).toEqual(['Иванов Иван Иванович 31']);

    // Его /my-submissions — таблица вместо предупреждения.
    fixture.destroy();
    env.loginAs('student31');
    fixture = env.mount(MySubmissionsPage);
    env.settle(fixture, 2);
    const myRoot = fixture.nativeElement as HTMLElement;
    expect(myRoot.querySelector('.no-group')).withContext('предупреждения нет').toBeNull();
    expect(myRoot.querySelector('p-table')).withContext('таблица вместо предупреждения').not.toBeNull();
  }));

  it('TS-355: перевод студента 26 из ИК-222 в ИК-221 — счётчики 26/4, студент в ведомости ИК-221 и отсутствует в ИК-222, составы групп согласованы', fakeAsync(() => {
    const setGroupSpy = spyOn(studentsService, 'setGroup').and.callThrough();
    createAccessPage();
    clickPageButton('3');
    env.settle(fixture, 1);
    expect(tableRowFullNames()[5]).withContext('student26 на странице 3').toBe('Иванов Иван Иванович 26');

    openSelect(fixture, rowSelect(5));
    pickOption(fixture, 'ИК-221');
    expect(setGroupSpy).toHaveBeenCalledWith(student26, ik221);
    env.settle(fixture, 2);
    expect(rowSelectLabel(5)).toBe('ИК-221');

    // Счётчики пересчитаны.
    let list!: Awaited<ReturnType<GroupsService['getList']>>;
    void groupsService.getList().then((result) => (list = result));
    env.settleRequests(1);
    expect(list.find((group) => group.id === ik221)?.studentCount).toBe(26);
    expect(list.find((group) => group.id === ik222)?.studentCount).toBe(4);

    // Ведомости: студент в ИК-221 и отсутствует в ИК-222.
    let grid221!: Awaited<ReturnType<SubmissionsService['getGrid']>>;
    void submissionsService.getGrid({ groupId: ik221, semester: 1, page: 6 }).then((result) => (grid221 = result));
    env.settleRequests(1);
    expect(grid221.total).toBe(26);
    expect(grid221.students.map((student) => student.fullName)).toContain('Иванов Иван Иванович 26');

    let grid222!: Awaited<ReturnType<SubmissionsService['getGrid']>>;
    void submissionsService.getGrid({ groupId: ik222, semester: 1, page: 1 }).then((result) => (grid222 = result));
    env.settleRequests(1);
    expect(grid222.total).toBe(4);
    expect(grid222.students.map((student) => student.fullName)).not.toContain('Иванов Иван Иванович 26');

    // Составы /groups/:id согласованы с ведомостью.
    let members221!: Awaited<ReturnType<GroupsService['getStudents']>>;
    void groupsService.getStudents(ik221, 3).then((result) => (members221 = result));
    env.settleRequests(1);
    expect(members221.items.some((student) => student.login === 'student26')).toBeTrue();

    let members222!: Awaited<ReturnType<GroupsService['getStudents']>>;
    void groupsService.getStudents(ik222, 1).then((result) => (members222 = result));
    env.settleRequests(1);
    expect(members222.items.some((student) => student.login === 'student26')).toBeFalse();
  }));

  it('TS-356: исключение — диалог «Исключить студента … из группы?»; «No» — селектор вернулся, setGroup не вызывался; «Yes» — «Без группы», из ведомости ИК-221 исчез (total 24), предупреждение в /my-submissions', fakeAsync(() => {
    const setGroupSpy = spyOn(studentsService, 'setGroup').and.callThrough();
    createAccessPage();

    openSelect(fixture, rowSelect(0)); // student01, ИК-221
    pickOption(fixture, 'Без группы');
    expect(qsIn(fixture, '.p-confirmdialog')?.textContent).toContain(
      'Исключить студента Иванов Иван Иванович 01 из группы?',
    );
    expect(setGroupSpy).not.toHaveBeenCalled();

    rowConfirmButton('reject').click();
    env.waitConfirmClosed(fixture);
    env.flushNgModel(fixture);
    expect(qsIn(fixture, '.p-confirmdialog')).withContext('диалог закрыт').toBeNull();
    expect(rowSelectLabel(0)).withContext('селектор вернулся на «ИК-221»').toBe('ИК-221');
    expect(setGroupSpy).not.toHaveBeenCalled();

    // Повторно → «Yes».
    openSelect(fixture, rowSelect(0));
    pickOption(fixture, 'Без группы');
    rowConfirmButton('accept').click();
    expect(setGroupSpy).toHaveBeenCalledWith(student01, null);
    env.settle(fixture, 2);
    expect(rowSelectLabel(0)).toBe('Без группы');

    // Студент исчез из ведомости ИК-221 (total 24).
    let grid!: Awaited<ReturnType<SubmissionsService['getGrid']>>;
    void submissionsService.getGrid({ groupId: ik221, semester: 1, page: 1 }).then((result) => (grid = result));
    env.settleRequests(1);
    expect(grid.total).toBe(24);
    expect(grid.students.map((student) => student.fullName)).not.toContain('Иванов Иван Иванович 01');

    // Его /my-submissions — предупреждение.
    fixture.destroy();
    env.loginAs('student01');
    fixture = env.mount(MySubmissionsPage);
    env.settle(fixture, 2);
    expect((fixture.nativeElement as HTMLElement).querySelector('.no-group')?.textContent).toContain(
      TEXT_NO_GROUP,
    );
  }));

  it('TS-357: согласованность groupId из students.getList и groups.getStudents; селекторы используют groupId как значение опций (аменда 6)', fakeAsync(() => {
    createAccessPage();
    clickPageButton('3');
    env.settle(fixture, 1);

    // Перевод: student26 → ИК-221.
    openSelect(fixture, rowSelect(5));
    pickOption(fixture, 'ИК-221');
    env.settle(fixture, 2);

    // Исключение: student01 → «Без группы» (Yes).
    clickPageButton('1');
    env.settle(fixture, 1);
    openSelect(fixture, rowSelect(0));
    pickOption(fixture, 'Без группы');
    rowConfirmButton('accept').click();
    env.settle(fixture, 2);

    // Источник 1: students.getList того же студента.
    let fromList!: Awaited<ReturnType<StudentsService['getList']>>;
    void studentsService.getList({ search: 'student26', page: 1 }).then((result) => (fromList = result));
    env.settleRequests(1);
    const transferred = fromList.items[0]!;
    expect(transferred.groupId).toBe(ik221);
    expect(transferred.groupName).toBe('ИК-221');

    // Источник 2: groups.getStudents той же группы — groupId совпадает с id.
    let fromGroup!: Awaited<ReturnType<GroupsService['getStudents']>>;
    void groupsService.getStudents(ik221, 3).then((result) => (fromGroup = result));
    env.settleRequests(1);
    const inGroup = fromGroup.items.find((student) => student.login === 'student26')!;
    expect(inGroup.groupId).toBe(ik221);
    expect(inGroup.groupId).toBe(transferred.groupId);
    expect(inGroup.groupName).toBe(transferred.groupName);
    for (const item of fromGroup.items) {
      expect(item.groupId).withContext('в составе группы groupId = id группы').toBe(ik221);
    }

    // Исключённый: null в обоих источниках (null = без группы).
    let excluded!: Awaited<ReturnType<StudentsService['getList']>>;
    void studentsService.getList({ search: 'student01', page: 1 }).then((result) => (excluded = result));
    env.settleRequests(1);
    expect(excluded.items[0]!.groupId).toBeNull();
    expect(excluded.items[0]!.groupName).toBeNull();
    let group221Page1!: Awaited<ReturnType<GroupsService['getStudents']>>;
    void groupsService.getStudents(ik221, 1).then((result) => (group221Page1 = result));
    env.settleRequests(1);
    expect(group221Page1.items.some((student) => student.login === 'student01')).toBeFalse();

    // Селекторы /access используют groupId как значение опций (не имя).
    typeSearch('student26');
    env.settle(fixture, 1);
    expect(rowModelValue(0)).withContext('значение селектора — uuid группы').toBe(ik221);
    typeSearch('student01');
    env.settle(fixture, 1);
    expect(rowModelValue(0)).withContext('без группы — null из DTO').toBeNull();
  }));

  it('TS-358: дебаунс поиска 300 мс — один отложенный запрос с финальной строкой; быстрые смены фильтра — отрисован ответ последнего действия', fakeAsync(() => {
    const getListSpy = spyOn(studentsService, 'getList').and.callThrough();
    createAccessPage();
    expect(getListSpy.calls.count()).withContext('первичная загрузка').toBe(1);

    // Посимвольный ввод «student0» — быстро, меньше дебаунса.
    typeSearch('s');
    tick(100);
    typeSearch('st');
    tick(100);
    typeSearch('stu');
    tick(100);
    typeSearch('student0');
    expect(getListSpy.calls.count()).withContext('запроса ещё нет').toBe(1);
    env.settle(fixture, 1); // 300 мс дебаунса + ответ бэкенда

    expect(getListSpy.calls.count()).withContext('ровно один отложенный запрос').toBe(2);
    expect(getListSpy.calls.mostRecent().args[0]).toEqual({
      search: 'student0',
      groupId: null,
      page: 1,
    });

    // Быстро дважды переключить фильтр «Без группы»: оба запроса уходят
    // немедленно, отрисовывается ответ последнего (включённый фильтр).
    clickNoGroupFilter();
    clickNoGroupFilter();
    clickNoGroupFilter();
    env.settle(fixture, 1);
    expect(getListSpy.calls.mostRecent().args[0]).toEqual({
      search: 'student0',
      groupId: 'none',
      page: 1,
    });
    // «student0» не матчится с student31/32 — пустое состояние, подпись согласована.
    expect(tableRowFullNames()).toEqual(['Записей нет']);
    expect(caption()).toBe('Показать записи с 1 по 0 из 0');
  }));

  it('TS-359: отказ setGroup по удалённой группе — ApiError 404 «Группа не найдена» баннером якорем header, страница перезагружена, строка показывает фактическое состояние, блокировка снята', fakeAsync(() => {
    const setGroupSpy = spyOn(studentsService, 'setGroup').and.callThrough();
    const getListSpy = spyOn(studentsService, 'getList').and.callThrough();
    createAccessPage();

    // Другой сеанс удаляет ИК-222 после загрузки справочника: в селекторах
    // остаётся устаревший id; члены группы теряют её (семантика groups.remove).
    env.backend.removeGroupDirect(ik222);

    // student31 (страница 4, «Без группы») выбирает устаревшую «ИК-222».
    clickPageButton('4');
    env.settle(fixture, 1);
    openSelect(fixture, rowSelect(0));
    pickOption(fixture, 'ИК-222');
    expect(setGroupSpy).toHaveBeenCalledWith(student31, ik222);

    env.settle(fixture, 1); // отказ setGroup (404 от бэкенда по чужому id)
    expect(notifications.desktopMessage()).withContext('баннер якорем header').toEqual({
      severity: 'error',
      text: 'Группа не найдена',
    });
    env.drainNotifications();

    env.settle(fixture, 1); // перезагрузка страницы списка
    expect(getListSpy.calls.count()).withContext('страница списка перезагружена').toBeGreaterThanOrEqual(2);
    expect(rowSelect(0).classList).not.toContain('p-disabled');
    expect(rowSelectLabel(0)).withContext('строка показывает фактическое состояние').toBe('Без группы');
  }));
});
