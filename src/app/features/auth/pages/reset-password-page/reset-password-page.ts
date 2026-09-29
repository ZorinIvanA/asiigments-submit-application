/**
 * Шаг 3 восстановления пароля — /reset-password (C-110, SCR-005,
 * FR-4.2/US-4, FR-4.10, IF-101/IF-102/IF-109): поля «Новый пароль»/
 * «Повторите пароль», требования к паролю выведены на экран, кнопки
 * «Отмена»/«Сменить пароль». resetToken берётся из RecoveryFlowStore
 * (IF-102) — на экране не показывается.
 *
 * Клиентская валидация — requiredTrim + passwordRules + passwordMatch
 * (IF-010): баннер «Данные заполнены неверно» + тексты полей из словаря
 * ERROR_TEXTS, запрос к моку не выполняется.
 *
 * Успех: пароль изменён, AuthService полностью очищает поток (IF-102),
 * страница возвращает на /login БЕЗ автологина (§4.2); уведомления об
 * успехе нет (как на /register).
 *
 * Терминальная ветка (FR-010/ISS-103): 400 «Ссылка восстановления
 * недействительна или истекла» — баннер дословно с якорем
 * 'reset-password-form' (IF-109), ЭКРАН ОСТАЁТСЯ ОТКРЫТЫМ с появляющейся
 * ссылкой «Запросить код заново» → /recovery; автоматического перехода
 * нет. Токен при этой ошибке гасится и удаляется из store, email
 * сохраняется — удаляет его AuthService (IF-101/IF-102); страница
 * распознаёт ветку по литералу AUTH_ERRORS.resetLinkInvalid,
 * реэкспортированному сервисом (дубликата литерала нет — ревью
 * CR-001 T-111). Повторная отправка того же погашенного токена даёт ту же
 * ошибку (переход потока token_issued → idle, ADR-106).
 */
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';

import { AUTH_ERRORS, AuthService } from '../../../../core/services/auth.service';
import { RecoveryFlowStore } from '../../../../core/services/recovery-flow-store';
import { toApiError } from '../../../../shared/api-error';
import { submitting } from '../../../../shared/loading/submitting';
import { NotificationAnchor } from '../../../../shared/notifications/notification-anchor';
import { NotificationService } from '../../../../shared/notifications/notification-service';
import {
  firstErrorText,
  passwordMatch,
  passwordRules,
  requiredTrim,
  trimFormValues,
} from '../../../../shared/validation/validators';

/** Имена полей формы шага 3. */
type ResetPasswordField = 'password' | 'repeatPassword';

@Component({
  selector: 'app-reset-password-page',
  imports: [ReactiveFormsModule, RouterLink, ButtonModule, InputTextModule, NotificationAnchor],
  templateUrl: './reset-password-page.html',
  styleUrl: './reset-password-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ResetPasswordPage {
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly auth = inject(AuthService);
  private readonly flow = inject(RecoveryFlowStore);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationService);

  /** Идентификатор формы и якоря мобильных уведомлений (IF-109). */
  readonly formId = 'reset-password-form';

  /** Текст баннера клиентской валидации (§8, IF-101 invalidData). */
  readonly invalidDataText = 'Данные заполнены неверно';

  /** Правила — те же валидаторы, что применяет мок; пара — passwordMatch. */
  readonly form = this.fb.group(
    {
      password: ['', [requiredTrim(), passwordRules()]],
      repeatPassword: ['', [requiredTrim()]],
    },
    { validators: [passwordMatch('password', 'repeatPassword')] },
  );

  /** Тексты полей показываются после первой попытки отправки (макет SCR-005). */
  private readonly attempted = signal(false);

  /** Состояние отправки: loading кнопки + защита от повторного сабмита. */
  readonly submitState = submitting();

  /**
   * Терминальная ветка токена (FR-010): после отказа «Ссылка…» экран
   * остаётся открытым и показывает ссылку «Запросить код заново».
   */
  private readonly resetLinkInvalid = signal(false);

  /**
   * Текст поля из словаря ERROR_TEXTS; для повтора — ещё и групповое
   * правило «Пароли не совпадают». null — поле не подсвечивается.
   */
  errorTextOf(field: ResetPasswordField): string | null {
    if (!this.attempted()) {
      return null;
    }
    const own = firstErrorText(this.form.controls[field].errors);
    if (own !== null) {
      return own;
    }
    return field === 'repeatPassword' ? firstErrorText(this.form.errors) : null;
  }

  /** Ссылка «Запросить код заново» показывается только в терминальной ветке. */
  isResetLinkInvalid(): boolean {
    return this.resetLinkInvalid();
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
    const resetToken = this.flow.resetToken() ?? '';
    const { password, repeatPassword } = this.form.getRawValue();
    await this.submitState.run(async () => {
      try {
        // Успех: AuthService полностью очищает поток (IF-102); автологина
        // нет (§4.2) — возврат на вход.
        await this.auth.resetPassword(resetToken, password, repeatPassword);
        await this.router.navigateByUrl('/login');
      } catch (error: unknown) {
        const apiError = toApiError(error);
        if (
          apiError.status === 400 &&
          apiError.body.message === AUTH_ERRORS.resetLinkInvalid
        ) {
          // Терминальная ветка (FR-010): экран остаётся открытым. Токен из
          // store удаляет AuthService (IF-102), email сохраняется —
          // отдельной подстраховки страницы не требуется (ревью CR-001 T-111).
          this.resetLinkInvalid.set(true);
        }
        this.notifications.notifyError(apiError.body.message, { formId: this.formId });
      }
    });
  }
}
