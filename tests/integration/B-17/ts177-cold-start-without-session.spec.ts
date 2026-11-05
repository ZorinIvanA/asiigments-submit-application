/**
 * TS-177 (P1, FR-092 AC «Холодный старт без сессии»): загрузка приложения
 * на /works, когда cookie нет/протухли (GET /auth/me → 401).
 *
 * given — cookie нет/протухли; GET /auth/me эмулируется 401;
 * when  — загрузка приложения на /works;
 * then  — loadMe завершён (401 обработан как null, без необработанных
 *         исключений — ApplicationInitStatus разрешён); выполнен redirect
 *         на /login; циклических редиректов нет (ровно одна завершённая
 *         навигация, ни одного повторного /auth/me, ровно один refresh).
 *         401 на /auth/me порождает ровно один дедуплицированный
 *         POST /auth/refresh — /auth/me не входит в исключённое множество
 *         IF-014; во время инициализации интерцептор не навигирует —
 *         редирект определяют guards.
 *
 * Реальные компоненты: appConfig (маршруты + guards + интерцептор +
 * provideAppInitializer), AuthService, SessionLifecycle.
 */
import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { NavigationEnd } from '@angular/router';

import {
  RESPOND_401,
  completeInit,
  createB17App,
} from './helpers/b17-harness';

describe('TS-177: холодный старт без сессии — редирект /login без циклов (FR-092)', () => {
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

  it('401 на /auth/me обработан как null; /works → /login; циклических редиректов нет', async () => {
    const ctx = await createB17App();
    const navigationEnds: string[] = [];
    ctx.router.events.subscribe((event) => {
      if (event instanceof NavigationEnd) {
        navigationEnds.push(event.urlAfterRedirects);
      }
    });

    // when: загрузка приложения без сессии — 401 от /auth/me и отказ
    // единственного дедуплицированного refresh (перехват в completeInit).
    await completeInit(ctx, RESPOND_401);

    // then: loadMe завершён — 401 обработан как null, без необработанных
    // исключений (await completeInit не отвергся — init разрешён).
    expect(ctx.auth.currentUser()).withContext('аноним: кэш пуст').toBeNull();
    // Дедупликация и отсутствие цикла на старте: единственность /auth/me и
    // refresh гарантирована expectOne в completeInit («found N» при лишних).
    // Angular 20: match() изымает запросы (consume-on-read) — здесь
    // утверждается отсутствие ПОВТОРНЫХ запросов (анти-цикл).
    expect(ctx.httpMock.match(`${ctx.apiBase}/auth/me`).length)
      .withContext('повторных запросов сессии нет — один, без цикла')
      .toBe(0);
    expect(ctx.httpMock.match(`${ctx.apiBase}/auth/refresh`).length)
      .withContext('401 на /auth/me → повторных refresh нет')
      .toBe(0);

    // when: загрузка на /works.
    await ctx.harness.navigateByUrl('/works');
    await ctx.harness.fixture.whenStable();

    // then: redirect на /login; циклов нет — ровно одна завершённая
    // навигация, повторных HTTP-запросов нет (verify в afterEach).
    expect(ctx.router.url).withContext('редирект на /login').toBe('/login');
    expect(navigationEnds).withContext('ровно одна навигация — цикла нет').toEqual([
      '/login',
    ]);
    expect(console.error).not.toHaveBeenCalled();
  });

  it('401 во время инициализации: единственный refresh, навигации из интерцептора нет — редирект определяют guards', async () => {
    const ctx = await createB17App();

    // Инициализация: 401 на /auth/me → единственный дедуплицированный
    // POST /auth/refresh (перехвачен и закрыт в completeInit); навигации
    // из интерцептора во время init нет (Router.navigate — только вне фазы
    // инициализации, IF-014).
    await completeInit(ctx, RESPOND_401);

    expect(ctx.router.url)
      .withContext('во время инициализации навигации нет — решают guards')
      .not.toBe('/login');
    expect(ctx.auth.currentUser())
      .withContext('исчерпанный refresh оставляет анонима (401 → null)')
      .toBeNull();
  });
});
