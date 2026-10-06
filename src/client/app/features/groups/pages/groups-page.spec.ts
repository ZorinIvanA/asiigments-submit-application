/**
 * Unit-тесты GroupsPage (T-116, SCR-011, контракты IF-104/IF-109):
 * список групп со ссылками на карточки; диалоги создания и переименования —
 * 400 (текст поля в диалоге), 409 («Группа с таким названием уже
 * существует» в диалоге), успех («Сохранено» + перечитывание); удаление —
 * подтверждение с именем группы и кнопками «Yes»/«No» (ADR-111), «Yes» →
 * «Удалено» и группа исчезает, «No» — без вызова мока; ошибки загрузки
 * списка — якорь 'header'. Сервисы замещены заглушками (unit-уровень),
 * уведомления — шпионы на NotificationService (без таймеров автозакрытия).
 */
import { BreakpointObserver } from '@angular/cdk/layout';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';

import { MockBreakpointObserver } from '../../../../testing/mock-breakpoint-observer';
import { GroupsService } from '../../../core/services/groups.service';
import { NotificationService } from '../../../shared/notifications/notification-service';
import { ApiError, GroupDto } from '../../../shared/models';
import { GroupsPage } from './groups-page';

/** Сид списка групп — как в макете SCR-011. */
const GROUP_221: GroupDto = { id: 'g-221', name: 'ИК-221', studentCount: 25 };
const GROUP_222: GroupDto = { id: 'g-222', name: 'ИК-222', studentCount: 5 };
const GROUP_223: GroupDto = { id: 'g-223', name: 'ИК-223', studentCount: 0 };
const GROUP_224: GroupDto = { id: 'g-224', name: 'ИК-224', studentCount: 0 };

/** ApiError в транспортной форме мока (FR-003). */
function apiError(status: number, message: string, errors?: Record<string, string[]>): ApiError {
  return { status, body: errors === undefined ? { message } : { message, errors } };
}

class GroupsServiceFake {
  getList = jasmine.createSpy('getList');
  create = jasmine.createSpy('create');
  rename = jasmine.createSpy('rename');
  remove = jasmine.createSpy('remove');
}

