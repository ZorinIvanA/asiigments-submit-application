/**
 * Интеграционные спеки экрана «Сдача работ» /my-submissions (батч 3,
 * FR-4.4 US-11 + FR-4.10/NFR-4.10, SCR-010): реальные MySubmissionsPage +
 * core-сервисы на реальном HTTP-ядре (HttpClient, без интерцептора) поверх
 * программируемого HttpTestingController-бэкенда GridBackendStub с сидом
 * зоны; аутентификации нет — бэкенд всегда отвечает от имени
 * сид-преподавателя: GET /me/profile отдаёт профиль без группы, GET
 * /me/submissions — {hasGroup: false, labs: [], submissions: []}.
 *
 * Сценарии (automation: automated): TS-320 (экран только для чтения —
 * предупреждение «без группы» вместо таблицы, селектор семестра активен),
 * TS-323 (смена семестра перезагружает getMy), TS-324 (нет семестров —
 * getMy не вызывается), TS-325 (без группы и без семестров — только
 * предупреждение), TS-326 (семестр опустел между запросами — предупреждение
 * сохраняется, селектор активен), TS-380 (граница 768px: предупреждение
 * корректно в обеих вёрстках — NFR-4.10, MockBreakpointObserver).
 */
import { ComponentFixture, fakeAsync } from '@angular/core/testing';

import { SubmissionsService } from '../../../core/services/submissions.service';
import { NotificationService } from '../../../shared/notifications/notification-service';
import {
  MySubmissionsPage,
  TEXT_NO_GROUP,
  TEXT_NO_SEMESTERS,
} from '../../../features/my-submissions/pages/my-submissions-page';
import { GridAccessEnv } from './integration-env';

