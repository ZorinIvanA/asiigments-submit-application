/**
 * Презентационный баннер уведомления (FR-021): красный (ошибка) или
 * зелёный (успех), иконка, текст дословно, крестик закрытия.
 *
 * Компонент не знает ни о режимах, ни о таймерах: автозакрытием и
 * позицией управляет NotificationService и якоря-хосты
 * (NotificationAnchor / HeaderNotification).
 */
import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

import { NotificationSeverity } from './notification-model';

@Component({
  selector: 'app-notification-banner',
  templateUrl: './notification-banner.html',
  styleUrl: './notification-banner.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NotificationBanner {
  readonly severity = input.required<NotificationSeverity>();
  readonly text = input.required<string>();

  /** Нажатие крестика (раннее закрытие до автозакрытия 5000 мс). */
  readonly closed = output<void>();

  readonly role = computed<'alert' | 'status'>(() =>
    this.severity() === 'error' ? 'alert' : 'status',
  );

  readonly iconClass = computed<string>(() =>
    this.severity() === 'error'
      ? 'app-notification-banner__icon pi pi-times-circle'
      : 'app-notification-banner__icon pi pi-check-circle',
  );
}
