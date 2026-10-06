/**
 * Общее интеграционное окружение демо-режима (общая инфраструктура батчей
 * интеграционных тестов, RSK-004/RSK-005): единая процедура сброса
 * демо-данных и установка сессии.
 *
 * Процедура сброса перед КАЖДЫМ сценарием (в beforeEach):
 *  1) `resetDemoEnv()` — очистка localStorage (mock.db.v1, mock.session.userId)
 *     и sessionStorage (recovery.flow.v1) + подключение детерминированного
 *     сида `seedFixtures` поставщиком мок-БД;
 *  2) СВЕЖИЙ `MockApiClient` (`new MockApiClient()` + `setupMockLayer(client)`),
 *     предоставленный TestBed провайдером `{ provide: MockApiClient, useValue:
 *     client }` — состояние в памяти клиента переживает `localStorage.clear()`,
 *     поэтому старый экземпляр продолжит отдавать протекшие данные (паттерн
 *     T-101 notes_for_tester);
 *  3) в afterEach — `resetDemoEnvAfterSpec()`: возврат дефолтного (пустого)
 *     поставщика сида и повторная очистка storage, чтобы сид батча не
 *     «протекал» в чужие спеки (RSK-005).
 *
 * Ручная процедура сброса для визуальных сценариев VS-001..VS-011 (без кода):
 * в браузере открыть DevTools → Application → Storage → «Clear site data»
 * (либо выполнить в консоли `localStorage.clear(); sessionStorage.clear();`)
 * и перезагрузить страницу — при первом же обращении мок-БД применит свежий
 * детерминированный сид (демо-учётки: teacher/teacher123!,
 * student01..student32/student123!).
 */
import { configureMockDbSeed, resetMockDbSeed } from '../mock/mock-db';
import { seedFixtures } from '../mock/seed';
import { STORAGE_KEYS } from '../shared/models';

/**
 * Сброс демо-окружения перед сценарием: чистые хранилища + детерминированный
 * сид как поставщик мок-БД (применится при первом обращении свежего клиента).
 */
export function resetDemoEnv(): void {
  localStorage.clear();
  sessionStorage.clear();
  configureMockDbSeed(seedFixtures);
}

/**
 * Сброс после спеки (afterEach): дефолтный пустой поставщик сида + очистка
 * хранилищ — изоляция от соседних спек-файлов (RSK-005).
 */
export function resetDemoEnvAfterSpec(): void {
  resetMockDbSeed();
  localStorage.clear();
  sessionStorage.clear();
}

/**
 * Установка сессии mock.session.userId (после resetDemoEnv, до первого
 * обращения приложения к guards/сервисам): `loginAs(seedUserIdByLogin(...))`.
 */
export function loginAs(userId: string): void {
  localStorage.setItem(STORAGE_KEYS.session, userId);
}

/**
 * uuid пользователя детерминированного сида по логину (ADR-107: id
 * воспроизводимы — seed 20260927); чтение напрямую из сида, без мок-вызова.
 */
export function seedUserIdByLogin(login: string): string {
  const user = seedFixtures().users.find((u) => u.login === login);
  if (user === undefined) {
    throw new Error(`seedUserIdByLogin: в сиде нет пользователя «${login}»`);
  }
  return user.id;
}