describe('MySubmissionsPage — интеграция с реальным HTTP-ядром (батч 3, FR-4.4/FR-4.10)', () => {
  let env: GridAccessEnv;
  let notifications: NotificationService;
  let submissions: SubmissionsService;
  let fixture: ComponentFixture<MySubmissionsPage>;

  beforeEach(() => {
    env = GridAccessEnv.setup();
    notifications = env.notifications;
    submissions = env.inject(SubmissionsService);
  });

  afterEach(() => {
    notifications.dismissMobile();
    fixture?.destroy();
    env.stop();
  });

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  /**
   * Открывает экран анонимно (консоль преподавателя): страница запрашивает
   * профиль (GET /me/profile, волна 1), getSemesters и getMy (волна 2).
   */
  function openMySubmissions(): void {
    fixture = env.mount(MySubmissionsPage);
    env.settle(fixture, 2);
  }

  /** Нативный select семестра страницы. */
  function semesterSelect(): HTMLSelectElement {
    return root().querySelector(
      '#my-submissions-semester',
    ) as HTMLSelectElement;
  }

  it('TS-320: экран строго read-only — предупреждение «без группы» дословно (role=status), таблицы нет, селектор семестра активен с семестрами [1,2]', fakeAsync(() => {
    const updateSpy = spyOn(submissions, 'update').and.callThrough();
    openMySubmissions();

    const warning = root().querySelector('.no-group');
    expect(warning?.getAttribute('role')).toBe('status');
    expect(warning?.textContent?.trim()).toContain(TEXT_NO_GROUP);
    expect(root().querySelector('p-table'))
      .withContext('таблицы нет')
      .toBeNull();

    // Селектор семестра активен — работы в системе есть (сид: семестры 1 и 2).
    const options = Array.from(semesterSelect().options).map((option) =>
      option.text.trim(),
    );
    expect(options).toEqual(['1', '2']);
    expect(semesterSelect().disabled).toBeFalse();

    // Read-only: календаря в ячейках нет, input-контролов нет (кроме
    // селектора семестра), вызовов submissions.update нет.
    expect(root().querySelector('p-datepicker'))
      .withContext('нет календарей')
      .toBeNull();
    expect(Array.from(root().querySelectorAll('input')).length)
      .withContext('нет input-контролов')
      .toBe(0);
    root().querySelectorAll('tbody td')[1]?.dispatchEvent(new Event('click'));
    env.flushNgModel(fixture);
    expect(updateSpy).not.toHaveBeenCalled();

    // Данных ведомости не отдаётся: контракт /me/submissions — пустой ответ
    // без группы (IF-106).
    let response!: Awaited<ReturnType<SubmissionsService['getMy']>>;
    void submissions.getMy(1).then((result) => (response = result));
    env.settleRequests(1);
    expect(response).toEqual({ hasGroup: false, labs: [], submissions: [] });
  }));

  it('TS-323: смена семестра перезагружает getMy (запрошен выбранный семестр), предупреждение сохраняется', fakeAsync(() => {
    const getMySpy = spyOn(submissions, 'getMy').and.callThrough();
    openMySubmissions();
    expect(getMySpy).toHaveBeenCalledWith(1);

    semesterSelect().value = '2';
    semesterSelect().dispatchEvent(new Event('change'));
    env.settle(fixture, 1);

    expect(getMySpy).toHaveBeenCalledWith(2);
    expect(root().querySelector('.no-group')?.textContent).toContain(
      TEXT_NO_GROUP,
    );
    expect(root().querySelector('p-table'))
      .withContext('таблицы по-прежнему нет')
      .toBeNull();
  }));

  it('TS-324: работы удалены — getMy не вызван ни разу, селектор пуст/disabled, «Нет семестров с работами» не показан (приоритет предупреждения), без баннеров', fakeAsync(() => {
    const getMySpy = spyOn(submissions, 'getMy').and.callThrough();
    env.backend.mutate((state) => {
      state.labs = [];
      state.submissions = [];
    });

    openMySubmissions();

    expect(getMySpy)
      .withContext('вызов getMy с семестром вне 1..10 запрещён')
      .not.toHaveBeenCalled();
    expect(semesterSelect().disabled)
      .withContext('селектор пуст/disabled')
      .toBeTrue();
    expect(root().querySelector('.no-group')?.textContent).toContain(
      TEXT_NO_GROUP,
    );
    expect(root().textContent).not.toContain(TEXT_NO_SEMESTERS);
    expect(notifications.desktopMessage())
      .withContext('ошибочных баннеров нет')
      .toBeNull();
    expect(notifications.mobileMessage()).toBeNull();
  }));

  it('TS-325: без группы И без семестров — только предупреждение, «Нет семестров с работами» не дублируется', fakeAsync(() => {
    env.backend.mutate((state) => {
      state.labs = [];
      state.submissions = [];
    });

    openMySubmissions();

    expect(root().querySelector('.no-group')?.textContent).toContain(
      TEXT_NO_GROUP,
    );
    expect(root().textContent).not.toContain(TEXT_NO_SEMESTERS);
  }));

  it('TS-326: выбранный семестр опустел между запросами — предупреждение сохраняется, селектор остаётся активен', fakeAsync(() => {
    openMySubmissions();

    // Каскадное удаление работ семестра 2 между запросами (другой сеанс).
    env.backend.mutate((state) => {
      state.labs = state.labs.filter((lab) => lab.semester !== 2);
      state.submissions = state.submissions.filter((candidate) =>
        state.labs.some((lab) => lab.id === candidate.labId),
      );
    });

    semesterSelect().value = '2';
    semesterSelect().dispatchEvent(new Event('change'));
    env.settle(fixture, 1);

    expect(root().querySelector('.no-group')?.textContent).toContain(
      TEXT_NO_GROUP,
    );
    expect(root().textContent).not.toContain(TEXT_NO_SEMESTERS);
    expect(semesterSelect().disabled)
      .withContext('селектор активен')
      .toBeFalse();
    expect(root().querySelector('p-table'))
      .withContext('таблицы нет')
      .toBeNull();
    expect(notifications.desktopMessage())
      .withContext('ошибочного баннера нет')
      .toBeNull();
    env.drainNotifications();
  }));

  it('TS-380 (NFR-4.10): 767px — предупреждение без карточек; 768px — то же предупреждение без таблицы (в обеих вёрстках)', fakeAsync(() => {
    // Мобильная ширина (767px) до создания страницы.
    env.breakpoints.simulate(true);
    openMySubmissions();

    expect(root().querySelector('.no-group')?.textContent).toContain(
      TEXT_NO_GROUP,
    );
    expect(root().querySelectorAll('.card').length)
      .withContext('карточек нет')
      .toBe(0);
    expect(root().querySelector('p-table'))
      .withContext('таблицы нет')
      .toBeNull();

    // 768px — предупреждение сохраняется, таблица не появляется.
    env.breakpoints.simulate(false);
    fixture.detectChanges();
    expect(root().querySelector('.no-group')?.textContent).toContain(
      TEXT_NO_GROUP,
    );
    expect(root().querySelector('p-table'))
      .withContext('таблицы нет')
      .toBeNull();
    expect(semesterSelect().disabled)
      .withContext('селектор активен')
      .toBeFalse();
  }));
});
