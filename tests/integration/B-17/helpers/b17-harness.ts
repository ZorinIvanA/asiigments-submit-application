/**
 * Общий харнес интеграционных тестов батча B-17 (FR-092/FR-093, кейсы
 * TS-176/TS-177/TS-178/TS-179/TS-180/TS-198).
 *
 * Назначение — прогон сценариев холодного старта, guards и интерцептора на
 * РЕАЛЬНОЙ конфигурации приложения (appConfig.providers: provideRouter с
 * композицией guards, provideHttpClient(withInterceptors([authInterceptor])),
 * provideAppInitializer сессии) с перехватом HTTP через
 * HttpTestingController: provideHttpClientTesting ставится ПОСЛЕДНИМ
 * провайдером — побеждает как HttpBackend, цепочка интерцепторов
 * сохраняется (конвенция app.config.spec.ts).
 *
 * Заглушки — ТОЛЬКО компоненты вне предмета кейсов:
 *  - LabsService — данные страницы WorksPage (активируется на /works),
 *    чтобы сценарий сессии не зависел от этапа конверсии сервисов;
 *  - BreakpointObserver — медиазапросы окна (MockBreakpointObserver,
 *    существующий хелпер src/client/testing).
 * AuthService, SessionLifecycle, guards, authInterceptor, маршруты и
 * APP_INITIALIZER — всегда реальные.
 *
 * Конвенция URL — ADR-014: ожидаемые URL строятся из
 * TestBed.inject(API_BASE_URL); литералы '/api/v1' в spec запрещены (QG-004).
 */
import { BreakpointObserver } from '@angular/cdk/layout';
import { ApplicationInitStatus } from '@angular/core';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { API_BASE_URL } from '../../../../src/client/app/core/api-base-url';
import { appConfig } from '../../../../src/client/app/core/config/app.config';
import { AuthService, MeDto } from '../../../../src/client/app/core/services/auth.service';
import { LabsService } from '../../../../src/client/app/core/services/labs.service';
import { MockBreakpointObserver } from '../../../../src/client/testing/mock-breakpoint-observer';

/** MeDto преподавателя с действующей сессией (given TS-176). */
export const TEACHER_ME: MeDto = {
  login: 'teacher',
  fullName: 'Сидоров Семён Семёнович',
  role: 'teacher',
  groupName: null,
};

/** MeDto студента (given TS-198 — чужая роль для teacher-маршрута /works). */
export const STUDENT_ME: MeDto = {
  login: 'student01',
  fullName: 'Иванов Иван Иванович',
  role: 'student',
  groupName: null,
};

const EMPTY_PAGE = { items: [], total: 0, page: 1, pageSize: 10 };

/**
 * Заглушка данных списка работ: WorksPage (активируется на /works при
 * успешном guard) получает пустую страницу без обращения к домену.
 */
function makeLabsStub(): LabsService {
  const stub = {
    getList: jasmine
      .createSpy<LabsService['getList']>('getList')
      .and.resolveTo(EMPTY_PAGE),
    getById: jasmine
      .createSpy<LabsService['getById']>('getById')
      .and.rejectWith(new Error('B-17: getById не используется сценариями')),
    create: jasmine
      .createSpy<LabsService['create']>('create')
      .and.rejectWith(new Error('B-17: create не используется сценариями')),
    update: jasmine
      .createSpy<LabsService['update']>('update')
      .and.rejectWith(new Error('B-17: update не используется сценариями')),
    remove: jasmine
      .createSpy<LabsService['remove']>('remove')
      .and.resolveTo(undefined),
    getSemesters: jasmine
      .createSpy<LabsService['getSemesters']>('getSemesters')
      .and.resolveTo([]),
  };
  return stub as unknown as LabsService;
}

/** Контекст сценария холодного старта (TS-176/TS-177/TS-198). */
export interface B17AppContext {
  harness: RouterTestingHarness;
  router: Router;
  httpMock: HttpTestingController;
  apiBase: string;
  auth: AuthService;
}

/**
 * Конфигурирует TestBed РЕАЛЬНЫМ appConfig + тестовый HTTP-бэкенд и
 * создаёт RouterTestingHarness. APP_INITIALIZER ЕЩЁ НЕ завершён: сценарий
 * доводит его через completeInit(), перехватывая GET /auth/me.
 */
export async function createB17App(): Promise<B17AppContext> {
  TestBed.configureTestingModule({
    providers: [
      ...appConfig.providers,
      { provide: LabsService, useValue: makeLabsStub() },
      { provide: BreakpointObserver, useValue: new MockBreakpointObserver() },
      provideHttpClientTesting(),
      provideNoopAnimations(),
    ],
  });
  const harness = await RouterTestingHarness.create();
  return {
    harness,
    router: TestBed.inject(Router),
    httpMock: TestBed.inject(HttpTestingController),
    apiBase: TestBed.inject(API_BASE_URL),
    auth: TestBed.inject(AuthService),
  };
}

/**
 * Sentinel: «Cookie нет/протухли» (given TS-177) — /auth/me отвечает 401.
 */
export const RESPOND_401 = { sentinel: '401' } as const;

/**
 * Завершает фазу инициализации (APP_INITIALIZER): перехватывает
 * единственный GET /auth/me и отвечает 200 MeDto либо 401 (sentinel
 * RESPOND_401), затем ждёт готовности ApplicationInitStatus.
 *
 * Ветка 401: /auth/me НЕ входит в исключённое множество IF-014
 * {/auth/login, /auth/register, /auth/refresh, /auth/recovery/*,
 * /auth/reset-password}, поэтому 401 от /auth/me порождает ровно один
 * дедуплицированный POST /auth/refresh интерцептора. completeInit
 * перехватывает этот refresh, отвечает отказом 401 и только затем ждёт
 * завершения инициализации: исходный /auth/me завершается нормализованным
 * 401 — loadMe трактует его как анонима; навигации из интерцептора во
 * время init нет (фаза инициализации — редирект определяют guards).
 */
export async function completeInit(
  ctx: B17AppContext,
  me: MeDto | typeof RESPOND_401,
): Promise<void> {
  // Обращение к ApplicationInitStatus запускает/ждёт APP_INITIALIZER-цепочку
  // (инициализация сессии → GET /auth/me).
  const initDone = TestBed.inject(ApplicationInitStatus).donePromise;
  const meRequest = ctx.httpMock.expectOne(`${ctx.apiBase}/auth/me`);
  expect(meRequest.request.method)
    .withContext('инициализация вызывает GET /auth/me')
    .toBe('GET');
  expect(meRequest.request.withCredentials)
    .withContext('запрос сессии — с cookie (withCredentials)')
    .toBeTrue();
  if (me === RESPOND_401) {
    meRequest.flush(
      { message: 'Не авторизован' },
      { status: 401, statusText: 'Unauthorized' },
    );
    const refreshRequest = ctx.httpMock.expectOne(`${ctx.apiBase}/auth/refresh`);
    expect(refreshRequest.request.method)
      .withContext('401 на /auth/me → ровно один POST /auth/refresh (IF-014)')
      .toBe('POST');
    expect(refreshRequest.request.withCredentials)
      .withContext('refresh — с cookie (withCredentials)')
      .toBeTrue();
    refreshRequest.flush(
      { message: 'Не авторизован' },
      { status: 401, statusText: 'Unauthorized' },
    );
  } else {
    meRequest.flush(me);
  }
  // Инициализация обязана завершиться без необработанных исключений
  // (IF-014: 401/сеть → аноним, markInitDone).
  await initDone;
}
