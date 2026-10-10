/**
 * SessionLifecycle — канал жизненного цикла сессии (C-013, контракт IF-014,
 * ADR-019, закрытие ISS-003): единственная точка сопряжения интерцептора
 * (C-013) с доменными сервисами (AuthService из C-014) без импорта самих
 * сервисов — файлы C-013 не знают об AuthService.
 *
 * Контракт:
 *  - notifySessionExpired() — вызывается ТОЛЬКО интерцептором после
 *    неудачного refresh; подписчик (AuthService) сбрасывает состояние
 *    сессии в памяти без единого HTTP-запроса (никаких POST /auth/logout);
 *  - sessionExpired$ — сигнал недействительной сессии; подписка в
 *    конструкторе AuthService (T-015); повторная публикация идемпотентна
 *    для сброса состояния; на дедуплицированный refresh событие публикуется
 *    не более одного раза (гарантия IF-014);
 *  - markSessionRestored() — вызывается ТОЛЬКО AuthService (T-015)
 *    синхронно после УСПЕШНЫХ login()/register() (2xx; регистрация
 *    выполняет автоматический вход — FR-007); sessionRestored$ — сигнал
 *    восстановления сессии: RefreshCoordinator интерцептора по нему
 *    сбрасывает фиксатор неудачного refresh — фиксатор действует до
 *    следующей успешной аутентификации, а не всё время жизни приложения
 *    (ADR-021, закрытие CR-001); повторная публикация безопасна —
 *    сброс фиксатора идемпотентен (IF-014);
 *  - initializing / markInitDone() — фаза холодного старта (ADR-016):
 *    устанавливается инициализацией сессии (AuthService из C-014),
 *    читается интерцептором — во время init навигация на /login не
 *    выполняется, редирект определяют guards.
 */
import { Injectable } from '@angular/core';
import { Observable, Subject } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class SessionLifecycle {
  /** Внутренний издатель события; наружу отдаётся только sessionExpired$. */
  private readonly sessionExpiredSubject = new Subject<void>();

  /** Внутренний издатель события; наружу отдаётся только sessionRestored$. */
  private readonly sessionRestoredSubject = new Subject<void>();

  /** Признак завершения инициализации; initializing — его инверт (ADR-016). */
  private initDone = false;

  /** Сигнал недействительной сессии — потребляет AuthService (T-015). */
  readonly sessionExpired$: Observable<void> = this.sessionExpiredSubject.asObservable();

  /**
   * Сигнал восстановления сессии — потребляет RefreshCoordinator
   * интерцептора (C-013): сброс фиксатора неудачного refresh (ADR-021).
   */
  readonly sessionRestored$: Observable<void> = this.sessionRestoredSubject.asObservable();

  /**
   * Фаза холодного старта: true до markInitDone() — интерцептор в этой фазе
   * НЕ выполняет навигацию на /login, чтобы не конкурировать с guards
   * (ADR-016, предотвращение цикла при холодном старте без сессии).
   */
  get initializing(): boolean {
    return !this.initDone;
  }

  /**
   * Публикует событие недействительной сессии; вызывающий — только
   * интерцептор (после неудачного refresh). Сброс состояния выполняют
   * подписчики канала, HTTP-запросов при этом нет (ADR-019).
   */
  notifySessionExpired(): void {
    this.sessionExpiredSubject.next();
  }

  /**
   * Публикует событие восстановления сессии; вызывающий — только AuthService
   * (T-015) после успешных login/register (2xx). Потребитель — интерцептор:
   * сброс фиксатора неудачного refresh идемпотентен, повторная публикация
   * безвредна (ADR-021, IF-014).
   */
  markSessionRestored(): void {
    this.sessionRestoredSubject.next();
  }

  /** Завершает фазу инициализации (вызывает инициализация сессии, T-015). */
  markInitDone(): void {
    this.initDone = true;
  }
}
