/**
 * Шаг 2 восстановления пароля — /recovery/code (C-110, SCR-004,
 * FR-4.2/US-3, IF-101/IF-102/IF-109): поле «Код восстановления», кнопки
 * «Отмена»/«Ввести код» и ссылка «Переотправить код». Email берётся из
 * RecoveryFlowStore (IF-102) — на экране не показывается.
 *
 * Клиентская валидация кода — requiredTrim + code6 (IF-010): баннер
 * «Данные заполнены неверно» + текст поля из словаря ERROR_TEXTS,
 * запрос к моку не выполняется. Единый отказ подтверждения — 400 «Код
 * восстановления не подходит» (неверный/просроченный/использованный/
 * аннулированный код и незарегистрированный email — §9): баннер дословно
 * с якорем 'recovery-code-form' (IF-109), ЭКРАН ОСТАЁТСЯ ОТКРЫТЫМ
 * (ISS-110/AR-011). У «Ввести код» ошибки 429 нет — только 400 (IF-101).
 *
 * Успех: resetToken запоминает AuthService в RecoveryFlowStore (IF-102),
 * страница переходит на шаг 3 — /reset-password.
 *
 * «Переотправить код» — повторный auth.recovery.request по сохранённому
 * email: экран не покидается (ISS-113); после аннулирования кода 5-й
 * неверной попыткой это единственное продолжение потока (AR-011),
 * store.email сохраняется. У «Отмена» — полный сброс потока и /login.
 */
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';

import { AuthService } from '../../../../core/services/auth.service';
import { RecoveryFlowStore } from '../../../../core/services/recovery-flow-store';
import { toApiError } from '../../../../shared/api-error';
import { submitting } from '../../../../shared/loading/submitting';
import { NotificationAnchor } from '../../../../shared/notifications/notification-anchor';
import { NotificationService } from '../../../../shared/notifications/notification-service';
import {
  code6,
  firstErrorText,
  requiredTrim,
  trimFormValues,
} from '../../../../shared/validation/validators';

/** Имена полей формы шага 2. */
type RecoveryCodeField = 'code';

@Component({
  selector: 'app-recovery-code-page',
  imports: [ReactiveFormsModule, ButtonModule, InputTextModule, NotificationAnchor],
  templateUrl: './recovery-code-page.html',
  styleUrl: './recovery-code-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RecoveryCodePage {
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly auth = inject(AuthService);
  private readonly flow = inject(RecoveryFlowStore);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationService);

  /** Идентификатор формы и якоря мобильных уведомлений (IF-109). */
  readonly formId = 'recovery-code-form';

  /** Текст баннера клиентской валидации (§8, IF-101 invalidData). */
  readonly invalidDataText = 'Данные заполнены неверно';

  /** Правила — те же валидаторы, что применяет мок (§8). */
  readonly form = this.fb.group({
    code: ['', [requiredTrim(), code6()]],
  });

  /** Тексты полей показываются после первой попытки отправки (макет SCR-004). */
  private readonly attempted = signal(false);

  /**
   * Состояние отправки — общее для «Ввести код» и «Переотправить код»:
   * loading кнопок + защита от параллельных запросов одного экрана.
   */
  readonly submitState = submitting();

  /** Текст поля из словаря ERROR_TEXTS; null — поле не подсвечивается. */
  errorTextOf(field: RecoveryCodeField): string | null {
    if (!this.attempted()) {
      return null;
    }
    return firstErrorText(this.form.controls[field].errors);
  }

  /** «Отмена» (IF-102): полный сброс потока и возврат на вход. */
  async cancel(): Promise<void> {
    this.flow.clear();
    await this.router.navigateByUrl('/login');
  }

  async submit(): Promise<void> {
    if (this.submitState.busy()) {
      return;
    }
    if (this.form.invalid) {
      this.attempted.set(true);
      this.notifications.notifyError(this.invalidDataText, { formId: this.formId });
      return;
    }
    trimFormValues(this.form);
    const email = this.flow.email() ?? '';
    const code = this.form.getRawValue().code;
    await this.submitState.run(async () => {
      try {
        // resetToken в store ставит AuthService (IF-102).
        await this.auth.confirmRecoveryCode(email, code);
        await this.router.navigateByUrl('/reset-password');
      } catch (error: unknown) {
        // Единый 400 «Код восстановления не подходит»: экран остаётся
        // открытым, продолжение — «Переотправить код» (IF-101/AR-011).
        this.notifications.notifyError(toApiError(error).body.message, { formId: this.formId });
      }
    });
  }

  /**
   * «Переотправить код»: повторный запрос кода по сохранённому email;
   * при любом исходе экран /recovery/code не покидается (ISS-113).
   */
  async resend(): Promise<void> {
    if (this.submitState.busy()) {
      return;
    }
    const email = this.flow.email() ?? '';
    await this.submitState.run(async () => {
      try {
        await this.auth.requestRecoveryCode(email);
      } catch (error: unknown) {
        this.notifications.notifyError(toApiError(error).body.message, { formId: this.formId });
      }
    });
  }
}
