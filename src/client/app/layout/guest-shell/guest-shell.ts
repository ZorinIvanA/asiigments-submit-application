/**
 * Гостевая оболочка (C-109, §4.8, VS-006, фикс VBUG-004): топбар
 * неавторизованной части — бренд «Сдача лабораторных» и три вкладки
 * «Вход», «Регистрация», «Восстановление пароля» (routerLink +
 * routerLinkActive, набор GUEST_NAV_ITEMS); под ним — маршрутизируемая
 * страница фичи auth (SCR-001..005).
 *
 * Монтируется в app.routes.ts внутри componentless-обёртки с guestGuard
 * (IF-108): топбар виден только гостю — после входа guestGuard уводит на
 * домашний маршрут роли, состав вкладок меняется на наборы AppShell без
 * перезагрузки. Уведомления auth-экранов привязаны к формам (IF-109),
 * поэтому хост мобильного баннера «под шапкой», как в AppShell, здесь не
 * нужен; переменная --app-header-height:56px объявляется для единообразия
 * с оболочкой (высота шапки макета — 56px).
 *
 * Мобильная ширина (<768px, OQ-005) — как у AppShell: высота топбара
 * сохраняется, вкладки скроллятся горизонтально.
 */
import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import type { NavItem } from '../app-shell/app-shell';

/**
 * Вкладки топбара гостя (§4.8, IF-111): «Вход», «Регистрация»,
 * «Восстановление пароля» — подписи и ссылки дословно спеке.
 */
export const GUEST_NAV_ITEMS: readonly NavItem[] = [
  { label: 'Вход', path: '/login' },
  { label: 'Регистрация', path: '/register' },
  { label: 'Восстановление пароля', path: '/recovery' },
];

@Component({
  selector: 'app-guest-shell',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './guest-shell.html',
  styleUrl: './guest-shell.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GuestShell {
  /** Вкладки топбара гостя (§4.8) — фиксированный набор, без роли. */
  protected readonly navItems = GUEST_NAV_ITEMS;
}
