/**
 * Шаг 1 восстановления пароля — /recovery (C-110, SCR-003, FR-4.2/US-2,
 * IF-101/IF-102/IF-109): карточка с полем «Email» и кнопками
 * «Отмена»/«Отправить код». Клиентская валидация — баннер «Данные
 * заполнены неверно» + тексты полей из словаря ERROR_TEXTS (IF-010,
 * единообразно с /login и /register); запрос к моку не выполняется.
 *
 * Отказ запроса кода — только 429 «Слишком много попыток. Повторите
 * позже» (IF-101): баннер дословно с якорем 'recovery-form' (IF-109).
 * Успех и незарегистрированный email неразличимы (§9, всегда 200):
 * email запоминает AuthService в RecoveryFlowStore (IF-102), страница
 * переходит на шаг 2 — /recovery/code. «Отмена» — полная очистка потока
 * (store.clear, IF-102) и возврат на /login. Перед DTO значение
 * триммится (trimFormValues, IF-010); на время запроса кнопка в
 * состоянии loading (submitting, FR-025).
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
  emailFormat,
  firstErrorText,
  requiredTrim,
  trimFormValues,
} from '../../../../shared/validation/validators';

/** Имена полей формы шага 1. */
type RecoveryField = 'email';

@Component({
  selector: 'app-recovery-page',
  imports: [ReactiveFormsModule, ButtonModule, InputTextModule, NotificationAnchor],
  templateUrl: './recovery-page.html',
  styleUrl: './recovery-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RecoveryPage {
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly auth = inject(AuthService);
  private readonly flow = inject(RecoveryFlowStore);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationService);

  /** Идентификатор формы и якоря мобильных уведомлений (IF-109). */
  readonly formId = 'recovery-form';

  /** Текст баннера клиентской валидации (§8, IF-101 invalidData). */
  readonly invalidDataText = 'Данные заполнены неверно';

  /** Правила — те же валидаторы, что применяет мок (§8). */
  readonly form = this.fb.group({
    email: ['', [requiredTrim(), emailFormat()]],
  });

  /** Тексты полей показываются после первой попытки отправки (макет SCR-003). */
  private readonly attempted = signal(false);

  /** Состояние отправки: loading кнопки + защита от повторного сабмита. */
  readonly submitState = submitting();

  /** Текст поля из словаря ERROR_TEXTS; null — поле не подсвечивается. */
  errorTextOf(field: RecoveryField): string | null {
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
    const email = this.form.getRawValue().email;
    await this.submitState.run(async () => {
      try {
        // Всегда 200 (и для незарегистрированного email — §9): email в
        // store ставит AuthService (IF-102); ответа страница не различает.
        await this.auth.requestRecoveryCode(email);
        await this.router.navigateByUrl('/recovery/code');
      } catch (error: unknown) {
        this.notifications.notifyError(toApiError(error).body.message, { formId: this.formId });
      }
    });
  }
}
