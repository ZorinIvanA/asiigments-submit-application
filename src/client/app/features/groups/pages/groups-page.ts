/**
 * Страница «Группы» (C-114, SCR-011, FR-4.5): список групп мока
 * (порядок name↑ без учёта регистра от GroupsService) с диалогами
 * создания и переименования и подтверждением удаления (ADR-111,
 * p-confirmDialog с кнопками «Yes»/«No»).
 *
 * Контракты IF-104 (GroupsService) и IF-109 (уведомления): успехи
 * «Сохранено»/«Удалено» без якоря формы (экран без форм — фиксированно
 * под шапкой), отказы 400 «Данные заполнены неверно» (текст поля из
 * ERROR_TEXTS) и 409 «Группа с таким названием уже существует»
 * показываются внутри диалога (ключевые условия T-116), прочие отказы —
 * баннер под шапкой (якорь 'header') и перечитывание списка.
 */
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ConfirmationService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { TableModule } from 'primeng/table';

import { GroupsService } from '../../../core/services/groups.service';
import { toApiError } from '../../../shared/api-error';
import { submitting } from '../../../shared/loading/submitting';
import { NotificationService } from '../../../shared/notifications/notification-service';
import { GroupDto } from '../../../shared/models';
import {
  firstErrorText,
  groupName as groupNameFormat,
  requiredTrim,
  trimFormValues,
} from '../../../shared/validation/validators';

/** Форма диалога создания/переименования: единственное поле «Название». */
type NameForm = FormGroup<{ name: FormControl<string> }>;

@Component({
  selector: 'app-groups-page',
  templateUrl: './groups-page.html',
  styleUrl: './groups-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    ReactiveFormsModule,
    DialogModule,
    ConfirmDialogModule,
    TableModule,
    ButtonModule,
    InputTextModule,
  ],
  viewProviders: [ConfirmationService],
})
export class GroupsPage {
  private readonly groupsService = inject(GroupsService);
  private readonly notifications = inject(NotificationService);
  private readonly confirmationService = inject(ConfirmationService);

  /** Список групп (GroupsService.getList, порядок мока). */
  readonly groups = signal<GroupDto[]>([]);

  /** Видимость диалогов создания и переименования. */
  readonly createVisible = signal(false);
  readonly renameVisible = signal(false);
  /** Переименовываемая группа; null — диалог не открыт. */
  readonly renameTarget = signal<GroupDto | null>(null);

  /**
   * Ошибки открытого диалога (диалоги не пересекаются, поэтому пара
   * сигналов общая): текст поля «Название» (клиентская валидация или
   * 400 errors.name) и сообщение мока внутри диалога (409 и 400 без
   * ошибки поля).
   */
  readonly nameError = signal<string | null>(null);
  readonly dialogError = signal<string | null>(null);

  readonly createForm: NameForm = new FormGroup({
    name: new FormControl<string>('', {
      nonNullable: true,
      validators: [requiredTrim(), groupNameFormat()],
    }),
  });
  readonly renameForm: NameForm = new FormGroup({
    name: new FormControl<string>('', {
      nonNullable: true,
      validators: [requiredTrim(), groupNameFormat()],
    }),
  });

  /** Индикатор отправки диалога: [loading]/[disabled] кнопки действия. */
  readonly createSubmit = submitting();
  readonly renameSubmit = submitting();

  constructor() {
    void this.reload();
  }

  openCreate(): void {
    this.resetDialogErrors();
    this.createForm.reset({ name: '' });
    this.createVisible.set(true);
  }

  openRename(group: GroupDto): void {
    this.resetDialogErrors();
    this.renameTarget.set(group);
    this.renameForm.reset({ name: group.name });
    this.renameVisible.set(true);
  }

  cancelCreate(): void {
    this.createVisible.set(false);
  }

  cancelRename(): void {
    this.renameVisible.set(false);
  }

  /** «Создать» (диалог создания): успех — «Сохранено» и перечитывание списка. */
  async submitCreate(): Promise<void> {
    const name = this.takeValidatedName(this.createForm);
    if (name === null) {
      return;
    }
    await this.createSubmit.run(async () => {
      try {
        await this.groupsService.create(name);
        this.createVisible.set(false);
        this.notifications.notifySuccess('Сохранено');
        await this.reload();
      } catch (error) {
        this.showDialogFailure(error);
      }
    });
  }

  /** «Сохранить» (диалог переименования): те же правила, что и в создании. */
  async submitRename(): Promise<void> {
    const target = this.renameTarget();
    const name = this.takeValidatedName(this.renameForm);
    if (target === null || name === null) {
      return;
    }
    await this.renameSubmit.run(async () => {
      try {
        await this.groupsService.rename(target.id, name);
        this.renameVisible.set(false);
        this.notifications.notifySuccess('Сохранено');
        await this.reload();
      } catch (error) {
        this.showDialogFailure(error);
      }
    });
  }

  /**
   * «🗑 Удалить» — подтверждение ADR-111: сообщение с именем группы
   * дословно FR-017, кнопки «Yes»/«No» из перевода PRIME_NG_RU.
   */
  confirmRemove(group: GroupDto): void {
    this.confirmationService.confirm({
      header: 'Удаление группы',
      message: `Вы точно хотите удалить группу ${group.name}?`,
      accept: () => void this.remove(group),
    });
  }

  /** «Yes» в подтверждении: успех — «Удалено», список перечитывается. */
  private async remove(group: GroupDto): Promise<void> {
    try {
      await this.groupsService.remove(group.id);
      this.notifications.notifySuccess('Удалено');
    } catch (error) {
      this.notifications.notifyError(toApiError(error).body.message, 'header');
    }
    await this.reload();
  }

  /**
   * Клиентская валидация формы диалога перед отправкой: ошибка поля
   * показывается внутри диалога; триммированное имя — наружу, null —
   * отправку прервать.
   */
  private takeValidatedName(form: NameForm): string | null {
    this.resetDialogErrors();
    if (form.invalid) {
      this.nameError.set(firstErrorText(form.controls.name.errors));
      return null;
    }
    trimFormValues(form);
    return form.controls.name.value ?? '';
  }

  /**
   * Отказ создания/переименования (IF-104): 400 — текст поля в диалоге,
   * 409 (и 400 без errors.name) — баннер в диалоге; прочие статусы —
   * баннер под шапкой, диалог закрывается, список перечитывается.
   */
  private showDialogFailure(error: unknown): void {
    const apiError = toApiError(error);
    const fieldError =
      apiError.status === 400 ? apiError.body.errors?.['name']?.[0] : undefined;
    if (fieldError !== undefined) {
      this.nameError.set(fieldError);
      return;
    }
    if (apiError.status === 400 || apiError.status === 409) {
      this.dialogError.set(apiError.body.message);
      return;
    }
    this.createVisible.set(false);
    this.renameVisible.set(false);
    this.notifications.notifyError(apiError.body.message, 'header');
    void this.reload();
  }

  private resetDialogErrors(): void {
    this.nameError.set(null);
    this.dialogError.set(null);
  }

  /** Перечитывание списка (после мутаций и при открытии экрана). */
  private async reload(): Promise<void> {
    try {
      this.groups.set(await this.groupsService.getList());
    } catch (error) {
      this.notifications.notifyError(toApiError(error).body.message, 'header');
    }
  }
}
