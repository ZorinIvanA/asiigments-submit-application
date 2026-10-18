/**
 * Форма лабораторной работы — один компонент для маршрутов /works/new и
 * /works/:id/edit (ADR-103: маршрутная форма вместо Dialog; карточка в стиле
 * макетов 400px, C-111, контракты IF-103/IF-109).
 *
 * Режим определяется параметром маршрута id: без id — создание, с id —
 * редактирование (labs.getById; 404 «Лабораторная не найдена» → баннер
 * и возврат на /works, IF-103; тот же путь — при 404 на сохранение:
 * запись удалена, пока форма была открыта, аменда 5/CR-001). Клиентская
 * валидация — validators.ts,
 * тексты полей — ERROR_TEXTS: нарушение → баннер «Данные заполнены неверно»
 * с якорем формы 'lab-form' и подсветкой полей. Отказы мока показываются
 * дословно: 409 «Лабораторная с таким номером уже есть в семестре» и др.
 * Успех — «Сохранено» и переход на /works.
 *
 * «Ссылка на задание» необязательна: кнопка-цепочка (тултип «Нажмите на эту
 * кнопку чтобы добавить или изменить ссылку») открывает поле URL с
 * валидатором labUrl; пустое значение уходит в DTO как null (модель Lab).
 *
 * Правила IF-010 соблюдены: перед построением DTO вызывается
 * trimFormValues, тексты полей читаются только из ERROR_TEXTS
 * (через firstErrorText), флаг busy — хелпер submitting (FR-025).
 */
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TooltipModule } from 'primeng/tooltip';

import { LabsService } from '../../../../core/services/labs.service';
import type { LabInput } from '../../../../core/services/labs.service';
import { toApiError } from '../../../../shared/api-error';
import { submitting } from '../../../../shared/loading/submitting';
import { LabDto, MAX_SEMESTER } from '../../../../shared/models';
import { NotificationAnchor } from '../../../../shared/notifications/notification-anchor';
import type { NotificationAnchor as NotificationAnchorType } from '../../../../shared/notifications/notification-model';
import { NotificationService } from '../../../../shared/notifications/notification-service';
import {
  firstErrorText,
  labContent,
  labNumber,
  labSemester,
  labUrl,
  requiredTrim,
  trimFormValues,
} from '../../../../shared/validation/validators';

/** Текст тултипа кнопки-цепочки — дословно по подзадаче (макета формы нет). */
const LINK_BUTTON_TOOLTIP = 'Нажмите на эту кнопку чтобы добавить или изменить ссылку';

/** Подпись блока «Ссылка на задание» (метка поля и имя кнопки-цепочки). */
const URL_FIELD_LABEL = 'Ссылка на задание';

/** Баннер клиентской валидации (FR-023: единый текст всех форм). */
const INVALID_FORM_MESSAGE = 'Данные заполнены неверно';

/** Маршрут списка работ — точка возврата формы («Назад» и после успеха). */
const WORKS_ROUTE = '/works';

/** Идентификатор формы и якоря мобильных уведомлений (IF-109). */
const FORM_ID = 'lab-form';

/** Якорь мобильного баннера этой формы (IF-109). */
const FORM_ANCHOR: NotificationAnchorType = { formId: FORM_ID };

/** Типизированные контролы формы лабораторной (IF-103: LabInput). */
export interface LabFormControls {
  /** Номер: строка ввода либо число (после загрузки записи). */
  number: FormControl<string | number>;
  /** Семестр 1..MAX_SEMESTER; null — не выбран. */
  semester: FormControl<number | null>;
  /** Содержание работы, 1–500 символов после трима. */
  content: FormControl<string>;
  /** Признак «Нужна защита». */
  defenseRequired: FormControl<boolean>;
  /** Ссылка на задание; пустая строка → null в DTO. */
  assignmentUrl: FormControl<string>;
}

