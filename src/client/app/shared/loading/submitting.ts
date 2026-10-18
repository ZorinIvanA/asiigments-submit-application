/**
 * Хелпер submitting (FR-025, ADR-002): обёртка выполнения действия
 * мок-сервиса с флагом загрузки и защитой от повторной отправки.
 *
 * Использование в компоненте:
 *   private readonly saveState = submitting();
 *
 *   async save(): Promise<void> {
 *     await this.saveState.run(async () => {
 *       try {
 *         await this.labsService.create(dto);
 *         this.notifications.notifySuccess('Сохранено');
 *       } catch (error) {
 *         this.notifications.notifyError(toApiError(error).message, { formId: 'lab-form' });
 *       }
 *     });
 *   }
 *
 * В шаблоне флаг подключается и к индикатору, и к блокировке:
 *   <p-button label="Сохранить" [loading]="saveState.busy()" [disabled]="saveState.busy()" … />
 *
 * Повторный вызов run, пока действие выполняется, игнорируется —
 * параллельного дублирования одного запроса не возникает (FR-025).
 * Ошибка действия пробрасывается вызывающему коду (уведомление решает
 * компонент); флаг сбрасывается в finally при любом исходе.
 */
import { Signal, signal } from '@angular/core';

/** Контроллер выполнения действия с индикацией загрузки (FR-025). */
export interface SubmittingController {
  /** true, пока действие выполняется: индикатор загрузки + disabled. */
  readonly busy: Signal<boolean>;

  /**
   * Выполняет действие не параллельно самому себе: вызовы в состоянии
   * busy игнорируются. Возвращает промис, завершающийся вместе
   * с действием (или с его ошибкой).
   */
  run(action: () => Promise<void>): Promise<void>;
}

export function submitting(): SubmittingController {
  const busy = signal(false);
  return {
    busy: busy.asReadonly(),
    run: async (action: () => Promise<void>): Promise<void> => {
      // Защита от повторной отправки: пока флаг поднят, новое действие
      // не стартует (двойной клик по «Сохранить» — одна запись).
      if (busy()) {
        return;
      }
      busy.set(true);
      try {
        await action();
      } finally {
        busy.set(false);
      }
    },
  };
}
