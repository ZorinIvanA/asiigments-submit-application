/**
 * Юнит-тесты конвейера мок-вызова (C-002, IF-001, FR-003, NFR-001):
 * задержка каждого вызова ∈ [500; 800] мс (fakeAsync-границы 499/500 для
 * resolve и reject + реальные таймеры с замерами timestamp до/после для
 * шести разных методов), трансляция MockValidationError → ApiError по
 * статусам, реестр обработчиков, передача db/params, персистентность
 * мутаций через полный конвейер вызова.
 *
 * Доменные обработчики — зона T-006/T-012/T-015/T-018/T-021, поэтому
 * сценарий AC «зарегистрирован тестовый обработчик» реализуется
 * собственными обработчиками теста через публичный register().
 */
import { fakeAsync, tick } from '@angular/core/testing';

import { ApiError, Group, MOCK_DELAY_MS, STORAGE_KEYS } from '../shared/models';
import { MockApiClient } from './mock-api-client';
import { MockDbData } from './mock-db';
import { MockErrorStatus, MockValidationError } from './mock-error';
import { newRuntimeId } from './ids';

function makeClient(): MockApiClient {
  return new MockApiClient();
}

/** Ловит отказ промиса в переменную (fakeAsync дружелюбно). */
function captureRejection(promise: Promise<unknown>): { error?: unknown } {
  const captured: { error?: unknown } = {};
  promise.catch((error: unknown) => {
    captured.error = error;
  });
  return captured;
}

function stored(): MockDbData {
  return JSON.parse(localStorage.getItem(STORAGE_KEYS.mockDb) ?? 'null') as MockDbData;
}

