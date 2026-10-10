/**
 * ProfileService — клиент REST-домена профиля (C-014, контракт IF-013,
 * FR-4.7/US-18): просмотр профиля текущего пользователя сессии,
 * редактирование ФИО/email и смена пароля. Доступен любой авторизованной
 * роли (§4.7). Базовый префикс — токен API_BASE_URL (ADR-009); запросы идут
 * с withCredentials: true через authInterceptor (C-013), HTTP-отказы
 * нормализуются им в ApiError {status, body: {message, errors?}}
 * (http-errors.ts): 400 «Данные заполнены неверно» с errors по полям
 * {fullName, email} / {password, confirmPassword}, 400 «Неверный текущий
 * пароль», 409 «Пользователь с таким email уже существует», 401 «Не
 * авторизован» — страницы показывают body.message дословно (IF-109).
 */
import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { API_BASE_URL } from '../api-base-url';
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
  private readonly http = inject(HttpClient);
  private readonly apiBase = inject(API_BASE_URL);

  /** Профиль текущего пользователя сессии (GET /me/profile). */
  get(): Promise<ProfileDto> {
    return firstValueFrom(this.http.get<ProfileDto>(`${this.apiBase}/me/profile`));
  }

  /** Редактирование ФИО и email → обновлённый ProfileDto (PUT /me/profile). */
  update(input: ProfileUpdateInput): Promise<ProfileDto> {
    return firstValueFrom(this.http.put<ProfileDto>(`${this.apiBase}/me/profile`, input));
  }

  /** Смена пароля из профиля (PUT /me/password → 204). */
  changePassword(input: ProfileChangePasswordInput): Promise<void> {
    return firstValueFrom(
      this.http.put<void>(`${this.apiBase}/me/password`, input),
    );
  }
}
