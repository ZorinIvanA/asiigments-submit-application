/**
 * TS-176 (P0, FR-092 AC «Холодный старт с сессией»): загрузка приложения
 * на маршруте /works через APP_INITIALIZER при действующих cookie от
 * прежнего входа teacher.
 *
 * given — действующие cookie от прежнего входа teacher; HttpTestingController
 *         перехватывает вызовы (тестовый HttpBackend вместо сети);
 * when  — загрузка приложения на маршруте /works (APP_INITIALIZER —
 *         реальная цепочка appConfig);
 * then  — инициализация вызывает GET /auth/me (с базовым префиксом API,
 *         withCredentials); роль teacher известна ДО решения guard (кэш
 *         currentUser заполнен инициализацией, guard HTTP-вызовов не
 *         выполняет); маршрут /works открывается без редиректа на /login.
 *
 * Реальные компоненты: appConfig (маршруты + guards + интерцептор +
 * provideAppInitializer), AuthService, SessionLifecycle. Заглушки — только
 * данные страницы WorksPage (LabsService) и медиазапросы — helpers/b17-harness.
 */
import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { NavigationEnd } from '@angular/router';

import {
  TEACHER_ME,
  completeInit,
  createB17App,
} from './helpers/b17-harness';

describe('TS-176: холодный старт с сессией — loadMe до решения guard (FR-092)', () => {
  beforeEach(() => {
    // Сессия живёт ТОЛЬКО в памяти AuthService (FR-092): localStorage-маркер
    // прежнего входа не читается — стартуем с чистыми хранилищами.
    localStorage.clear();
    sessionStorage.clear();
    spyOn(console, 'error');
  });

  afterEach(() => {
    // Ни одного незакрытого/лишнего запроса: повторный /auth/me от guard
    // не прошёл бы verify — «роль известна до решения guard».
    TestBed.inject(HttpTestingController).verify();
    localStorage.clear();
    sessionStorage.clear();
  });

  it('инициализация вызывает GET /auth/me; роль teacher известна до guard; /works открывается без редиректа на /login', async () => {
    const ctx = await createB17App();
    const navigationEnds: string[] = [];
    ctx.router.events.subscribe((event) => {
      if (event instanceof NavigationEnd) {
        navigationEnds.push(event.urlAfterRedirects);
      }
    });

    // when: загрузка приложения — APP_INITIALIZER (реальная цепочка appConfig)
    // с действующей cookie (перехваченный /auth/me отвечает 200 MeDto teacher).
    await completeInit(ctx, TEACHER_ME);

    // then: роль teacher известна ДО решения guard — кэш currentUser
    // заполнен инициализацией ещё до первой навигации.
    expect(ctx.auth.currentUser())
      .withContext('currentUser заполнен инициализацией до навигации')
      .toEqual(jasmine.objectContaining({ role: 'teacher' }));
    expect(ctx.auth.isAuthenticated())
      .withContext('isAuthenticated синхронно отражает кэш')
      .toBeTrue();
    // Единственность запроса сессии гарантирована expectOne в completeInit
    // (повторный/лишний уронил бы его с «found N»). Angular 20: match()
    // ИЗЪЯТ из открытых запросов (consume-on-read), поэтому здесь и далее
    // утверждается отсутствие ПОВТОРНЫХ /auth/me.
    expect(ctx.httpMock.match(`${ctx.apiBase}/auth/me`).length)
      .withContext('повторных запросов сессии на холодном старте нет')
      .toBe(0);

    // when: загрузка на маршруте /works — guard решает по кэшу, без HTTP.
    await ctx.harness.navigateByUrl('/works');
    await ctx.harness.fixture.whenStable();
    ctx.harness.fixture.detectChanges();

    // then: маршрут /works открыт без редиректа на /login; guard повторного
    // /auth/me не выполняет (незакрытый повтор не прошёл бы verify).
    expect(ctx.router.url).withContext('финальный URL — /works').toBe('/works');
    expect(navigationEnds).withContext('без редиректов на /login').toEqual(['/works']);
    expect(ctx.httpMock.match(`${ctx.apiBase}/auth/me`).length)
      .withContext('guard не повторяет запрос сессии — роль уже известна')
      .toBe(0);
    // Страница активирована внутри оболочки AppShell (корневой outlet
    // RouterTestingHarness всегда рендерит AppShell) — конвенция
    // app.routes.spec.ts по селектору хоста.
    expect(ctx.harness.fixture.nativeElement.querySelector('app-works-page'))
      .withContext('страница работ активирована внутри оболочки')
      .not.toBeNull();
    expect(console.error).not.toHaveBeenCalled();
  });
});
