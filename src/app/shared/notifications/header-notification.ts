/**
 * Хост мобильного баннера «фиксированно под шапкой» (FR-021, AR-013):
 * единственный механизм показа на экранах без формы — /works, /submissions,
 * /groups, /groups/:id, /access, /my-submissions (исчерпывающий перечень),
 * а также адрес резервного якоря 'header' и всех уведомлений об успехе.
 *
 * Размещается оболочкой (layout) сразу после шапки, до основного
 * контента. Вертикальное смещение — высота шапки макета — задаётся
 * переменной --app-header-height (fallback 56px на случай, когда оболочка
 * её не объявила).
 */
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';

import { NotificationBanner } from './notification-banner';
import { MobileNotification } from './notification-model';
import { NotificationService } from './notification-service';

@Component({
  selector: 'app-header-notification',
  templateUrl: './header-notification.html',
  styleUrl: './header-notification.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NotificationBanner],
})
export class HeaderNotification {
  private readonly notifications = inject(NotificationService);

  /** Уведомление с якорем 'header'; null — не показывать. */
  readonly notification = computed<MobileNotification | null>(() => {
    const message = this.notifications.mobileMessage();
    if (
      message === null ||
      message.formId !== null ||
      !this.notifications.isMobile()
    ) {
      return null;
    }
    return message;
  });

  dismiss(): void {
    this.notifications.dismissMobile();
  }
}
