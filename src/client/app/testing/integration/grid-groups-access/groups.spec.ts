/**
 * Интеграционные спеки раздела «Группы» (батч 3, FR-4.5, SCR-011/012):
 * реальные GroupsPage/GroupPage + core-сервисы + мок-слой (IF-104/IF-105)
 * поверх сида seedFixtures, сессия teacher через SessionStore. Карточка
 * группы монтируется на настоящем ActivatedRoute-параметре (paramMap).
 *
 * Сценарии (automation: automated): TS-331 (создание группы), TS-332
 * (дубликат названия без учёта регистра), TS-333 (границы названия: пустое/
 * пробелы/101 блокируются клиентской валидацией с полевым текстом из
 * ERROR_TEXTS, 100 символов — успех; ADR-112/QG-005), TS-334 (переименование:
 * успех и конфликт), TS-335 (удаление группы: студенты сохраняются, доступ
 * теряется — швы на /access, /submissions, /my-submissions), TS-336
 * (карточка: состав и пагинация 10), TS-337 (исключение из карточки),
 * TS-340 (карточка пустой группы, подпись «с 1 по 0 из 0» — аменда 7).
 *
 * TS-330/338/339 — маршрутизация/переходы: см. groups-routing.spec.ts.
 */
import { BreakpointObserver } from '@angular/cdk/layout';
import { tick } from '@angular/core/testing';
import { ComponentFixture, fakeAsync, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';

import { MockBreakpointObserver } from '../../../../testing/mock-breakpoint-observer';
import { AuthService } from '../../../core/services/auth.service';
import { GroupsService } from '../../../core/services/groups.service';
import { StudentsService } from '../../../core/services/students.service';
import { SubmissionsService } from '../../../core/services/submissions.service';
import { NotificationService } from '../../../shared/notifications/notification-service';
import { AccessPage } from '../../../features/access/pages/access-page/access-page';
import { GroupPage } from '../../../features/groups/pages/group-page';
import { GroupsPage } from '../../../features/groups/pages/groups-page';
import {
  MySubmissionsPage,
  TEXT_NO_GROUP,
} from '../../../features/my-submissions/pages/my-submissions-page';
import {
  drainNotifications,
  flushNgModel,
  groupIdOf,
  hydrateSeed,
  installMockLayer,
  loginAs,
  mockDbOf,
  resetZoneEnvAfterSpec,
  settle,
  switchSession,
  userIdOf,
} from './integration-env';

type AnyPage = GroupsPage | GroupPage | AccessPage | MySubmissionsPage;

describe('GroupsPage/GroupPage — интеграция с реальным мок-слоем (батч 3, FR-4.5)', () => {
  let breakpoints: MockBreakpointObserver;
  let notifications: NotificationService;
  let groupsService: GroupsService;
  let studentsService: StudentsService;
  let submissionsService: SubmissionsService;
  let paramMap: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let fixture: ComponentFixture<AnyPage>;

  let student26: string;
  let ik221: string;
  let ik222: string;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    breakpoints = new MockBreakpointObserver();
    paramMap = new BehaviorSubject(convertToParamMap({ id: 'not-mounted' }));
    TestBed.configureTestingModule({
      providers: [
        { provide: BreakpointObserver, useValue: breakpoints },
        provideNoopAnimations(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap } },
      ],
    });
    notifications = TestBed.inject(NotificationService);
    groupsService = TestBed.inject(GroupsService);
    studentsService = TestBed.inject(StudentsService);
    submissionsService = TestBed.inject(SubmissionsService);
    installMockLayer();

    const db = hydrateSeed();
    student26 = userIdOf(db, 'student26');
    ik221 = groupIdOf(db, 'ИК-221');
    ik222 = groupIdOf(db, 'ИК-222');
    loginAs(db, 'teacher');
  });

  afterEach(() => {
    notifications.dismissMobile();
    fixture?.destroy();
    resetZoneEnvAfterSpec();
  });

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  /** Список групп: открытие + первичное getList (500 мс). */
  function createGroupsPage(): void {
    fixture = TestBed.createComponent(GroupsPage);
    fixture.detectChanges();
    settle(fixture, 600);
  }

  /** Карточка группы: активация параметра маршрута, getList + getStudents. */
  function createGroupPage(groupId: string): void {
    paramMap.next(convertToParamMap({ id: groupId }));
    fixture = TestBed.createComponent(GroupPage);
    fixture.detectChanges();
    settle(fixture, 1200);
  }

  function rowButton(rowText: string, ariaLabel: string): HTMLButtonElement {
    const button = rowByText(rowText).querySelector<HTMLButtonElement>(
      `button[aria-label="${ariaLabel}"]`,
    );
    if (button === null) {
      throw new Error(`groups.spec: в строке ${rowText} нет кнопки ${ariaLabel}`);
    }
    return button;
  }

  function rowByText(text: string): HTMLTableRowElement {
    const found = Array.from(root().querySelectorAll<HTMLTableRowElement>('tbody tr')).find(
      (row) => row.textContent?.includes(text),
    );
    if (found === undefined) {
      throw new Error(`groups.spec: не найдена строка ${text}`);
    }
    return found;
  }

  function listNames(): string[] {
    return Array.from(root().querySelectorAll('tbody tr')).map(
      (row) => row.querySelector('td')?.textContent?.trim() ?? '',
    );
  }

  /** Кнопка диалога по точному тексту («Создать»/«Сохранить»/«Отмена»). */
  function dialogButton(label: string): HTMLButtonElement {
    const found = Array.from(root().querySelectorAll('button')).find(
      (candidate) => candidate.textContent?.trim() === label,
    );
    if (found === undefined) {
      throw new Error(`groups.spec: не найдена кнопка «${label}»`);
    }
    return found;
  }

  function confirmMessage(): string | null {
    return root().querySelector('.p-confirmdialog-message')?.textContent?.trim() ?? null;
  }

  /**
   * Дожидается закрытия confirm-оверлея (CR-009): оверлей анимируется и
   * исчезает через несколько циклов CD — одного detectChanges недостаточно.
   */
  function waitConfirmClosed(): void {
    for (let attempt = 0; attempt < 20; attempt++) {
      tick(50);
      fixture.detectChanges();
      if (confirmMessage() === null) {
        return;
      }
    }
    throw new Error('groups.spec: confirm-оверлей не закрылся');
  }

  function confirmButton(kind: 'accept' | 'reject'): HTMLButtonElement {
    const found = root().querySelector<HTMLButtonElement>(`.p-confirmdialog-${kind}-button`);
    if (found === null) {
      throw new Error(`groups.spec: нет кнопки подтверждения ${kind}`);
    }
    return found;
  }

  /** Ввод значения в поле диалога событием input (как при наборе). */
  function typeName(inputId: string, value: string): void {
    const input = root().querySelector<HTMLInputElement>(`#${inputId}`);
    if (input === null) {
      throw new Error(`groups.spec: не найдено поле #${inputId}`);
    }
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  it('TS-331: создание группы «ИК-224» — диалог закрыт, «Сохранено», группа в списке с 0 студентов', fakeAsync(() => {
    createGroupsPage();

    dialogButton('Создать группу').click();
    flushNgModel(fixture);
    expect(root().querySelector('.p-dialog-title')?.textContent?.trim()).toBe('Создать группу');

    typeName('group-create-name', 'ИК-224');
    dialogButton('Создать').click();
    settle(fixture, 1200); // groups.create (500) + перечитывание списка (500)

    expect(notifications.desktopMessage()).toEqual({ severity: 'success', text: 'Сохранено' });
    drainNotifications();
    expect(root().querySelector('.p-dialog-title')).withContext('диалог закрыт').toBeNull();
    expect(mockDbOf().read().groups.some((group) => group.name === 'ИК-224')).toBeTrue();

    const row = rowByText('ИК-224');
    expect(row.textContent).withContext('в списке с 0 студентов').toContain('0');
    expect(listNames()).toEqual(['ИК-221', 'ИК-222', 'ИК-223', 'ИК-224']);
  }));

  it('TS-332: дубликат названия без учёта регистра — 409 в диалоге при создании и переименовании, группа не создана / имя не изменено', fakeAsync(() => {
    createGroupsPage();

    // Создание: «ик-221» против существующей «ИК-221».
    dialogButton('Создать группу').click();
    flushNgModel(fixture);
    typeName('group-create-name', 'ик-221');
    dialogButton('Создать').click();
    settle(fixture, 600);

    expect(root().querySelector('.groups-page__dialog-error')?.textContent?.trim()).toBe(
      'Группа с таким названием уже существует',
    );
    expect(root().querySelector('.p-dialog-title')?.textContent?.trim())
      .withContext('диалог остаётся открытым')
      .toBe('Создать группу');
    expect(mockDbOf().read().groups.length).withContext('группа не создана').toBe(3);

    // Переименование: «ИК-223» → «ик-221» — тот же 409, имя не изменено.
    dialogButton('Отмена').click();
    flushNgModel(fixture);
    rowButton('ИК-223', 'Переименовать').click();
    flushNgModel(fixture);
    typeName('group-rename-name', 'ик-221');
    dialogButton('Сохранить').click();
    settle(fixture, 600);

    expect(root().querySelector('.groups-page__dialog-error')?.textContent?.trim()).toBe(
      'Группа с таким названием уже существует',
    );
    expect(listNames()).withContext('имя не изменено').toContain('ИК-223');
    expect(listNames()).not.toContain('ик-221');
  }));

  it('TS-334: переименование ИК-223 → «ИК-223А» — «Сохранено», новое имя в списке; конфликт с «ИК-221» — 409 в диалоге, имя осталось «ИК-223А»', fakeAsync(() => {
    createGroupsPage();

    rowButton('ИК-223', 'Переименовать').click();
    flushNgModel(fixture);
    typeName('group-rename-name', 'ИК-223А');
    dialogButton('Сохранить').click();
    settle(fixture, 1200); // rename (500) + перечитывание списка (500)

    expect(notifications.desktopMessage()).toEqual({ severity: 'success', text: 'Сохранено' });
    drainNotifications();
    expect(root().querySelector('.p-dialog-title')).withContext('диалог закрыт').toBeNull();
    expect(listNames()).toEqual(['ИК-221', 'ИК-222', 'ИК-223А']);

    // Конфликт: «ИК-223А» → «ИК-221».
    rowButton('ИК-223А', 'Переименовать').click();
    flushNgModel(fixture);
    typeName('group-rename-name', 'ИК-221');
    dialogButton('Сохранить').click();
    settle(fixture, 600);

    expect(root().querySelector('.groups-page__dialog-error')?.textContent?.trim()).toBe(
      'Группа с таким названием уже существует',
    );
    expect(listNames()).withContext('имя осталось «ИК-223А»').toContain('ИК-223А');
  }));

  /**
   * TS-333 (редакция сценариев, ADR-112/QG-005): сохранение блокируется
   * КЛИЕНТСКОЙ валидацией — полевой текст из ERROR_TEXTS у поля внутри
   * диалога, диалог остаётся открытым, мок-метод groups.create не вызывается,
   * группа не создана; ровно 100 символов — успех «Сохранено».
   */
  it('TS-333: пустое/пробелы/101 символ — полевой текст из ERROR_TEXTS в диалоге, мок не вызван, группа не создана; 100 символов — «Сохранено»', fakeAsync(() => {
    const createSpy = spyOn(groupsService, 'create').and.callThrough();
    createGroupsPage();

    dialogButton('Создать группу').click();
    flushNgModel(fixture);
    expect(root().querySelector('.p-dialog-title')?.textContent?.trim()).toBe('Создать группу');

    const fieldError = (): string =>
      root().querySelector('.groups-page__field-error')?.textContent?.trim() ?? '';

    // Пустое имя: requiredTrim → «Заполните поле».
    dialogButton('Создать').click();
    flushNgModel(fixture);
    expect(fieldError()).withContext('полевой текст у поля внутри диалога').toBe('Заполните поле');
    expect(root().querySelector('.p-dialog-title')?.textContent?.trim())
      .withContext('диалог остаётся открытым')
      .toBe('Создать группу');
    expect(createSpy).not.toHaveBeenCalled();

    // Только пробелы: requiredTrim трактует как пустое.
    typeName('group-create-name', '   ');
    dialogButton('Создать').click();
    flushNgModel(fixture);
    expect(fieldError()).toBe('Заполните поле');
    expect(createSpy).not.toHaveBeenCalled();

    // 101 символ: groupName → «Название группы — от 1 до 100 символов».
    typeName('group-create-name', 'И'.repeat(101));
    dialogButton('Создать').click();
    flushNgModel(fixture);
    expect(fieldError()).toBe('Название группы — от 1 до 100 символов');
    expect(createSpy).not.toHaveBeenCalled();
    expect(mockDbOf().read().groups.length).withContext('группа не создана').toBe(3);
    expect(root().querySelector('.p-dialog-title')?.textContent?.trim()).toBe('Создать группу');

    // Ровно 100 символов — успех: «Сохранено», группа создана.
    typeName('group-create-name', 'И'.repeat(100));
    dialogButton('Создать').click();
    settle(fixture, 1200); // groups.create (500) + перечитывание списка (500)

    expect(createSpy).toHaveBeenCalledTimes(1);
    expect(notifications.desktopMessage()).toEqual({ severity: 'success', text: 'Сохранено' });
    drainNotifications();
    expect(root().querySelector('.p-dialog-title')).withContext('диалог закрыт').toBeNull();
    expect(mockDbOf().read().groups.length).withContext('группа создана').toBe(4);
    expect(rowByText('И'.repeat(100))).toBeDefined();
  }));

  it('TS-335: удаление ИК-222 — «No» ничего не меняет, «Yes» удаляет; студенты 26–30 остаются («Без группы»), исчезают из ведомостей, у студента 26 — предупреждение; восстановление не предлагается', fakeAsync(() => {
    createGroupsPage();

    // Действия строки — только переименование и удаление (out_of_scope v1:
    // массовый импорт и восстановление групп отсутствуют).
    const actionLabels = Array.from(rowByText('ИК-222').querySelectorAll('button')).map((button) =>
      button.getAttribute('aria-label'),
    );
    expect(actionLabels).toEqual(['Переименовать', 'Удалить']);
    expect(root().textContent).not.toContain('Восстанов');
    expect(root().textContent).not.toContain('Импорт');

    // 🗑 → диалог с именем группы → «No»: ничего не изменилось, диалог закрыт.
    rowButton('ИК-222', 'Удалить').click();
    flushNgModel(fixture);
    expect(confirmMessage()).toBe('Вы точно хотите удалить группу ИК-222?');
    expect(confirmButton('accept').textContent?.trim()).toBe('Yes');
    expect(confirmButton('reject').textContent?.trim()).toBe('No');
    confirmButton('reject').click();
    waitConfirmClosed();
    flushNgModel(fixture);
    expect(confirmMessage()).withContext('диалог закрыт').toBeNull();
    expect(mockDbOf().read().groups.length).withContext('ничего не изменилось').toBe(3);
    expect(listNames()).toContain('ИК-222');

    // Повторно → «Yes»: «Удалено», группа исчезла.
    rowButton('ИК-222', 'Удалить').click();
    flushNgModel(fixture);
    confirmButton('accept').click();
    settle(fixture, 1200); // groups.remove (500) + перечитывание списка (500)

    expect(notifications.desktopMessage()).toEqual({ severity: 'success', text: 'Удалено' });
    drainNotifications();
    expect(listNames()).withContext('группа исчезла').toEqual(['ИК-221', 'ИК-223']);
    expect(mockDbOf().read().groups.length).toBe(2);
    fixture.destroy();

    // Студенты 26–30 сохранились: в /access — «Без группы» и в фильтре
    // (вместе с сидовыми 31/32 — 7 записей).
    fixture = TestBed.createComponent(AccessPage);
    fixture.detectChanges();
    settle(fixture, 1100);
    root().querySelector<HTMLInputElement>('input[type="checkbox"]')!.click();
    settle(fixture, 600);
    const names = Array.from(root().querySelectorAll('tbody tr td:first-child')).map((cell) =>
      cell.textContent?.trim() ?? '',
    );
    expect(names).toEqual([
      'Иванов Иван Иванович 26',
      'Иванов Иван Иванович 27',
      'Иванов Иван Иванович 28',
      'Иванов Иван Иванович 29',
      'Иванов Иван Иванович 30',
      'Иванов Иван Иванович 31',
      'Иванов Иван Иванович 32',
    ]);
    fixture.destroy();

    // Их нет в ведомости ни одной группы: ИК-221 — 25, ИК-223 — 0.
    const ik223 = groupIdOf(mockDbOf().read(), 'ИК-223');
    for (const groupId of [ik221, ik223]) {
      let grid!: Awaited<ReturnType<SubmissionsService['getGrid']>>;
      void submissionsService
        .getGrid({ groupId, semester: 1, page: 1 })
        .then((result) => (grid = result));
      tick(600);
      expect(grid.total).withContext(`ведомость группы ${groupId}`).toBe(groupId === ik221 ? 25 : 0);
      const members = grid.students.map((student) => student.fullName);
      for (let nn = 26; nn <= 30; nn++) {
        expect(members).not.toContain(`Иванов Иван Иванович ${String(nn).padStart(2, '0')}`);
      }
    }

    // /my-submissions студента 26 — предупреждение вместо таблицы.
    switchSession(student26);
    void TestBed.inject(AuthService).loadMe();
    fixture = TestBed.createComponent(MySubmissionsPage);
    settle(fixture, 600); // loadMe (500)
    fixture.detectChanges();
    settle(fixture, 1100); // getSemesters (500) + getMy (500)
    expect(root().querySelector('.no-group')?.textContent).toContain(TEXT_NO_GROUP);
    expect(root().querySelector('p-table')).toBeNull();
  }));

  it('TS-336: карточка ИК-221 — крошки, «Студентов: 25», колонки, пагинация 10 (стр.3 — 5 записей, подпись «с 21 по 25 из 25»), порядок ФИО↑', fakeAsync(() => {
    createGroupPage(ik221);

    const crumbs = root().querySelector('.group-page__crumbs')?.textContent ?? '';
    expect(crumbs).toContain('Группы');
    expect(crumbs).toContain('ИК-221');
    expect(root().querySelector('h1')?.textContent?.trim()).toBe('ИК-221');
    expect(root().querySelector('.group-page__count')?.textContent?.trim()).toBe('Студентов: 25');

    const headers = Array.from(root().querySelectorAll('thead th')).map((th) =>
      th.textContent?.trim(),
    );
    expect(headers).toEqual(['ФИО', 'Логин', 'Email', 'Действия']);
    // Кнопки исключения не имеют aria-label (p-button label) — матч по
    // тексту кнопки (CR-002).
    const excludeButtons = Array.from(root().querySelectorAll('tbody button')).filter(
      (button) => button.textContent?.trim() === 'Исключить из группы',
    );
    expect(excludeButtons.length).withContext('кнопка исключения у каждой строки').toBe(10);

    const expected = Array.from({ length: 25 }, (_, index) =>
      `Иванов Иван Иванович ${String(index + 1).padStart(2, '0')}`,
    );
    const firstNames = (): string[] =>
      Array.from(root().querySelectorAll('tbody tr td:first-child')).map(
        (cell) => cell.textContent?.trim() ?? '',
      );

    expect(firstNames()).toEqual(expected.slice(0, 10));
    expect(root().querySelector('.group-page__range')?.textContent?.trim()).toBe(
      'Показать записи с 1 по 10 из 25',
    );

    const pageButton = (label: string): HTMLButtonElement => {
      const found = Array.from(
        root().querySelectorAll<HTMLButtonElement>('.group-page__page'),
      ).find((candidate) => candidate.textContent?.trim() === label);
      if (found === undefined) {
        throw new Error(`groups.spec: нет кнопки страницы ${label}`);
      }
      return found;
    };

    pageButton('2').click();
    settle(fixture, 600);
    expect(firstNames()).toEqual(expected.slice(10, 20));

    pageButton('3').click();
    settle(fixture, 600);
    expect(firstNames()).toEqual(expected.slice(20, 25));
    expect(firstNames().length).toBe(5);
    expect(root().querySelector('.group-page__range')?.textContent?.trim()).toBe(
      'Показать записи с 21 по 25 из 25',
    );
  }));

  it('TS-337: исключение из карточки — подтверждение «Yes», «Сохранено», «Студентов: 24», студент ушёл со страницы, в /access «Без группы», счётчик списка обновлён', fakeAsync(() => {
    createGroupPage(ik221);

    root().querySelector<HTMLButtonElement>('tbody tr button')!.click();
    flushNgModel(fixture);
    expect(confirmMessage()).toBe('Исключить студента Иванов Иван Иванович 01 из группы?');
    confirmButton('accept').click();
    settle(fixture, 1200); // setGroup (500) + перечитывание состава (500)

    expect(notifications.desktopMessage()).toEqual({ severity: 'success', text: 'Сохранено' });
    drainNotifications();
    expect(root().querySelector('.group-page__count')?.textContent?.trim()).toBe('Студентов: 24');
    expect(root().querySelector('tbody')?.textContent).not.toContain('Иванов Иван Иванович 01');

    // В /access студент — «Без группы» (фильтр «none»).
    let none!: Awaited<ReturnType<StudentsService['getList']>>;
    void studentsService.getList({ groupId: 'none', page: 1 }).then((result) => (none = result));
    tick(600);
    expect(none.items.some((student) => student.login === 'student01')).toBeTrue();

    // Счётчик в списке /groups обновился.
    let list!: Awaited<ReturnType<GroupsService['getList']>>;
    void groupsService.getList().then((result) => (list = result));
    tick(600);
    expect(list.find((group) => group.id === ik221)?.studentCount).toBe(24);
  }));

  // CR-010 (был known implementation defect): фикс реализован в
  // group-page.html — подпись выводится всегда (аменда 7), при total=0
  // скрываются только кнопки страниц; тест зелёный и фиксирует исправление.
  it('TS-340: карточка пустой группы ИК-223 — «Студентов: 0», пустая таблица и подпись «Показать записи с 1 по 0 из 0» (аменда 7)', fakeAsync(() => {
    createGroupPage(groupIdOf(mockDbOf().read(), 'ИК-223'));

    expect(root().querySelector('h1')?.textContent?.trim()).toBe('ИК-223');
    expect(root().querySelector('.group-page__count')?.textContent?.trim()).toBe('Студентов: 0');
    expect(root().querySelectorAll('tbody tr').length)
      .withContext('таблица в состоянии пусто')
      .toBe(0);

    // Аменда 7 (ADR-111): подпись выводится всегда, скрытие при total=0
    // запрещено — «Показать записи с 1 по 0 из 0».
    const range = root().querySelector('.group-page__range');
    expect(range).withContext('подпись присутствует при total=0 (аменда 7)').not.toBeNull();
    expect(range?.textContent?.trim()).toBe('Показать записи с 1 по 0 из 0');
  }));
});
