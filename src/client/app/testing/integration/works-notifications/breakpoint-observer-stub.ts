/**
 * Двойник зоны works-notifications: заглушка BreakpointObserver (FR-022:
 * единственный брейкпоинт 768px) — собственный тестовый двойник батча,
 * не зависящий ни от мок-слоя, ни от общей тестовой инфраструктуры
 * src/client/testing (изоляция зоны, FR-026).
 *
 * observe() немедленно выдаёт текущее состояние, simulate() эмулирует
 * изменение ширины окна всем подписчикам — синхронно и детерминированно,
 * без реальных медиазапросов.
 */
import { Observable } from 'rxjs';

import { BreakpointObserver, BreakpointState } from '@angular/cdk/layout';

export class BreakpointObserverStub implements Partial<BreakpointObserver> {
  private current = false;
  private readonly emitters: Array<(matches: boolean) => void> = [];

  /** Калитка состояния: true — мобильная ширина (<768px), false — десктоп. */
  simulate(matches: boolean): void {
    this.current = matches;
    for (const emit of [...this.emitters]) {
      emit(matches);
    }
  }

  observe(_query: string | string[]): Observable<BreakpointState> {
    return new Observable<BreakpointState>((subscriber) => {
      const emit = (matches: boolean): void => {
        subscriber.next({ matches, breakpoints: {} });
      };
      emit(this.current);
      this.emitters.push(emit);
      return () => {
        const index = this.emitters.indexOf(emit);
        if (index !== -1) {
          this.emitters.splice(index, 1);
        }
      };
    });
  }
}
