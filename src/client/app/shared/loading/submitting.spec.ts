/**
 * Unit-тесты хелпера submitting (FR-025): флаг загрузки на время действия,
 * блокировка повторной отправки (параллельного дубля запроса нет),
 * сброс флага при успехе и при ошибке, последовательные запуски.
 */
import { submitting } from './submitting';

describe('submitting', () => {
  let busyLog: boolean[];

  beforeEach(() => {
    busyLog = [];
  });

  it('изначально флаг загрузки не поднят', () => {
    const state = submitting();
    expect(state.busy()).toBe(false);
  });

  it('флаг поднят во время действия и сброшен после завершения', async () => {
    const state = submitting();

    const done = state.run(async () => {
      busyLog.push(state.busy());
    });

    expect(state.busy()).withContext('флаг поднят синхронно со стартом').toBe(true);
    await done;
    expect(state.busy()).toBe(false);
    expect(busyLog).toEqual([true]);
  });

  it('повторная отправка во время выполнения игнорируется (FR-025)', async () => {
    const state = submitting();
    let firstStarted = false;
    let secondStarted = false;

    const first = state.run(async () => {
      firstStarted = true;
      // «Долгий» мок-вызов: пока он висит, происходит второй клик.
      await state.run(async () => {
        secondStarted = true;
      });
    });

    await first;
    expect(firstStarted).toBe(true);
    expect(secondStarted)
      .withContext('действие в состоянии busy не стартует — дубля запроса нет')
      .toBe(false);
    expect(state.busy()).toBe(false);
  });

  it('после завершения действие можно выполнить снова', async () => {
    const state = submitting();
    let runs = 0;

    await state.run(async () => {
      runs += 1;
    });
    await state.run(async () => {
      runs += 1;
    });

    expect(runs).toBe(2);
  });

  it('ошибка действия пробрасывается, флаг сбрасывается', async () => {
    const state = submitting();

    await expectAsync(
      state.run(async () => {
        busyLog.push(state.busy());
        throw new Error('409 Конфликт');
      }),
    ).toBeRejectedWithError('409 Конфликт');

    expect(state.busy()).toBe(false);
    expect(busyLog).toEqual([true]);
  });

  it('после ошибки действие доступно снова', async () => {
    const state = submitting();
    let recovered = false;

    await expectAsync(
      state.run(async () => {
        throw new Error('сбой');
      }),
    ).toBeRejected();

    await state.run(async () => {
      recovered = true;
    });

    expect(recovered).toBe(true);
  });
});
