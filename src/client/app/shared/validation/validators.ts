/**
 * Reactive Forms валидаторы FR-023 — контракт IF-010 (компонент C-010).
 *
 * Единый набор правил клиентской валидации для всех форм приложения.
 * Каждый валидатор возвращает ValidationErrors с ключами словаря
 * ERROR_TEXTS (shared/validation/error-texts.ts) — тексты ошибок полей
 * читаются из словаря и только из него.
 *
 * Принципы (гарантии IF-010):
 *  - трим до проверки: все строковые правила проверяют значение после
 *    trim(); обязательность тоже («строка из одних пробелов невалидна»);
 *  - обязательность — отдельный валидатор requiredTrim: форматные
 *    валидаторы на пустом значении (null/''/пробелы) возвращают null,
 *    чтобы пустое обязательное поле показывало «Заполните поле», а не
 *    текст формата. Формы собирают их в compose:
 *      Validators.compose([requiredTrim(), groupName()])
 *  - к моку уходят триммированные значения: перед построением DTO
 *    компонент вызывает trimFormValues(form) — контроль со строковым
 *    значением нормализуется до триммированного вида;
 *  - пароли не триммятся в правилах и не сравниваются триммированно:
 *    пробел — легальный спецзнак (любой символ, не буква и не цифра),
 *    а passwordMatch сравнивает значения дословно.
 *
 * Те же правила продублированы в мок-слое (валидация контрактов) —
 * мок-слой импортирует этот файл как источник границ и словаря.
 */
import {
  AbstractControl,
  FormArray,
  FormGroup,
  ValidationErrors,
  ValidatorFn,
} from '@angular/forms';

import { ERROR_TEXTS, FieldErrorKey } from './error-texts';

/** Ошибки одного поля: подмножество ключей словаря ERROR_TEXTS. */
export type FieldErrors = Partial<Record<FieldErrorKey, boolean>>;

/** Минимальная длина пароля (FR-023: >= 8 символов). */
const PASSWORD_MIN_LENGTH = 8;
/** Максимальная длина email (FR-023: 254 включительно). */
const EMAIL_MAX_LENGTH = 254;
/** Максимальная длина логина после трима (FR-023: 1–100). */
const LOGIN_MAX_LENGTH = 100;
/** Границы семестра (FR-023: 1–10 включительно; MaxSemester — ADR-010). */
const SEMESTER_MIN = 1;
const SEMESTER_MAX = 10;
/** Максимальная длина содержания работы (FR-023: 1–500). */
const LAB_CONTENT_MAX_LENGTH = 500;
/** Максимальная длина названия группы (FR-023: 1–100). */
const GROUP_NAME_MAX_LENGTH = 100;
/** Максимальная длина ФИО (FR-023: 1–200). */
const FULL_NAME_MAX_LENGTH = 200;

/** Цифра — ASCII 0–9 (код восстановления и пароль проверяются по ним). */
const DIGIT_PATTERN = /\d/;
/** Буква любого алфавита (латиница, кириллица, …) — \p{L}. */
const LETTER_PATTERN = /\p{L}/u;
/** Спецзнак — любой символ, не являющийся буквой или цифрой (FR-023). */
const SPECIAL_CHAR_PATTERN = /[^\p{L}\d]/u;
/**
 * Email вида логин@домен.зона: без пробелов, «@» ровно одна, домен —
 * непустые метки через точку, зона после последней точки непустая
 * («a@b» невалиден, «a@b.ru» валиден, «a@b..ru»/«a@b.ru.» невалидны).
 */
const EMAIL_PATTERN = /^[^\s@]+@([^\s@.]+\.)+[^\s@.]+$/;
/** Логин: латиница, цифры, точка, дефис, подчёркивание (FR-023). */
const LOGIN_PATTERN = /^[A-Za-z0-9._-]+$/;
/** Код восстановления: ровно 6 цифр (только ASCII 0–9). */
const CODE_PATTERN = /^\d{6}$/;
/**
 * Ссылка начинается с http:// или https:// — дословное правило FR-023
 * (проверяется только префикс, в нижнем регистре).
 */
const URL_PREFIX_PATTERN = /^https?:\/\//;
/** Целое число в строковом контроле: только цифры (без знака и точки). */
const INTEGER_PATTERN = /^\d+$/;

/** Собирает ошибки поля из кодов словаря; пустой перечень — ошибок нет. */
function packErrors(keys: readonly FieldErrorKey[]): FieldErrors | null {
  return keys.length === 0
    ? null
    : (Object.fromEntries(keys.map((key) => [key, true])) as FieldErrors);
}

/** Строковое значение контроля либо null (отсутствует/нестроковое). */
function asString(value: unknown): string | null {
  return typeof value === 'string' ? value : null;
}

/** Пустое ли значение для форматного валидатора: нет значения либо только пробелы. */
function isEmptyInput(value: unknown): boolean {
  return (
    value === null ||
    value === undefined ||
    (typeof value === 'string' && value.trim() === '')
  );
}

