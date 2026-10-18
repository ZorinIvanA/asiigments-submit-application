/**
 * Юнит-тесты API_BASE_URL (C-013, контракт IF-014, ADR-009/ADR-014):
 *  - токен предоставляется фабрикой без явных провайдеров — TestBed.inject
 *    работает в любом spec без обвязки (конвенция тестовых URL ADR-014);
 *  - значение по умолчанию — непустой ОТНОСИТЕЛЬНЫЙ префикс (под прокси
 *    dev, FR-027); сам литерал в spec не воспроизводится — единственное
 *    строковое вхождение префикса живёт в api-base-url.ts (гейт QG-004);
 *  - токен переопределяется провайдером (точка конфигурации клиента).
 */
import { TestBed } from '@angular/core/testing';

import { API_BASE_URL } from './api-base-url';

describe('API_BASE_URL — контракт IF-014 (ADR-009/ADR-014)', () => {
  it('дефолт предоставляется фабрикой токена без явных провайдеров', () => {
    TestBed.configureTestingModule({});

    const apiBase = TestBed.inject(API_BASE_URL);

    expect(typeof apiBase).toBe('string');
    expect(apiBase.length).toBeGreaterThan(0);
    // Относительный префикс: запросы идут через прокси dev-сервера (FR-027).
    expect(apiBase.startsWith('/')).withContext('относительный путь').toBeTrue();
    expect(apiBase.endsWith('/')).withContext('без хвостового слеша').toBeFalse();
  });

  it('переопределяется провайдером — единая точка конфигурации базового URL', () => {
    TestBed.configureTestingModule({
      providers: [{ provide: API_BASE_URL, useValue: '/test-api' }],
    });

    expect(TestBed.inject(API_BASE_URL)).toBe('/test-api');
  });
});
