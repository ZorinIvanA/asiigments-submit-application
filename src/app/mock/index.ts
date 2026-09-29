/**
 * Агрегатор мок-слоя (C-107, IF-110, ADR-101/ADR-110): единственная точка
 * сборки — подключение сида и регистрация обработчиков всех шести доменов.
 *
 * Вызывается один раз при бутстрапе через provideAppInitializer
 * (core/config/app.config.ts) до первого обращения страниц к сервисам core;
 * повторный вызов идемпотентен — повторная регистрация заменяет обработчик
 * (IF-001). Наружу мок-слой импортируют только сервисы core (FR-002);
 * страницы и сервисы не импортируют доменные обработчики напрямую.
 */
import { inject } from '@angular/core';

import { registerAuthHandlers } from './auth/handlers';
import { registerGroupsHandlers } from './groups/handlers';
import { registerLabsHandlers } from './labs/handlers';
import { MockApiClient } from './mock-api-client';
import { configureMockDbSeed } from './mock-db';
import { registerProfileHandlers } from './profile/handlers';
import { seedFixtures } from './seed';
import { registerStudentsHandlers } from './students/handlers';
import { registerSubmissionsHandlers } from './submissions/handlers';

/**
 * Собирает мок-слой: configureMockDbSeed(seedFixtures) + последовательная
 * регистрация обработчиков всех шести доменов (IF-110). Клиент — параметр:
 * бутстрап вызывает без аргумента (инжекционный контекст APP_INITIALIZER —
 * берётся root-экземпляр MockApiClient, тот же, что у сервисов core), тесты
 * подставляют собственный экземпляр.
 */
export function setupMockLayer(client?: MockApiClient): void {
  const target = client ?? inject(MockApiClient);
  configureMockDbSeed(seedFixtures);
  registerAuthHandlers(target);
  registerLabsHandlers(target);
  registerGroupsHandlers(target);
  registerStudentsHandlers(target);
  registerSubmissionsHandlers(target);
  registerProfileHandlers(target);
}
