/**
 * Мок-обработчики домена Auth (C-101, контракты IF-101, FR-4.1/FR-4.2,
 * §4.1/§4.2/§8/§9 спеки): регистрация, вход, выход, текущий пользователь,
 * восстановление пароля (запрос кода → подтверждение → сброс пароля).
 *
 * Принципы:
 *  - обработчики — чистые синхронные функции над MockDb (ADR-003); отказ
 *    сообщается выбросом MockValidationError, MockApiClient транслирует
 *    его в ApiError (IF-001);
 *  - момент времени передаётся параметром now (детерминизм unit-тестов,
 *    DoD T-101): Date.now в этом файле нет — реальное время подставляет
 *    только registerAuthHandlers через (инжектируемые) часы;
 *  - тексты ошибок — дословно из IF-101/§8; полевые ошибки строятся по
 *    правилам shared/validation/validators.ts (единый источник границ) со
 *    словарём ERROR_TEXTS;
 *  - сессия — ключ mock.session.userId через SessionStore (ADR-105),
 *    экземпляр замыкается при регистрации обработчиков;
 *  - существование email/логина не раскрывается (§9): единый текст 401
 *    входа, always-200 запроса кода, единый текст отказа confirm.
 */
import { FormControl, FormGroup, ValidatorFn } from '@angular/forms';

import { MeDto, User } from '../../shared/models';
import { ERROR_TEXTS } from '../../shared/validation/error-texts';
import {
  emailFormat,
  fullName as fullNameRule,
  loginCharset,
  passwordMatch,
  passwordRules,
  requiredTrim,
} from '../../shared/validation/validators';
import { newRuntimeId } from '../ids';
import { MockApiClient } from '../mock-api-client';
import { MockDb, MockDbData } from '../mock-db';
import { MockValidationError } from '../mock-error';
import { RateLimiter } from '../rate-limiter';
import { SessionStore } from './session-store';

/**
 * Текущий пользователь — ответ auth.register/auth.login/auth.me (IF-101).
 * Интерфейс живёт в shared/models.ts (единый словарь DTO, ревью CR-001
 * T-101); сюда он реэкспортируется, чтобы контракт домена Auth оставался
 * самодостаточным для потребителей мок-слоя (AuthService, guards).
 */
export type { MeDto };

/** Параметры auth.register (IF-101). */
export interface RegisterParams {
  fullName: string;
  login: string;
  email: string;
  password: string;
  repeatPassword: string;
}

/** Параметры auth.login (IF-101). */
export interface LoginParams {
  login: string;
  password: string;
}

/** Параметры auth.recovery.request (IF-101). */
export interface RecoveryRequestParams {
  email: string;
}

/** Параметры auth.recovery.confirm (IF-101). */
export interface RecoveryConfirmParams {
  email: string;
  code: string;
}

/** Результат auth.recovery.confirm (IF-101): краткоживущий токен сброса. */
export interface ConfirmRecoveryResult {
  resetToken: string;
}

/** Параметры auth.reset-password (IF-101). */
export interface ResetPasswordParams {
  resetToken: string;
  password: string;
  confirmPassword: string;
}

/** Тексты отказов IF-101 — дословно (§8); единый источник для мока и сервиса. */
export const AUTH_ERRORS = {
  /** Единый отказ входа — не раскрывает существование логина (§4.1). */
  wrongCredentials: 'Неверный логин или пароль',
  /** Баннер полевых ошибок (§8). */
  invalidData: 'Данные заполнены неверно',
  /** Дубликаты регистрации: логин проверяется первым (§8). */
  loginTaken: 'Пользователь с таким логином уже существует',
  emailTaken: 'Пользователь с таким email уже существует',
  /** Превышен лимит частоты — вход/регистрация/запрос кода (IF-101). */
  tooManyAttempts: 'Слишком много попыток. Повторите позже',
  /** Код неверный/просроченный/использованный/аннулированный (§4.2). */
  recoveryCodeRejected: 'Код восстановления не подходит',
  /** Токен сброса неверный/просроченный/использованный (IF-101). */
  resetLinkInvalid: 'Ссылка восстановления недействительна или истекла',
  /** Нет/битая сессия у метода авторизованного домена (IF-101, OQ-002). */
  unauthorized: 'Не авторизован',
} as const;

