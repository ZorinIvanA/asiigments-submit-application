/**
 * Якорь мобильного inline-баннера под формой-инициатором (FR-021/AR-017).
 *
 * Размещается страницей сразу после формы:
 *   <form [formGroup]="form" (ngSubmit)="save()">…</form>
 *   <app-notification-anchor formId="lab-form" />
 *
 * На экранах с несколькими формами (/profile) каждая форма получает свой
 * formId и свой якорь: ошибка действия показывается под той формой,
 * которая его инициировала. При монтировании якорь регистрирует formId
 * в NotificationService (маршрутизация уведомлений), при уничтожении —
 * снимает регистрацию, после чего formId снова считается неизвестным
 * и уходит резервно под шапку (IF-009).
 *
 * formId считается статическим идентификатором экрана и не меняется
 * в течение жизни якоря.
 */
import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, computed, inject, input } from '@angular/core';

import { NotificationBanner } from './notification-banner';
import { MobileNotification } from './notification-model';
import { NotificationService } from './notification-service';

@Component({
  selector: 'app-notification-anchor',
  templateUrl: './notification-anchor.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NotificationBanner],
})
export class NotificationAnchor implements OnInit, OnDestroy {
  /** Идентификатор формы-инициатора; уникален в пределах экрана. */
  readonly formId = input.required<string>();

  private readonly notifications = inject(NotificationService);

  /** Уведомление, адресованное этому якорю; null — не показывать. */
  readonly notification = computed<MobileNotification | null>(() => {
    const message = this.notifications.mobileMessage();
    if (
      message === null ||
      message.formId !== this.formId() ||
      !this.notifications.isMobile()
    ) {
      return null;
    }
    return message;
  });

  ngOnInit(): void {
    this.notifications.registerAnchor(this.formId());
  }

  ngOnDestroy(): void {
    this.notifications.unregisterAnchor(this.formId());
  }

  dismiss(): void {
    this.notifications.dismissMobile();
  }
}
