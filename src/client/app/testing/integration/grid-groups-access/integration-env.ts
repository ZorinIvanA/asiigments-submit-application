/**
 * Тестовая среда интеграционных спеков батча 3 «Группы · Доступ · Ведомость ·
 * Сдачи студента · Оболочка» (файловая зона test-writer'а батча 3).
 *
 * Интеграционный уровень: страницы фич создаются через TestBed с НАСТОЯЩИМИ
 * core-сервисами и НАСТОЯЩИМ мок-слоем (setupMockLayer поверх root-экземпляра
 * MockApiClient, IF-110) и сидом seedFixtures (ADR-107); сессия выставляется
 * через владельца ключа mock.session.userId — SessionStore домена Auth
 * (ADR-105). Ни один сервис домена не замещается заглушкой.
 *
 * Хелперы — shared-инфраструктура батча 3 (shared_infra_requests из
 * scenarios.batch3.json): хелпер сессии/входа и хелпер эмуляции другого
 * сеанса реализованы локально в зоне батча. ОБЩИЙ
 * src/client/app/testing/integration-env.ts (батч 1) уже существует — зона остаётся
 * самодостаточной, консолидация отложена; сверка подходов на протечку
 * состояния (CR-012):
 *  - батч 1: СВЕЖИЙ `new MockApiClient()` на каждый spec через TestBed-
 *    провайдер + resetDemoEnvAfterSpec() (resetMockDbSeed + очистка storage);
 *  - батч 3 (эта зона): root-экземпляр MockApiClient через TestBed.inject —
 *    TestBed пересоздаёт корневой инжектор на каждый spec, поэтому клиент
 *    (и его кэш MockDb в памяти) не протекает между спеками так же, как
 *    свежий провайдер; чтобы сид зоны не протекал в ЧУЖИЕ спек-файлы,
 *    идущие после неё в прогоне, каждый afterEach вызывает
 *    resetZoneEnvAfterSpec() (resetMockDbSeed + очистка storage) — ровно
 *    по образцу resetDemoEnvAfterSpec батча 1 (RSK-005).
 *
 *  - installMockLayer()  — сборка мок-слоя на TestBed-клиенте (IF-110);
 *  - hydrateSeed()       — детерминированная гидратация сида в mock.db.v1;
 *  - loginAs(...)        — сессия демо-учётки сида (teacher/studentNN);
 *  - mutateMockDb(fn)    — эмуляция другого сеанса: точечная транзакционная
 *                          мутация mock.db.v1 (MockDb.mutate персистит
 *                          localStorage и держит кэш обработчиков
 *                          согласованным — тот же экземпляр MockDb);
 *  - settle(...)         — доведение таймингов мока (fakeAsync + tick по
 *                          500 мс на вызов, MOCK_DELAY_MS) и циклов CD
 *                          PrimeNG-контролов (ngModel доезжает с запаздыванием).
 */
import { tick, ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Select } from 'primeng/select';

import { setupMockLayer } from '../../../mock';
import { SessionStore } from '../../../mock/auth/session-store';
import { MockApiClient } from '../../../mock/mock-api-client';
import { MockDb, MockDbData, resetMockDbSeed } from '../../../mock/mock-db';
import { STORAGE_KEYS } from '../../../shared/models';
import { NOTIFICATION_AUTO_CLOSE_MS } from '../../../shared/notifications/notification-model';

/** Подключает обработчики всех шести доменов к TestBed-клиенту (IF-110). */
export function installMockLayer(): MockApiClient {
  const client = TestBed.inject(MockApiClient);
  setupMockLayer(client);
  return client;
}

/**
 * Сброс после спеки зоны батча 3 (afterEach) — по образцу
 * resetDemoEnvAfterSpec общего интеграционного окружения батча 1 (RSK-005):
 * дефолтный пустой поставщик сида + очистка хранилищ, чтобы сид зоны не
 * «протекал» в чужие спек-файлы, идущие после неё в прогоне.
 */
export function resetZoneEnvAfterSpec(): void {
  resetMockDbSeed();
  localStorage.clear();
  sessionStorage.clear();
}

/**
 * Экземпляр MockDb, над которым работают обработчики мок-слоя: это приватное
 * поле MockApiClient, доступ к которому тесту нужен, чтобы мутации «другого
 * сеанса» шли через тот же объект (кэш в памяти остаётся согласованным).
 *
 * Контракт хелпера (CR-013): поле `db` — часть внутренней структуры
 * MockApiClient; хелпер опирается на неё как на тестовый шов и
 * СОПРОВОЖДАЕТСЯ sanity-тестом (submissions-grid.spec.ts, «integration-env
 * (CR-013)»), доказывающим эквивалентность «мутация через mockDbOf ↔
 * видимая обработчикам ↔ персистентна в localStorage[mock.db.v1]».
 * Публичный API мока для этого не расширяется (вне зоны тестописца).
 */
export function mockDbOf(client: MockApiClient = TestBed.inject(MockApiClient)): MockDb {
  return (client as unknown as { db: MockDb }).db;
}

/** Гидратация сида: первая вычитка пишет mock.db.v1 из seedFixtures. */
export function hydrateSeed(): MockDbData {
  return mockDbOf().read();
}

/** uuid пользователя по логину сида (teacher/studentNN); без него — ошибка. */
export function userIdOf(db: MockDbData, login: string): string {
  const user = db.users.find((candidate) => candidate.login === login);
  if (user === undefined) {
    throw new Error(`integration-env: в сиде нет пользователя ${login}`);
  }
  return user.id;
}

