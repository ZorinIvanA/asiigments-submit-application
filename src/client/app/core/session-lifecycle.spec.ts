/**
 * Юнит-тесты SessionLifecycle (C-013, контракт IF-014, ADR-019/ADR-021/
 * ADR-016):
 *  - фаза холодного старта: initializing=true до markInitDone(), false после;
 *  - канал сброса сессии: подписчик-стаб на sessionExpired$ получает событие
 *    notifySessionExpired (AuthService не используется — сопряжение только
 *    через канал, ADR-019);
 *  - канал восстановления сессии: подписчик-стаб на sessionRestored$
 *    (роль RefreshCoordinator) получает событие markSessionRestored
 *    (роль AuthService после успешных login/register — ADR-021, CR-001);
 *  - каналы независимы: событие одного канала не слышно подписчику другого;
 *  - multicast: все подписчики канала получают каждую публикацию.
 */
import { TestBed } from '@angular/core/testing';

import { SessionLifecycle } from './session-lifecycle';

describe('SessionLifecycle — контракт IF-014 (ADR-019/ADR-021)', () => {
  let lifecycle: SessionLifecycle;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    lifecycle = TestBed.inject(SessionLifecycle);
  });

  it('холодный старт: initializing=true до markInitDone, false после (ADR-016)', () => {
    expect(lifecycle.initializing).withContext('до завершения инициализации').toBeTrue();

    lifecycle.markInitDone();

    expect(lifecycle.initializing).withContext('после markInitDone').toBeFalse();
  });

  it('notifySessionExpired публикует событие подписчику sessionExpired$ (подписчик-стаб)', () => {
    const events: void[] = [];
    const subscriber = lifecycle.sessionExpired$.subscribe(() => events.push(undefined));

    lifecycle.notifySessionExpired();
    lifecycle.notifySessionExpired();

    expect(events.length).toBe(2);
    subscriber.unsubscribe();
  });

  it('без notifySessionExpired подписчик событий не получает', () => {
    let events = 0;
    const subscriber = lifecycle.sessionExpired$.subscribe(() => (events += 1));

    expect(events).toBe(0);
    subscriber.unsubscribe();
  });

  it('событие получают все подписчики канала (интерцептор публикует, потребители сбрасываются)', () => {
    let firstEvents = 0;
    let secondEvents = 0;
    const first = lifecycle.sessionExpired$.subscribe(() => (firstEvents += 1));
    const second = lifecycle.sessionExpired$.subscribe(() => (secondEvents += 1));

    lifecycle.notifySessionExpired();

    expect(firstEvents).toBe(1);
    expect(secondEvents).toBe(1);
    first.unsubscribe();
    second.unsubscribe();
  });

  it('markSessionRestored публикует событие подписчику sessionRestored$ (ADR-021, CR-001)', () => {
    let restoredEvents = 0;
    const subscriber = lifecycle.sessionRestored$.subscribe(() => (restoredEvents += 1));

    expect(restoredEvents).withContext('без публикации событий нет').toBe(0);

    lifecycle.markSessionRestored();
    lifecycle.markSessionRestored();

    // Повторные публикации проходят (сброс фиксатора на стороне потребителя
    // идемпотентен — IF-014).
    expect(restoredEvents).toBe(2);
    subscriber.unsubscribe();
  });

  it('событие sessionRestored получают все подписчики канала (AuthService публикует, интерцептор сбрасывает фиксатор)', () => {
    let firstEvents = 0;
    let secondEvents = 0;
    const first = lifecycle.sessionRestored$.subscribe(() => (firstEvents += 1));
    const second = lifecycle.sessionRestored$.subscribe(() => (secondEvents += 1));

    lifecycle.markSessionRestored();

    expect(firstEvents).toBe(1);
    expect(secondEvents).toBe(1);
    first.unsubscribe();
    second.unsubscribe();
  });

  it('каналы независимы: sessionExpired не слышно в sessionRestored$ и наоборот', () => {
    let expiredEvents = 0;
    let restoredEvents = 0;
    const expiredSubscriber = lifecycle.sessionExpired$.subscribe(() => (expiredEvents += 1));
    const restoredSubscriber = lifecycle.sessionRestored$.subscribe(() => (restoredEvents += 1));

    lifecycle.notifySessionExpired();
    expect(expiredEvents).toBe(1);
    expect(restoredEvents).withContext('канал восстановления не сработал').toBe(0);

    lifecycle.markSessionRestored();
    expect(expiredEvents).withContext('канал сброса не сработал').toBe(1);
    expect(restoredEvents).toBe(1);

    expiredSubscriber.unsubscribe();
    restoredSubscriber.unsubscribe();
  });
});
