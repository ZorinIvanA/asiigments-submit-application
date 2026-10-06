/**
 * Модель уведомлений общего UI-кита (C-010, контракт IF-009 / FR-021).
 *
 * Единственный источник констант режима показа: автозакрытие 5000 мс,
 * граница мобильной вёрстки 768px (FR-022) и ключ PrimeNG Toast.
 */

/**
 * Якорь размещения мобильного inline-баннера (FR-021/AR-017):
 * 'header' — фиксированно под шапкой; { formId } — под формой-инициатором
 * (на /profile — под той из двух форм, чьё действие вызвало уведомление).
 */
export type NotificationAnchor = 'header' | { readonly formId: string };

/** Допустимые тексты уведомлений об успехе (FR-021: только эти три). */
export type SuccessNotificationText = 'Сохранено' | 'Удалено' | 'Пароль изменён';

/** Ошибка — красный баннер/Toast, успех — зелёный. */
export type NotificationSeverity = 'error' | 'success';

/** Уведомление десктоп-режима (>=768px): PrimeNG Toast справа вверху. */
export interface DesktopNotification {
  readonly severity: NotificationSeverity;
  readonly text: string;
}

/**
 * Уведомление мобильного режима (<768px): inline-баннер.
 * formId идентифицирует форму-инициатора; null — якорь «под шапкой».
 */
export interface MobileNotification {
  readonly severity: NotificationSeverity;
  readonly text: string;
  readonly formId: string | null;
}

/** Автозакрытие уведомления в обоих режимах, мс (FR-021). */
export const NOTIFICATION_AUTO_CLOSE_MS = 5000;

/**
 * Мобильный медиазапрос — единственный брейкпоинт приложения (FR-022).
 * 767.98px — стандартная конвенция CDK: отсекает дробные ширины между
 * 767 и 768, мобильным считается всё строго меньше 768px.
 */
export const NOTIFICATION_MOBILE_MEDIA_QUERY = '(max-width: 767.98px)';

/** Ключ PrimeNG Toast: перенос уведомлений в <p-toast> хоста NotificationToast. */
export const NOTIFICATION_TOAST_KEY = 'app-notifications';
