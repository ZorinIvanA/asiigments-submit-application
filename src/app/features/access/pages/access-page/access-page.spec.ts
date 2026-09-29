/**
 * Юнит-тесты AccessPage (C-115, SCR-013, FR-4.6, IF-105/IF-104/IF-109):
 * начальная загрузка с пагинацией и подписью (в том числе при total=0 —
 * аменда 7), дебаунс поиска 300 мс со сбросом на страницу 1, maxlength=200
 * поля поиска, фильтр «Без группы», немедленное включение/перевод с
 * блокировкой селектора строки и спиннером, подтверждение исключения
 * «Yes»/«No» с откатом селектора, пустое состояние, откат опустевшей
 * страницы (CR-001), значение селектора из student.groupId и деградация
 * при ошибке справочника групп (аменда 6), отсутствие success-тостов,
 * обработка ошибки setGroup (notifyError + перезагрузка страницы списка).
 *
 * Мок-слой реальный: обработчики доменов Students/Groups регистрируются на
 * экземпляре MockApiClient из TestBed (так же, как в spec сервисов core);
 * сессия преподавателя — SessionStore; задержка мок-вызова гасится
 * fakeAsync + tick. PrimeNG-оверлеи (p-select, p-confirmdialog) управляются
 * DOM-событиями при provideNoopAnimations; уведомления проверяются по
 * сигналам NotificationService с MockBreakpointObserver.
 */
import { ComponentFixture, fakeAsync, TestBed, tick } from '@angular/core/testing';
import { BreakpointObserver } from '@angular/cdk/layout';
import { By } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Select } from 'primeng/select';

import { MockBreakpointObserver } from '../../../../../testing/mock-breakpoint-observer';
import { GroupsService } from '../../../../core/services/groups.service';
import { StudentsService } from '../../../../core/services/students.service';
import { SessionStore } from '../../../../mock/auth/session-store';
import { MockApiClient } from '../../../../mock/mock-api-client';
import { MockDbData, emptyMockDbData } from '../../../../mock/mock-db';
import { registerGroupsHandlers } from '../../../../mock/groups/handlers';
import { registerStudentsHandlers } from '../../../../mock/students/handlers';
import { ApiError, STORAGE_KEYS, User } from '../../../../shared/models';
import {
  NOTIFICATION_AUTO_CLOSE_MS,
} from '../../../../shared/notifications/notification-model';
import { NotificationService } from '../../../../shared/notifications/notification-service';
import { ACCESS_ROUTES } from '../../access.routes';
import { AccessPage } from './access-page';

const TEACHER_ID = 'aaaaaaaa-0000-4000-8000-000000000001';
const GROUP_221 = 'bbbbbbbb-0000-4000-8000-000000000001';
const GROUP_222 = 'bbbbbbbb-0000-4000-8000-000000000002';
const GROUP_223 = 'bbbbbbbb-0000-4000-8000-000000000003';
/** «Антонов Иванов 05» — единственный студент, содержащий «иванов 05». */
const ANT_ID = 'cccccccc-0000-4000-8000-000000000001';

/** uuid студента NN: 1 → …0002, … 14 → …000f. */
function studentId(nn: number): string {
  return `cccccccc-0000-4000-8000-0000000000${(nn + 1).toString(16).padStart(2, '0')}`;
}

/**
 * Сид по правилам демо-данных (ADR-107): 15 студентов — «Антонов Иванов 05»
 * и «Иванов Иван Иванович NN» (NN = 01..14); последние noGroupCount студентов
 * без группы (по умолчанию 13 и 14 — аналог студентов 31/32 сида в AC
 * no-group-filter), остальные в ИК-221.
 */
