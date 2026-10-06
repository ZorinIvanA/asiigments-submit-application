/**
 * Интеграционная спека границ скоупа (батч 3, TS-390): на экранах батча
 * (/groups, /groups/:id, /access, /submissions, /my-submissions) не
 * реализовано ничего из out_of_scope v1 — реальные страницы, core-сервисы
 * и мок-слой поверх сида seedFixtures (fakeAsync + tick по 500 мс).
 *
 * Проверки по then сценария:
 *  - нет массового импорта пользователей и восстановления удалённых групп;
 *  - /my-submissions — только чтение (никаких update-вызовов и редактирования);
 *  - /access не показывает success-уведомлений (аменда 6);
 *  - отсутствие специальных мобильных макетов разделов преподавателя —
 *    не дефект по §4.10, поэтому кодом не проверяется (в сценарии отмечено
 *    как допустимое).
 */
import { BreakpointObserver } from '@angular/cdk/layout';
import { ComponentFixture, fakeAsync, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';

import { MockBreakpointObserver } from '../../../../testing/mock-breakpoint-observer';
import { AuthService } from '../../../core/services/auth.service';
import { StudentsService } from '../../../core/services/students.service';
import { SubmissionsService } from '../../../core/services/submissions.service';
import { NotificationService } from '../../../shared/notifications/notification-service';
import { AccessPage } from '../../../features/access/pages/access-page/access-page';
import { GroupPage } from '../../../features/groups/pages/group-page';
import { GroupsPage } from '../../../features/groups/pages/groups-page';
import { MySubmissionsPage } from '../../../features/my-submissions/pages/my-submissions-page';
import { SubmissionsPage } from '../../../features/submissions/pages/submissions-page';
import {
  flushNgModel,
  groupIdOf,
  hydrateSeed,
  installMockLayer,
  loginAs,
  resetZoneEnvAfterSpec,
  settle,
  switchSession,
  userIdOf,
} from './integration-env';

type AnyPage = GroupsPage | GroupPage | AccessPage | SubmissionsPage | MySubmissionsPage;

describe('Границы скоупа v1 на экранах батча (TS-390, FR-4.4/4.5/4.6)', () => {
  let breakpoints: MockBreakpointObserver;
  let notifications: NotificationService;
  let studentsService: StudentsService;
  let submissionsService: SubmissionsService;
  let paramMap: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let fixture: ComponentFixture<AnyPage>;

  let ik221: string;

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
    studentsService = TestBed.inject(StudentsService);
    submissionsService = TestBed.inject(SubmissionsService);
    installMockLayer();

    const db = hydrateSeed();
    ik221 = groupIdOf(db, 'ИК-221');
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

  it('TS-390: на /groups, /groups/:id, /access, /submissions, /my-submissions нет функциональности вне v1', fakeAsync(() => {
    // /groups: действия строк — только переименование и удаление; нет
    // импорта пользователей и восстановления групп.
    fixture = TestBed.createComponent(GroupsPage);
    fixture.detectChanges();
    settle(fixture, 600);
    const groupsActions = Array.from(root().querySelectorAll('tbody tr button')).map((button) =>
      button.getAttribute('aria-label'),
    );
    expect(new Set(groupsActions)).toEqual(new Set(['Переименовать', 'Удалить']));
    expect(root().textContent).not.toContain('Импорт');
    expect(root().textContent).not.toContain('Восстанов');
    expect(root().textContent).not.toContain('Импортировать');
    fixture.destroy();

    // /groups/:id: единственное действие строки — исключение из группы.
    paramMap.next(convertToParamMap({ id: ik221 }));
    fixture = TestBed.createComponent(GroupPage);
    fixture.detectChanges();
    settle(fixture, 1200);
    const cardActions = Array.from(root().querySelectorAll('tbody button')).map((button) =>
      button.textContent?.trim(),
    );
    expect(new Set(cardActions)).toEqual(new Set(['Исключить из группы']));
    expect(root().textContent).not.toContain('Импорт');
    expect(root().textContent).not.toContain('Восстанов');
    fixture.destroy();

    // /my-submissions: только чтение — update не вызывается, редактирующих
    // контролов нет (клики по ячейкам ничего не открывают).
    switchSession(userIdOf(hydrateSeed(), 'student01'));
    void TestBed.inject(AuthService).loadMe();
    fixture = TestBed.createComponent(MySubmissionsPage);
    settle(fixture, 600);
    fixture.detectChanges();
    settle(fixture, 1100);
    const updateSpy = spyOn(submissionsService, 'update').and.callThrough();
    const myRoot = root();
    expect(myRoot.querySelector('p-datepicker')).withContext('календарей нет').toBeNull();
    expect(myRoot.querySelector('input')).withContext('полей ввода нет').toBeNull();
    expect(myRoot.querySelectorAll('button').length).withContext('кнопок действий нет').toBe(0);
    myRoot.querySelectorAll('tbody td')[1]?.dispatchEvent(new Event('click'));
    flushNgModel(fixture);
    expect(updateSpy).not.toHaveBeenCalled();

    // /access: включение student31 — БЕЗ success-уведомлений (аменда 6).
    switchSession(userIdOf(hydrateSeed(), 'teacher'));
    void TestBed.inject(AuthService).loadMe();
    fixture = TestBed.createComponent(AccessPage);
    fixture.detectChanges();
    settle(fixture, 1100);
    const setGroupSpy = spyOn(studentsService, 'setGroup').and.callThrough();
    const page4 = Array.from(root().querySelectorAll<HTMLButtonElement>('.access__page')).find(
      (candidate) => candidate.textContent?.trim() === '4',
    );
    page4!.click();
    settle(fixture, 600);
    const firstRowSelect = root().querySelector<HTMLElement>('tbody tr p-select');
    firstRowSelect!.click();
    flushNgModel(fixture);
    const option = Array.from(document.querySelectorAll('li.p-select-option')).find(
      (candidate) => candidate.textContent?.trim() === 'ИК-221',
    );
    (option as HTMLElement).click();
    settle(fixture, 1100); // setGroup (500) + перезагрузка страницы (500)
    expect(setGroupSpy).toHaveBeenCalled();
    expect(notifications.desktopMessage()).withContext('успехов на /access нет (аменда 6)').toBeNull();
    expect(notifications.mobileMessage()).toBeNull();

    // /submissions: тулбар и таблица — без дополнительных действий вне v1
    // (никаких кнопок массовых операций у таблицы ведомости).
    fixture.destroy();
    fixture = TestBed.createComponent(SubmissionsPage);
    fixture.detectChanges();
    settle(fixture, 1100);
    expect(root().querySelectorAll('thead button').length)
      .withContext('у колонок ведомости нет кнопок массовых действий')
      .toBe(0);
    expect(root().textContent).not.toContain('Импорт');
  }));
});
