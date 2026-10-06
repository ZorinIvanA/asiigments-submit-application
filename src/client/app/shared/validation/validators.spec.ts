/**
 * Юнит-тесты валидаторов FR-023 (контракт IF-010, компонент C-010).
 *
 * Покрытие по unit_test_requirements подзадачи:
 *  - каждое правило на границах: пусто / минимум / максимум / максимум+1;
 *  - кейсы AC FR-023: email «a@b»/«a@b.ru»/254/255 символов, логин
 *    с пробелом, код «12345»/«123456»/«12a456», содержание 500/501,
 *    пробелы в обязательном поле, трим перед длиной;
 *  - соответствие ключей ошибок словарю ERROR_TEXTS (тексты у полей).
 */
import { FormControl, FormGroup, ValidatorFn, Validators } from '@angular/forms';

import { ERROR_TEXTS } from './error-texts';
import {
  code6,
  emailFormat,
  firstErrorText,
  fullName,
  groupName,
  labContent,
  labNumber,
  labSemester,
  labUrl,
  loginCharset,
  passwordMatch,
  passwordRules,
  requiredTrim,
  trimFormValues,
} from './validators';

/** Ошибки валидатора для значения без построения формы. */
function errorsOf(validator: ValidatorFn, value: unknown) {
  return validator(new FormControl(value));
}

/** Email длиной length: 'a@bbb…b.ru' с полным контролем длины строки. */
function emailOfLength(length: number): string {
  const email = `a@${'b'.repeat(length - 5)}.ru`; // 2 + (length-5) + 3 = length
  expect(email.length).toBe(length); // защита самого генератора теста
  return email;
}