describe('GroupsPage', () => {
  let fixture: ComponentFixture<GroupsPage>;
  let groupsService: GroupsServiceFake;
  let notifications: NotificationService;

  beforeEach(async () => {
    groupsService = new GroupsServiceFake();
    groupsService.getList.and.resolveTo([GROUP_221, GROUP_222, GROUP_223]);

    TestBed.configureTestingModule({
      imports: [GroupsPage],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        { provide: BreakpointObserver, useValue: new MockBreakpointObserver() },
        { provide: GroupsService, useValue: groupsService },
      ],
    });
    notifications = TestBed.inject(NotificationService);
    spyOn(notifications, 'notifySuccess');
    spyOn(notifications, 'notifyError');

    fixture = TestBed.createComponent(GroupsPage);
    await flush();
  });

  /** Прогон микрозадач (мок-вызовы — немедленные Promise) и двух CD. */
  async function flush(): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    return fixture.nativeElement;
  }

  function button(label: string): HTMLButtonElement {
    const candidates = Array.from(root().querySelectorAll<HTMLButtonElement>('button'));
    const found = candidates.find((candidate) => candidate.textContent?.trim() === label);
    expect(found).withContext(`кнопка «${label}»`).not.toBeNull();
    return found!;
  }

  function rowByName(name: string): HTMLTableRowElement {
    const rows = Array.from(root().querySelectorAll<HTMLTableRowElement>('tbody tr'));
    const found = rows.find((row) => row.textContent?.includes(name));
    expect(found).withContext(`строка группы «${name}»`).not.toBeNull();
    return found!;
  }

  function dialogTitle(): string | null {
    return root().querySelector('.p-dialog-title')?.textContent?.trim() ?? null;
  }

  function setDialogName(inputId: string, value: string): void {
    const input = root().querySelector<HTMLInputElement>(`#${inputId}`);
    expect(input).withContext(`#${inputId}`).not.toBeNull();
    input!.value = value;
    input!.dispatchEvent(new Event('input'));
  }

  it('показывает h1 «Группы», кнопку «Создать группу» и список групп со ссылками на карточки', () => {
    expect(root().querySelector('h1')?.textContent?.trim()).toBe('Группы');
    expect(button('Создать группу')).toBeTruthy();

    const rows = Array.from(root().querySelectorAll<HTMLTableRowElement>('tbody tr'));
    expect(rows.length).toBe(3);
    expect(rows[0]!.textContent).toContain('ИК-221');
    expect(rows[0]!.textContent).toContain('25');
    expect(rows[2]!.textContent).toContain('ИК-223');
    expect(rows[2]!.textContent).toContain('0');

    const link = rows[0]!.querySelector<HTMLAnchorElement>('a');
    expect(link?.getAttribute('href')).toBe('/groups/g-221');
    expect(rows[0]!.querySelector('button[aria-label="Переименовать"]')).toBeTruthy();
    expect(rows[0]!.querySelector('button[aria-label="Удалить"]')).toBeTruthy();
  });

  it('AC create-group: диалог создаёт «ИК-224» — группа в списке, «Сохранено»', async () => {
    button('Создать группу').click();
    await flush();

    expect(dialogTitle()).toBe('Создать группу');
    expect(root().querySelector('label[for="group-create-name"]')?.textContent?.trim()).toBe(
      'Название',
    );
    expect(button('Отмена')).toBeTruthy();
    expect(button('Создать')).toBeTruthy();

    groupsService.create.and.resolveTo(GROUP_224);
    groupsService.getList.and.resolveTo([GROUP_221, GROUP_222, GROUP_223, GROUP_224]);
    setDialogName('group-create-name', 'ИК-224');
    button('Создать').click();
    await flush();

    expect(groupsService.create).toHaveBeenCalledWith('ИК-224');
    expect(notifications.notifySuccess).toHaveBeenCalledWith('Сохранено');
    expect(groupsService.getList).toHaveBeenCalledTimes(2); // начальная + после создания
    expect(dialogTitle()).toBeNull(); // диалог закрыт
    const names = Array.from(root().querySelectorAll('tbody tr')).map((row) =>
      row.textContent?.trim(),
    );
    expect(names.some((name) => name?.includes('ИК-224')))
      .withContext('созданная группа в списке')
      .toBeTrue();
    expect(notifications.notifyError).not.toHaveBeenCalled();
  });

  it('create: пустое имя — «Заполните поле» в диалоге, мок не вызывается', async () => {
    button('Создать группу').click();
    await flush();

    button('Создать').click();
    await flush();

    expect(root().textContent).toContain('Заполните поле');
    expect(groupsService.create).not.toHaveBeenCalled();
    expect(dialogTitle()).toBe('Создать группу');
  });

  it('create: 400 «Данные заполнены неверно» — текст поля из ERROR_TEXTS в диалоге', async () => {
    button('Создать группу').click();
    await flush();

    groupsService.create.and.rejectWith(
      apiError(400, 'Данные заполнены неверно', {
        name: ['Название группы — от 1 до 100 символов'],
      }),
    );
    setDialogName('group-create-name', 'ИК-224');
    button('Создать').click();
    await flush();

    expect(root().textContent).toContain('Название группы — от 1 до 100 символов');
    expect(dialogTitle()).toBe('Создать группу'); // диалог остался открытым
    expect(notifications.notifyError).not.toHaveBeenCalled();
    expect(notifications.notifySuccess).not.toHaveBeenCalled();
  });

  it('create: 409 от мока — «Группа с таким названием уже существует» в диалоге', async () => {
    button('Создать группу').click();
    await flush();

    groupsService.create.and.rejectWith(
      apiError(409, 'Группа с таким названием уже существует'),
    );
    setDialogName('group-create-name', 'ик-221');
    button('Создать').click();
    await flush();

    expect(root().textContent).toContain('Группа с таким названием уже существует');
    expect(dialogTitle()).toBe('Создать группу');
    expect(notifications.notifyError).not.toHaveBeenCalled();
    expect(notifications.notifySuccess).not.toHaveBeenCalled();
  });

  it('AC rename-conflict: 409 при переименовании «ИК-221» в существующее имя — текст в диалоге, имя не изменено', async () => {
    rowByName('ИК-221').querySelector<HTMLButtonElement>('button[aria-label="Переименовать"]')!
      .click();
    await flush();

    expect(dialogTitle()).toBe('Переименовать группу');
    expect(
      root().querySelector<HTMLInputElement>('#group-rename-name')?.value,
    ).toBe('ИК-221');
    expect(button('Отмена')).toBeTruthy();
    expect(button('Сохранить')).toBeTruthy();

    groupsService.rename.and.rejectWith(
      apiError(409, 'Группа с таким названием уже существует'),
    );
    setDialogName('group-rename-name', 'ИК-222');
    button('Сохранить').click();
    await flush();

    expect(groupsService.rename).toHaveBeenCalledWith('g-221', 'ИК-222');
    expect(root().textContent).toContain('Группа с таким названием уже существует');
    expect(notifications.notifySuccess).not.toHaveBeenCalled();
    const names = Array.from(root().querySelectorAll('tbody tr')).map((row) =>
      row.textContent?.trim(),
    );
    expect(names.some((name) => name?.includes('ИК-221'))).toBeTrue();
  });

  it('rename: успех — «Сохранено», диалог закрыт, список перечитан', async () => {
    rowByName('ИК-221').querySelector<HTMLButtonElement>('button[aria-label="Переименовать"]')!
      .click();
    await flush();

    const renamed: GroupDto = { id: 'g-221', name: 'ИК-225', studentCount: 25 };
    groupsService.rename.and.resolveTo(renamed);
    groupsService.getList.and.resolveTo([renamed, GROUP_222, GROUP_223]);
    setDialogName('group-rename-name', '  ИК-225  ');
    button('Сохранить').click();
    await flush();

    expect(groupsService.rename).toHaveBeenCalledWith('g-221', 'ИК-225'); // трим перед моком
    expect(notifications.notifySuccess).toHaveBeenCalledWith('Сохранено');
    expect(groupsService.getList).toHaveBeenCalledTimes(2);
    expect(dialogTitle()).toBeNull();
    expect(root().textContent).toContain('ИК-225');
  });

  it('rename: пустое имя — «Заполните поле» в диалоге, мок не вызывается', async () => {
    rowByName('ИК-222').querySelector<HTMLButtonElement>('button[aria-label="Переименовать"]')!
      .click();
    await flush();

    setDialogName('group-rename-name', '   ');
    button('Сохранить').click();
    await flush();

    expect(root().textContent).toContain('Заполните поле');
    expect(groupsService.rename).not.toHaveBeenCalled();
  });

  it('AC delete-group: подтверждение с именем группы и «Yes»/«No»; «Yes» — «Удалено», группа исчезла', async () => {
    rowByName('ИК-222').querySelector<HTMLButtonElement>('button[aria-label="Удалить"]')!.click();
    fixture.detectChanges();

    const message = root().querySelector('.p-confirmdialog-message');
    expect(message?.textContent?.trim()).toBe('Вы точно хотите удалить группу ИК-222?');
    const accept = root().querySelector<HTMLButtonElement>('.p-confirmdialog-accept-button');
    const reject = root().querySelector<HTMLButtonElement>('.p-confirmdialog-reject-button');
    expect(accept?.textContent?.trim()).toBe('Yes');
    expect(reject?.textContent?.trim()).toBe('No');

    groupsService.remove.and.resolveTo(undefined);
    groupsService.getList.and.resolveTo([GROUP_221, GROUP_223]);
    accept!.click();
    await flush();

    expect(groupsService.remove).toHaveBeenCalledWith('g-222');
    expect(notifications.notifySuccess).toHaveBeenCalledWith('Удалено');
    const names = Array.from(root().querySelectorAll('tbody tr')).map((row) =>
      row.textContent?.trim(),
    );
    expect(names.some((name) => name?.includes('ИК-222')))
      .withContext('удалённая группа исчезла из списка')
      .toBeFalse();
    expect(notifications.notifyError).not.toHaveBeenCalled();
  });

  it('delete: «No» закрывает подтверждение, мок не вызывается', async () => {
    rowByName('ИК-222').querySelector<HTMLButtonElement>('button[aria-label="Удалить"]')!.click();
    fixture.detectChanges();

    root().querySelector<HTMLButtonElement>('.p-confirmdialog-reject-button')!.click();
    await flush();

    expect(groupsService.remove).not.toHaveBeenCalled();
    expect(notifications.notifySuccess).not.toHaveBeenCalled();
    expect(root().querySelector('.p-confirmdialog-message')).toBeNull();
  });

  it('отказ getList при загрузке — уведомление с якорем header (IF-109)', async () => {
    // Новый fixture с отказом начальной загрузки: пересобираем TestBed-статус
    // через собственный createComponent — заглушка уже заменена ниже.
    groupsService.getList.and.rejectWith(apiError(401, 'Не авторизован'));
    const failing = TestBed.createComponent(GroupsPage);
    fixture = failing;
    await flush();

    expect(notifications.notifyError).toHaveBeenCalledWith('Не авторизован', 'header');
    expect(notifications.notifySuccess).not.toHaveBeenCalled();
  });
});
