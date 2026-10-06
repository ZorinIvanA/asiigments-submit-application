/**
 * Страница 403 «Доступ запрещён» (C-109, IF-108: roleGuard redirect
 * чужой роли → /403; макета нет — минимальная карточка по OQ-004):
 * заголовок «Доступ запрещён» и кнопка перехода на домашний раздел роли.
 *
 * Домашний маршрут берётся из ROLE_HOME (core/navigation/guards.ts,
 * IF-108: teacher → /works, student → /my-submissions) — единственный
 * источник соответствия роль → маршрут, без дублирования. Пользователь
 * читается из кэша AuthService.currentUser; на 403 гвард-пайплайн уже
 * проверял сессию, кэш заполнен; крайний случай (кэш сброшен, например
 * сессия истекла во время показа) → /login.
 */
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Router } from '@angular/router';

import { ROLE_HOME } from '../../core/navigation/guards';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-page-403',
  templateUrl: './page-403.html',
  styleUrl: './page-403.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Page403 {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  /**
   * Домашний маршрут текущей роли (ROLE_HOME, IF-108); без пользователя
   * в кэше — /login (переход на вход вместо аварийного состояния).
   */
  protected readonly homeRoute = computed<string>(() => {
    const role = this.auth.currentUser()?.role;
    return role === undefined ? '/login' : ROLE_HOME[role];
  });

  /** Кнопка «На главную» — переход на домашний раздел роли. */
  protected async goHome(): Promise<void> {
    await this.router.navigate([this.homeRoute()]);
  }
}