describe('Валидаторы FR-023 (IF-010)', () => {
  describe('requiredTrim — обязательность по значению после трима', () => {
    it('AC «Пробелы в обязательном поле»: «   » → required, текст «Заполните поле»', () => {
      const errors = errorsOf(requiredTrim(), '   ');
      expect(errors).toEqual({ required: true });
      expect(firstErrorText(errors)).toBe('Заполните поле');
    });

    it('пустая строка, null и undefined → required', () => {
      expect(errorsOf(requiredTrim(), '')).toEqual({ required: true });
      expect(errorsOf(requiredTrim(), null)).toEqual({ required: true });
      expect(errorsOf(requiredTrim(), undefined)).toEqual({ required: true });
    });

    it('строка из неразрывных пробелов тоже пуста после трима', () => {
      expect(errorsOf(requiredTrim(), '\u00A0\u00A0')).toEqual({ required: true });
    });

    it('AC «Трим перед длиной»: «  А  » валидно — значащий символ есть', () => {
      expect(errorsOf(requiredTrim(), '  А  ')).toBeNull();
      expect(errorsOf(requiredTrim(), 'А')).toBeNull();
    });

    it('композиция с форматом: ФИО из пробелов даёт только required (без текста формата)', () => {
      const control = new FormControl('   ', Validators.compose([requiredTrim(), fullName()]));
      expect(control.errors).toEqual({ required: true });
    });
  });

  describe('passwordRules — пароль >= 8, >= 1 цифра, >= 1 буква, >= 1 спецзнак', () => {
    it('AC «Слабый пароль»: «abcdefgh» → password.digit и password.special вместе', () => {
      const errors = errorsOf(passwordRules(), 'abcdefgh');
      expect(errors).toEqual({ 'password.digit': true, 'password.special': true });
    });

    it('AC «Слабый пароль»: тексты ошибок — дословно из словаря', () => {
      const errors = errorsOf(passwordRules(), 'abcdefgh');
      expect(firstErrorText(errors)).toBe(ERROR_TEXTS['password.digit']);
      expect(Object.keys(errors ?? {})).toEqual(['password.digit', 'password.special']);
    });

    it('AC «Слабый пароль»: «abcdef1!» валиден', () => {
      expect(errorsOf(passwordRules(), 'abcdef1!')).toBeNull();
    });

    it('граница длины: 7 символов → password.min; ровно 8 → валиден', () => {
      expect(errorsOf(passwordRules(), 'a1!aaaa')).toEqual({ 'password.min': true });
      expect(errorsOf(passwordRules(), 'a1!bcdef')).toBeNull();
    });

    it('только цифры «12345678» → нет буквы и спецзнака', () => {
      expect(errorsOf(passwordRules(), '12345678')).toEqual({
        'password.letter': true,
        'password.special': true,
      });
    });

    it('только спецзнаки «!@#$%^&*» → нет цифры и буквы', () => {
      expect(errorsOf(passwordRules(), '!@#$%^&*')).toEqual({
        'password.digit': true,
        'password.letter': true,
      });
    });

    it('каждое правило по отдельности: нет цифры / нет буквы / нет спецзнака', () => {
      expect(errorsOf(passwordRules(), 'abcdefg!')).toEqual({ 'password.digit': true });
      expect(errorsOf(passwordRules(), '1234567!')).toEqual({ 'password.letter': true });
      expect(errorsOf(passwordRules(), 'abcdefg1')).toEqual({ 'password.special': true });
    });

    it('несколько нарушений возвращаются вместе: «ab» → min + digit + special', () => {
      expect(errorsOf(passwordRules(), 'ab')).toEqual({
        'password.min': true,
        'password.digit': true,
        'password.special': true,
      });
    });

    it('пустое значение не проверяется (обязательность — requiredTrim)', () => {
      expect(errorsOf(passwordRules(), '')).toBeNull();
      expect(errorsOf(passwordRules(), null)).toBeNull();
    });

    it('кириллическая буква — тоже буква: «Пароль1!» валиден', () => {
      expect(errorsOf(passwordRules(), 'Пароль1!')).toBeNull();
    });

    it('пробел — легальный спецзнак (любой символ, не буква и не цифра): «abcd12g » валиден', () => {
      expect(errorsOf(passwordRules(), 'abcd12g ')).toBeNull();
    });
  });

  describe('passwordMatch — пароль и повтор совпадают', () => {
    /** Форма смены пароля с валидатором совпадения на группе. */
    function matchForm(password: string | null, repeat: string | null): FormGroup {
      return new FormGroup(
        {
          password: new FormControl(password),
          repeatPassword: new FormControl(repeat),
        },
        { validators: passwordMatch('password', 'repeatPassword') },
      );
    }

    it('совпадающие значения → форма без ошибки', () => {
      expect(matchForm('abc123!x', 'abc123!x').errors).toBeNull();
    });

    it('разные значения → password.mismatch с текстом словаря', () => {
      const form = matchForm('abc123!x', 'abc123!y');
      expect(form.errors).toEqual({ 'password.mismatch': true });
      expect(form.hasError('password.mismatch')).toBe(true);
      expect(firstErrorText(form.errors)).toBe('Пароли не совпадают');
    });

    it('пустой повтор не даёт mismatch — его ошибку показывает requiredTrim', () => {
      expect(matchForm('abc123!x', '').errors).toBeNull();
      expect(matchForm('abc123!x', null).errors).toBeNull();
      expect(matchForm('', '').errors).toBeNull();
    });

    it('повтор при пустом пароле — уже несовпадение', () => {
      expect(matchForm('', 'x').errors).toEqual({ 'password.mismatch': true });
    });

    it('сравнение дословное, без трима: «abc» и «abc » не совпадают', () => {
      expect(matchForm('abc', 'abc ').errors).toEqual({ 'password.mismatch': true });
      expect(matchForm(' abc', ' abc').errors).toBeNull();
    });

    it('несуществующие имена контролов → ошибка совпадения не возникает', () => {
      const form = new FormGroup(
        { password: new FormControl('a'), repeat: new FormControl('b') },
        { validators: passwordMatch('password', 'no-such-control') },
      );
      expect(form.errors).toBeNull();
    });
  });

  describe('emailFormat — логин@домен.зона и длина 1–254', () => {
    it('AC «Границы email»: «a@b» → email, текст «Введите корректный email»', () => {
      const errors = errorsOf(emailFormat(), 'a@b');
      expect(errors).toEqual({ email: true });
      expect(firstErrorText(errors)).toBe('Введите корректный email');
    });

    it('AC «Границы email»: «a@b.ru» валиден', () => {
      expect(errorsOf(emailFormat(), 'a@b.ru')).toBeNull();
    });

    it('AC «Граница длины email»: ровно 254 символа валиден', () => {
      expect(errorsOf(emailFormat(), emailOfLength(254))).toBeNull();
    });

    it('AC «Граница длины email»: 255 символов → email.length', () => {
      const errors = errorsOf(emailFormat(), emailOfLength(255));
      expect(errors).toEqual({ 'email.length': true });
      expect(firstErrorText(errors)).toBe('Email — не более 254 символов');
    });

    it('пустое значение не проверяется (обязательность — requiredTrim)', () => {
      expect(errorsOf(emailFormat(), '')).toBeNull();
      expect(errorsOf(emailFormat(), '   ')).toBeNull();
      expect(errorsOf(emailFormat(), null)).toBeNull();
    });

    it('пробел внутри, две «@», пустые метки домена и точка в конце — невалидны', () => {
      expect(errorsOf(emailFormat(), 'a b@c.ru')).toEqual({ email: true });
      expect(errorsOf(emailFormat(), 'a@b@c.ru')).toEqual({ email: true });
      expect(errorsOf(emailFormat(), 'a@b..ru')).toEqual({ email: true });
      expect(errorsOf(emailFormat(), 'a@b.ru.')).toEqual({ email: true });
      expect(errorsOf(emailFormat(), 'a@.ru')).toEqual({ email: true });
    });

    it('многометочный домен валиден: «a@b.c.d»', () => {
      expect(errorsOf(emailFormat(), 'a@b.c.d')).toBeNull();
    });

    it('трим по краям до проверки: « user@example.com » валиден', () => {
      expect(errorsOf(emailFormat(), ' user@example.com ')).toBeNull();
    });
  });

  describe('loginCharset — латиница/цифры/./-/_, длина 1–100', () => {
    it('AC «Алфавит логина»: «stu dent01» → login.charset с текстом словаря', () => {
      const errors = errorsOf(loginCharset(), 'stu dent01');
      expect(errors).toEqual({ 'login.charset': true });
      expect(firstErrorText(errors)).toBe(
        'Логин может содержать только латинские буквы, цифры, точку, дефис и подчёркивание',
      );
    });

    it('разрешённый алфавит целиком: «Student.01-x_y» валиден', () => {
      expect(errorsOf(loginCharset(), 'Student.01-x_y')).toBeNull();
      expect(errorsOf(loginCharset(), 'student01')).toBeNull();
    });

    it('кириллица и прочие символы невалидны', () => {
      expect(errorsOf(loginCharset(), 'студент01')).toEqual({ 'login.charset': true });
      expect(errorsOf(loginCharset(), 'stu!dent')).toEqual({ 'login.charset': true });
    });

    it('граница длины: 1 символ, ровно 100 — валидны; 101 → login', () => {
      expect(errorsOf(loginCharset(), 'a')).toBeNull();
      expect(errorsOf(loginCharset(), 'a'.repeat(100))).toBeNull();
      const errors = errorsOf(loginCharset(), 'a'.repeat(101));
      expect(errors).toEqual({ login: true });
      expect(firstErrorText(errors)).toBe('Логин — от 1 до 100 символов');
    });

    it('трим по краям до проверки: «  student01  » валиден', () => {
      expect(errorsOf(loginCharset(), '  student01  ')).toBeNull();
    });

    it('длина и алфавит нарушены вместе — обе ошибки в одном ответе', () => {
      expect(errorsOf(loginCharset(), `${'a'.repeat(101)} b`)).toEqual({
        login: true,
        'login.charset': true,
      });
    });

    it('пустое значение не проверяется (обязательность — requiredTrim)', () => {
      expect(errorsOf(loginCharset(), '')).toBeNull();
      expect(errorsOf(loginCharset(), '   ')).toBeNull();
    });
  });

  describe('code6 — ровно 6 цифр', () => {
    it('AC «Формат кода»: «12345» → code', () => {
      const errors = errorsOf(code6(), '12345');
      expect(errors).toEqual({ code: true });
      expect(firstErrorText(errors)).toBe('Код должен состоять из 6 цифр');
    });

    it('AC «Формат кода»: «123456» валиден', () => {
      expect(errorsOf(code6(), '123456')).toBeNull();
    });

    it('AC «Формат кода»: «12a456» → code', () => {
      expect(errorsOf(code6(), '12a456')).toEqual({ code: true });
    });

    it('7 цифр и пробел внутри — невалидны', () => {
      expect(errorsOf(code6(), '1234567')).toEqual({ code: true });
      expect(errorsOf(code6(), '12 456')).toEqual({ code: true });
    });

    it('только ASCII-цифры: восточноарабские цифры не проходят', () => {
      expect(errorsOf(code6(), '١٢٣٤٥٦')).toEqual({ code: true });
    });

    it('пробелы по краям тримятся: « 123456 » валиден; пусто не проверяется', () => {
      expect(errorsOf(code6(), ' 123456 ')).toBeNull();
      expect(errorsOf(code6(), '')).toBeNull();
    });
  });

  describe('labNumber — целое > 0 без верхней границы', () => {
    it('граница: 1 валиден (строкой и числом)', () => {
      expect(errorsOf(labNumber(), '1')).toBeNull();
      expect(errorsOf(labNumber(), 1)).toBeNull();
    });

    it('0 невалиден — число должно быть положительным', () => {
      const errors = errorsOf(labNumber(), '0');
      expect(errors).toEqual({ 'lab.number': true });
      expect(firstErrorText(errors)).toBe('Номер должен быть положительным числом');
      expect(errorsOf(labNumber(), 0)).toEqual({ 'lab.number': true });
    });

    it('отрицательное, дробное и нечисловое — невалидны', () => {
      expect(errorsOf(labNumber(), '-3')).toEqual({ 'lab.number': true });
      expect(errorsOf(labNumber(), '1.5')).toEqual({ 'lab.number': true });
      expect(errorsOf(labNumber(), 'abc')).toEqual({ 'lab.number': true });
      expect(errorsOf(labNumber(), '1e3')).toEqual({ 'lab.number': true });
      expect(errorsOf(labNumber(), 2.5)).toEqual({ 'lab.number': true });
    });

    it('верхней границы нет: 999 и 1000 валидны', () => {
      expect(errorsOf(labNumber(), '999')).toBeNull();
      expect(errorsOf(labNumber(), 1000)).toBeNull();
    });

    it('пустое значение не проверяется; пробелы тримятся', () => {
      expect(errorsOf(labNumber(), '')).toBeNull();
      expect(errorsOf(labNumber(), '  ')).toBeNull();
      expect(errorsOf(labNumber(), ' 5 ')).toBeNull();
    });
  });

  describe('labSemester — целое 1–10 включительно', () => {
    it('границы диапазона: 1 и 10 валидны, строкой и числом', () => {
      expect(errorsOf(labSemester(), 1)).toBeNull();
      expect(errorsOf(labSemester(), 10)).toBeNull();
      expect(errorsOf(labSemester(), '1')).toBeNull();
      expect(errorsOf(labSemester(), '10')).toBeNull();
    });

    it('за границами: 0 и 11 → lab.semester', () => {
      const errors = errorsOf(labSemester(), 11);
      expect(errors).toEqual({ 'lab.semester': true });
      expect(firstErrorText(errors)).toBe('Семестр — число от 1 до 10');
      expect(errorsOf(labSemester(), 0)).toEqual({ 'lab.semester': true });
    });

    it('дробное, отрицательное и нечисловое — невалидны', () => {
      expect(errorsOf(labSemester(), '1.5')).toEqual({ 'lab.semester': true });
      expect(errorsOf(labSemester(), '-1')).toEqual({ 'lab.semester': true });
      expect(errorsOf(labSemester(), 'abc')).toEqual({ 'lab.semester': true });
    });

    it('пустое значение не проверяется; пробелы тримятся', () => {
      expect(errorsOf(labSemester(), '')).toBeNull();
      expect(errorsOf(labSemester(), ' 4 ')).toBeNull();
    });
  });

  describe('labContent — 1–500 символов после трима', () => {
    it('AC «Границы содержания работы»: 500 символов валидны', () => {
      expect(errorsOf(labContent(), 'a'.repeat(500))).toBeNull();
    });

    it('AC «Границы содержания работы»: 501 символ → lab.content', () => {
      const errors = errorsOf(labContent(), 'a'.repeat(501));
      expect(errors).toEqual({ 'lab.content': true });
      expect(firstErrorText(errors)).toBe('Содержание — от 1 до 500 символов');
    });

    it('граница минимума: 1 символ валиден; трим по краям не съедает границу', () => {
      expect(errorsOf(labContent(), 'a')).toBeNull();
      expect(errorsOf(labContent(), ` ${'a'.repeat(500)} `)).toBeNull();
    });

    it('пустое значение не проверяется (обязательность — requiredTrim)', () => {
      expect(errorsOf(labContent(), '')).toBeNull();
      expect(errorsOf(labContent(), '   ')).toBeNull();
    });
  });

  describe('labUrl — начинается с http:// или https://', () => {
    it('http и https валидны; пустая ссылка допустима (assignmentUrl nullable)', () => {
      expect(errorsOf(labUrl(), 'http://example.com/task.pdf')).toBeNull();
      expect(errorsOf(labUrl(), 'https://example.com')).toBeNull();
      expect(errorsOf(labUrl(), '')).toBeNull();
      expect(errorsOf(labUrl(), '   ')).toBeNull();
    });

    it('чужая схема, опечатка и отсутствие схемы → lab.url', () => {
      const errors = errorsOf(labUrl(), 'ftp://example.com');
      expect(errors).toEqual({ 'lab.url': true });
      expect(firstErrorText(errors)).toBe('Ссылка должна начинаться с http:// или https://');
      expect(errorsOf(labUrl(), 'htp://example.com')).toEqual({ 'lab.url': true });
      expect(errorsOf(labUrl(), 'example.com')).toEqual({ 'lab.url': true });
    });

    it('правило дословное: префикс в нижнем регистре; «HTTP://…» невалиден', () => {
      expect(errorsOf(labUrl(), 'HTTP://example.com')).toEqual({ 'lab.url': true });
    });

    it('проверяется только префикс: «http://» без адреса проходит правило', () => {
      expect(errorsOf(labUrl(), 'http://')).toBeNull();
    });

    it('пробелы по краям тримятся: « http://example.com » валиден', () => {
      expect(errorsOf(labUrl(), ' http://example.com ')).toBeNull();
    });
  });

  describe('groupName — 1–100 символов после трима', () => {
    it('AC «Трим перед проверкой длины»: «  А  » валидно — поле значимо', () => {
      expect(errorsOf(groupName(), '  А  ')).toBeNull();
      expect(errorsOf(groupName(), 'А')).toBeNull();
      expect(errorsOf(groupName(), 'ИК-221')).toBeNull();
    });

    it('границы длины: ровно 100 — валидно; 101 → group.name', () => {
      expect(errorsOf(groupName(), 'a'.repeat(100))).toBeNull();
      const errors = errorsOf(groupName(), 'a'.repeat(101));
      expect(errors).toEqual({ 'group.name': true });
      expect(firstErrorText(errors)).toBe('Название группы — от 1 до 100 символов');
    });

    it('пустое значение не проверяется (обязательность — requiredTrim)', () => {
      expect(errorsOf(groupName(), '')).toBeNull();
      expect(errorsOf(groupName(), '   ')).toBeNull();
    });
  });

  describe('fullName — 1–200 символов после трима', () => {
    it('обычное ФИО и ФИО с крайними пробелами валидны', () => {
      expect(errorsOf(fullName(), 'Иванов Иван Иванович')).toBeNull();
      expect(errorsOf(fullName(), ' Иван ')).toBeNull();
    });

    it('границы длины: ровно 200 — валидно; 201 → fullName', () => {
      expect(errorsOf(fullName(), 'a'.repeat(200))).toBeNull();
      const errors = errorsOf(fullName(), 'a'.repeat(201));
      expect(errors).toEqual({ fullName: true });
      expect(firstErrorText(errors)).toBe('ФИО — от 1 до 200 символов');
    });

    it('пустое значение не проверяется (обязательность — requiredTrim)', () => {
      expect(errorsOf(fullName(), '')).toBeNull();
      expect(errorsOf(fullName(), '   ')).toBeNull();
    });
  });

  describe('trimFormValues — к моку уходят триммированные значения', () => {
    it('AC «Трим перед длиной»: форма с «  А  » отдаёт в form.value «А»', () => {
      const form = new FormGroup({ name: new FormControl('  А  ') });
      trimFormValues(form);
      expect(form.value).toEqual({ name: 'А' });
    });

    it('триммит рекурсивно все строковые контролы, включая вложенные группы', () => {
      const form = new FormGroup({
        name: new FormControl('  А  '),
        login: new FormControl(' student01 '),
        semester: new FormControl(5),
        inner: new FormGroup({ fullName: new FormControl(' Иван ') }),
      });
      trimFormValues(form);
      expect(form.value).toEqual({
        name: 'А',
        login: 'student01',
        semester: 5,
        inner: { fullName: 'Иван' },
      });
    });

    it('внутренние пробелы сохраняются, значения без крайних пробелов не меняются', () => {
      const form = new FormGroup({ fullName: new FormControl(' Иванов Иван ') });
      trimFormValues(form);
      expect(form.value).toEqual({ fullName: 'Иванов Иван' });

      const clean = new FormGroup({ name: new FormControl('ИК-221') });
      trimFormValues(clean);
      expect(clean.value).toEqual({ name: 'ИК-221' });
    });

    it('нестроковые значения не трогаются: число и null проходят как есть', () => {
      const form = new FormGroup({
        number: new FormControl(7),
        note: new FormControl(null),
      });
      trimFormValues(form);
      expect(form.value).toEqual({ number: 7, note: null });
    });

    it('нормализация не порождает событий valueChanges', () => {
      const form = new FormGroup({ name: new FormControl('  А  ') });
      const emitted: unknown[] = [];
      form.valueChanges.subscribe((value) => emitted.push(value));
      trimFormValues(form);
      expect(emitted).toEqual([]);
      expect(form.value).toEqual({ name: 'А' });
    });

    it('строка из одних пробелов нормализуется в пустую строку', () => {
      const form = new FormGroup({ name: new FormControl('   ') });
      trimFormValues(form);
      expect(form.value).toEqual({ name: '' });
    });
  });

  describe('firstErrorText — тексты полей только из словаря', () => {
    it('null и объект без словарных ключей → null', () => {
      expect(firstErrorText(null)).toBeNull();
      expect(firstErrorText({})).toBeNull();
    });

    it('первый словарный ключ даёт дословный текст словаря', () => {
      expect(firstErrorText({ required: true })).toBe(ERROR_TEXTS.required);
      expect(firstErrorText({ code: true })).toBe(ERROR_TEXTS.code);
    });

    it('чужие ключи (встроенные валидаторы Angular) пропускаются', () => {
      expect(firstErrorText({ minlength: { requiredLength: 5 } })).toBeNull();
      expect(
        firstErrorText({ minlength: { requiredLength: 5 }, code: true }),
      ).toBe(ERROR_TEXTS.code);
    });
  });

  describe('композиция валидаторов на контролах форм (как в экранах)', () => {
    it('пароль «abcdefgh» на форме регистрации: только password.digit и password.special', () => {
      const control = new FormControl(
        'abcdefgh',
        Validators.compose([requiredTrim(), passwordRules()]),
      );
      expect(control.errors).toEqual({
        'password.digit': true,
        'password.special': true,
      });
      expect(control.valid).toBe(false);
    });

    it('название группы «  А  » на форме создания: контрол валиден (AC «Трим перед длиной»)', () => {
      const control = new FormControl(
        '  А  ',
        Validators.compose([requiredTrim(), groupName()]),
      );
      expect(control.errors).toBeNull();
      expect(control.valid).toBe(true);
    });

    it('логин с пробелом на форме регистрации: контрол невалиден, ключ login.charset', () => {
      const control = new FormControl(
        'stu dent01',
        Validators.compose([requiredTrim(), loginCharset()]),
      );
      expect(control.errors).toEqual({ 'login.charset': true });
      expect(control.valid).toBe(false);
    });
  });
});
