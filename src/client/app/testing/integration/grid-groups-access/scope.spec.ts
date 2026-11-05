/**
 * Интеграционная спека границ скоупа (батч 3, TS-390): на экранах батча
 * (/groups, /groups/:id, /access, /submissions, /my-submissions) не
 * реализовано ничего из out_of_scope v1 — реальные страницы и core-сервисы
 * на реальном HTTP-ядре (HttpClient + authInterceptor) поверх
 * программируемого HttpTestingController-бэкенда GridBackendStub с сидом
 * зоны (fakeAsync, волны settle).
 *
 * Проверки по then сценария:
 *  - нет массового импорта пользователей и восстановления удалённых групп;
 *  - /my-submissions — только чтение (никаких update-вызовов и редактирования);
 *  - /access не показывает success-уведомлений (аменда 6);
 *  - отсутствие специальных мобильных макетов разделов преподавателя —
 *    не дефект по §4.10, поэтому кодом не проверяется (в сценарии отмечено
 *    как допустимое).
 */
import { ComponentFixture, fakeAsync } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';

import { StudentsService } from '../../../core/services/students.service';
import { SubmissionsService } from '../../../core/services/submissions.service';
import { NotificationService } from '../../../shared/notifications/notification-service';
import { AccessPage } from '../../../features/access/pages/access-page/access-page';
import { GroupPage } from '../../../features/groups/pages/group-page';
import { GroupsPage } from '../../../features/groups/pages/groups-page';
import { MySubmissionsPage } from '../../../features/my-submissions/pages/my-submissions-page';
import { SubmissionsPage } from '../../../features/submissions/pages/submissions-page';
import { GridAccessEnv, openSelect, pickOption } from './integration-env';

type AnyPage = GroupsPage | GroupPage | AccessPage | SubmissionsPage | MySubmissionsPage;

describe('Границы скоупа v1 на экранах батча (TS-390, FR-4.4/4.5/4.6)', () => {
  let env: GridAccessEnv;
  let notifications: NotificationService;
  let studentsService: StudentsService;
  let submissionsService: SubmissionsService;
  let paramMap: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let fixture: ComponentFixture<AnyPage>;

  let ik221: string;

  beforeEach(() => {
    paramMap = new BehaviorSubject(convertToParamMap({ id: 'not-mounted' }));
    env = GridAccessEnv.setup({
      providers: [provideRouter([]), { provide: ActivatedRoute, useValue: { paramMap } }],
    });
    notifications = env.notifications;
    studentsService = env.inject(StudentsService);
    submissionsService = env.inject(SubmissionsService);
    ik221 = env.backend.groupIdByName('ИК-221');
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

  it('TS-390: на /groups, /groups/:id, /access, /submissions, /my-submissions нет функциональности вне v1', fakeAsync(() => {
    // /groups: действия строк — только переименование и удаление; нет
    // импорта пользователей и восстановления групп.
    fixture = env.mount(GroupsPage);
    env.settle(fixture, 1);
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
    fixture = env.mount(GroupPage);
    env.settle(fixture, 2);
    const cardActions = Array.from(root().querySelectorAll('tbody button')).map((button) =>
      button.textContent?.trim(),
    );
    expect(new Set(cardActions)).toEqual(new Set(['Исключить из группы']));
    expect(root().textContent).not.toContain('Импорт');
    expect(root().textContent).not.toContain('Восстанов');
    fixture.destroy();

    // /my-submissions: только чтение — update не вызывается, редактирующих
    // контролов нет (клики по ячейкам ничего не открывают).
    env.loginAs('student01');
    fixture = env.mount(MySubmissionsPage);
    env.settle(fixture, 2);
    const updateSpy = spyOn(submissionsService, 'update').and.callThrough();
    const myRoot = root();
    expect(myRoot.querySelector('p-datepicker')).withContext('календарей нет').toBeNull();
    expect(myRoot.querySelector('input')).withContext('полей ввода нет').toBeNull();
    expect(myRoot.querySelectorAll('button').length).withContext('кнопок действий нет').toBe(0);
    myRoot.querySelectorAll('tbody td')[1]?.dispatchEvent(new Event('click'));
    env.flushNgModel(fixture);
    expect(updateSpy).not.toHaveBeenCalled();
    fixture.destroy();

    // /access: включение student31 — БЕЗ success-уведомлений (аменда 6).
    env.loginAs('teacher');
    fixture = env.mount(AccessPage);
    env.settle(fixture, 2);
    const setGroupSpy = spyOn(studentsService, 'setGroup').and.callThrough();
    const page4 = Array.from(root().querySelectorAll<HTMLButtonElement>('.access__page')).find(
      (candidate) => candidate.textContent?.trim() === '4',
    );
    page4!.click();
    env.settle(fixture, 1);
    const firstRowSelect = root().querySelector<HTMLElement>('tbody tr p-select');
    openSelect(fixture, firstRowSelect!);
    pickOption(fixture, 'ИК-221');
    env.settle(fixture, 2); // setGroup + перезагрузка страницы
    expect(setGroupSpy).toHaveBeenCalled();
    expect(notifications.desktopMessage()).withContext('успехов на /access нет (аменда 6)').toBeNull();
    expect(notifications.mobileMessage()).toBeNull();
    fixture.destroy();

    // /submissions: тулбар и таблица — без дополнительных действий вне v1
    // (никаких кнопок массовых операций у таблицы ведомости).
    fixture = env.mount(SubmissionsPage);
    env.settle(fixture, 2);
    expect(root().querySelectorAll('thead button').length)
      .withContext('у колонок ведомости нет кнопок массовых действий')
      .toBe(0);
    expect(root().textContent).not.toContain('Импорт');
  }));
});
