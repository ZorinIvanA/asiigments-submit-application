/**
 * Страница «Профиль» (/profile) — C-116, макет SCR-014, FR-4.7/US-18.
 *
 * Одна карточка-форма «Основные данные» (логин (только чтение), роль
 * по-русски, группа (если есть), редактируемые Email и ФИО с подсказкой
 * макета; «Сохранить» → ProfileService.update, успех «Сохранено»). Карточка
 * «Смена пароля» и PUT /me/password удалены вместе с аутентификацией.
 *
 * Клиентская валидация (IF-010): баннер «Данные заполнены неверно» +
 * тексты полей из словаря ERROR_TEXTS; якорь мобильных уведомлений формы —
 * 'profile-form', ошибки бэкенда (409 «Пользователь с таким email уже
 * существует») — дословно под той же формой.
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
  ProfileService,
  ProfileUpdateInput,
} from '../../../../core/services/profile.service';
import { toApiError } from '../../../../shared/api-error';
import { submitting } from '../../../../shared/loading/submitting';
import { ProfileDto } from '../../../../shared/models';
import { NotificationAnchor } from '../../../../shared/notifications/notification-anchor';
import { NotificationService } from '../../../../shared/notifications/notification-service';
import {
  emailFormat,
  firstErrorText,
  fullName as fullNameRule,
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

  /** Профиль пользователя; null — ещё загружается (profile.get). */
  readonly profile = signal<ProfileDto | null>(null);

  /** true, пока идёт первичная загрузка профиля. */
  readonly loading = signal(true);

  /** Роль по-русски для отображения (макет SCR-014). */
  readonly roleLabel = computed(() =>
    this.profile()?.role === 'teacher' ? 'Преподаватель' : 'Студент',
  );

  /** Идентификатор якоря уведомлений формы (IF-109). */
  readonly profileFormId = 'profile-form';

  /** Текст макета SCR-014 (дословно). */
  readonly fullNameHint = 'От 1 до 200 символов';

  /** Форма «Основные данные»: редактируются только email и fullName (§4.7). */
  readonly profileForm = new FormGroup({
    email: new FormControl('', { nonNullable: true, validators: [requiredTrim(), emailFormat()] }),
    fullName: new FormControl('', {
      nonNullable: true,
      validators: [requiredTrim(), fullNameRule()],
    }),
  });

  protected readonly saveProfileState = submitting();

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

  protected emailError(): string | null {
    return this.fieldError(this.profileForm.controls.email);
  }

  protected fullNameError(): string | null {
    return this.fieldError(this.profileForm.controls.fullName);
  }

  /** Текст ошибки поля после взаимодействия (touched) — только словарь IF-010. */
  private fieldError(control: AbstractControl): string | null {
    return control.touched ? firstErrorText(control.errors) : null;
  }
}
