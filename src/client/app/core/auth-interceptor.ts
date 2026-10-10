/**
 * Функциональный HTTP-интерцептор аутентификации (C-013, контракт IF-014,
 * FR-025/FR-026):
 *  - on API-запросы (URL от API_BASE_URL) выставляется withCredentials —
 *    cookie-аутентификация (FR-025); запросы вне API проходят без изменений;
 *  - 401 на API-запрос вне списка {/auth/login, /auth/register,
 *    /auth/refresh, /auth/recovery/*, /auth/reset-password} —
 *    дедуплицируемый однократный POST /auth/refresh; успех — повтор
 *    исходного запроса ровно один раз; неудача — событие SessionLifecycle
 *    и редирект /login (вне фазы инициализации — ADR-016);
 *  - параллельные 401 дожидаются одного refresh-запроса (SHOULD FR-026);
 *    после неудачного refresh повторные 401 не порождают новых refresh-
 *    вызовов (нет цикла): фиксатор неудачного refresh действует ДО
 *    СЛЕДУЮЩЕЙ УСПЕШНОЙ АУТЕНТИФИКАЦИИ — сброс происходит по каналу
 *    SessionLifecycle.sessionRestored$ (AuthService публикует после
 *    успешных login/register), и после повторного входа без перезагрузки
 *    страницы тихий refresh восстанавливается (ADR-021, закрытие CR-001);
 *    событие недействительной сессии публикуется не более одного раза на
 *    дедуплицированный refresh (гарантия IF-014);
 *  - HTTP-отказы нормализуются в ApiError {status, body: {message, errors?}}
 *    — баннеры body.message работают без изменений (FR-025).
 *
 * Сопряжение с доменными сервисами — ТОЛЬКО через канал SessionLifecycle
 * (ADR-019, закрытие ISS-003): файлы C-013 не импортируют AuthService и не
 * выполняют запросов сброса сессии (никаких POST /auth/logout — сброс
 * состояния происходит по подписке sessionExpired$ без HTTP-вызовов).
 *
 * Регистрация — в app.config (T-018): provideHttpClient(withInterceptors([
 * authInterceptor])) + provideHttpClient с API_BASE_URL по умолчанию.
 */
import {
  HttpErrorResponse,
  HttpEvent,
  HttpHandlerFn,
  HttpInterceptorFn,
  HttpRequest,
  HttpClient,
} from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { catchError, finalize, map, shareReplay, switchMap } from 'rxjs/operators';

import { API_BASE_URL } from './api-base-url';
import { normalizeHttpError } from './http-errors';
import { SessionLifecycle } from './session-lifecycle';

/**
 * Пути auth-эндпойнтов (относительно базового URL), чьи 401/отказы
 * обрабатываются вызывающей страницей: refresh для них не запускается
 * (FR-026: логин с неверным паролем не триггерит refresh).
 */
const AUTH_EXCLUDED_PATHS = [
  '/auth/login',
  '/auth/register',
  '/auth/refresh',
  '/auth/reset-password',
] as const;

/** API-запрос: URL начинается с базового префикса API_BASE_URL (ADR-009). */
function isApiUrl(url: string, apiBase: string): boolean {
  return url === apiBase || url.startsWith(`${apiBase}/`);
}

/** Auth-эндпойнт (включая всё семейство /auth/recovery/*) — вне refresh. */
function isAuthEndpoint(url: string, apiBase: string): boolean {
  if (!url.startsWith(apiBase)) {
    return false;
  }
  const path = url.slice(apiBase.length);
  return (
    (AUTH_EXCLUDED_PATHS as readonly string[]).includes(path) ||
    path === '/auth/recovery' ||
    path.startsWith('/auth/recovery/')
  );
}

/**
 * Дедупликация refresh — внутреннее состояние интерцептора (C-013):
 * первый 401 создаёт единственный POST /auth/refresh, параллельные 401
 * дожидаются его же результата; неудача фиксируется «фиксатором» —
 * повторные 401 больше не порождают refresh-вызовов (запрет цикла,
 * FR-026). Фиксатор действует ДО СЛЕДУЮЩЕЙ УСПЕШНОЙ АУТЕНТИФИКАЦИИ,
 * а не всё время жизни приложения: координатор подписан на канал
 * SessionLifecycle.sessionRestored$ (AuthService публикует
 * markSessionRestored() после успешных login/register — 2xx) и сбрасывает
 * фиксатор (broken=false), поэтому после повторного входа SPA-навигацией
 * без перезагрузки страницы тихий refresh восстанавливается (ADR-021,
 * закрытие CR-001); сброс идемпотентен, повторная публикация безвредна
 * (IF-014). Публикация события недействительной сессии выполняется здесь —
 * интерцепторной стороной C-013 через канал SessionLifecycle (ADR-019) —
 * ровно один раз на дедуплицированный refresh.
 */