@Component({
  selector: 'app-lab-form-page',
  templateUrl: './lab-form-page.html',
  styleUrl: './lab-form-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    ButtonModule,
    CheckboxModule,
    InputTextModule,
    SelectModule,
    TooltipModule,
    NotificationAnchor,
  ],
})
export class LabFormPage {
  private readonly labs = inject(LabsService);
  private readonly notifications = inject(NotificationService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  /** Перечень семестров селектора: 1..MAX_SEMESTER (§4.3). */
  protected readonly semesters: number[] = Array.from(
    { length: MAX_SEMESTER },
    (_, index) => index + 1,
  );

  /** Тултип кнопки-цепочки — константа шаблона. */
  protected readonly linkButtonTooltip = LINK_BUTTON_TOOLTIP;

  /** Подпись блока «Ссылка на задание»: метка поля и имя кнопки-цепочки. */
  protected readonly urlFieldLabel = URL_FIELD_LABEL;

  /** Идентификатор формы и якоря мобильных уведомлений (IF-109). */
  readonly formId = FORM_ID;

  /** Флаг отправки/загрузки: индикатор и блокировка «Сохранить» (FR-025). */
  protected readonly busy = computed(
    () => this.saveState.busy() || this.loadingInitial(),
  );

  private readonly saveState = submitting();

  /** Первый запрос getById выполняется. */
  private readonly loadingInitial = signal(false);

  /** uuid правимой записи; null — режим создания. */
  private readonly labId = signal<string | null>(null);

  /** true — режим редактирования (заголовок и обработчик «Сохранить»). */
  protected readonly isEdit = computed(() => this.labId() !== null);

  /** Заголовок карточки — по режиму (макета формы нет, стиль SCR-001). */
  protected readonly title = computed(() =>
    this.isEdit() ? 'Редактирование лабораторной работы' : 'Новая лабораторная работа',
  );

  /** Поле URL показано после клика по кнопке-цепочке либо когда ссылка есть. */
  protected readonly urlVisible = signal(false);

  protected readonly form = new FormGroup<LabFormControls>({
    number: new FormControl<string | number>('', {
      nonNullable: true,
      validators: [requiredTrim(), labNumber()],
    }),
    semester: new FormControl<number | null>(null, [requiredTrim(), labSemester()]),
    content: new FormControl<string>('', {
      nonNullable: true,
      validators: [requiredTrim(), labContent()],
    }),
    defenseRequired: new FormControl<boolean>(false, { nonNullable: true }),
    assignmentUrl: new FormControl<string>('', { nonNullable: true, validators: [labUrl()] }),
  });

  constructor() {
    // Один компонент на оба маршрута: paramMap отрабатывает и первичную
    // навигацию, и переключение «новая ↔ редактирование» без пересоздания.
    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      const id = params.get('id');
      this.labId.set(id);
      this.urlVisible.set(false);
      this.resetForm();
      if (id !== null) {
        void this.load(id);
      }
    });
  }

  /** Текст первой ошибки поля из словаря; null — поле не подсвечивается. */
  protected errorText(name: keyof LabFormControls): string | null {
    const control = this.form.controls[name];
    if (!control.touched && !control.dirty) {
      return null;
    }
    return firstErrorText(control.errors);
  }

  /** Кнопка-цепочка: открывает поле URL («добавить или изменить ссылку»). */
  protected openUrlField(): void {
    this.urlVisible.set(true);
  }

  /** «Назад» — возврат к списку без сохранения. */
  protected back(): void {
    void this.router.navigate([WORKS_ROUTE]);
  }

  /** «Сохранить»: клиентская валидация → DTO → create/update (IF-103). */
  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.notifications.notifyError(INVALID_FORM_MESSAGE, FORM_ANCHOR);
      return;
    }
    // К моку уходят триммированные значения (IF-010).
    trimFormValues(this.form);
    const input = this.buildInput();
    await this.saveState.run(async () => {
      try {
        const id = this.labId();
        if (id === null) {
          await this.labs.create(input);
        } else {
          await this.labs.update(id, input);
        }
      } catch (error) {
        const failure = toApiError(error);
        if (failure.status === 404) {
          // Запись исчезла, пока форма была открыта: мёртвая форма не
          // остаётся на экране (аменда 5/CR-001) — путь ветки загрузки.
          await this.leaveToWorks(failure.body.message);
          return;
        }
        this.notifications.notifyError(failure.body.message, FORM_ANCHOR);
        return;
      }
      this.notifications.notifySuccess('Сохранено');
      await this.router.navigate([WORKS_ROUTE]);
    });
  }

  /** DTO формы: числа, трим по правилам IF-010, пустая ссылка → null. */
  private buildInput(): LabInput {
    const raw = this.form.getRawValue();
    const url = raw.assignmentUrl.trim();
    return {
      number: Number(raw.number),
      semester: Number(raw.semester),
      content: raw.content,
      assignmentUrl: url === '' ? null : url,
      defenseRequired: raw.defenseRequired === true,
    };
  }

  /**
   * Запись не существует (404, IF-103/аменда 5): баннер дословно под шапкой —
   * экран покидается, якорю формы нечего хостить после редиректа — и возврат
   * к списку. Общий путь веток загрузки (load) и сохранения (save).
   */
  private async leaveToWorks(message: string): Promise<void> {
    this.notifications.notifyError(message);
    await this.router.navigate([WORKS_ROUTE]);
  }

  /** Загрузка правимой записи: 404 → баннер (якорь 'header') и /works. */
  private async load(id: string): Promise<void> {
    this.loadingInitial.set(true);
    try {
      const lab: LabDto = await this.labs.getById(id);
      if (this.labId() === id) {
        this.form.setValue({
          number: lab.number,
          semester: lab.semester,
          content: lab.content,
          defenseRequired: lab.defenseRequired,
          assignmentUrl: lab.assignmentUrl ?? '',
        });
        // Существующая ссылка сразу видна — «изменить ссылку» (тултип).
        this.urlVisible.set(lab.assignmentUrl !== null);
      }
    } catch (error) {
      if (this.labId() === id) {
        await this.leaveToWorks(toApiError(error).body.message);
      }
    } finally {
      if (this.labId() === id) {
        this.loadingInitial.set(false);
      }
    }
  }

  /** Чистая форма режима создания (и сброс при смене маршрута). */
  private resetForm(): void {
    this.form.reset({
      number: '',
      semester: null,
      content: '',
      defenseRequired: false,
      assignmentUrl: '',
    });
  }
}