describe('MockApiClient — конвейер мок-вызова (IF-001)', () => {
  beforeEach(() => localStorage.clear());

  afterEach(() => localStorage.clear());

  describe('задержка вызова (AC «Задержка вызова», NFR-001)', () => {
    it('успешный вызов не завершён на 499 мс и завершён ровно на 500 мс', fakeAsync(() => {
      const client = makeClient();
      client.register('test.ok', () => ({ value: 42 }));

      const startedAt = Date.now();
      let settled = false;
      let duration = Number.NaN;
      client.call<{ value: number }>('test.ok', {}).then(() => {
        duration = Date.now() - startedAt;
        settled = true;
      });

      tick(499);
      expect(settled).withContext('не ранее 500 мс от начала').toBeFalse();
      tick(1);
      expect(settled).withContext('завершается после 500 мс').toBeTrue();
      expect(duration).toBe(500);
    }));

    it('отказ тоже задерживается: reject не ранее 500 мс', fakeAsync(() => {
      const client = makeClient();
      client.register('test.conflict', () => {
        throw new MockValidationError(409, 'Конфликт');
      });

      const startedAt = Date.now();
      let rejected = false;
      let duration = Number.NaN;
      client.call('test.conflict', {}).catch(() => {
        duration = Date.now() - startedAt;
        rejected = true;
      });

      tick(499);
      expect(rejected).toBeFalse();
      tick(1);
      expect(rejected).toBeTrue();
      expect(duration).toBe(500);
    }));

    it('медленный обработчик не нарушает нижнюю границу: итоговая длительность ≥ 500 мс', fakeAsync(() => {
      const client = makeClient();
      client.register('test.slow', () => {
        tick(200); // обработчик занял 200 мс синхронного времени (замкнутый цикл ожидания)
        return 'ok';
      });

      const startedAt = Date.now();
      let settled = false;
      let duration = Number.NaN;
      client.call('test.slow', null).then(() => {
        duration = Date.now() - startedAt;
        settled = true;
      });

      tick(500); // 200 мс ушли на обработчик, задержка добирает остаток
      expect(settled).toBeTrue();
      expect(duration).toBeGreaterThanOrEqual(500);
    }));

    it('NFR-001: длительности шести разных вызовов (успехи и отказы) — в [500; 800] мс на реальных таймерах', async () => {
      const client = makeClient();
      client.register('test.echo', (_db, params: { marker: string }) => params.marker);
      client.register('test.validation', () => {
        throw new MockValidationError(400, 'Данные заполнены неверно', { number: ['Номер должен быть положительным числом'] });
      });
      client.register('test.unauthorized', () => {
        throw new MockValidationError(401, 'Не авторизован');
      });
      client.register('test.notfound', () => {
        throw new MockValidationError(404, 'Не найдено');
      });
      client.register('test.ratelimit', () => {
        throw new MockValidationError(429, 'Слишком много попыток');
      });
      client.register('test.mutate', (db) =>
        db.mutate((data) => {
          data.groups.push({ id: newRuntimeId(), name: 'ИК-229', studentCount: 0 });
          return data.groups.length;
        }),
      );

      const measure = async (invoke: () => Promise<unknown>): Promise<number> => {
        const startedAt = performance.now();
        await invoke().catch(() => undefined); // отказ — тоже завершение вызова
        return performance.now() - startedAt;
      };

      // mock_call_duration_bounds (observability): шесть разных методов параллельно.
      const durations = await Promise.all([
        measure(() => client.call('test.echo', { marker: 'ping' })),
        measure(() => client.call('test.validation', {})),
        measure(() => client.call('test.unauthorized', {})),
        measure(() => client.call('test.notfound', {})),
        measure(() => client.call('test.ratelimit', {})),
        measure(() => client.call('test.mutate', null)),
      ]);

      expect(durations.length).toBe(6);
      for (const duration of durations) {
        // Нижняя граница с допуском 1 мс (фикс flaky, ревью T-119): реализация
        // считает задержку по Date.now, замер здесь — по performance.now;
        // расхождение часов даёт 499.x мс при фактически отработавшей
        // задержке (setTimeout срабатывает не ранее срока). Верхняя граница
        // NFR-001 — строго 800 мс.
        expect(duration)
          .withContext(`длительность ${duration} мс`)
          .toBeGreaterThanOrEqual(MOCK_DELAY_MS - 1);
        expect(duration).withContext(`длительность ${duration} мс`).toBeLessThanOrEqual(800);
      }
    });
  });

  describe('форма ApiError (AC «Форма ApiError»)', () => {
    it('MockValidationError(400, message, errors) → reject ApiError {status: 400, body: {message, errors}}', fakeAsync(() => {
      const client = makeClient();
      client.register('test.bad-input', () => {
        throw new MockValidationError(400, 'Данные заполнены неверно', {
          number: ['Номер должен быть положительным числом'],
          semester: ['Семестр — целое от 1 до 10'],
        });
      });

      const captured = captureRejection(client.call('test.bad-input', {}));
      tick(500);

      expect(captured.error).toEqual({
        status: 400,
        body: {
          message: 'Данные заполнены неверно',
          errors: {
            number: ['Номер должен быть положительным числом'],
            semester: ['Семестр — целое от 1 до 10'],
          },
        },
      } satisfies ApiError);
    }));

    it('без errors тело содержит только message: ключа errors нет', fakeAsync(() => {
      const client = makeClient();
      client.register('test.plain', () => {
        throw new MockValidationError(409, 'Группа с таким названием уже существует');
      });

      const captured = captureRejection(client.call('test.plain', {}));
      tick(500);

      const apiError = captured.error as ApiError;
      expect(apiError.status).toBe(409);
      expect(apiError.body.message).toBe('Группа с таким названием уже существует');
      expect(Object.prototype.hasOwnProperty.call(apiError.body, 'errors')).toBeFalse();
    }));

    it('все статусы набора IF-001 маппируются с сохранением message', fakeAsync(() => {
      const client = makeClient();
      const statuses: MockErrorStatus[] = [400, 401, 403, 404, 409, 429];
      const capturedByStatus = new Map<MockErrorStatus, unknown>();
      for (const status of statuses) {
        client.register(`test.status-${status}`, () => {
          throw new MockValidationError(status, `Отказ ${status}`);
        });
        capturedByStatus.set(status, captureRejection(client.call(`test.status-${status}`, null)));
      }
      tick(500);

      for (const status of statuses) {
        const apiError = capturedByStatus.get(status) as { error?: ApiError };
        expect(apiError.error?.status).withContext(`статус ${status}`).toBe(status);
        expect(apiError.error?.body.message).toBe(`Отказ ${status}`);
      }
    }));

    it('чужое исключение обработчика пробрасывается как есть (не маскируется под ApiError)', fakeAsync(() => {
      const client = makeClient();
      const boom = new Error('взрыв обработчика');
      client.register('test.bug', () => {
        throw boom;
      });

      const captured = captureRejection(client.call('test.bug', null));
      tick(500);
      expect(captured.error).toBe(boom);
    }));
  });

  describe('реестр обработчиков (ADR-003)', () => {
    it('call возвращает значение обработника; обработник получает params целиком', fakeAsync(() => {
      const client = makeClient();
      const params = { page: 2, pageSize: 10 };
      let seenParams: unknown = null;
      client.register('test.echo-params', (_db, p: unknown) => {
        seenParams = p;
        return { echoed: p };
      });

      let resolved: { echoed: unknown } | undefined;
      client.call<{ echoed: unknown }>('test.echo-params', params).then((r) => {
        resolved = r;
      });
      tick(500);

      expect(seenParams).toBe(params);
      expect(resolved?.echoed).toBe(params);
    }));

    it('обработник получает db мока: мутация через конвейер персистится в mock.db.v1', fakeAsync(() => {
      const client = makeClient();
      client.register('groups.create', (db, p: { name: string }) =>
        db.mutate((data) => {
          const group = { id: newRuntimeId(), name: p.name, studentCount: 0 };
          data.groups.push(group);
          return group;
        }),
      );

      let created: Group | undefined;
      client.call<Group>('groups.create', { name: 'ИК-224' }).then((g) => {
        created = g;
      });
      tick(500);

      // AC «Персистентность мутации» через полный конвейер: чтение ключа
      // localStorage содержит изменение.
      expect(created?.name).toBe('ИК-224');
      expect(created?.studentCount).toBe(0);
      expect(stored().groups).toEqual([created as Group]);
    }));

    it('неизвестный метод — отказ Error с именем метода, без ApiError', async () => {
      const client = makeClient();
      const error = await client.call('ghost.method', {}).then(
        () => undefined,
        (e: unknown) => e,
      );
      expect(error).toBeInstanceOf(Error);
      expect((error as Error).message).toContain('ghost.method');
      expect((error as ApiError).status).toBeUndefined();
      expect(localStorage.getItem(STORAGE_KEYS.mockDb)).toBeNull(); // мок-БД не трогалась
    });

    it('повторная register заменяет обработчик того же метода', fakeAsync(() => {
      const client = makeClient();
      client.register('test.replace', () => 'первый');
      client.register('test.replace', () => 'второй');

      let resolved: string | undefined;
      client.call<string>('test.replace', null).then((r) => {
        resolved = r;
      });
      tick(500);
      expect(resolved).toBe('второй');
    }));
  });
});
