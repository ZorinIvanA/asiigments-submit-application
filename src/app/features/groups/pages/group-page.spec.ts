/**
 * Unit-тесты GroupPage (T-116, SCR-012, контракты IF-104/IF-105/IF-109):
 * крошки, имя, счётчик «Студентов: N»; состав группы — 10 строк на страницу
 * с подписью «Показать записи с X по Y из Z» (ADR-109); исключение студента
 * через подтверждение «Исключить студента <ФИО> из группы?» с «Yes»/«No»
 * (ADR-111) и StudentsService.setGroup(id, null); 404 группы — «Группа не
 * найдена» под шапкой (якорь 'header') и возврат к /groups. Страница
 * тестируется через RouterTestingHarness (ADR-110: маршруты приложения
 * появляются только в T-119), сервисы замещены заглушками.
 */
import { BreakpointObserver } from '@angular/cdk/layout';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { MockBreakpointObserver } from '../../../../testing/mock-breakpoint-observer';
import { GroupsService } from '../../../core/services/groups.service';
import { StudentsService } from '../../../core/services/students.service';
import { NotificationService } from '../../../shared/notifications/notification-service';
import { ApiError, GroupDto, PagedResult, StudentDto } from '../../../shared/models';
import { GroupPage } from './group-page';
import { GroupsPage } from './groups-page';

const GROUP_221: GroupDto = { id: 'g-221', name: 'ИК-221', studentCount: 25 };

/** ApiError в транспортной форме мока (FR-003). */
function apiError(status: number, message: string): ApiError {
  return { status, body: { message } };
}

/** Студенты ИК-221 по макету SCR-012: Иванов Иван Иванович NN / studentNN. */
function makeStudent(number: number): StudentDto {
  const padded = String(number).padStart(2, '0');
  return {
    id: `s-${padded}`,
    fullName: `Иванов Иван Иванович ${padded}`,
    login: `student${padded}`,
    email: `student${padded}@example.com`,
    groupId: 'g-221',
    groupName: 'ИК-221',
  };
}

class GroupsServiceFake {
  getList = jasmine.createSpy('getList');
  getStudents = jasmine.createSpy('getStudents');
}

class StudentsServiceFake {
  setGroup = jasmine.createSpy('setGroup');
}

