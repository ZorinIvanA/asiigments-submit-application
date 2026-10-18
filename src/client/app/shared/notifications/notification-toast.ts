/**
 * Хост PrimeNG Toast десктоп-режима (FR-021: >=768px, справа вверху).
 *
 * Размещается оболочкой (layout) один раз рядом с <router-outlet>.
 * MessageService предоставляется на уровне компонента: NotificationToast
 * и вложенный <p-toast> гарантированно используют один экземпляр без
 * глобальных провайдеров в appConfig.
 *
 * Автозакрытие 5000 мс и закрытие крестиком — стандартные средства
 * PrimeNG Toast (life + closable); NotificationService переносит
 * уведомление в Toast через effect и держит desktopMessage не дольше
 * того же интервала.
 */
import { ChangeDetectionStrategy, Component, effect, inject } from '@angular/core';
import { MessageService } from 'primeng/api';
import { Toast } from 'primeng/toast';

import { NotificationService } from './notification-service';
import {
  NOTIFICATION_AUTO_CLOSE_MS,
  NOTIFICATION_TOAST_KEY,
} from './notification-model';

@Component({
  selector: 'app-notification-toast',
  templateUrl: './notification-toast.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [MessageService],
  imports: [Toast],
})
export class NotificationToast {
  private readonly notifications = inject(NotificationService);
  private readonly messageService = inject(MessageService);

  protected readonly toastKey = NOTIFICATION_TOAST_KEY;

  constructor() {
    effect(() => {
      const message = this.notifications.desktopMessage();
      if (message === null) {
        return;
      }
      // Текст передаётся дословно (IF-009): без summary-префиксов.
      this.messageService.add({
        key: NOTIFICATION_TOAST_KEY,
        severity: message.severity,
        summary: message.text,
        life: NOTIFICATION_AUTO_CLOSE_MS,
        closable: true,
      });
    });
  }
}
