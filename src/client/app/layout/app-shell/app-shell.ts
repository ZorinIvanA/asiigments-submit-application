/**
 * Оболочка авторизованной части приложения (C-109, контракт IF-111,
 * FR-020/§4.8, макет design/mockups/works.html — шапка SCR-006):
 * топбар высотой 56px с брендом «Сдача лабораторных», вкладками по роли
 * (routerLink + routerLinkActive, наборы NAV_ITEMS), ФИО пользователя из
 * кэша AuthService.currentUser и кнопкой «Выйти» (AuthService.logout →
 * редирект /login; сессию и кэш очищает сам AuthService, IF-101).
 *
 * Сразу под шапкой монтируется <app-header-notification/> (IF-109: экраны
 * без формы — /works, /submissions, /my-submissions, /groups, /access — и
 * все уведомления об успехе показываются мобильным баннером «под шапкой»);
 * вертикальное смещение баннера берётся из переменной --app-header-height,
 * которую оболочка объявляет на хосте (высота шапки макета — 56px).
 *
 * Маршрутизируемая страница фичи рендерится в <router-outlet> внутри main
 * с отступами страницы (макет SCR-006/007: колонка 1200px по центру).
 *
 * Мобильная ширина (<768px, OQ-005): высота топбара сохраняется, вкладки
 * скроллятся горизонтально, ФИО скрывается.
 */
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
} from '@angular/core';
import {
  Router,
  RouterLink,
  RouterLinkActive,
  RouterOutlet,
} from '@angular/router';

import { AuthService } from '../../core/services/auth.service';
import { UserRole } from '../../shared/models';
import { HeaderNotification } from '../../shared/notifications/header-notification';

/** Пункт навигации топбара (IF-111). */
export interface NavItem {
  readonly label: string;
  readonly path: string;
}

/**
 * Наборы вкладок по роли (IF-111, §4.8): преподаватель — «Работы»,
 * «Сдача работ», «Группы», «Доступ», «Профиль»; студент — «Сдача работ»,
 * «Профиль». Подписи и ссылки — дословно контракту.
 */
export const NAV_ITEMS: Readonly<Record<UserRole, readonly NavItem[]>> = {
  teacher: [
    { label: 'Работы', path: '/works' },
    { label: 'Сдача работ', path: '/submissions' },
    { label: 'Группы', path: '/groups' },
    { label: 'Доступ', path: '/access' },
    { label: 'Профиль', path: '/profile' },
  ],
  student: [
    { label: 'Сдача работ', path: '/my-submissions' },
    { label: 'Профиль', path: '/profile' },
  ],
};

@Component({
  selector: 'app-shell',
  imports: [HeaderNotification, RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppShell {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  /** Кэш текущего пользователя (AuthService, IF-101) — ФИО в шапке. */
  protected readonly currentUser = this.auth.currentUser;

  /** Вкладки по роли текущего пользователя (IF-111); без сессии — пусто. */
  protected readonly navItems = computed<readonly NavItem[]>(() => {
    const user = this.currentUser();
    return user === null ? [] : NAV_ITEMS[user.role];
  });

  /**
   * Выход (IF-111): очистку сессии (мок) и кэша currentUser выполняет
   * AuthService.logout, оболочка выполняет редирект на /login.
   */
  protected async logout(): Promise<void> {
    await this.auth.logout();
    await this.router.navigate(['/login']);
  }
}
