/**
 * Страница «Профиль» (/profile) — C-116, макет SCR-014, FR-4.7/US-18, FR-4.10.
 *
 * Две независимые карточки-формы (AR-017), у каждой — свой якорь мобильных
 * уведомлений (IF-109):
 *  - «Основные данные»: логин (только чтение), роль по-русски, группа
 *    (только у студента), редактируемые Email и ФИО с подсказкой макета;
 *    «Сохранить» → ProfileService.update, успех «Сохранено»;
 *  - «Смена пароля»: блок «Требования к новому паролю», поля Текущий/Новый/
 *    Повторите пароль; «Сменить пароль» → ProfileService.changePassword,
 *    успех «Пароль изменён», поля формы сбрасываются (при отказе — нет).
 *
 * Клиентская валидация (IF-010): баннер «Данные заполнены неверно» +
 * тексты полей из словаря ERROR_TEXTS; ошибка одной формы не трогает
 * другую — уведомление уходит под якорь formId инициировавшей формы
 * ('profile-form' / 'password-form'), ошибки мока (409 «Пользователь
 * с таким email уже существует», 400 «Неверный текущий пароль») —
 * дословно под той же формой.
 *
 * Мобильная вёрстка (<768px, FR-022) — миксин breakpoint: сетка полей
 * в одну колонку.
 */
import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';

import {
  ProfileChangePasswordInput,
  ProfileService,
  ProfileUpdateInput,
} from '../../../../core/services/profile.service';
import { toApiError } from '../../../../shared/api-error';
import { submitting } from '../../../../shared/loading/submitting';
import { ProfileDto } from '../../../../shared/models';
import { NotificationAnchor } from '../../../../shared/notifications/notification-anchor';
import { NotificationService } from '../../../../shared/notifications/notification-service';
import { ERROR_TEXTS } from '../../../../shared/validation/error-texts';
import {
  emailFormat,
  firstErrorText,
  fullName as fullNameRule,
  passwordMatch,
  passwordRules,
  requiredTrim,
  trimFormValues,
} from '../../../../shared/validation/validators';

/** Баннер клиентской валидации (FR-023: текст формы, а не поля). */
const INVALID_FORM_MESSAGE = 'Данные заполнены неверно';

