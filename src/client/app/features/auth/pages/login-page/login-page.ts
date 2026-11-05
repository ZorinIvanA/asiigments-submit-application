/**
 * Страница входа (C-110, SCR-001, FR-4.1/FR-4.10): карточка 400px с
 * кикером «Сдача лабораторных» и заголовком «Вход», поля «Логин»/«Пароль»,
 * кнопка «Войти» заблокирована при пустом логине ИЛИ пароле; полевых
 * клиентских ошибок и баннера валидации на /login нет (FR-023).
 *
 * Отказы входа (401 «Неверный логин или пароль», 429 «Слишком много
 * попыток. Повторите позже») — единый механизм уведомлений (IF-109):
 * desktop — тост справа вверху, mobile — inline-баннер ПОД ФОРМОЙ,
 * якорь 'login-form'. Успех — редирект на домашний маршрут роли
 * (ROLE_HOME, IF-108): teacher → /works, student → /my-submissions.
 *
 * Ссылки макета: «Я забыл пароль» → /recovery (поток SCR-003..005),
 * «Зарегистрироваться» → /register (SCR-002). Перед построением DTO
 * значения триммятся (trimFormValues, IF-010); на время запроса кнопка
 * в состоянии loading и заблокирована (submitting, FR-025).
 */
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';

import { ROLE_HOME } from '../../../../core/navigation/guards';
import {
  AuthService,
  LoginParams,
} from '../../../../core/services/auth.service';
import { toApiError } from '../../../../shared/api-error';
import { submitting } from '../../../../shared/loading/submitting';
import { NotificationAnchor } from '../../../../shared/notifications/notification-anchor';
import { NotificationService } from '../../../../shared/notifications/notification-service';
import {
  requiredTrim,
  trimFormValues,
} from '../../../../shared/validation/validators';

@Component({
  selector: 'app-login-page',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    ButtonModule,
    InputTextModule,
    NotificationAnchor,
  ],
  templateUrl: './login-page.html',
  styleUrl: './login-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginPage {
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationService);

  /** Идентификатор формы и якоря мобильных уведомлений (IF-109). */
  readonly formId = 'login-form';

  /** Валидаторы нужны только для блокировки кнопки: ошибок полям нет (FR-023). */
  readonly form = this.fb.group({
    login: ['', [requiredTrim()]],
    password: ['', [requiredTrim()]],
  });

  /** Состояние отправки: loading кнопки + защита от повторного сабмита. */
  readonly submitState = submitting();

  async submit(): Promise<void> {
    if (this.form.invalid || this.submitState.busy()) {
      return;
    }
    trimFormValues(this.form);
    const params: LoginParams = this.form.getRawValue();
    await this.submitState.run(async () => {
      try {
        const me = await this.auth.login(params);
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