function seedDb(noGroupCount = 2): void {
  const teacher: User = {
    id: TEACHER_ID,
    login: 'teacher',
    email: 'teacher@example.com',
    fullName: 'Сидоров Семён Семёнович',
    role: 'teacher',
    groupId: null,
    password: 'teacher123!',
  };
  const students: User[] = [
    {
      id: ANT_ID,
      login: 'antonov05',
      email: 'antonov05@example.com',
      fullName: 'Антонов Иванов 05',
      role: 'student',
      groupId: GROUP_221,
      password: 'student123!',
    },
  ];
  for (let nn = 1; nn <= 14; nn++) {
    const suffix = String(nn).padStart(2, '0');
    students.push({
      id: studentId(nn),
      login: `student${suffix}`,
      email: `student${suffix}@example.com`,
      fullName: `Иванов Иван Иванович ${suffix}`,
      role: 'student',
      groupId: nn <= 14 - noGroupCount ? GROUP_221 : null,
      password: 'student123!',
    });
  }
  const data: MockDbData = {
    ...emptyMockDbData(),
    users: [teacher, ...students],
    groups: [
      { id: GROUP_221, name: 'ИК-221', studentCount: 0 },
      { id: GROUP_222, name: 'ИК-222', studentCount: 0 },
      { id: GROUP_223, name: 'ИК-223', studentCount: 0 },
    ],
  };
  localStorage.setItem(STORAGE_KEYS.mockDb, JSON.stringify(data));
}

/** Сессия выставляется через владельца ключа — SessionStore домена Auth. */
const sessionStore = new SessionStore();

