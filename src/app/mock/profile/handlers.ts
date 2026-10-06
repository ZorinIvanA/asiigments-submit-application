/**
 * Обработчики домена Profile (C-106, контракт IF-107, FR-4.7/US-18).
 *
 * Методы реестра (эмуляция REST §4.7):
 *  - 'profile.get'            → ProfileDto текущего пользователя сессии;
 *  - 'profile.update'         {fullName, email} → обновлённый ProfileDto;
 *  - 'profile.changePassword' {currentPassword, password, confirmPassword}.
 *
 * Доступны любой авторизованной роли (§4.7); без сессии или с битой сессией
 * (пользователь удалён) — 401 «Не авторизован». Логин не редактируется;
 * update меняет только fullName/email (роль и группу не трогает), при этом
 * groupName в ответе вычисляется заново по текущему состоянию groups —
 * переименование/удаление группы студента сразу видно в профиле.
 *
 * Валидация — единый источник правил и текстов (§8): поля проверяются теми
 * же валидаторами shared/validation/validators, что и клиентские формы
 * (состав FormControl'ов повторяет состав форм профиля), тексты полевых
 * ошибок читаются из ERROR_TEXTS дословно. Пароли в правилах не триммируются
 * (пробел — легальный спецзнак), сравнение паролей дословное.
 *
 * Сессия читается через SessionStore домена Auth (C-101) — единственного
 * владельца ключа mock.session.userId; экземпляр не имеет состояния, все
 * операции идут в localStorage напрямую.
 */
import { AbstractControl, FormControl, FormGroup, ValidationErrors } from '@angular/forms';

import { SessionStore } from '../auth/session-store';
import { ProfileDto, User } from '../../shared/models';
import { ERROR_TEXTS } from '../../shared/validation/error-texts';
import {
  emailFormat,
  fullName as fullNameRule,
  passwordMatch,
  passwordRules,
  requiredTrim,
} from '../../shared/validation/validators';
import { MockApiClient } from '../mock-api-client';
import { MockDb, MockDbData } from '../mock-db';
import { MockValidationError } from '../mock-error';

const sessionStore = new SessionStore();

/** Параметры 'profile.update' — тело PUT /api/v1/me/profile. */
export interface ProfileUpdateParams {
  fullName: string;
  email: string;
}

/** Параметры 'profile.changePassword' — тело PUT /api/v1/me/password. */
export interface ProfileChangePasswordParams {
  currentPassword: string;
  password: string;
  confirmPassword: string;
}

/** Сообщение об отсутствии авторизованной сессии (IF-107; текст — OQ-002). */
const UNAUTHORIZED_MESSAGE = 'Не авторизован';