/**
 * Целое число из значения контроля (строка тримится, число берётся как
 * есть) либо null — если значение не целое (дробное, со знаком, мусор).
 */
function integerValue(value: unknown): number | null {
  if (typeof value === 'number') {
    return Number.isInteger(value) ? value : null;
  }
  if (typeof value === 'string' && INTEGER_PATTERN.test(value.trim())) {
    return Number(value.trim());
  }
  return null;
}

/**
 * Обязательность с тримом (FR-023): null/undefined, пустая строка и
 * строка из одних пробелов → ошибка `required`; любое значащее значение
 * (в том числе «  А  ») валидно. Ключ совпадает с ключом required
 * встроенных валидаторов Angular — один и тот же текст «Заполните поле».
 */
export function requiredTrim(): ValidatorFn {
  return (control: AbstractControl): FieldErrors | null => {
    if (isEmptyInput(control.value)) {
      return { required: true };
    }
    return null;
  };
}

/**
 * Правила пароля (FR-023): >= 8 символов, >= 1 цифра, >= 1 буква,
 * >= 1 спецзнак (любой символ, не буква и не цифра — пробел подходит).
 * Возвращает ВСЕ нарушенные правила одновременно (например, для
 * «abcdefgh» — password.digit и password.special вместе). Пустое
 * значение не проверяется — обязательность задаёт requiredTrim.
 * Пароль не триммится: пробел — легальный спецзнак.
 */
export function passwordRules(): ValidatorFn {
  return (control: AbstractControl): FieldErrors | null => {
    const value = asString(control.value);
    if (value === null || value === '') {
      return null;
    }
    const violated: FieldErrorKey[] = [];
    if (value.length < PASSWORD_MIN_LENGTH) {
      violated.push('password.min');
    }
    if (!DIGIT_PATTERN.test(value)) {
      violated.push('password.digit');
    }
    if (!LETTER_PATTERN.test(value)) {
      violated.push('password.letter');
    }
    if (!SPECIAL_CHAR_PATTERN.test(value)) {
      violated.push('password.special');
    }
    return packErrors(violated);
  };
}

/**
 * Совпадение пароля и повтора (FR-023) — валидатор на всю FormGroup:
 * ошибки доступаются как form.hasError('password.mismatch') и показываются
 * у поля повтора. Пустой повтор не даёт mismatch (его ошибку показывает
 * requiredTrim); сравнение дословное, без трима (пробел — спецзнак).
 *
 *   new FormGroup({
 *     password: new FormControl('', [requiredTrim(), passwordRules()]),
 *     repeatPassword: new FormControl('', [requiredTrim()]),
 *   }, { validators: passwordMatch('password', 'repeatPassword') })
 */
export function passwordMatch(
  passwordControlName: string,
  repeatControlName: string,
): ValidatorFn {
  return (group: AbstractControl): FieldErrors | null => {
    const password = group.get(passwordControlName);
    const repeat = group.get(repeatControlName);
    if (password === null || repeat === null) {
      return null;
    }
    if (isEmptyInput(repeat.value)) {
      return null;
    }
    return password.value === repeat.value
      ? null
      : { 'password.mismatch': true };
  };
}

/**
 * Email (FR-023): формат логин@домен.зона и длина 1–254 символа после
 * трима (254 включительно). Формат: без пробелов, «@» одна, после
 * домена есть «.» и непустая зона. Пустое значение не проверяется —
 * обязательность задаёт requiredTrim.
 */
export function emailFormat(): ValidatorFn {
  return (control: AbstractControl): FieldErrors | null => {
    const raw = asString(control.value);
    if (raw === null || raw.trim() === '') {
      return null;
    }
    const email = raw.trim();
    if (!EMAIL_PATTERN.test(email)) {
      return { email: true };
    }
    if (email.length > EMAIL_MAX_LENGTH) {
      return { 'email.length': true };
    }
    return null;
  };
}

/**
 * Логин (FR-023): 1–100 символов после трима и только латинские буквы,
 * цифры, точка, дефис, подчёркивание; пробелы внутри и иные символы
 * невалидны. Обе ошибки возвращаются вместе, если нарушены обе.
 * Пустое значение не проверяется — обязательность задаёт requiredTrim.
 */
export function loginCharset(): ValidatorFn {
  return (control: AbstractControl): FieldErrors | null => {
    const raw = asString(control.value);
    if (raw === null || raw.trim() === '') {
      return null;
    }
    const login = raw.trim();
    const violated: FieldErrorKey[] = [];
    if (login.length > LOGIN_MAX_LENGTH) {
      violated.push('login');
    }
    if (!LOGIN_PATTERN.test(login)) {
      violated.push('login.charset');
    }
    return packErrors(violated);
  };
}

/**
 * Код восстановления (FR-023): ровно 6 цифр (ASCII 0–9; «12a456» и
 * «1234567» невалидны). Пустое значение не проверяется — обязательность
 * задаёт requiredTrim.
 */
