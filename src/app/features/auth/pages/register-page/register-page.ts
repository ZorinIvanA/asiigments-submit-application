/**
 * Страница регистрации (C-110, SCR-002, FR-4.1/FR-4.10): карточка 400px с
 * полями ФИО/Логин/Email/Пароль/Повторите пароля и подсказками макета;
 * требования к паролю выведены на экран. Роли на форме нет — регистрация
 * всегда создаёт студента (§4.1). Клиентская валидация: баннер «Данные
 * заполнены неверно» + тексты полей из словаря ERROR_TEXTS (IF-010,
 * QG-005 — тексты только из словаря); несовпадение пар (правило пары
 * passwordMatch) показывается у поля повтора. Отказы мока (400 полей,
 * 409 «Пользователь с таким логином/email уже существует», 429 «Слишком
 * много попыток…») — дословно (IF-101) с якорем 'register-form' (IF-109).
 *
 * Успех — автоматический вход (мок ставит сессию, сервис обновляет
 * currentUser) и редирект домашнего маршрута роли (ROLE_HOME): у создаваемого
 * студента это /my-submissions; уведомления об успехе нет (§4.1/US-15).
 * Ссылка «У меня уже есть аккаунт» → /login. Значения перед DTO триммятся
 * (trimFormValues, IF-010); на время запроса кнопка в состоянии loading.
 */
import {
  ChangeDetectionStrategy,
  Component,
  inject,
  signal,
} from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';

import { ROLE_HOME } from '../../../../core/navigation/guards';
import {
  AuthService,
  RegisterParams,
} from '../../../../core/services/auth.service';
import { toApiError } from '../../../../shared/api-error';
import { submitting } from '../../../../shared/loading/submitting';
import { NotificationAnchor } from '../../../../shared/notifications/notification-anchor';
import { NotificationService } from '../../../../shared/notifications/notification-service';
import {
  emailFormat,
  fullName as fullNameRule,
  firstErrorText,
  loginCharset,
  passwordMatch,
  passwordRules,
  requiredTrim,
  trimFormValues,
} from '../../../../shared/validation/validators';

/** Имена полей формы регистрации. */
type RegisterField =
  'fullName' | 'login' | 'email' | 'password' | 'repeatPassword';

@Component({
  selector: 'app-register-page',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    ButtonModule,
    InputTextModule,
    NotificationAnchor,
  ],
  templateUrl: './register-page.html',
  styleUrl: './register-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RegisterPage {
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationService);

  /** Идентификатор формы и якоря мобильных уведомлений (IF-109). */
  readonly formId = 'register-form';

  /** Текст баннера клиентской валидации (§8, IF-101 invalidData). */
  readonly invalidDataText = 'Данные заполнены неверно';

  /**
   * Правила — те же валидаторы, что применяет мок (§8, единый источник
   * границ); пары — passwordMatch на всю группу.
   */
  readonly form = this.fb.group(
    {
      fullName: ['', [requiredTrim(), fullNameRule()]],
      login: ['', [requiredTrim(), loginCharset()]],
      email: ['', [requiredTrim(), emailFormat()]],
      password: ['', [requiredTrim(), passwordRules()]],
      repeatPassword: ['', [requiredTrim()]],
    },
    { validators: [passwordMatch('password', 'repeatPassword')] },
  );

  /** Тексты полей показываются после первой попытки отправки (макет SCR-002). */
  private readonly attempted = signal(false);

  /** Состояние отправки: loading кнопки + защита от повторного сабмита. */
  readonly submitState = submitting();

  /**
   * Текст поля из словаря ERROR_TEXTS; для повтора — ещё и групповое
   * правило «Пароли не совпадают». null — поле не подсвечивается.
   */
  errorTextOf(field: RegisterField): string | null {
    if (!this.attempted()) {
      return null;
    }
    const own = firstErrorText(this.form.controls[field].errors);
    if (own !== null) {
      return own;
    }
    return field === 'repeatPassword' ? firstErrorText(this.form.errors) : null;
  }

  async submit(): Promise<void> {
    if (this.submitState.busy()) {
      return;
    }
    if (this.form.invalid) {
      this.attempted.set(true);
      this.notifications.notifyError(this.invalidDataText, {
        formId: this.formId,
      });
      return;
    }
    trimFormValues(this.form);
    const params: RegisterParams = this.form.getRawValue();
    await this.submitState.run(async () => {
      try {
        const me = await this.auth.register(params);
        // Автологин уже выполнен моком (сессия) и сервисом (кэш currentUser).
        await this.router.navigateByUrl(ROLE_HOME[me.role]);
      } catch (error: unknown) {
        // Текст отказа мока показывается дословно (IF-101/IF-109).
        this.notifications.notifyError(toApiError(error).body.message, {
          formId: this.formId,
        });
      }
    });
  }
}