@Component({
  selector: 'app-profile-page',
  imports: [ReactiveFormsModule, ButtonModule, InputTextModule, NotificationAnchor],
  templateUrl: './profile-page.html',
  styleUrl: './profile-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProfilePage implements OnInit {
  private readonly profileService = inject(ProfileService);
  private readonly notifications = inject(NotificationService);

  /** Профиль пользователя сессии; null — ещё загружается (profile.get). */
  readonly profile = signal<ProfileDto | null>(null);

  /** true, пока идёт первичная загрузка профиля. */
  readonly loading = signal(true);

  /** Роль по-русски для отображения (макет SCR-014). */
  readonly roleLabel = computed(() =>
    this.profile()?.role === 'teacher' ? 'Преподаватель' : 'Студент',
  );

  /** Идентификаторы якорей уведомлений форм (IF-109/AR-017). */
  readonly profileFormId = 'profile-form';
  readonly passwordFormId = 'password-form';

  /** Тексты макета SCR-014 (дословно). */
  readonly fullNameHint = 'От 1 до 200 символов';
  readonly passwordRequirementsTitle = 'Требования к новому паролю';
  readonly passwordRequirements =
    'не менее 8 символов; минимум одна цифра; минимум одна буква; минимум один спецзнак; пароли должны совпадать';

  /** Форма «Основные данные»: редактируются только email и fullName (§4.7). */
  readonly profileForm = new FormGroup({
    email: new FormControl('', { nonNullable: true, validators: [requiredTrim(), emailFormat()] }),
    fullName: new FormControl('', {
      nonNullable: true,
      validators: [requiredTrim(), fullNameRule()],
    }),
  });

  /** Форма «Смена пароля»: текущий обязателен (§4.7), новый — по FR-023. */
  readonly passwordForm = new FormGroup(
    {
      currentPassword: new FormControl('', { nonNullable: true, validators: [requiredTrim()] }),
      password: new FormControl('', {
        nonNullable: true,
        validators: [requiredTrim(), passwordRules()],
      }),
      confirmPassword: new FormControl('', { nonNullable: true, validators: [requiredTrim()] }),
    },
    { validators: passwordMatch('password', 'confirmPassword') },
  );

  protected readonly saveProfileState = submitting();
  protected readonly changePasswordState = submitting();

  async ngOnInit(): Promise<void> {
    try {
      const profile = await this.profileService.get();
      this.profile.set(profile);
      this.profileForm.setValue({ email: profile.email, fullName: profile.fullName });
    } catch (error) {
      // Отказ загрузки — не действие формы: резервный якорь «под шапкой» (IF-109).
      this.notifications.notifyError(toApiError(error).body.message);
    } finally {
      this.loading.set(false);
    }
  }

  /** «Сохранить»: валидация → trimFormValues → profile.update → «Сохранено». */
  protected async saveProfile(): Promise<void> {
    if (this.profileForm.invalid) {
      this.profileForm.markAllAsTouched();
      this.notifications.notifyError(INVALID_FORM_MESSAGE, { formId: this.profileFormId });
      return;
    }
    trimFormValues(this.profileForm);
    const input: ProfileUpdateInput = {
      fullName: this.profileForm.controls.fullName.value,
      email: this.profileForm.controls.email.value,
    };
    await this.saveProfileState.run(async () => {
      try {
        const updated = await this.profileService.update(input);
        this.profile.set(updated);
        this.profileForm.setValue({ email: updated.email, fullName: updated.fullName });
        this.notifications.notifySuccess('Сохранено');
      } catch (error) {
        this.notifications.notifyError(toApiError(error).body.message, {
          formId: this.profileFormId,
        });
      }
    });
  }

  /**
   * «Сменить пароль»: валидация → trimFormValues → changePassword →
   * «Пароль изменён» и сброс полей. При отказе («Неверный текущий
   * пароль» 400) поля формы не сбрасываются.
   */
  protected async changePassword(): Promise<void> {
    if (this.passwordForm.invalid) {
      this.passwordForm.markAllAsTouched();
      this.notifications.notifyError(INVALID_FORM_MESSAGE, { formId: this.passwordFormId });
      return;
    }
    trimFormValues(this.passwordForm);
    const input: ProfileChangePasswordInput = this.passwordForm.getRawValue();
    await this.changePasswordState.run(async () => {
      try {
        await this.profileService.changePassword(input);
        this.passwordForm.reset();
        this.notifications.notifySuccess('Пароль изменён');
      } catch (error) {
        this.notifications.notifyError(toApiError(error).body.message, {
          formId: this.passwordFormId,
        });
      }
    });
  }

  protected emailError(): string | null {
    return this.fieldError(this.profileForm.controls.email);
  }

  protected fullNameError(): string | null {
    return this.fieldError(this.profileForm.controls.fullName);
  }

  protected currentPasswordError(): string | null {
    return this.fieldError(this.passwordForm.controls.currentPassword);
  }

  protected newPasswordError(): string | null {
    return this.fieldError(this.passwordForm.controls.password);
  }

  /** У повтора — своя ошибка либо групповая «Пароли не совпадают» (passwordMatch). */
  protected repeatPasswordError(): string | null {
    const own = this.fieldError(this.passwordForm.controls.confirmPassword);
    if (own !== null) {
      return own;
    }
    const mismatchVisible =
      this.passwordForm.hasError('password.mismatch') &&
      this.passwordForm.controls.confirmPassword.touched;
    return mismatchVisible ? ERROR_TEXTS['password.mismatch'] : null;
  }

  /** Текст ошибки поля после взаимодействия (touched) — только словарь IF-010. */
  private fieldError(control: AbstractControl): string | null {
    return control.touched ? firstErrorText(control.errors) : null;
  }
}
