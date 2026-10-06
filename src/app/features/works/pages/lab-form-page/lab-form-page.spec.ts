/**
 * Юнит-тесты LabFormPage (T-113, C-111, контракты IF-103/IF-109):
 *  - один компонент на оба маршрута: создание (без id) и редактирование
 *    (getById-загрузка значений, 404 → баннер и /works);
 *  - клиентская валидация: баннер «Данные заполнены неверно» с якорем
 *    'lab-form', тексты полей из ERROR_TEXTS, сервис не вызывался;
 *  - отказы мока дословно: 409 «Лабораторная с таким номером уже есть
 *    в семестре», 404 «Лабораторная не найдена»;
 *  - кнопка-цепочка: тултип, появление поля URL, пустой URL → null в DTO;
 *  - кнопки: «Сохранить» в loading (submitting), «Назад» без сохранения;
 *  - селект семестра 1..MAX_SEMESTER; trimFormValues перед построением DTO.
 *
 * Мок-слой не поднимается: LabsService шпионируется, отказы — ApiError
 * (транспортная форма FR-003); маршрутизация — provideRouter([]) со шпионом
 * на Router.navigate; режим уведомлений — MockBreakpointObserver.
 */
import { BreakpointObserver } from '@angular/cdk/layout';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormGroup } from '@angular/forms';
import { By } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { Select } from 'primeng/select';
import { Tooltip } from 'primeng/tooltip';
import { of } from 'rxjs';

import { LabsService } from '../../../../core/services/labs.service';
import { MockBreakpointObserver } from '../../../../../testing/mock-breakpoint-observer';
import { ApiError, LabDto } from '../../../../shared/models';
import { NotificationService } from '../../../../shared/notifications/notification-service';
import { LabFormControls, LabFormPage } from './lab-form-page';

const LINK_TOOLTIP = 'Нажмите на эту кнопку чтобы добавить или изменить ссылку';
const NOT_FOUND_MESSAGE = 'Лабораторная не найдена';
const DUPLICATE_MESSAGE = 'Лабораторная с таким номером уже есть в семестре';
const INVALID_FORM_MESSAGE = 'Данные заполнены неверно';

/** Работа из сида — грузится getById в режиме редактирования. */
const ASSIGNMENT_URL = 'https://git.example.com/assignments/2/5';
const LAB: LabDto = {
  id: 'lab-1',
  number: 5,
  semester: 2,
  content: 'Содержание лабораторной работы №5',
  assignmentUrl: ASSIGNMENT_URL,
  defenseRequired: true,
};

function apiError(status: number, message: string): ApiError {
  return { status, body: { message } };
}

/** Доступ к protected-членам компонента из спецификации (форма). */
function formOf(component: LabFormPage): FormGroup<LabFormControls> {
  return (component as unknown as { form: FormGroup<LabFormControls> }).form;
}

/** Приводит компонент к стабильному состоянию: микротаски + CD. */
async function settled(fixture: ComponentFixture<LabFormPage>): Promise<void> {
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
}

function root(fixture: ComponentFixture<LabFormPage>): HTMLElement {
  return fixture.nativeElement as HTMLElement;
}

function buttonByLabel(fixture: ComponentFixture<LabFormPage>, label: string): HTMLButtonElement {
  const element = root(fixture);
  const buttons = Array.from(element.querySelectorAll<HTMLButtonElement>('button'));
  const button = buttons.find((candidate) => (candidate.textContent ?? '').trim() === label);
  expect(button).withContext(`кнопка «${label}»`).toBeDefined();
  return button!;
}

/** Имитация ввода пользователя в поле (событие input для ReactiveForms). */
function setInputValue(fixture: ComponentFixture<LabFormPage>, selector: string, value: string): void {
  const input = root(fixture).querySelector<HTMLInputElement>(selector);
  expect(input).withContext(selector).toBeDefined();
  input!.value = value;
  input!.dispatchEvent(new Event('input', { bubbles: true }));
}