/** uuid демо-группы по названию (ИК-221/ИК-222/ИК-223). */
export function groupIdOf(db: MockDbData, name: string): string {
  const group = db.groups.find((candidate) => candidate.name === name);
  if (group === undefined) {
    throw new Error(`integration-env: в сиде нет группы ${name}`);
  }
  return group.id;
}

/** uuid работы по паре (семестр, номер). */
export function labIdOf(db: MockDbData, semester: number, number: number): string {
  const lab = db.labs.find(
    (candidate) => candidate.semester === semester && candidate.number === number,
  );
  if (lab === undefined) {
    throw new Error(`integration-env: в сиде нет работы ${semester}:${number}`);
  }
  return lab.id;
}

/** Логин демо-студента по номеру NN (1 → student01). */
export function studentLogin(nn: number): string {
  return `student${String(nn).padStart(2, '0')}`;
}

/**
 * Сессия демо-учётки: ключ mock.session.userId ставит владелец ключа —
 * SessionStore домена Auth (ADR-105). Возвращает uuid пользователя.
 */
export function loginAs(db: MockDbData, login: string): string {
  const id = userIdOf(db, login);
  TestBed.inject(SessionStore).setUserId(id);
  return id;
}

/** Сессия по готовому uuid (переключение «другого сеанса» внутри теста). */
export function switchSession(userId: string): void {
  TestBed.inject(SessionStore).setUserId(userId);
}

/** Ключ сессии в localStorage — для прямых проверок очистки (TS-375). */
export function sessionKeyRaw(): string | null {
  return localStorage.getItem(STORAGE_KEYS.session);
}

/**
 * Эмуляция другого сеанса (shared_infra_requests, TS-307/310/313/314/324/
 * 325/326/327/335/359): транзакционная мутация mock.db.v1 мимо UI —
 * обработчики продолжают работать над тем же состоянием (кэш MockDb общий),
 * изменение немедленно персистится в localStorage.
 */
export function mutateMockDb(mutation: (data: MockDbData) => void): void {
  mockDbOf().mutate((data) => {
    mutation(data);
    return undefined;
  });
}

/**
 * Доводит асинхронные цепочки страницы до конца при fakeAsync: тайминги мока
 * (tick ms — 500 мс на каждый мок-вызов) плюс три раунда «микрозадача + CD» —
 * внутренние состояния p-select/p-datepicker (label, disabled, value
 * внутреннего input) доезжают с запаздыванием на один-два прохода CD.
 */
export function settle(fixture: ComponentFixture<unknown>, ms: number): void {
  tick(ms);
  fixture.detectChanges();
  for (let round = 0; round < 3; round++) {
    tick(0);
    fixture.detectChanges();
  }
}

/** Один проход микрозадач NgModel + CD (без таймингов мока). */
export function flushNgModel(fixture: ComponentFixture<unknown>): void {
  tick(0);
  fixture.detectChanges();
}

/** Гасит таймер автозакрытия уведомления (5000 мс, IF-009) — для fakeAsync. */
export function drainNotifications(): void {
  tick(NOTIFICATION_AUTO_CLOSE_MS + 1);
}

/** Поиск по фикстуре с фолбэком в документ — оверлеи PrimeNG вне фикстуры. */
export function qsIn(fixture: ComponentFixture<unknown>, selector: string): HTMLElement | null {
  const root = fixture.nativeElement as HTMLElement;
  return root.querySelector(selector) ?? (document.querySelector(selector) as HTMLElement | null);
}

/** То же для querySelectorAll. */
export function qsAllIn(fixture: ComponentFixture<unknown>, selector: string): HTMLElement[] {
  const root = fixture.nativeElement as HTMLElement;
  const inFixture = Array.from(root.querySelectorAll<HTMLElement>(selector));
  return inFixture.length > 0 ? inFixture : Array.from(document.querySelectorAll<HTMLElement>(selector));
}

/** Кнопка по точному тексту — в фикстуре или в документе (оверлеи). */
export function buttonByText(fixture: ComponentFixture<unknown>, label: string): HTMLButtonElement {
  const found = qsAllIn(fixture, 'button').find(
    (candidate) => candidate.textContent?.trim() === label,
  );
  if (found === undefined) {
    throw new Error(`integration-env: не найдена кнопка «${label}»`);
  }
  return found as HTMLButtonElement;
}

/** Открывает оверлей p-select (клик, как пользователь). */
export function openSelect(fixture: ComponentFixture<unknown>, select: Element): void {
  (select as HTMLElement).click();
  fixture.detectChanges();
}

/** Выбирает опцию с подписью label в открытом оверлее p-select. */
export function pickOption(fixture: ComponentFixture<unknown>, label: string): void {
  const option = qsAllIn(fixture, 'li.p-select-option').find(
    (candidate) => candidate.textContent?.trim() === label,
  );
  if (option === undefined) {
    throw new Error(`integration-env: в открытом селекторе нет опции «${label}»`);
  }
  option.click();
  fixture.detectChanges();
}

/** Подпись выбранной опции p-select. */
export function selectLabelOf(select: Element): string {
  return select.querySelector('.p-select-label')?.textContent?.trim() ?? '';
}

/** Значение модели p-select в строке таблицы (аменда 6: groupId из DTO). */
export function selectModelValueOf(
  fixture: ComponentFixture<unknown>,
  row: HTMLElement,
): unknown {
  const rowDe = fixture.debugElement
    .queryAll(By.css('tbody tr'))
    .find((candidate) => candidate.nativeElement === row);
  if (rowDe === undefined) {
    throw new Error('integration-env: строка не найдена в debugElement');
  }
  const selectDe = rowDe.query(By.directive(Select));
  if (selectDe === null) {
    throw new Error('integration-env: в строке нет p-select');
  }
  return (selectDe.componentInstance as unknown as { modelValue(): unknown }).modelValue();
}
