/**
 * TS-176 (P0, FR-092 AC «Холодный старт с сессией»): загрузка приложения
 * на маршруте /works через APP_INITIALIZER при действующих cookie от
 * прежнего входа teacher.
 *
 * given — действующие cookie от прежнего входа teacher; HttpTestingController
 *         перехватывает вызовы (тестовый HttpBackend вместо сети);
 * when  — загрузка приложения на маршруте /works (инициализация через
 *         APP_INITIALIZER — реальная цепочка appConfig);
 * then  — инициализация вызывает GET /auth/me (базовый префикс API);
 *         роль teacher известна
 *         ДО решения guard (кэш currentUser заполнен инициализацией, guard
 *         повторного loadMe не выполняет); маршрут /works открывается без
 *         редиректа на /login.
 *
 * Реальные компоненты: appConfig (маршруты + guards + интерцептор +
 * provideAppInitializer), AuthService, SessionLifecycle. Заглушки — только
 * данные страниц (LabsService) и медиазапросы (MockBreakpointObserver) —
 * см. helpers/b06-harness.ts.
 */
import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { NavigationEnd } from '@angular/router';

import {
  TEACHER_ME,
  completeInit,
  createB06App,
} from './helpers/b06-harness';

describe('TS-176: холодный старт с сессией — loadMe до решения guard (FR-092)', () => {
  beforeEach(() => {
    localStorage.clear();
    spyOn(console, 'error');
  });

  afterEach(() => {
    // Ни одного незакрытого/лишнего запроса (в т.ч. повторного /auth/me):
    // незакрытый повторный запрос сессии не прошёл бы verify.
    TestBed.inject(HttpTestingController).verify();
    localStorage.clear();
  });

  it('инициализация вызывает GET /auth/me; роль teacher известна до guard; /works открывается без /login', async () => {
    const ctx = await createB06App();
    const navigationEnds: string[] = [];
    ctx.router.events.subscribe((event) => {
      if (event instanceof NavigationEnd) {
        navigationEnds.push(event.urlAfterRedirects);
      }
    });

    // when: загрузка приложения — APP_INITIALIZER (реальная цепочка appConfig).
    await completeInit(ctx, TEACHER_ME);

    // then: роль teacher известна ДО решения guard — кэш заполнен init'ом.
    expect(ctx.auth.currentUser())
      .withContext('currentUser заполнен инициализацией до навигации')
      .toEqual(jasmine.objectContaining({ role: 'teacher' }));

    // when: загрузка на маршруте /works — guard решает по кэшу.
    await ctx.harness.navigateByUrl('/works');
    await ctx.harness.fixture.whenStable();
    ctx.harness.fixture.detectChanges();

    // then: маршрут /works открыт без редиректа на /login.
    expect(ctx.router.url).withContext('финальный URL — /works').toBe('/works');
    expect(navigationEnds).withContext('без редиректов на /login').toEqual(['/works']);
    // Страница — внутри outlet оболочки AppShell (корневой outlet
    // RouterTestingHarness всегда рендерит AppShell): активация страницы
    // проверяется селектором хоста — конвенция app.routes.spec.ts.
    expect(ctx.harness.fixture.nativeElement.querySelector('app-works-page'))
      .withContext('страница работ активирована внутри оболочки')
      .not.toBeNull();
    expect(console.error).not.toHaveBeenCalled();
  });

  it('loadMe выполняется ровно один раз — guard не повторяет запрос сессии', async () => {
    const ctx = await createB06App();
    await completeInit(ctx, TEACHER_ME);

    // Навигация по кэшу (роль уже известна): повторный /auth/me не появляется.
    await ctx.harness.navigateByUrl('/works');
    await ctx.harness.fixture.whenStable();

    expect(ctx.router.url).withContext('маршрут открыт без редиректа').toBe('/works');
    expect(console.error).not.toHaveBeenCalled();
  });
});