@Injectable({ providedIn: 'root' })
class RefreshCoordinator {
  private readonly http = inject(HttpClient);
  private readonly apiBase = inject(API_BASE_URL);
  private readonly lifecycle = inject(SessionLifecycle);

  /** Активный (единственный) refresh-вызов; null — активных нет. */
  private inflight: Observable<boolean> | null = null;

  /** Фиксатор неудачного refresh: true — refresh не вызывается до сброса. */
  private broken = false;

  constructor() {
    // Сброс фиксатора по каналу восстановления сессии (ADR-021, CR-001):
    // только факты успешной аутентификации (login/register из AuthService)
    // возвращают тихий refresh в строй. Подписка живёт столько же, сколько
    // root-координатор (время жизни приложения); интерцептор не импортирует
    // AuthService — сопряжение исключительно через SessionLifecycle
    // (ADR-019).
    this.lifecycle.sessionRestored$.subscribe(() => {
      this.broken = false;
    });
  }

  /** true — сессия обновлена, исходный запрос можно повторить; false — нет. */
  tryRefresh(): Observable<boolean> {
    if (this.broken) {
      return of(false);
    }
    if (this.inflight !== null) {
      return this.inflight;
    }
    this.inflight = this.http
      .post(`${this.apiBase}/auth/refresh`, null, { withCredentials: true })
      .pipe(
        map(() => true),
        catchError(() => {
          this.broken = true;
          // Ровно одно событие на дедуплицированный refresh (IF-014).
          this.lifecycle.notifySessionExpired();
          return of(false);
        }),
        finalize(() => {
          this.inflight = null;
        }),
        shareReplay({ bufferSize: 1, refCount: false }),
      );
    return this.inflight;
  }
}

/** Зависимости ветки обработки 401 — снимаются в контексте инъекции. */
interface UnauthorizedDeps {
  refresh: RefreshCoordinator;
  lifecycle: SessionLifecycle;
  router: Router;
  error: HttpErrorResponse;
}

/**
 * Неудачный исходный запрос (401 вне auth-эндпойнтов): refresh → успех —
 * повтор исходного ровно один раз; неудача — событие уже опубликовано
 * координатором, остаётся редирект /login вне фазы инициализации (ADR-016)
 * и нормализованный ApiError исходного отказа потребителю.
 */
function handleUnauthorized(
  req: HttpRequest<unknown>,
  next: HttpHandlerFn,
  deps: UnauthorizedDeps,
): Observable<HttpEvent<unknown>> {
  return deps.refresh.tryRefresh().pipe(
    switchMap((refreshed) => {
      if (!refreshed) {
        if (!deps.lifecycle.initializing) {
          void deps.router.navigate(['/login']);
        }
        return throwError(() => normalizeHttpError(deps.error));
      }
      // Повтор исходного запроса ровно один раз (без повторного refresh:
      // next ведёт в цепочку НИЖЕ этого интерцептора).
      return next(req).pipe(
        catchError((retryError: unknown) =>
          throwError(() => normalizeHttpError(retryError)),
        ),
      );
    }),
  );
}

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const apiBase = inject(API_BASE_URL);
  if (!isApiUrl(req.url, apiBase)) {
    return next(req);
  }
  const lifecycle = inject(SessionLifecycle);
  const router = inject(Router);
  const refresh = inject(RefreshCoordinator);
  const apiReq = req.clone({ withCredentials: true });

  return next(apiReq).pipe(
    catchError((error: unknown) => {
      if (
        error instanceof HttpErrorResponse &&
        error.status === 401 &&
        !isAuthEndpoint(apiReq.url, apiBase)
      ) {
        return handleUnauthorized(apiReq, next, { refresh, lifecycle, router, error });
      }
      return throwError(() => normalizeHttpError(error));
    }),
  );
};
