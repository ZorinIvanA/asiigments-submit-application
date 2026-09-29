/**
 * ProfileService — клиент мок-домена Profile (C-106, контракт IF-107,
 * FR-4.7/US-18): просмотр профиля текущего пользователя сессии, редактирование
 * ФИО/email и смена пароля. Доступен любой авторизованной роли (§4.7).
 *
 * Отказы мока приходят как ApiError {status, body: {message, errors?}}
 * (reject промиса): 400 «Данные заполнены неверно» с errors по полям
 * {fullName, email} / {password, confirmPassword}, 400 «Неверный текущий
 * пароль», 409 «Пользователь с таким email уже существует», 401 «Не
 * авторизован» — страницы показывают body.message дословно (IF-109).
 */
import { Injectable, inject } from '@angular/core';

import { MockApiClient } from '../../mock/mock-api-client';
import { ProfileDto } from '../../shared/models';

/** Вход update(): поля формы «Основные данные» (после trimFormValues). */
export interface ProfileUpdateInput {
  fullName: string;
  email: string;
}

/** Вход changePassword(): поля формы «Смена пароля». */
export interface ProfileChangePasswordInput {
  currentPassword: string;
  password: string;
  confirmPassword: string;
}

@Injectable({ providedIn: 'root' })
export class ProfileService {
  private readonly client = inject(MockApiClient);

  /** Профиль текущего пользователя сессии (мок 'profile.get'). */
  get(): Promise<ProfileDto> {
    return this.client.call<ProfileDto>('profile.get', null);
  }

  /** Редактирование ФИО и email → обновлённый ProfileDto (мок 'profile.update'). */
  update(input: ProfileUpdateInput): Promise<ProfileDto> {
    return this.client.call<ProfileDto>('profile.update', input);
  }

  /** Смена пароля из профиля (мок 'profile.changePassword'). */
  changePassword(input: ProfileChangePasswordInput): Promise<void> {
    return this.client.call<void>('profile.changePassword', input);
  }
}