describe('GroupPage', () => {
  let groupsService: GroupsServiceFake;
  let studentsService: StudentsServiceFake;
  let notifications: NotificationService;
  let harness: RouterTestingHarness;
  /** Полный состав группы; нарезку страниц имитирует пейджер мока. */
  let roster: StudentDto[];

  beforeEach(async () => {
    roster = Array.from({ length: 25 }, (_, index) => makeStudent(index + 1));

    groupsService = new GroupsServiceFake();
    groupsService.getList.and.resolveTo([GROUP_221]);
    groupsService.getStudents.and.callFake(
      (_id: unknown, page: number): Promise<PagedResult<StudentDto>> =>
        Promise.resolve(pageOf(page)),
    );

    studentsService = new StudentsServiceFake();

    TestBed.configureTestingModule({
      imports: [GroupPage, GroupsPage],
      providers: [
        provideRouter([
          { path: 'groups', component: GroupsPage },
          { path: 'groups/:id', component: GroupPage },
        ]),
        provideNoopAnimations(),
        { provide: BreakpointObserver, useValue: new MockBreakpointObserver() },
        { provide: GroupsService, useValue: groupsService },
        { provide: StudentsService, useValue: studentsService },
      ],
    });
    notifications = TestBed.inject(NotificationService);
    spyOn(notifications, 'notifySuccess');
    spyOn(notifications, 'notifyError');

    harness = await RouterTestingHarness.create();
  });

  /** Нарезка страницы (ADR-109) над текущим roster. */
  function pageOf(page: number): PagedResult<StudentDto> {
    const start = (page - 1) * 10;
    return {
      items: roster.slice(start, start + 10),
      total: roster.length,
      page,
      pageSize: 10,
    };
  }

  /** Прогон микрозадач (немедленные Promise заглушек) и CD. */
  async function flush(): Promise<void> {
    await harness.fixture.whenStable();
    harness.fixture.detectChanges();
    harness.fixture.detectChanges();
  }

  function root(): HTMLElement {
    return harness.fixture.nativeElement as HTMLElement;
  }

  function studentRows(): HTMLTableRowElement[] {
    return Array.from(root().querySelectorAll<HTMLTableRowElement>('tbody tr'));
  }

  function rowButton(row: HTMLTableRowElement, label: string): HTMLButtonElement {
    const found = row.querySelector<HTMLButtonElement>(`button[aria-label="${label}"]`);
    expect(found).withContext(`кнопка «${label}» в строке`).not.toBeNull();
    return found!;
  }

  /** Кнопка номера страницы пейджера. */
  function pageButton(number: string): HTMLButtonElement {
    const found = Array.from(root().querySelectorAll<HTMLButtonElement>('.group-page__page')).find(
      (candidate) => candidate.textContent?.trim() === number,
    );
    expect(found).withContext(`кнопка страницы «${number}»`).toBeDefined();
    return found!;
  }

  /** Кнопка исключения в строке: имя доступности — видимый текст кнопки. */
  function excludeButtonInRow(row: HTMLTableRowElement): HTMLButtonElement {
    const found = Array.from(row.querySelectorAll<HTMLButtonElement>('button')).find((candidate) =>
      candidate.textContent?.includes('Исключить из группы'),
    );
    expect(found).withContext('кнопка «Исключить из группы» в строке').not.toBeNull();
    return found!;
  }

  async function openCard(): Promise<GroupPage> {
    const page = await harness.navigateByUrl('/groups/g-221', GroupPage);
    await flush();
    return page!;
  }

  it('рисует крошки «Группы / ИК-221», заголовок с именем и счётчик «Студентов: 25»', async () => {
    await openCard();

    const crumbsLink = root().querySelector<HTMLAnchorElement>('.group-page__crumbs-link');
    expect(crumbsLink?.getAttribute('href')).toBe('/groups');
    expect(crumbsLink?.textContent?.trim()).toBe('Группы');
    expect(root().querySelector('.group-page__crumbs-name')?.textContent?.trim()).toBe('ИК-221');
    expect(root().querySelector('h1')?.textContent?.trim()).toBe('ИК-221');
    expect(root().querySelector('.group-page__count')?.textContent?.trim()).toBe('Студентов: 25');
  });

  it('таблица состава: ФИО/Логин/Email, 10 строк первой страницы, кнопка исключения', async () => {
    await openCard();

    const headers = Array.from(root().querySelectorAll('thead th')).map((th) =>
      th.textContent?.trim(),
    );
    expect(headers).toEqual(['ФИО', 'Логин', 'Email', 'Действия']);

    const rows = studentRows();
    expect(rows.length).toBe(10);
    const cells = Array.from(rows[0]!.querySelectorAll('td')).map((td) =>
      td.textContent?.trim(),
    );
    expect(cells.slice(0, 3)).toEqual([
      'Иванов Иван Иванович 01',
      'student01',
      'student01@example.com',
    ]);
    expect(rows[9]!.textContent).toContain('Иванов Иван Иванович 10');
    expect(rows[0]!.textContent).toContain('Исключить из группы');
  });

  it('подпись «Показать записи с 1 по 10 из 25»; клик по страницам перечитывает состав', async () => {
    await openCard();

    expect(root().querySelector('.group-page__range')?.textContent?.trim()).toBe(
      'Показать записи с 1 по 10 из 25',
    );
    const prev = root().querySelector<HTMLButtonElement>('button[aria-label="Предыдущая страница"]');
    expect(prev?.disabled).toBeTrue();

    const pageTwo = Array.from(root().querySelectorAll<HTMLButtonElement>('.group-page__page')).find(
      (candidate) => candidate.textContent?.trim() === '2',
    );
    expect(pageTwo).toBeDefined();
    pageTwo!.click();
    await flush();

    expect(groupsService.getStudents).toHaveBeenCalledWith('g-221', 2);
    expect(root().querySelector('.group-page__range')?.textContent?.trim()).toBe(
      'Показать записи с 11 по 20 из 25',
    );
    expect(studentRows()[0]!.textContent).toContain('Иванов Иван Иванович 11');

    root()
      .querySelector<HTMLButtonElement>('button[aria-label="Следующая страница"]')!
      .click();
    await flush();

    expect(groupsService.getStudents).toHaveBeenCalledWith('g-221', 3);
    expect(root().querySelector('.group-page__range')?.textContent?.trim()).toBe(
      'Показать записи с 21 по 25 из 25',
    );
    expect(studentRows().length).toBe(5);
    const next = root().querySelector<HTMLButtonElement>('button[aria-label="Следующая страница"]');
    expect(next?.disabled).toBeTrue();
  });

  it('AC exclude-student: «No» в подтверждении не изменяет состав', async () => {
    await openCard();

    excludeButtonInRow(studentRows()[0]!).click();
    harness.fixture.detectChanges();

    const message = root().querySelector('.p-confirmdialog-message');
    expect(message?.textContent?.trim()).toBe(
      'Исключить студента Иванов Иван Иванович 01 из группы?',
    );
    expect(
      root().querySelector<HTMLButtonElement>('.p-confirmdialog-accept-button')?.textContent?.trim(),
    ).toBe('Yes');
    expect(
      root().querySelector<HTMLButtonElement>('.p-confirmdialog-reject-button')?.textContent?.trim(),
    ).toBe('No');

    root().querySelector<HTMLButtonElement>('.p-confirmdialog-reject-button')!.click();
    await flush();

    expect(studentsService.setGroup).not.toHaveBeenCalled();
    expect(root().querySelector('.p-confirmdialog-message')).toBeNull();
    expect(root().querySelector('.group-page__count')?.textContent?.trim()).toBe('Студентов: 25');
  });

  it('AC exclude-student: «Yes» — setGroup(id, null), «Сохранено», счётчик 24, студент ушёл', async () => {
    await openCard();

    studentsService.setGroup.and.callFake(
      (studentId: string): Promise<void> => {
        roster = roster.filter((student) => student.id !== studentId);
        return Promise.resolve();
      },
    );

    excludeButtonInRow(studentRows()[0]!).click();
    harness.fixture.detectChanges();
    root().querySelector<HTMLButtonElement>('.p-confirmdialog-accept-button')!.click();
    await flush();

    expect(studentsService.setGroup).toHaveBeenCalledWith('s-01', null);
    expect(notifications.notifySuccess).toHaveBeenCalledWith('Сохранено');
    // Счётчик и страница перечитаны у мока (сервер — истина).
    expect(groupsService.getStudents).toHaveBeenCalledWith('g-221', 1);
    expect(root().querySelector('.group-page__count')?.textContent?.trim()).toBe('Студентов: 24');
    expect(root().querySelector('.group-page__range')?.textContent?.trim()).toBe(
      'Показать записи с 1 по 10 из 24',
    );
    const firstRowCells = Array.from(studentRows()[0]!.querySelectorAll('td')).map((td) =>
      td.textContent?.trim(),
    );
    expect(firstRowCells[0]).toBe('Иванов Иван Иванович 02');
    expect(studentRows().some((row) => row.textContent?.includes('Иванов Иван Иванович 01')))
      .withContext('исключённый студент ушёл со страницы')
      .toBeFalse();
  });

  it('исключение единственного студента последней страницы смещает страницу назад', async () => {
    roster = Array.from({ length: 21 }, (_, index) => makeStudent(index + 1));
    studentsService.setGroup.and.callFake(
      (studentId: string): Promise<void> => {
        roster = roster.filter((student) => student.id !== studentId);
        return Promise.resolve();
      },
    );
    await openCard();

    // Страница 3 — единственный студент s-21.
    const pageThree = Array.from(
      root().querySelectorAll<HTMLButtonElement>('.group-page__page'),
    ).find((candidate) => candidate.textContent?.trim() === '3');
    pageThree!.click();
    await flush();
    expect(studentRows().length).toBe(1);

    excludeButtonInRow(studentRows()[0]!).click();
    harness.fixture.detectChanges();
    root().querySelector<HTMLButtonElement>('.p-confirmdialog-accept-button')!.click();
    await flush();

    expect(root().querySelector('.group-page__count')?.textContent?.trim()).toBe('Студентов: 20');
    expect(root().querySelector('.group-page__range')?.textContent?.trim()).toBe(
      'Показать записи с 11 по 20 из 20',
    );
    expect(studentRows().length).toBe(10);
  });

  it('404: группа отсутствует в списке — «Группа не найдена» под шапкой и возврат к /groups', async () => {
    groupsService.getList.and.resolveTo([]);
    await harness.navigateByUrl('/groups/ghost');
    await flush();

    expect(notifications.notifyError).toHaveBeenCalledWith('Группа не найдена', 'header');
    expect(TestBed.inject(Router).url).toBe('/groups');
    expect(root().querySelector('h1')?.textContent?.trim()).toBe('Группы');
  });

  it('404 при пагинации: отказ getStudents — баннер под шапкой и возврат к /groups', async () => {
    await openCard();

    groupsService.getStudents.and.rejectWith(apiError(404, 'Группа не найдена'));
    root()
      .querySelector<HTMLButtonElement>('button[aria-label="Следующая страница"]')!
      .click();
    await flush();

    expect(notifications.notifyError).toHaveBeenCalledWith('Группа не найдена', 'header');
    expect(TestBed.inject(Router).url).toBe('/groups');
  });

  it('пустая группа (TS-340, аменда 7): «Студентов: 0», подпись «Показать записи с 1 по 0 из 0», кнопки страниц скрыты', async () => {
    roster = [];
    await openCard();

    expect(root().querySelector('.group-page__count')?.textContent?.trim()).toBe('Студентов: 0');
    expect(studentRows().length).toBe(0);
    expect(root().querySelector('.group-page__range')?.textContent?.trim()).toBe(
      'Показать записи с 1 по 0 из 0',
    );
    expect(root().querySelector('.group-page__pages')).withContext('кнопки страниц').toBeNull();
  });

  it('клик по активной странице и вне диапазона не порождает лишних вызовов', async () => {
    const page = await openCard();

    const callsAfterLoad = groupsService.getStudents.calls.count();
    page.goToPage(1); // активная страница
    page.goToPage(0); // вне диапазона
    page.goToPage(99); // вне диапазона
    await flush();

    expect(groupsService.getStudents).toHaveBeenCalledTimes(callsAfterLoad);
  });

  it('переход на другую /groups/:id перезагружает карточку без пересоздания страницы', async () => {
    await openCard();

    const other: GroupDto = { id: 'g-222', name: 'ИК-222', studentCount: 5 };
    groupsService.getList.and.resolveTo([GROUP_221, other]);
    await harness.navigateByUrl('/groups/g-222', GroupPage);
    await flush();

    expect(root().querySelector('h1')?.textContent?.trim()).toBe('ИК-222');
    expect(root().querySelector('.group-page__count')?.textContent?.trim()).toBe('Студентов: 25');
    expect(groupsService.getStudents).toHaveBeenCalledWith('g-222', 1);
  });

  it('быстрое листание: ответ устаревшей страницы не применяется к экрану (loadSeq)', async () => {
    await openCard();

    // Запрос страницы 2 зависает; страницы 3 — завершается сразу.
    let resolvePage2!: (value: PagedResult<StudentDto>) => void;
    const page2 = new Promise<PagedResult<StudentDto>>((resolve) => {
      resolvePage2 = resolve;
    });
    groupsService.getStudents.and.callFake(
      (_id: unknown, requested: number): Promise<PagedResult<StudentDto>> =>
        requested === 2 ? page2 : Promise.resolve(pageOf(requested)),
    );

    pageButton('2').click();
    pageButton('3').click();
    await flush();

    expect(root().querySelector('.group-page__range')?.textContent?.trim()).toBe(
      'Показать записи с 21 по 25 из 25',
    );
    expect(studentRows()[0]!.textContent).toContain('Иванов Иван Иванович 21');

    // Поздний ответ страницы 2 (после более свежего запроса) отбрасывается.
    resolvePage2(pageOf(2));
    await flush();

    expect(root().querySelector('.group-page__range')?.textContent?.trim()).toBe(
      'Показать записи с 21 по 25 из 25',
    );
    expect(studentRows()[0]!.textContent).toContain('Иванов Иван Иванович 21');
    expect(notifications.notifyError).not.toHaveBeenCalled();
  });
});