export function code6(): ValidatorFn {
  return (control: AbstractControl): FieldErrors | null => {
    const raw = asString(control.value);
    if (raw === null || raw.trim() === '') {
      return null;
    }
    return CODE_PATTERN.test(raw.trim()) ? null : { code: true };
  };
}

/**
 * Номер лабораторной работы (FR-023): целое > 0 без верхней границы.
 * Принимает число или строку из цифр (после трима); «0», «-3», «1.5»,
 * «abc» невалидны. Пустое значение не проверяется — обязательность
 * задаёт requiredTrim.
 */
export function labNumber(): ValidatorFn {
  return (control: AbstractControl): FieldErrors | null => {
    if (isEmptyInput(control.value)) {
      return null;
    }
    const parsed = integerValue(control.value);
    return parsed !== null && parsed > 0 ? null : { 'lab.number': true };
  };
}

/**
 * Семестр лабораторной работы (FR-023): целое 1–10 включительно.
 * Принимает число или строку из цифр (после трима). Пустое значение
 * не проверяется — обязательность задаёт requiredTrim.
 */
export function labSemester(): ValidatorFn {
  return (control: AbstractControl): FieldErrors | null => {
    if (isEmptyInput(control.value)) {
      return null;
    }
    const parsed = integerValue(control.value);
    return parsed !== null && parsed >= SEMESTER_MIN && parsed <= SEMESTER_MAX
      ? null
      : { 'lab.semester': true };
  };
}

/**
 * Содержание работы (FR-023): 1–500 символов после трима. Пустое
 * значение не проверяется — обязательность задаёт requiredTrim.
 */
export function labContent(): ValidatorFn {
  return boundedText('lab.content', LAB_CONTENT_MAX_LENGTH);
}

/**
 * Ссылка на задание (FR-023): должна начинаться с http:// или https://.
 * Пустая ссылка допустима (assignmentUrl nullable в модели Lab).
 */
export function labUrl(): ValidatorFn {
  return (control: AbstractControl): FieldErrors | null => {
    const raw = asString(control.value);
    if (raw === null || raw.trim() === '') {
      return null;
    }
    return URL_PREFIX_PATTERN.test(raw.trim()) ? null : { 'lab.url': true };
  };
}

/**
 * Название группы (FR-023): 1–100 символов после трима. Пустое значение
 * не проверяется — обязательность задаёт requiredTrim.
 */
export function groupName(): ValidatorFn {
  return boundedText('group.name', GROUP_NAME_MAX_LENGTH);
}

/**
 * ФИО (FR-023): 1–200 символов после трима. Пустое значение
 * не проверяется — обязательность задаёт requiredTrim.
 */
export function fullName(): ValidatorFn {
  return boundedText('fullName', FULL_NAME_MAX_LENGTH);
}

/** Строковое поле с верхней границей длины по триммированному значению. */
function boundedText(errorKey: FieldErrorKey, maxLength: number): ValidatorFn {
  return (control: AbstractControl): FieldErrors | null => {
    const raw = asString(control.value);
    if (raw === null || raw.trim() === '') {
      return null;
    }
    return raw.trim().length > maxLength ? packErrors([errorKey]) : null;
  };
}

/**
 * Нормализация формы перед отправкой (гарантия IF-010 «к моку уходят
 * триммированные значения»): рекурсивно триммит строковые значения всех
 * контролов формы. Вызывается компонентом в обработчике отправки ДО
 * построения DTO и вызова сервиса (после успешной client-валидации):
 *
 *   if (this.form.invalid) { …баннер/тексты полей… return; }
 *   trimFormValues(this.form);
 *   await this.labsService.create(this.form.value as LabDto);
 *
 * Обратите внимание: триммит и поля пароля — единообразно на всех
 * экранах (регистрация/логин/смена), поэтому пароли с крайними
 * пробелами ведут себя согласованно на всём жизненном цикле.
 *
 * setValue с emitEvent: false — нормализация не порождает событий
 * valueChanges (отправка — не правка пользователем).
 */
export function trimFormValues(form: AbstractControl): void {
  if (form instanceof FormGroup || form instanceof FormArray) {
    for (const child of Object.values(form.controls)) {
      trimFormValues(child);
    }
    return;
  }
  const value = form.value;
  if (typeof value === 'string' && value.trim() !== value) {
    form.setValue(value.trim(), { emitEvent: false });
  }
}

/**
 * Текст первой ошибки контроля из словаря ERROR_TEXTS (единственный
 * источник текстов полей): проход по ключам errors, первый ключ словаря
 * даёт текст, чужие ключи (встроенные валидаторы Angular и пр.)
 * пропускаются. null/нет словарных ключей → null (текст не показывается).
 *
 *   firstErrorText(control.errors) // → «Заполните поле»
 */
export function firstErrorText(errors: ValidationErrors | null): string | null {
  if (errors === null) {
    return null;
  }
  for (const key of Object.keys(errors)) {
    const dictionary = ERROR_TEXTS as Record<string, string>;
    if (key in dictionary) {
      return dictionary[key];
    }
  }
  return null;
}