/** Значение параметра как строка (мок получает JSON страниц; защита от мусора). */
function asString(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

/** Пользователь сессии; без сессии или с битой (пользователь удалён) — 401. */
function requireSessionUser(data: MockDbData): User {
  const userId = sessionStore.getUserId();
  if (userId === null) {
    throw new MockValidationError(401, UNAUTHORIZED_MESSAGE);
  }
  const user = data.users.find((candidate) => candidate.id === userId);
  if (user === undefined) {
    throw new MockValidationError(401, UNAUTHORIZED_MESSAGE);
  }
  return user;
}

/** ProfileDto пользователя: groupName вычисляется по текущему состоянию groups. */
function toProfileDto(data: MockDbData, user: User): ProfileDto {
  const groupName =
    user.groupId === null
      ? null
      : (data.groups.find((group) => group.id === user.groupId)?.name ?? null);
  return {
    login: user.login,
    email: user.email,
    fullName: user.fullName,
    role: user.role,
    groupName,
  };
}

/** Тексты полевых ошибок контрола — дословно из ERROR_TEXTS, в порядке ключей. */
function controlErrorTexts(control: AbstractControl): string[] {
  const errors: ValidationErrors | null = control.errors;
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

/** Валидация формы «Основные данные» → errors {fullName, email} либо null. */
function profileUpdateFieldErrors(
  fullName: string,
  email: string,
): Record<string, string[]> | null {
  // Состав валидаторов повторяет клиентскую форму «Основные данные» (§8).
  const form = new FormGroup({
    fullName: new FormControl(fullName, [requiredTrim(), fullNameRule()]),
    email: new FormControl(email, [requiredTrim(), emailFormat()]),
  });
  const errors: Record<string, string[]> = {};
  const fullNameTexts = controlErrorTexts(form.controls.fullName);
  if (fullNameTexts.length > 0) {
    errors['fullName'] = fullNameTexts;
  }
  const emailTexts = controlErrorTexts(form.controls.email);
  if (emailTexts.length > 0) {
    errors['email'] = emailTexts;
  }
  return Object.keys(errors).length > 0 ? errors : null;
}

/** Валидация формы «Смена пароля» → errors {password, confirmPassword} либо null. */
function changePasswordFieldErrors(
  password: string,
  confirmPassword: string,
): Record<string, string[]> | null {
  // Состав валидаторов повторяет клиентскую форму «Смена пароля» (§8):
  // passwordRules + requiredTrim у пароля, requiredTrim у повтора и
  // групповой passwordMatch, чья ошибка показывается у поля повтора.
  const form = new FormGroup(
    {
      password: new FormControl(password, [requiredTrim(), passwordRules()]),
      confirmPassword: new FormControl(confirmPassword, [requiredTrim()]),
    },
    { validators: passwordMatch('password', 'confirmPassword') },
  );
  const errors: Record<string, string[]> = {};
  const passwordTexts = controlErrorTexts(form.controls.password);
  if (passwordTexts.length > 0) {
    errors['password'] = passwordTexts;
  }
  const confirmPasswordTexts = controlErrorTexts(form.controls.confirmPassword);
  if (form.hasError('password.mismatch')) {
    confirmPasswordTexts.push(ERROR_TEXTS['password.mismatch']);
  }
  if (confirmPasswordTexts.length > 0) {
    errors['confirmPassword'] = confirmPasswordTexts;
  }
  return Object.keys(errors).length > 0 ? errors : null;
}

/**
 * Регистрирует обработчики домена Profile в реестре клиента (IF-110).
 * Повторная регистрация заменяет обработчики — идемпотентно для агрегатора.
 */
export function registerProfileHandlers(client: MockApiClient): void {
  client.register('profile.get', (db: MockDb): ProfileDto => {
    const data = db.read();
    const user = requireSessionUser(data);
    return toProfileDto(data, user);
  });

  client.register('profile.update', (db: MockDb, params: ProfileUpdateParams): ProfileDto => {
    const data = db.read();
    const user = requireSessionUser(data);

    const fullName = asString(params?.fullName);
    const email = asString(params?.email);
    const fieldErrors = profileUpdateFieldErrors(fullName, email);
    if (fieldErrors !== null) {
      throw new MockValidationError(400, 'Данные заполнены неверно', fieldErrors);
    }

    // Страницы отправляют триммированные значения (trimFormValues); мок
    // дополнительно триммит — правила §8 формулируются по триммированному
    // значению, и в БД хранится только нормализованный вид.
    const trimmedFullName = fullName.trim();
    const trimmedEmail = email.trim();
    const emailTaken = data.users.some(
      (other) => other.id !== user.id && other.email.toLowerCase() === trimmedEmail.toLowerCase(),
    );
    if (emailTaken) {
      throw new MockValidationError(409, 'Пользователь с таким email уже существует');
    }

    return db.mutate((draft) => {
      const target = draft.users.find((candidate) => candidate.id === user.id);
      if (target === undefined) {
        throw new MockValidationError(401, UNAUTHORIZED_MESSAGE);
      }
      target.fullName = trimmedFullName;
      target.email = trimmedEmail;
      return toProfileDto(draft, target);
    });
  });

  client.register(
    'profile.changePassword',
    (db: MockDb, params: ProfileChangePasswordParams): void => {
      const data = db.read();
      const user = requireSessionUser(data);

      const currentPassword = asString(params?.currentPassword);
      // Текущий пароль обязателен (§4.7): любое несовпадение — единый текст
      // «Неверный текущий пароль», до полевых ошибок нового пароля.
      if (currentPassword !== user.password) {
        throw new MockValidationError(400, 'Неверный текущий пароль');
      }

      const password = asString(params?.password);
      const confirmPassword = asString(params?.confirmPassword);
      const fieldErrors = changePasswordFieldErrors(password, confirmPassword);
      if (fieldErrors !== null) {
        throw new MockValidationError(400, 'Данные заполнены неверно', fieldErrors);
      }

      db.mutate((draft) => {
        const target = draft.users.find((candidate) => candidate.id === user.id);
        if (target === undefined) {
          throw new MockValidationError(401, UNAUTHORIZED_MESSAGE);
        }
        target.password = password;
      });
    },
  );
}