describe('AccessPage (C-115, SCR-013, FR-4.6)', () => {
  let breakpoints: MockBreakpointObserver;
  let studentsService: StudentsService;
  let groupsService: GroupsService;
  let notifications: NotificationService;
  let fixture: ComponentFixture<AccessPage>;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    seedDb();
    sessionStore.setUserId(TEACHER_ID);

    breakpoints = new MockBreakpointObserver();
    TestBed.configureTestingModule({
      providers: [
        { provide: BreakpointObserver, useValue: breakpoints },
        provideNoopAnimations(),
      ],
    });
    const client = TestBed.inject(MockApiClient);
    registerGroupsHandlers(client);
    registerStudentsHandlers(client);
    studentsService = TestBed.inject(StudentsService);
    groupsService = TestBed.inject(GroupsService);
    notifications = TestBed.inject(NotificationService);
    fixture = TestBed.createComponent(AccessPage);
  });

  afterEach(() => {
    notifications.dismissMobile();
    fixture.destroy();
    localStorage.clear();
    sessionStorage.clear();
  });

  /** Корневой элемент фикстуры (nativeElement ComponentFixture типизирован any). */
  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  /** Поиск по фикстуре, при пустом результате — по документу (оверлеи). */
  function qs(selector: string): HTMLElement | null {
    return root().querySelector(selector) ?? (document.querySelector(selector) as HTMLElement | null);
  }

  function qsAll(selector: string): HTMLElement[] {
    const inFixture = Array.from(root().querySelectorAll<HTMLElement>(selector));
    return inFixture.length > 0
      ? inFixture
      : Array.from(document.querySelectorAll<HTMLElement>(selector));
  }

  /**
   * Дожидается мок-задержки ms, рендерит пришедшие строки и дотягивает
   * микрозадачи NgModel: синхронизация [ngModel] → writeValue селектора
   * выполняется Angular в микрозадаче (resolvedPromise.then) и без неё
   * подпись свежесозданного p-select показывает прежнее значение.
   */
  function settle(ms: number): void {
    tick(ms);
    fixture.detectChanges();
    tick(0);
    fixture.detectChanges();
  }

  /** Создаёт фикстуру и дожидается init-загрузки: группы (500) → список (500). */
  function createPage(): void {
    fixture.detectChanges();
    settle(1100);
  }

  /** Микрозадачи NgModel для существующих селекторов (без мок-задержки). */
  function flushNgModel(): void {
    tick(0);
    fixture.detectChanges();
  }

  /** ФИО строк таблицы в порядке отображения. */
  function tableRowFullNames(): string[] {
    const cells = root().querySelectorAll('tbody tr td:first-child');
    return Array.from(cells, (cell) => (cell as HTMLElement).textContent?.trim() ?? '');
  }

  /** Селектор группы в строке с порядковым номером index. */
  function rowSelect(index: number): HTMLElement {
    const row = qsAll('tbody tr')[index]!;
    return row.querySelector('p-select')!;
  }

  /** Экземпляр компонента Select в строке (для значения модели). */
  function selectInstance(index: number): { modelValue(): unknown } {
    const rowDe = fixture.debugElement.queryAll(By.css('tbody tr'))[index]!;
    return rowDe.query(By.directive(Select))!.componentInstance as unknown as {
      modelValue(): unknown;
    };
  }

  /** Подпись выбранной опции селектора строки. */
  function selectLabel(select: HTMLElement): string {
    return select.querySelector('.p-select-label')!.textContent?.trim() ?? '';
  }

  /** Открывает оверлей селектора строки (клик по p-select). */
  function openSelect(select: HTMLElement): void {
    select.click();
    fixture.detectChanges();
  }

  /** Выбирает опцию с подписью label в открытом оверлее. */
  function pickOption(label: string): void {
    const option = qsAll('li.p-select-option').find(
      (element) => element.textContent?.trim() === label,
    )!;
    option.click();
    fixture.detectChanges();
  }

  /** Ввод в поле поиска (событие input — как при наборе пользователем). */
  function typeSearch(value: string): void {
    const input = root().querySelector<HTMLInputElement>('input[type="search"]')!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  /** Чекбокс «Без группы» (событие change — как клик пользователя). */
  function clickNoGroupFilter(): void {
    root().querySelector<HTMLInputElement>('input[type="checkbox"]')!.click();
    fixture.detectChanges();
  }

  /** Клик по кнопке пагинации с подписью label («‹», номер, «›»). */
  function clickPageButton(label: string): void {
    const button = qsAll('.access__page').find(
      (element) => element.textContent?.trim() === label,
    ) as HTMLButtonElement;
    button.click();
    fixture.detectChanges();
  }

  function clickConfirm(answer: 'Yes' | 'No'): void {
    // Классы p-confirmdialog-*-button PrimeNG ставит на внутренние <button>.
    const kind = answer === 'Yes' ? 'accept' : 'reject';
    const button = qs(`button.p-confirmdialog-${kind}-button`) as HTMLButtonElement;
    button.click();
    fixture.detectChanges();
  }

  /** Гасит таймер автозакрытия уведомления (5000 мс, FR-021). */
  function drainNotificationTimer(): void {
    tick(NOTIFICATION_AUTO_CLOSE_MS + 1);
  }

  function storedGroupId(userId: string): string | null {
    const stored = JSON.parse(localStorage.getItem(STORAGE_KEYS.mockDb) ?? 'null') as {
      users: Array<{ id: string; groupId: string | null }>;
    };
    return stored.users.find((user) => user.id === userId)?.groupId ?? null;
  }

  it('начальная загрузка: страница 1 — 10 из 15, подпись, порядок мока неизменен', fakeAsync(() => {
    createPage();

    expect(tableRowFullNames()).toEqual([
      'Антонов Иванов 05',
      'Иванов Иван Иванович 01',
      'Иванов Иван Иванович 02',
      'Иванов Иван Иванович 03',
      'Иванов Иван Иванович 04',
      'Иванов Иван Иванович 05',
      'Иванов Иван Иванович 06',
      'Иванов Иван Иванович 07',
      'Иванов Иван Иванович 08',
      'Иванов Иван Иванович 09',
    ]);
    expect(qs('.access__caption')!.textContent).toContain('Показать записи с 1 по 10 из 15');
    // «‹», «1», «2», «›»
    expect(qsAll('.access__page').length).toBe(4);
    expect((qs('button[aria-label="Предыдущая страница"]') as HTMLButtonElement).disabled).toBeTrue();
    expect((qs('button[aria-label="Следующая страница"]') as HTMLButtonElement).disabled).toBeFalse();
    expect(selectLabel(rowSelect(0))).toBe('ИК-221');
  }));

  it('значение селектора строки — student.groupId из DTO (аменда 6, не реверс по имени)', fakeAsync(() => {
    createPage();

    // студенты страницы 1 с группой: модель селектора равна uuid из DTO
    expect(selectInstance(0).modelValue()).toBe(GROUP_221);
    expect(selectInstance(9).modelValue()).toBe(GROUP_221);

    clickPageButton('2');
    settle(600);

    // студенты 13 и 14 без группы: null из DTO
    expect(selectInstance(3).modelValue()).toBeNull();
    expect(selectInstance(4).modelValue()).toBeNull();
  }));

  it('приглушённый стиль «Без группы»: класс только у селектора с groupId=null (CR-006)', fakeAsync(() => {
    createPage();

    expect(rowSelect(0).classList).not.toContain('access__select--no-group');

    clickPageButton('2');
    settle(600);

    // студенты 13 и 14 без группы
    expect(rowSelect(3).classList).toContain('access__select--no-group');
    expect(rowSelect(4).classList).toContain('access__select--no-group');
    expect(rowSelect(0).classList).not.toContain('access__select--no-group');
  }));

  it('страница 2: оставшиеся 5 строк, подпись «с 11 по 15 из 15», «›» неактивна', fakeAsync(() => {
    createPage();

    clickPageButton('2');
    settle(600);

    expect(tableRowFullNames()).toEqual([
      'Иванов Иван Иванович 10',
      'Иванов Иван Иванович 11',
      'Иванов Иван Иванович 12',
      'Иванов Иван Иванович 13',
      'Иванов Иван Иванович 14',
    ]);
    expect(qs('.access__caption')!.textContent).toContain('Показать записи с 11 по 15 из 15');
    expect((qs('button[aria-label="Следующая страница"]') as HTMLButtonElement).disabled).toBeTrue();
    // студенты 13 и 14 без группы — аналог 31/32 сида
    expect(selectLabel(rowSelect(3))).toBe('Без группы');
  }));

  it('поиск «иванов 05»: дебаунс 300 мс, page=1, ci-подстрока (AC search)', fakeAsync(() => {
    const getListSpy = spyOn(studentsService, 'getList').and.callThrough();
    createPage();
    expect(getListSpy.calls.count()).toBe(1);

    clickPageButton('›');
    settle(600);
    expect(getListSpy.calls.count()).toBe(2);
    expect(getListSpy.calls.mostRecent().args[0]).toEqual({ search: '', groupId: null, page: 2 });

    typeSearch('иванов 05');
    tick(200); // меньше дебаунса
    fixture.detectChanges();
    expect(getListSpy.calls.count()).toBe(2);

    tick(150); // суммарно 350 мс: дебаунс сработал, запрос отправлен
    fixture.detectChanges();
    expect(getListSpy.calls.count()).toBe(3);
    expect(getListSpy.calls.mostRecent().args[0]).toEqual({
      search: 'иванов 05',
      groupId: null,
      page: 1,
    });

    settle(600); // ответ мока
    // Многословный поиск (уточнение IF-105, CR-010): оба студента содержат
    // токены «иванов» и «05» в ФИО; порядок мока ФИО↑ сохранён
    expect(tableRowFullNames()).toEqual(['Антонов Иванов 05', 'Иванов Иван Иванович 05']);
    expect(qs('.access__caption')!.textContent).toContain('Показать записи с 1 по 2 из 2');
  }));

  it('поиск без учёта регистра: «ИВАНОВ 05» даёт тот же результат', fakeAsync(() => {
    const getListSpy = spyOn(studentsService, 'getList').and.callThrough();
    createPage();

    typeSearch('ИВАНОВ 05');
    tick(350); // дебаунс сработал, запрос отправлен
    fixture.detectChanges();

    expect(getListSpy.calls.mostRecent().args[0]).toEqual({
      search: 'ИВАНОВ 05',
      groupId: null,
      page: 1,
    });

    settle(600); // ответ мока
    expect(tableRowFullNames()).toEqual(['Антонов Иванов 05', 'Иванов Иван Иванович 05']);
  }));

  it('поле поиска: type=search, maxlength=200, placeholder и кнопка очистки (SCR-013)', fakeAsync(() => {
    createPage();

    const input = root().querySelector<HTMLInputElement>('input[type="search"]')!;
    expect(input.maxLength).toBe(200);
    expect(input.getAttribute('maxlength')).toBe('200');
    expect(input.getAttribute('placeholder')).toBe('Поиск по ФИО, логину или email');
    expect(input.getAttribute('aria-label')).toBe('Поиск по ФИО, логину или email');
    expect(qs('button[aria-label="Очистить поиск"]')).not.toBeNull();
  }));

  it('поле поиска достаточно широкое: полный placeholder без обрезки (VBUG-005)', fakeAsync(() => {
    // Ширина разрешается только для элемента в дереве документа.
    document.body.appendChild(root());
    createPage();

    const search = qs('.access__search')!;
    const input = search.querySelector<HTMLInputElement>('input[type="search"]')!;
    const searchWidth = parseFloat(getComputedStyle(search).width);
    const inputWidth = parseFloat(getComputedStyle(input).width);

    // 390px контейнера: placeholder «Поиск по ФИО, логину или email» при
    // базовом шрифте 16px требует ~265px + 80px отступов под иконки.
    expect(searchWidth).toBeGreaterThanOrEqual(390);
    expect(inputWidth).toBe(searchWidth);
    document.body.removeChild(root());
  }));

  it('кнопка очистки: поиск сброшен, перезагрузка без search на странице 1', fakeAsync(() => {
    const getListSpy = spyOn(studentsService, 'getList').and.callThrough();
    createPage();

    typeSearch('иванов 05');
    settle(950); // дебаунс 300 + ответ мока 500
    expect(tableRowFullNames()).toEqual(['Антонов Иванов 05', 'Иванов Иван Иванович 05']);

    (qs('button[aria-label="Очистить поиск"]') as HTMLButtonElement).click();
    settle(950);

    expect(getListSpy.calls.mostRecent().args[0]).toEqual({ search: '', groupId: null, page: 1 });
    expect(root().querySelector<HTMLInputElement>('input[type="search"]')!.value).toBe('');
    expect(tableRowFullNames().length).toBe(10);
  }));

  it('пустой результат: «Записей нет» и видимая подпись «с 1 по 0 из 0» (аменда 7)', fakeAsync(() => {
    createPage();

    typeSearch('петров');
    settle(950); // дебаунс 300 + ответ мока 500

    expect(tableRowFullNames()).toEqual(['Записей нет']);
    expect(qs('.access__caption')!.textContent).toContain('Показать записи с 1 по 0 из 0');
    expect((qs('button[aria-label="Следующая страница"]') as HTMLButtonElement).disabled).toBeTrue();
  }));

  it('фильтр «Без группы»: groupId=none и page=1, только студенты без группы (AC no-group-filter)', fakeAsync(() => {
    const getListSpy = spyOn(studentsService, 'getList').and.callThrough();
    createPage();

    clickPageButton('›');
    settle(600);

    clickNoGroupFilter();
    settle(600);

    expect(getListSpy.calls.mostRecent().args[0]).toEqual({
      search: '',
      groupId: 'none',
      page: 1,
    });
    expect(tableRowFullNames()).toEqual(['Иванов Иван Иванович 13', 'Иванов Иван Иванович 14']);
    expect(selectLabel(rowSelect(0))).toBe('Без группы');
    expect(qs('.access__caption')!.textContent).toContain('Показать записи с 1 по 2 из 2');

    // Снятие фильтра возвращает полный список, также на страницу 1
    clickNoGroupFilter();
    settle(600);

    expect(getListSpy.calls.mostRecent().args[0]).toEqual({
      search: '',
      groupId: null,
      page: 1,
    });
    expect(tableRowFullNames().length).toBe(10);
  }));

  it('включение в группу: немедленный setGroup без диалога, селектор заблокирован, спиннер (AC include-immediately)', fakeAsync(() => {
    const setGroupSpy = spyOn(studentsService, 'setGroup').and.callThrough();
    const notifySuccessSpy = spyOn(notifications, 'notifySuccess');
    createPage();
    clickNoGroupFilter();
    settle(600);

    openSelect(rowSelect(0)); // студент 13, без группы
    pickOption('ИК-222');

    // Немедленно: запрос без подтверждения, строка заблокирована, спиннер
    expect(setGroupSpy).toHaveBeenCalledWith(studentId(13), GROUP_222);
    expect(qs('.p-confirmdialog')).toBeNull();
    expect(rowSelect(0).classList).toContain('p-disabled');
    expect(qs('.access__spinner')).not.toBeNull();

    settle(1100); // setGroup (500) + перезагрузка страницы (500)

    expect(rowSelect(0).classList).not.toContain('p-disabled');
    expect(qs('.access__spinner')).toBeNull();
    // студент включён и исчез из фильтра «Без группы»
    expect(selectLabel(rowSelect(0))).toBe('Без группы');
    expect(tableRowFullNames()).toEqual(['Иванов Иван Иванович 14']);
    expect(storedGroupId(studentId(13))).toBe(GROUP_222);
    // аменда 6: успехи setGroup — без success-тостов
    expect(notifySuccessSpy).not.toHaveBeenCalled();
    expect(notifications.desktopMessage()).toBeNull();
    expect(notifications.mobileMessage()).toBeNull();
  }));

  it('перевод в группу: немедленный setGroup, groupId строки обновлён из DTO (аменда 6)', fakeAsync(() => {
    const setGroupSpy = spyOn(studentsService, 'setGroup').and.callThrough();
    createPage();

    openSelect(rowSelect(1)); // студент 01 в ИК-221
    pickOption('ИК-223');

    expect(setGroupSpy).toHaveBeenCalledWith(studentId(1), GROUP_223);
    expect(qs('.p-confirmdialog')).toBeNull();

    settle(1100);
    expect(selectLabel(rowSelect(1))).toBe('ИК-223');
    // после перезагрузки значение селектора — новый groupId из DTO
    expect(selectInstance(1).modelValue()).toBe(GROUP_223);
  }));

  it('исключение «No»: селектор вернулся на ИК-221, setGroup не вызывался (AC exclude-confirm)', fakeAsync(() => {
    const setGroupSpy = spyOn(studentsService, 'setGroup').and.callThrough();
    createPage();

    openSelect(rowSelect(1)); // студент 01 в ИК-221
    pickOption('Без группы');

    expect(qs('.p-confirmdialog')!.textContent).toContain(
      'Исключить студента Иванов Иван Иванович 01 из группы?',
    );
    expect(setGroupSpy).not.toHaveBeenCalled();

    clickConfirm('No');
    flushNgModel(); // откат селектора проходит через [ngModel] → микрозадача
    drainNotificationTimer();

    expect(qs('.p-confirmdialog')).toBeNull();
    expect(selectLabel(rowSelect(1))).toBe('ИК-221');
    expect(setGroupSpy).not.toHaveBeenCalled();
    expect(storedGroupId(studentId(1))).toBe(GROUP_221);
  }));

  it('исключение «Yes»: setGroup(null), студент без группы после перезагрузки', fakeAsync(() => {
    const setGroupSpy = spyOn(studentsService, 'setGroup').and.callThrough();
    createPage();

    openSelect(rowSelect(1));
    pickOption('Без группы');
    clickConfirm('Yes');

    expect(setGroupSpy).toHaveBeenCalledWith(studentId(1), null);

    settle(1100);
    expect(selectLabel(rowSelect(1))).toBe('Без группы');
    expect(storedGroupId(studentId(1))).toBeNull();
  }));

  it('ошибка setGroup: notifyError с текстом мока и перезагрузка страницы списка', fakeAsync(() => {
    const failure: ApiError = { status: 404, body: { message: 'Группа не найдена' } };
    spyOn(studentsService, 'setGroup').and.rejectWith(failure);
    const getListSpy = spyOn(studentsService, 'getList').and.callThrough();
    createPage();
    expect(getListSpy.calls.count()).toBe(1);

    openSelect(rowSelect(1));
    pickOption('ИК-222');

    tick(600); // отказ setGroup обрабатывается страницей
    fixture.detectChanges();

    expect(notifications.desktopMessage()?.severity).toBe('error');
    expect(notifications.desktopMessage()?.text).toBe('Группа не найдена');

    settle(600); // перезагрузка страницы списка

    expect(getListSpy.calls.count()).toBe(2);
    // строка восстановлена из мока: фактически студент 01 в ИК-221
    expect(selectLabel(rowSelect(1))).toBe('ИК-221');
    expect(storedGroupId(studentId(1))).toBe(GROUP_221);
    drainNotificationTimer();
  }));

  it('мобильная ошибка setGroup уходит с якорем header (IF-109: баннер под шапкой)', fakeAsync(() => {
    breakpoints.simulate(true); // <768px
    const failure: ApiError = { status: 404, body: { message: 'Группа не найдена' } };
    spyOn(studentsService, 'setGroup').and.rejectWith(failure);
    createPage();

    openSelect(rowSelect(1));
    pickOption('ИК-222');

    tick(600);
    fixture.detectChanges();

    expect(notifications.isMobile()).toBeTrue();
    expect(notifications.mobileMessage()?.severity).toBe('error');
    expect(notifications.mobileMessage()?.text).toBe('Группа не найдена');
    expect(notifications.mobileMessage()?.formId).toBeNull(); // якорь 'header'
    drainNotificationTimer();
  }));

  it('ошибка справочника групп: баннер + DTO-имя группы в ячейке вместо селектора (аменда 6, CR-007 iter2)', fakeAsync(() => {
    const failure: ApiError = { status: 500, body: { message: 'Неизвестная ошибка' } };
    spyOn(groupsService, 'getList').and.rejectWith(failure);
    createPage();

    // баннер с текстом мока, якорь 'header' (десктоп — Toast-сигнал)
    expect(notifications.desktopMessage()?.severity).toBe('error');
    expect(notifications.desktopMessage()?.text).toBe('Неизвестная ошибка');

    // строки рендерятся с данными DTO; селектор не рендерится — вместо
    // пустой подписи в ячейке группы DTO-шное groupName (CR-007 iter2)
    expect(tableRowFullNames().length).toBe(10);
    expect(qs('[data-test="group-fallback"]')?.textContent?.trim()).toBe('ИК-221');
    expect(root().querySelector('tbody p-select'))
      .withContext('селекторы групп скрыты в деградации')
      .toBeNull();
    drainNotificationTimer();
  }));

  it('опустевшая страница после включения: откат на предыдущую (CR-001)', fakeAsync(() => {
    // 11 студентов без группы: страницы 10 + 1; включение последнего
    // студента страницы 2 опустошает её (total 11 → 10)
    seedDb(11);
    const setGroupSpy = spyOn(studentsService, 'setGroup').and.callThrough();
    createPage();

    clickNoGroupFilter();
    settle(600);
    expect(qs('.access__caption')!.textContent).toContain('Показать записи с 1 по 10 из 11');

    clickPageButton('2');
    settle(600);
    expect(tableRowFullNames()).toEqual(['Иванов Иван Иванович 14']);

    openSelect(rowSelect(0));
    pickOption('ИК-222');
    expect(setGroupSpy).toHaveBeenCalledWith(studentId(14), GROUP_222);

    // setGroup (500) + перезагрузка пустой страницы 2 (500) + откат-загрузка
    // страницы 1 (500)
    settle(1700);

    // страница 2 опустела: откат на страницу 1, а не «Записей нет»
    expect(qs('tbody')!.textContent).not.toContain('Записей нет');
    expect(tableRowFullNames().length).toBe(10);
    expect(tableRowFullNames()[0]).toBe('Иванов Иван Иванович 04');
    expect(qs('.access__caption')!.textContent).toContain('Показать записи с 1 по 10 из 10');
    expect((qs('button[aria-label="Предыдущая страница"]') as HTMLButtonElement).disabled).toBeTrue();
    expect(storedGroupId(studentId(14))).toBe(GROUP_222);
  }));

  it('повторная смена группы у сохраняющейся строки игнорируется (селектор заблокирован)', fakeAsync(() => {
    const setGroupSpy = spyOn(studentsService, 'setGroup').and.callThrough();
    createPage();

    openSelect(rowSelect(1));
    pickOption('ИК-222');
    expect(setGroupSpy.calls.count()).toBe(1);

    // Селектор строки заблокирован на время запроса (макет SCR-013):
    // состояние p-disabled и спиннер, второй setGroup не отправляется.
    expect(rowSelect(1).classList).toContain('p-disabled');
    expect(qs('.access__spinner')).not.toBeNull();

    settle(1100);
    expect(setGroupSpy.calls.count()).toBe(1);
  }));
});

describe('access.routes (ADR-110, CR-004)', () => {
  it('маршрут /access объявлен с title «Доступ» и компонентом AccessPage', () => {
    expect(ACCESS_ROUTES.length).toBe(1);
    expect(ACCESS_ROUTES[0].path).toBe('access');
    expect(ACCESS_ROUTES[0].title).toBe('Доступ');
    expect(ACCESS_ROUTES[0].component).toBe(AccessPage);
  });
});
