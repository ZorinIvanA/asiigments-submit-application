/**
 * Оболочка приложения — консоль преподавателя (C-109, контракт IF-111,
 * FR-020/§4.8, макет design/mockups/works.html — шапка SCR-006): топбар
 * высотой 56px с брендом «Сдача лабораторных» и статичным набором вкладок
 * преподавателя (routerLink + routerLinkActive, NAV_ITEMS).
 *
 * Сразу под шапкой монтируется <app-header-notification/> (IF-109: экраны
 * без формы — /works, /submissions, /groups, /access — и все уведомления
 * об успехе показываются мобильным баннером «под шапкой»); вертикальное
 * смещение баннера берётся из переменной --app-header-height, которую
 * оболочка объявляет на хосте (высота шапки макета — 56px).
 *
 * Маршрутизируемая страница фичи рендерится в <router-outlet> внутри main
 * с отступами страницы (макет SCR-006/007: колонка 1200px по центру).
 *
 * Мобильная ширина (<768px, OQ-005): высота топбара сохраняется, вкладки
 * скроллятся горизонтально.
 */
import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { HeaderNotification } from '../../shared/notifications/header-notification';

/** Пункт навигации топбара (IF-111). */
export interface NavItem {
  readonly label: string;
  readonly path: string;
}

/**
 * Вкладки консоли преподавателя (IF-111, §4.8): «Работы», «Сдача работ»,
 * «Группы», «Доступ», «Профиль». Подписи и ссылки — дословно контракту.
 */
export const NAV_ITEMS: readonly NavItem[] = [
  { label: 'Работы', path: '/works' },
  { label: 'Сдача работ', path: '/submissions' },
  { label: 'Группы', path: '/groups' },
  { label: 'Доступ', path: '/access' },
  { label: 'Профиль', path: '/profile' },
];

@Component({
  selector: 'app-shell',
  imports: [HeaderNotification, RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppShell {
  /** Статичный набор вкладок преподавателя (IF-111). */
  protected readonly navItems = NAV_ITEMS;
}
