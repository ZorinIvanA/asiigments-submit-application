/**
 * NotificationService — единый механизм уведомлений (IF-009, FR-021).
 *
 * Публичный API контракта:
 *   notifyError(text, anchor?)  — ошибка мока (text — дословно body.message)
 *                                 либо клиентской валидации;
 *   notifySuccess(text)         — успех: только 'Сохранено' | 'Удалено' |
 *                                 'Пароль изменён' (тип-литерал union).
 *
 * Режим показа выбирается по ширине окна через BreakpointObserver(768px):
 *   >=768px — уведомление публикуется в desktopMessage, хост
 *             <app-notification-toast> переносит его в PrimeNG Toast
 *             справа вверху (автозакрытие и крестик — средствами Toast);
 *   <768px  — уведомление публикуется в mobileMessage, его читают якоря:
 *             <app-notification-anchor formId="…"> под формой-инициатором
 *             (AR-017: на /profile — своя форма на каждое действие) и
 *             <app-header-notification> фиксированно под шапкой (экраны
 *             без формы — исчерпывающий перечень FR-021).
 *
 * Неизвестный formId (якорь формы не смонтирован на экране) резервно
 * маршрутизируется под шапку — защита от потери сообщения (IF-009).
 *
 * Автозакрытие 5000 мс действует в обоих режимах и сбрасывается при
 * каждом новом уведомлении; dismissMobile() закрывает мобильный баннер
 * крестиком досрочно.
 */
import { BreakpointObserver, BreakpointState } from '@angular/cdk/layout';
import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import {
  DesktopNotification,
  MobileNotification,
  NotificationAnchor,
  NOTIFICATION_AUTO_CLOSE_MS,
  NOTIFICATION_MOBILE_MEDIA_QUERY,
  SuccessNotificationText,
} from './notification-model';

@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly breakpointObserver = inject(BreakpointObserver);
  private readonly destroyRef = inject(DestroyRef);

  private readonly isMobileSource = signal(false);
  /** Мобильный ли режим (<768px); переключается BreakpointObserver'ом. */
  readonly isMobile = this.isMobileSource.asReadonly();

  private readonly desktopMessageSource = signal<DesktopNotification | null>(null);
  /** Текущее уведомление десктоп-режима (null — не показано). */
  readonly desktopMessage = this.desktopMessageSource.asReadonly();

  private readonly mobileMessageSource = signal<MobileNotification | null>(null);
  /** Текущее уведомление мобильного режима (null — не показано). */
  readonly mobileMessage = this.mobileMessageSource.asReadonly();

  /** formId якорей форм, смонтированных на экране в данный момент. */
  private readonly registeredFormIds = new Set<string>();

  private autoCloseTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    this.breakpointObserver
      .observe(NOTIFICATION_MOBILE_MEDIA_QUERY)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((state: BreakpointState) => {
        this.isMobileSource.set(state.matches);
        // Смена режима гасит показанное уведомление, чтобы мобильный баннер
        // не «воскресал» при возврате на мобильную ширину по старому сигналу.
        this.clearCurrentNotification();
      });
  }

  notifyError(text: string, anchor?: NotificationAnchor): void {
    if (this.isMobile()) {
      this.showMobile({
        severity: 'error',
        text,
        formId: this.resolveAnchorFormId(anchor),
      });
    } else {
      this.showDesktop({ severity: 'error', text });
    }
  }

  notifySuccess(text: SuccessNotificationText): void {
    // Контракт IF-009 не даёт успеху якоря формы: на мобильном зелёный
    // баннер показывается фиксированно под шапкой.
    if (this.isMobile()) {
      this.showMobile({ severity: 'success', text, formId: null });
    } else {
      this.showDesktop({ severity: 'success', text });
    }
  }

  /** Ручное закрытие мобильного inline-баннера (крестик, FR-021). */
  dismissMobile(): void {
    this.clearCurrentNotification();
  }

  /**
   * Регистрация якоря формы (вызывается NotificationAnchor при инициализации).
   * Пока formId зарегистрирован, мобильные ошибки с этим якорем идут под форму;
   * после уничтожения якоря — снова резервно под шапку.
   */
  registerAnchor(formId: string): void {
    this.registeredFormIds.add(formId);
  }

  unregisterAnchor(formId: string): void {
    this.registeredFormIds.delete(formId);
  }

  /**
   * Разрешение якоря: 'header' и отсутствующий якорь → под шапку;
   * formId без смонтированного якоря → резервно под шапку (IF-009).
   */
  private resolveAnchorFormId(anchor: NotificationAnchor | undefined): string | null {
    if (anchor === undefined || anchor === 'header') {
      return null;
    }
    return this.registeredFormIds.has(anchor.formId) ? anchor.formId : null;
  }

  private showDesktop(notification: DesktopNotification): void {
    this.desktopMessageSource.set(notification);
    this.scheduleAutoClose(() => this.desktopMessageSource.set(null));
  }

  private showMobile(notification: MobileNotification): void {
    this.mobileMessageSource.set(notification);
    this.scheduleAutoClose(() => this.mobileMessageSource.set(null));
  }

  private scheduleAutoClose(clear: () => void): void {
    this.cancelAutoClose();
    this.autoCloseTimer = setTimeout(() => {
      this.autoCloseTimer = null;
      clear();
    }, NOTIFICATION_AUTO_CLOSE_MS);
  }

  private clearCurrentNotification(): void {
    this.cancelAutoClose();
    this.mobileMessageSource.set(null);
    this.desktopMessageSource.set(null);
  }

  private cancelAutoClose(): void {
    if (this.autoCloseTimer !== null) {
      clearTimeout(this.autoCloseTimer);
      this.autoCloseTimer = null;
    }
  }
}
