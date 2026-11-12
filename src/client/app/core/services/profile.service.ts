/**
 * ProfileService — клиент REST-домена профиля (C-014, контракт IF-013,
 * FR-4.7/US-18): просмотр профиля текущего пользователя и редактирование
 * ФИО/email (смена пароля и /me/password удалены вместе с аутентификацией —
 * приложение работает анонимно, от имени сид-преподавателя). Базовый
 * префикс — токен API_BASE_URL (ADR-009); HTTP-отказы нормализуются в
 * ApiError {status, body: {message, errors?}} (http-errors.ts): 400
 * «Данные заполнены неверно» с errors по полям {fullName, email}, 409
 * «Пользователь с таким email уже существует» — страница показывает
 * body.message дословно (IF-109).
 */
import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';

import { API_BASE_URL } from '../api-base-url';
import { ProfileDto } from '../../shared/models';
import { firstValueFromApi } from '../http-errors';

/** Вход update(): поля формы «Основные данные» (после trimFormValues). */
export interface ProfileUpdateInput {
  fullName: string;
  email: string;
}

@Injectable({ providedIn: 'root' })
export class ProfileService {
  private readonly http = inject(HttpClient);
  private readonly apiBase = inject(API_BASE_URL);

  /** Профиль текущего пользователя (GET /me/profile — сид-преподаватель). */
  get(): Promise<ProfileDto> {
    return firstValueFromApi(
      this.http.get<ProfileDto>(`${this.apiBase}/me/profile`),
    );
  }

  /** Редактирование ФИО и email → обновлённый ProfileDto (PUT /me/profile). */
  update(input: ProfileUpdateInput): Promise<ProfileDto> {
    return firstValueFromApi(
      this.http.put<ProfileDto>(`${this.apiBase}/me/profile`, input),
    );
  }
}
