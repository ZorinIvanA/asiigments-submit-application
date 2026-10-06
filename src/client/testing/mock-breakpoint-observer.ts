/**
 * Заглушка BreakpointObserver для unit-тестов (FR-022: единственный
 * брейкпоинт 768px). observe() немедленно выдаёт текущее состояние,
 * simulate() эмулирует изменение ширины окна всем подписчикам —
 * синхронно и детерминированно, без реальных медиазапросов.
 *
 * Использование:
 *   const breakpoints = new MockBreakpointObserver();
 *   TestBed.configureTestingModule({
 *     providers: [{ provide: BreakpointObserver, useValue: breakpoints }],
 *   });
 *   breakpoints.simulate(true); // ширина < 768px
 */
import { Observable } from 'rxjs';

import { BreakpointObserver, BreakpointState } from '@angular/cdk/layout';

export class MockBreakpointObserver implements Partial<BreakpointObserver> {
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
