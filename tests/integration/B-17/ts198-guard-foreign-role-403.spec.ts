/**
 * TS-198 (P1, FR-092): guard клиента — чужая роль → редирект /403.
 *
 * given — GET /auth/me (с базовым префиксом API) эмулируется 200 MeDto
 *         {role:'student', login, fullName, groupName:null}; открывается
 *         маршрут, доступный только роли teacher (список работ /works);
 * when  — загрузка приложения на /works с сессией student (инициализация
 *         завершена);
 * then  — выполнен редирект на /403 (не /login, не циклический — ровно одна
 *         завершённая навигация); маршрут /works не активирован
 *         (активирована страница 403 «Доступ запрещён»).
 *
 * Реальные компоненты: appConfig (маршруты с roleGuard('teacher') на /works),
 * authGuard/roleGuard, AuthService, SessionLifecycle.
 */
import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { NavigationEnd } from '@angular/router';

import {
  STUDENT_ME,
  completeInit,
  createB17App,
} from './helpers/b17-harness';

describe('TS-198: guard клиента — чужая роль → редирект /403 (FR-092)', () => {
  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    spyOn(console, 'error');
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    localStorage.clear();
    sessionStorage.clear();
  });

  it('сессия student на /works → редирект /403 (не /login), /works не активирован', async () => {
    const ctx = await createB17App();
    const navigationEnds: string[] = [];
    ctx.router.events.subscribe((event) => {
      if (event instanceof NavigationEnd) {
        navigationEnds.push(event.urlAfterRedirects);
      }
    });

    // given: сессия student — инициализация завершена (200 MeDto
    // {role:'student', login, fullName, groupName:null}).
    await completeInit(ctx, STUDENT_ME);
    expect(ctx.auth.currentUser())
      .withContext('сессия student установлена инициализацией')
      .toEqual(jasmine.objectContaining({ role: 'student', groupName: null }));

    // when: загрузка на маршруте /works (доступен только teacher).
    await ctx.harness.navigateByUrl('/works');
    await ctx.harness.fixture.whenStable();
    ctx.harness.fixture.detectChanges();

    // then: редирект на /403 — не /login и не цикл (ровно одна навигация).
    expect(ctx.router.url).withContext('финальный URL — /403').toBe('/403');
    expect(navigationEnds).withContext('ровно одна навигация — цикла нет').toEqual([
      '/403',
    ]);
    // Страница — внутри outlet оболочки AppShell (корневой outlet
    // RouterTestingHarness всегда рендерит AppShell): активация страницы
    // проверяется селектором хоста — конвенция app.routes.spec.ts.
    expect(ctx.harness.fixture.nativeElement.querySelector('app-page-403'))
      .withContext('активирована страница 403 внутри оболочки')
      .not.toBeNull();
    expect(ctx.harness.fixture.nativeElement.textContent)
      .withContext('текст страницы 403 отрисован')
      .toContain('Доступ запрещён');
    expect(ctx.harness.fixture.nativeElement.querySelector('app-works-page'))
      .withContext('маршрут /works не активирован')
      .toBeNull();
    expect(console.error).not.toHaveBeenCalled();
  });
});