/** TTL кода восстановления — 10 минут (§4.2). */
const RECOVERY_CODE_TTL_MS = 10 * 60 * 1000;
/** TTL токена сброса пароля — 15 минут (§4.2). */
const RESET_TOKEN_TTL_MS = 15 * 60 * 1000;
/** Попыток подбора на код — 5-я неверная аннулирует код (§4.2). */
const MAX_CODE_ATTEMPTS = 5;

/**
 * Лимиты частоты (§4.1–4.2, data_design MockRateLimitCounters):
 * скользящие окна RateLimiter; счётчик регистрации глобальный — редукция
 * «с одного IP» для браузера (SI-004).
 */
const REGISTER_LIMIT = new RateLimiter(60 * 60 * 1000, 5);
const LOGIN_FAILURE_LIMIT = new RateLimiter(60 * 1000, 5);
const RECOVERY_REQUEST_LIMIT = new RateLimiter(60 * 60 * 1000, 3);

/** Строковый параметр вызова: не-строка (мусор в payload) → пустая строка. */
function asText(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

/** Тексты ошибок значения по правилам validators.ts (те же границы, §8). */
function fieldTexts(value: string, rules: ValidatorFn[]): string[] {
  const control = new FormControl(value, rules);
  const errors = control.errors;
  if (errors === null) {
    return [];
  }
  const dictionary = ERROR_TEXTS as Record<string, string>;
  const texts: string[] = [];
  for (const key of Object.keys(errors)) {
    const text = dictionary[key];
    if (text !== undefined) {
      texts.push(text);
    }
  }
  return texts;
}

/** Текст «Пароли не совпадают» — групповое правило passwordMatch (§8). */
function repeatMismatchText(password: string, repeat: string): string | null {
  const group = new FormGroup(
    {
      password: new FormControl(password),
      repeatPassword: new FormControl(repeat),
    },
    { validators: [passwordMatch('password', 'repeatPassword')] },
  );
  return group.hasError('password.mismatch') ? ERROR_TEXTS['password.mismatch'] : null;
}

/** Полевые ошибки пары «пароль — повтор»: единообразно для register/reset. */
function passwordPairErrors(
  password: string,
  repeat: string,
): Record<string, string[]> {
  const errors: Record<string, string[]> = {};
  const passwordTexts = fieldTexts(password, [requiredTrim(), passwordRules()]);
  if (passwordTexts.length > 0) {
    errors['password'] = passwordTexts;
  }
  const repeatTexts = fieldTexts(repeat, [requiredTrim()]);
  const mismatch = repeatMismatchText(password, repeat);
  if (mismatch !== null) {
    repeatTexts.push(mismatch);
  }
  if (repeatTexts.length > 0) {
    errors['repeatPassword'] = repeatTexts;
  }
  return errors;
}

/** 400 «Данные заполнены неверно» + errors по полям, если ошибки есть. */
function throwIfInvalid(errors: Record<string, string[]>): void {
  if (Object.keys(errors).length > 0) {
    throw new MockValidationError(400, AUTH_ERRORS.invalidData, errors);
  }
}

/** MeDto по пользователю: имя группы резолвится из groups (IF-101). */
function toMeDto(data: MockDbData, user: User): MeDto {
  const groupName =
    user.role === 'student' && user.groupId !== null
      ? (data.groups.find((g) => g.id === user.groupId)?.name ?? null)
      : null;
  return { login: user.login, fullName: user.fullName, role: user.role, groupName };
}

/** Живой код восстановления: не использован и не истёк (граница включена). */
function isLiveCode(
  code: { usedAt: string | null; expiresAt: string },
  now: number,
): boolean {
  return code.usedAt === null && new Date(code.expiresAt).getTime() > now;
}

/** ISO-время для записи в мок-БД. */
function iso(now: number): string {
  return new Date(now).toISOString();
}

/** 6-значный код восстановления (§4.2) из криптографического источника. */
function generateRecoveryCode(): string {
  const buffer = new Uint32Array(1);
  crypto.getRandomValues(buffer);
  return String(buffer[0] % 1_000_000).padStart(6, '0');
}

/**
 * auth.register (§4.1): лимит ≤5/час → 400 валидации теми же validators.ts
 * → 409 дубликатов (логин первым, ci) → создание student → сессия → MeDto.
 */
export function handleAuthRegister(
  db: MockDb,
  session: SessionStore,
  params: RegisterParams,
  now: number,
): MeDto {
  const raw = (params ?? {}) as Partial<RegisterParams>;
  const allowed = db.mutate((data) =>
    REGISTER_LIMIT.tryAcquire(data.rateLimitCounters.register, now),
  );
  if (!allowed) {
    throw new MockValidationError(429, AUTH_ERRORS.tooManyAttempts);
  }

  const fullName = asText(raw.fullName).trim();
  const login = asText(raw.login).trim();
  const email = asText(raw.email).trim();
  const password = asText(raw.password); // пароль не триммится (§4.1)
  const repeatPassword = asText(raw.repeatPassword);

  const errors: Record<string, string[]> = {};
  const add = (field: string, texts: string[]): void => {
    if (texts.length > 0) {
      errors[field] = texts;
    }
  };
  add('fullName', fieldTexts(fullName, [requiredTrim(), fullNameRule()]));
  add('login', fieldTexts(login, [requiredTrim(), loginCharset()]));
  add('email', fieldTexts(email, [requiredTrim(), emailFormat()]));
  Object.assign(errors, passwordPairErrors(password, repeatPassword));
  throwIfInvalid(errors);

  const data = db.read();
  const loginTaken = data.users.some((u) => u.login.toLowerCase() === login.toLowerCase());
  if (loginTaken) {
    throw new MockValidationError(409, AUTH_ERRORS.loginTaken);
  }
  const emailTaken = data.users.some((u) => u.email.toLowerCase() === email.toLowerCase());
  if (emailTaken) {
    throw new MockValidationError(409, AUTH_ERRORS.emailTaken);
  }

  const created = db.mutate((d) => {
    const user: User = {
      id: newRuntimeId(),
      login,
      email,
      fullName,
      role: 'student',
      groupId: null,
      password,
    };
    d.users.push(user);
    return user;
  });
  session.setUserId(created.id); // успех → автоматический вход (§4.1)
  return toMeDto(db.read(), created);
}

/**
 * auth.login (§4.1): любые неверные данные — единый 401; неуспешные попытки
 * считаются по логину (ci), ≤5/мин, 6-я в окне → 429 (отказ не продлевает
 * окно); успех ставит сессию и возвращает MeDto.
 */
export function handleAuthLogin(
  db: MockDb,
  session: SessionStore,
  params: LoginParams,
  now: number,
): MeDto {
  const raw = (params ?? {}) as Partial<LoginParams>;
  const login = asText(raw.login).trim().toLowerCase();
  const password = asText(raw.password);

  const data = db.read();
  const user = data.users.find((u) => u.login.toLowerCase() === login);
  if (user === undefined || user.password !== password) {
    const allowed = db.mutate((d) => {
      const marks =
        d.rateLimitCounters.loginFailures[login] ??
        (d.rateLimitCounters.loginFailures[login] = []);
      return LOGIN_FAILURE_LIMIT.tryAcquire(marks, now);
    });
    if (!allowed) {
      throw new MockValidationError(429, AUTH_ERRORS.tooManyAttempts);
    }
    throw new MockValidationError(401, AUTH_ERRORS.wrongCredentials);
  }
  session.setUserId(user.id);
  return toMeDto(data, user);
}

/** auth.logout (§4.1): сессия очищается; без сессии — тоже успех. */
export function handleAuthLogout(session: SessionStore): null {
  session.clear();
  return null;
}

/**
 * auth.me (IF-101): MeDto по сессии; без сессии или с битой (userId нет в
 * мок-БД) — сессия очищается и отказ 401 «Не авторизован».
 */
export function handleAuthMe(db: MockDb, session: SessionStore): MeDto {
  const userId = session.getUserId();
  const data = db.read();
  const user = userId !== null ? data.users.find((u) => u.id === userId) : undefined;
  if (user === undefined) {
    session.clear();
    throw new MockValidationError(401, AUTH_ERRORS.unauthorized);
  }
  return toMeDto(data, user);
}

/**
 * auth.recovery.request (§4.2): всегда 200, существование email не
 * раскрывается; ≤3 запроса/час на email (ci) → 429. Код печатается в
 * консоль («письмо» демо-режима), хранится только для существующего email.
 * Аменда 9 (C): для существующего email перед созданием нового кода гасятся
 * все живые коды пользователя — одновременно жив максимум один код, прежние
 * коды после переотправки отвергаются веткой «использованный».
 */
export function handleAuthRecoveryRequest(
  db: MockDb,
  params: RecoveryRequestParams,
  now: number,
): null {
  const raw = (params ?? {}) as Partial<RecoveryRequestParams>;
  const email = asText(raw.email).trim();
  const emailKey = email.toLowerCase();

  const allowed = db.mutate((data) => {
    const marks =
      data.rateLimitCounters.recoveryRequests[emailKey] ??
      (data.rateLimitCounters.recoveryRequests[emailKey] = []);
    return RECOVERY_REQUEST_LIMIT.tryAcquire(marks, now);
  });
  if (!allowed) {
    throw new MockValidationError(429, AUTH_ERRORS.tooManyAttempts);
  }

  const code = generateRecoveryCode();
  const data = db.read();
  const user = data.users.find((u) => u.email.toLowerCase() === emailKey);
  if (user !== undefined) {
    db.mutate((d) => {
      // Аменда 9 (C): переотправка гасит все живые (неиспользованные и
      // непросроченные) коды пользователя; просроченные и использованные
      // не трогаются. Подтверждение прежним кодом после resend попадает
      // в ветку «использованный» → «Код восстановления не подходит».
      for (const previous of d.recoveryCodes) {
        if (previous.userId === user.id && isLiveCode(previous, now)) {
          previous.usedAt = iso(now);
        }
      }
      d.recoveryCodes.push({
        id: newRuntimeId(),
        userId: user.id,
        code,
        expiresAt: iso(now + RECOVERY_CODE_TTL_MS),
        usedAt: null,
        attempts: 0,
        createdAt: iso(now),
      });
    });
  }
  // Пишется всегда одинаково — по логу существование email не отличить (§9).
  console.info(`[mock-email] Код восстановления для ${email}: ${code}`);
  return null;
}

/**
 * auth.recovery.confirm (§4.2): совпадение с живым кодом пользователя →
 * одноразовый resetToken (TTL 15 мин), код гасится; иначе — единый 400
 * «Код восстановления не подходит» (в т.ч. для незарегистрированного email —
 * существование не раскрывается). Неверная попытка учитывается каждым живым
 * кодом пользователя; 5-я неверная аннулирует код (usedAt).
 */
export function handleAuthRecoveryConfirm(
  db: MockDb,
  params: RecoveryConfirmParams,
  now: number,
): ConfirmRecoveryResult {
  const raw = (params ?? {}) as Partial<RecoveryConfirmParams>;
  const email = asText(raw.email).trim();
  const code = asText(raw.code).trim();

  const data = db.read();
  const user = data.users.find((u) => u.email.toLowerCase() === email.toLowerCase());
  if (user !== undefined) {
    const matched = data.recoveryCodes.find(
      (c) => c.userId === user.id && isLiveCode(c, now) && c.code === code,
    );
    if (matched !== undefined) {
      const token = newRuntimeId();
      db.mutate((d) => {
        const stored = d.recoveryCodes.find((c) => c.id === matched.id);
        if (stored !== undefined) {
          stored.usedAt = iso(now); // код одноразовый
        }
        d.resetTokens.push({
          id: newRuntimeId(),
          userId: user.id,
          token,
          expiresAt: iso(now + RESET_TOKEN_TTL_MS),
          usedAt: null,
          createdAt: iso(now),
        });
      });
      return { resetToken: token };
    }
    // Неверный код: попытка на каждый живой код; 5-я — аннулирование.
    db.mutate((d) => {
      for (const candidate of d.recoveryCodes) {
        if (candidate.userId === user.id && isLiveCode(candidate, now)) {
          candidate.attempts += 1;
          if (candidate.attempts >= MAX_CODE_ATTEMPTS) {
            candidate.usedAt = iso(now);
          }
        }
      }
    });
  }
  throw new MockValidationError(400, AUTH_ERRORS.recoveryCodeRejected);
}

/**
 * auth.reset-password (§4.2): токен неверный/просроченный/использованный →
 * 400 «Ссылка…» (найденный неживой токен гасится), пароль не меняется;
 * валидация пароля — теми же validators.ts (токен при полевой ошибке не
 * гасится); успех — пароль сменён, все токены сброса пользователя погашены.
 * Аменда 9 (D): успех дополнительно отзывает сессию самого пользователя
 * (аналог «отзыва всех refresh-токенов»): если в mock.session.userId сидит
 * именно сбрасываемый userId, сессия очищается; сессия другого пользователя
 * не затрагивается (асимметрия со сменой пароля из профиля сохраняется).
 */
export function handleAuthResetPassword(
  db: MockDb,
  session: SessionStore,
  params: ResetPasswordParams,
  now: number,
): null {
  const raw = (params ?? {}) as Partial<ResetPasswordParams>;
  const tokenValue = asText(raw.resetToken).trim();
  const password = asText(raw.password);
  const confirmPassword = asText(raw.confirmPassword);

  const token = db
    .read()
    .resetTokens.find((t) => t.token === tokenValue);
  if (token === undefined || !isLiveCode(token, now)) {
    if (token !== undefined) {
      db.mutate((d) => {
        const stored = d.resetTokens.find((t) => t.id === token.id);
        if (stored !== undefined) {
          stored.usedAt = iso(now); // токен гасится (IF-101)
        }
      });
    }
    throw new MockValidationError(400, AUTH_ERRORS.resetLinkInvalid);
  }

  throwIfInvalid(passwordPairErrors(password, confirmPassword));

  db.mutate((d) => {
    const user = d.users.find((u) => u.id === token.userId);
    if (user !== undefined) {
      user.password = password;
    }
    // Аналог «отзыва всех refresh-токенов» (§4.2): гасятся все токены сброса.
    for (const t of d.resetTokens) {
      if (t.userId === token.userId && t.usedAt === null) {
        t.usedAt = iso(now);
      }
    }
  });
  if (session.getUserId() === token.userId) {
    session.clear(); // аменда 9 (D): сессия сбрасывающего отозвана
  }
  return null;
}

/**
 * Регистрирует обработчики auth.* в реестре клиента (ADR-003). Часы —
 * параметр (по умолчанию системные): тесты подставляют фиксированное время
 * и получают детерминированный полный конвейер вызова.
 */
export function registerAuthHandlers(
  client: MockApiClient,
  now: () => number = Date.now,
): void {
  const session = new SessionStore();
  client.register('auth.register', (db, params: RegisterParams) =>
    handleAuthRegister(db, session, params, now()),
  );
  client.register('auth.login', (db, params: LoginParams) =>
    handleAuthLogin(db, session, params, now()),
  );
  client.register('auth.logout', () => handleAuthLogout(session));
  client.register('auth.me', (db) => handleAuthMe(db, session));
  client.register('auth.recovery.request', (db, params: RecoveryRequestParams) =>
    handleAuthRecoveryRequest(db, params, now()),
  );
  client.register('auth.recovery.confirm', (db, params: RecoveryConfirmParams) =>
    handleAuthRecoveryConfirm(db, params, now()),
  );
  client.register('auth.reset-password', (db, params: ResetPasswordParams) =>
    handleAuthResetPassword(db, session, params, now()),
  );
}
