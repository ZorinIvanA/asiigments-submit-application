/**
 * Корневой компонент приложения (точка входа бутстрапа, селектор app-root).
 *
 * Относится к layout/ по NFR-003: это оболочка приложения. Каркас содержит
 * глобальный хост десктоп-уведомлений <app-notification-toast/> (IF-109:
 * единый Toast для всех экранов — страницы auth рендерятся вне app-shell)
 * и <router-outlet />: экраны добавляются задачами фич (features/), шапка,
 * страница 403 и ток-хост «под шапкой» — задачами layout/ (app-shell).
 */
import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { NotificationToast } from '../../shared/notifications/notification-toast';

@Component({
  selector: 'app-root',
  imports: [NotificationToast, RouterOutlet],
  templateUrl: './app-root.html',
  styleUrl: './app-root.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppRoot {}