function linkButton(fixture: ComponentFixture<LabFormPage>): HTMLButtonElement {
  const button = root(fixture).querySelector<HTMLButtonElement>(
    'button[aria-label="Ссылка на задание"]',
  );
  expect(button).withContext('кнопка-цепочка «Ссылка на задание»').toBeDefined();
  return button!;
}

/** Открытие панели селектора и выбор семестра кликом по опции. */
function chooseSemester(fixture: ComponentFixture<LabFormPage>, value: number): void {
  const select = fixture.debugElement.query(By.directive(Select))!.injector.get(Select);
  select.show();
  fixture.detectChanges();
  const option = Array.from(
    root(fixture).querySelectorAll<HTMLElement>('.p-select-option'),
  ).find((candidate) => (candidate.textContent ?? '').trim() === String(value));
  expect(option).withContext(`опция семестра «${value}»`).toBeDefined();
  option!.click();
  fixture.detectChanges();
}

describe('LabFormPage — форма лабораторной (T-113)', () => {
  function setup(params: Record<string, string> = {}): {
    start: () => ComponentFixture<LabFormPage>;
    labs: LabsService;
    notifications: NotificationService;
    router: Router;
  } {
    const paramMap = convertToParamMap(params);
    TestBed.configureTestingModule({
      imports: [LabFormPage],
      providers: [
        { provide: BreakpointObserver, useValue: new MockBreakpointObserver() },
        provideRouter([]),
        provideNoopAnimations(),
        // ВАЖНО: строго после provideRouter — ROUTER_PROVIDERS объявляет свой
        // ActivatedRoute (rootRoute), и последний провайдер по токену выигрывает.
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap }, paramMap: of(paramMap) } as unknown as ActivatedRoute,
        },
      ],
    }).compileComponents();

    const labs = TestBed.inject(LabsService);
    const notifications = TestBed.inject(NotificationService);
    spyOn(notifications, 'notifyError');
    spyOn(notifications, 'notifySuccess');
    const router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
    // Конструктор компонента подписывается на paramMap синхронно: в режиме
    // редактирования getById вызывается сразу при создании компонента, поэтому
    // шпион и его стратегия обязаны быть установлены ДО start(). Возвращаем
    // фабрику start(), а не готовую фикстуру.
    spyOn(labs, 'getById');

    return {
      start: () => TestBed.createComponent(LabFormPage),
      labs,
      notifications,
      router,
    };
  }

  describe('режим создания (/works/new)', () => {
    it('чистая форма: поле ссылки скрыто, защита выключена, «Сохранить» активна', async () => {
      const { labs, start } = setup();
      const fixture = start();
      await settled(fixture);
      const element = root(fixture);

      expect(element.querySelector('#lab-url')).toBeNull();
      // CR-002: форма помечена идентификатором якоря (конвенция auth-форм).
      expect(element.querySelector('form#lab-form')).not.toBeNull();
      // CR-003: метка «Ссылка на задание» рендерится только вместе с input.
      expect(element.querySelector('label[for="lab-url"]')).toBeNull();
      expect(element.querySelectorAll('button[aria-label="Ссылка на задание"]').length).toBe(1);
      expect(formOf(fixture.componentInstance).controls.defenseRequired.value).toBeFalse();
      expect(formOf(fixture.componentInstance).controls.number.value).toBe('');
      expect(buttonByLabel(fixture, 'Сохранить').disabled).toBeFalse();
      expect(labs.getById).not.toHaveBeenCalled();
    });

    it('create-success: валидная форма вызывает create с DTO, «Сохранено», редирект /works', async () => {
      const { labs, notifications, router, start } = setup();
      const fixture = start();
      const create = spyOn(labs, 'create').and.resolveTo({ ...LAB });
      await settled(fixture);
      const element = root(fixture);

      setInputValue(fixture, '#lab-number', '7');
      chooseSemester(fixture, 3);
      setInputValue(fixture, '#lab-content', 'Содержание работы №7');
      element.querySelector<HTMLInputElement>('.lab-form__field--checkbox input')!.click();
      fixture.detectChanges();
      linkButton(fixture).click();
      fixture.detectChanges();
      setInputValue(fixture, '#lab-url', 'https://git.example.com/tasks/7');

      buttonByLabel(fixture, 'Сохранить').click();
      await settled(fixture);

      expect(create).toHaveBeenCalledTimes(1);
      expect(create).toHaveBeenCalledWith({
        number: 7,
        semester: 3,
        content: 'Содержание работы №7',
        assignmentUrl: 'https://git.example.com/tasks/7',
        defenseRequired: true,
      });
      expect(notifications.notifySuccess).toHaveBeenCalledWith('Сохранено');
      expect(notifications.notifyError).not.toHaveBeenCalled();
      expect(router.navigate).toHaveBeenCalledWith(['/works']);
    });

    it('пустой URL уходит в DTO как null (assignmentUrl nullable)', async () => {
      const { labs, start } = setup();
      const fixture = start();
      const create = spyOn(labs, 'create').and.resolveTo({ ...LAB });
      await settled(fixture);

      linkButton(fixture).click();
      fixture.detectChanges();
      formOf(fixture.componentInstance).setValue({
        number: 3,
        semester: 1,
        content: 'Содержание',
        defenseRequired: false,
        assignmentUrl: '',
      });
      buttonByLabel(fixture, 'Сохранить').click();
      await settled(fixture);

      expect(create).toHaveBeenCalledWith({
        number: 3,
        semester: 1,
        content: 'Содержание',
        assignmentUrl: null,
        defenseRequired: false,
      });
    });

    it('trimFormValues: строки триммятся, номер из строки — число, URL из пробелов → null', async () => {
      const { labs, start } = setup();
      const fixture = start();
      const create = spyOn(labs, 'create').and.resolveTo({ ...LAB });
      await settled(fixture);

      formOf(fixture.componentInstance).setValue({
        number: ' 3 ',
        semester: 1,
        content: '  Содержание работы  ',
        defenseRequired: false,
        assignmentUrl: '   ',
      });
      buttonByLabel(fixture, 'Сохранить').click();
      await settled(fixture);

      expect(create).toHaveBeenCalledWith({
        number: 3,
        semester: 1,
        content: 'Содержание работы',
        assignmentUrl: null,
        defenseRequired: false,
      });
    });

    it('client-validation: номер 0 и пустое содержание — баннер, тексты полей, сервис не вызывался', async () => {
      const { labs, notifications, router, start } = setup();
      const fixture = start();
      const create = spyOn(labs, 'create');
      await settled(fixture);

      setInputValue(fixture, '#lab-number', '0');
      buttonByLabel(fixture, 'Сохранить').click();
      fixture.detectChanges();

      expect(notifications.notifyError).toHaveBeenCalledWith(INVALID_FORM_MESSAGE, {
        formId: 'lab-form',
      });
      expect(create).not.toHaveBeenCalled();
      expect(router.navigate).not.toHaveBeenCalled();
      expect(root(fixture).textContent).toContain('Номер должен быть положительным числом');
      expect(root(fixture).textContent).toContain('Заполните поле');
    });

    it('client-validation: неверная ссылка — текст «Ссылка должна начинаться с http:// или https://»', async () => {
      const { labs, notifications, start } = setup();
      const fixture = start();
      const create = spyOn(labs, 'create');
      await settled(fixture);

      linkButton(fixture).click();
      fixture.detectChanges();
      formOf(fixture.componentInstance).setValue({
        number: 2,
        semester: 1,
        content: 'Содержание',
        defenseRequired: false,
        assignmentUrl: 'ftp://example.com/file',
      });
      buttonByLabel(fixture, 'Сохранить').click();
      fixture.detectChanges();

      expect(root(fixture).textContent).toContain(
        'Ссылка должна начинаться с http:// или https://',
      );
      expect(notifications.notifyError).toHaveBeenCalledWith(INVALID_FORM_MESSAGE, {
        formId: 'lab-form',
      });
      expect(create).not.toHaveBeenCalled();
    });

    it('client-validation: семестр вне 1..10 и содержание длиннее 500 — тексты словаря', async () => {
      const { labs, notifications, start } = setup();
      const fixture = start();
      const create = spyOn(labs, 'create');
      await settled(fixture);

      formOf(fixture.componentInstance).setValue({
        number: 1,
        semester: 11,
        content: 'х'.repeat(501),
        defenseRequired: false,
        assignmentUrl: '',
      });
      buttonByLabel(fixture, 'Сохранить').click();
      fixture.detectChanges();

      expect(root(fixture).textContent).toContain('Семестр — число от 1 до 10');
      expect(root(fixture).textContent).toContain('Содержание — от 1 до 500 символов');
      expect(create).not.toHaveBeenCalled();
      expect(notifications.notifyError).toHaveBeenCalledWith(INVALID_FORM_MESSAGE, {
        formId: 'lab-form',
      });
    });

    it('conflict-409: баннер с текстом мока дословно, перехода нет', async () => {
      const { labs, notifications, router, start } = setup();
      const fixture = start();
      spyOn(labs, 'create').and.rejectWith(apiError(409, DUPLICATE_MESSAGE));
      await settled(fixture);

      formOf(fixture.componentInstance).setValue({
        number: 1,
        semester: 1,
        content: 'Содержание',
        defenseRequired: false,
        assignmentUrl: '',
      });
      buttonByLabel(fixture, 'Сохранить').click();
      await settled(fixture);

      expect(notifications.notifyError).toHaveBeenCalledWith(DUPLICATE_MESSAGE, {
        formId: 'lab-form',
      });
      expect(notifications.notifySuccess).not.toHaveBeenCalled();
      expect(router.navigate).not.toHaveBeenCalled();
    });

    it('кнопка-цепочка: тултип дословно, клик открывает поле URL и скрывает кнопку', async () => {
      const { start } = setup();
      const fixture = start();
      await settled(fixture);
      const element = root(fixture);

      // Ищем Tooltip именно на кнопке-цепочке: p-select держит собственный
      // внутренний экземпляр директивы с пустой подписью.
      const chainButton = fixture.debugElement.query(
        By.css('button[aria-label="Ссылка на задание"]'),
      );
      expect(chainButton).not.toBeNull();
      const tooltip = chainButton!.injector.get(Tooltip, null);
      expect(tooltip).withContext('директива Tooltip на кнопке-цепочке').not.toBeNull();
      expect(tooltip!.getOption('tooltipLabel')).toBe(LINK_TOOLTIP);

      expect(element.querySelector('#lab-url')).toBeNull();
      linkButton(fixture).click();
      fixture.detectChanges();

      expect(element.querySelector('#lab-url')).not.toBeNull();
      expect(element.querySelector('button[aria-label="Ссылка на задание"]')).toBeNull();
    });

    it('селект «Семестр» содержит опции 1..MAX_SEMESTER (1..10)', async () => {
      const { start } = setup();
      const fixture = start();
      await settled(fixture);

      const select = fixture.debugElement.query(By.directive(Select))!.injector.get(Select);
      select.show();
      fixture.detectChanges();

      const options = Array.from(root(fixture).querySelectorAll('.p-select-option'));
      expect(options.length).toBe(10);
      expect(options.map((option) => (option.textContent ?? '').trim())).toEqual([
        '1',
        '2',
        '3',
        '4',
        '5',
        '6',
        '7',
        '8',
        '9',
        '10',
      ]);
    });

    it('«Назад» ведёт на /works без обращения к сервису', async () => {
      const { labs, router, start } = setup();
      const fixture = start();
      const create = spyOn(labs, 'create');
      await settled(fixture);

      buttonByLabel(fixture, 'Назад').click();

      expect(router.navigate).toHaveBeenCalledWith(['/works']);
      expect(create).not.toHaveBeenCalled();
    });
  });

  describe('режим редактирования (/works/:id/edit)', () => {
    it('edit-load: getById заполняет форму, существующая ссылка раскрыта', async () => {
      const { labs, start } = setup({ id: 'lab-1' });
      const getById = labs.getById as unknown as jasmine.Spy;
      getById.and.resolveTo({ ...LAB });
      const fixture = start();
      await settled(fixture);

      expect(getById).toHaveBeenCalledWith('lab-1');
      const controls = formOf(fixture.componentInstance).controls;
      expect(controls.number.value).toBe(5);
      expect(controls.semester.value).toBe(2);
      expect(controls.content.value).toBe('Содержание лабораторной работы №5');
      expect(controls.defenseRequired.value).toBeTrue();
      expect(controls.assignmentUrl.value).toBe(ASSIGNMENT_URL);
      // CR-003: метка присутствует ровно вместе с раскрытым полем.
      expect(root(fixture).querySelector('label[for="lab-url"]')).not.toBeNull();
      expect(root(fixture).querySelector<HTMLInputElement>('#lab-url')!.value).toBe(
        ASSIGNMENT_URL,
      );
      expect(
        root(fixture).querySelector('button[aria-label="Ссылка на задание"]'),
      ).toBeNull();
    });

    it('edit-load-404: баннер «Лабораторная не найдена» и возврат на /works', async () => {
      const { labs, notifications, router, start } = setup({ id: 'missing' });
      (labs.getById as unknown as jasmine.Spy).and.rejectWith(apiError(404, NOT_FOUND_MESSAGE));
      const fixture = start();
      await settled(fixture);

      expect(notifications.notifyError).toHaveBeenCalledWith(NOT_FOUND_MESSAGE);
      expect(router.navigate).toHaveBeenCalledWith(['/works']);
      // Форма осталась чистой — данные не подставлены.
      expect(formOf(fixture.componentInstance).controls.number.value).toBe('');
      expect(formOf(fixture.componentInstance).controls.semester.value).toBeNull();
    });

    it('edit без ссылки: поле URL скрыто', async () => {
      const { labs, start } = setup({ id: 'lab-1' });
      (labs.getById as unknown as jasmine.Spy).and.resolveTo({ ...LAB, assignmentUrl: null });
      const fixture = start();
      await settled(fixture);

      expect(root(fixture).querySelector('#lab-url')).toBeNull();
      expect(
        root(fixture).querySelector('button[aria-label="Ссылка на задание"]'),
      ).not.toBeNull();
    });

    it('edit-success: «Сохранить» вызывает update с id записи, «Сохранено», /works', async () => {
      const { labs, notifications, router, start } = setup({ id: 'lab-1' });
      (labs.getById as unknown as jasmine.Spy).and.resolveTo({ ...LAB });
      const fixture = start();
      const update = spyOn(labs, 'update').and.resolveTo({ ...LAB });
      spyOn(labs, 'create');
      await settled(fixture);

      setInputValue(fixture, '#lab-content', 'Обновлённое содержание');
      buttonByLabel(fixture, 'Сохранить').click();
      await settled(fixture);

      expect(labs.create).not.toHaveBeenCalled();
      expect(update).toHaveBeenCalledTimes(1);
      expect(update).toHaveBeenCalledWith('lab-1', {
        number: 5,
        semester: 2,
        content: 'Обновлённое содержание',
        assignmentUrl: LAB.assignmentUrl,
        defenseRequired: true,
      });
      expect(notifications.notifySuccess).toHaveBeenCalledWith('Сохранено');
      expect(router.navigate).toHaveBeenCalledWith(['/works']);
    });

    it('edit-conflict-409: баннер «Лабораторная с таким номером уже есть в семестре»', async () => {
      const { labs, notifications, router, start } = setup({ id: 'lab-1' });
      (labs.getById as unknown as jasmine.Spy).and.resolveTo({ ...LAB });
      const fixture = start();
      spyOn(labs, 'update').and.rejectWith(apiError(409, DUPLICATE_MESSAGE));
      await settled(fixture);

      buttonByLabel(fixture, 'Сохранить').click();
      await settled(fixture);

      expect(notifications.notifyError).toHaveBeenCalledWith(DUPLICATE_MESSAGE, {
        formId: 'lab-form',
      });
      expect(router.navigate).not.toHaveBeenCalled();
    });

    it('edit-save-404: запись удалена после загрузки — баннер без formId, /works, submitting сброшен', async () => {
      const { labs, notifications, router, start } = setup({ id: 'lab-1' });
      (labs.getById as unknown as jasmine.Spy).and.resolveTo({ ...LAB });
      const update = spyOn(labs, 'update').and.rejectWith(apiError(404, NOT_FOUND_MESSAGE));
      const fixture = start();
      await settled(fixture);

      buttonByLabel(fixture, 'Сохранить').click();
      await settled(fixture);

      expect(update).toHaveBeenCalledTimes(1);
      // Якорь 'header' (formId=null): экран покидается, хоста у 'lab-form'
      // после редиректа не будет (аменда 5/CR-001).
      expect(notifications.notifyError).toHaveBeenCalledWith(NOT_FOUND_MESSAGE);
      expect(notifications.notifySuccess).not.toHaveBeenCalled();
      expect(router.navigate).toHaveBeenCalledWith(['/works']);
      // submitting сброшен: кнопка снова доступна, индикатора нет.
      const saveButton = buttonByLabel(fixture, 'Сохранить');
      expect(saveButton.disabled).toBeFalse();
      expect(saveButton.classList).not.toContain('p-button-loading');
    });

    it('пока запись грузится, «Сохранить» заблокирована; после загрузки доступна', async () => {
      const { labs, start } = setup({ id: 'lab-1' });
      let resolveGet!: (value: LabDto) => void;
      (labs.getById as unknown as jasmine.Spy).and.returnValue(
        new Promise<LabDto>((resolve) => {
          resolveGet = resolve;
        }),
      );
      const fixture = start();
      fixture.detectChanges();

      const saveButton = buttonByLabel(fixture, 'Сохранить');
      expect(saveButton.disabled).toBeTrue();

      resolveGet({ ...LAB });
      await settled(fixture);

      expect(saveButton.disabled).toBeFalse();
    });
  });

  describe('кнопка «Сохранить» (submitting)', () => {
    it('в состоянии отправки: disabled, класс loading, повторная отправка игнорируется', async () => {
      const { labs, router, start } = setup();
      const fixture = start();
      let resolveCreate!: (value: LabDto) => void;
      const create = spyOn(labs, 'create').and.returnValue(
        new Promise<LabDto>((resolve) => {
          resolveCreate = resolve;
        }),
      );
      await settled(fixture);

      formOf(fixture.componentInstance).setValue({
        number: 9,
        semester: 1,
        content: 'Содержание',
        defenseRequired: false,
        assignmentUrl: '',
      });
      buttonByLabel(fixture, 'Сохранить').click();
      fixture.detectChanges();

      const saveButton = buttonByLabel(fixture, 'Сохранить');
      expect(saveButton.disabled).toBeTrue();
      expect(saveButton.classList).toContain('p-button-loading');

      saveButton.click();
      fixture.detectChanges();
      expect(create).toHaveBeenCalledTimes(1);

      resolveCreate({ ...LAB });
      await settled(fixture);

      expect(saveButton.disabled).toBeFalse();
      expect(router.navigate).toHaveBeenCalledWith(['/works']);
    });
  });
});
