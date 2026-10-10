/**
 * Интеграционные тесты регистрации (batch1, FR-4.1/QG-005, §4.1):
 * TS-001 автологин студентом и редирект; TS-002/TS-003 дословные 409
 * (значения логина/email уходят как набрано — ci-нормализация на бэкенде);
 * TS-004 клиентская валидация без запроса; TS-005 границы правила пароля §8;
 * TS-013 двойной сабмит — один запрос (блокировка на время in-flight).
 *
 * Сценарии TS-006 (лимит регистраций 5/час) и TS-007/TS-012 (уникальность/
 * гонки создания учётных записей) переехали в зону бэкенд-домена Auth:
 * их клиентская поверхность — дословные 409/429 — покрыта TS-002/TS-003
 * здесь и TS-010 login-logout. Уровень — интеграционный: реальная страница
 * внутри полного дерева маршрутов, реальный HTTP-стек на программируемом
 * HttpTestingController (FR-026); состояние проверяется через границы
 * компонентов (память AuthService, журнал HTTP, текущий URL), а не мимо них.
 */
import {
  AuthProfileEnv,
  ME_FIXTURES,
  requestsOf,
  respond,
  root,
  settle,
  setInput,
  startAuthProfileEnv,
  stopAuthProfileEnv,
  submitButton,
  waitForUrl,
} from './auth-profile.env';

/** Валидные данные формы регистрации с уникальными логином/email. */
function validRegister(
  overrides: Partial<Record<string, string>> = {},
): Record<string, string> {
  return {
    '#full-name-input': 'Петров Пётр Петрович',
    '#login-input': 'newuser',
    '#email-input': 'newuser@test.ru',
    '#password-input': 'Abcdef1!',
    '#repeat-password-input': 'Abcdef1!',
    ...overrides,
  };
}

function fillRegisterForm(
  env: AuthProfileEnv,
  values: Record<string, string>,
): void {
  for (const [selector, value] of Object.entries(values)) {
    setInput(env, selector, value);
  }
}

/** Тело последнего register-запроса (граница HTTP). */
function lastRegisterBody(env: AuthProfileEnv): unknown {
  const registers = env.requests.filter((r) => r.path === '/auth/register');
  return registers[registers.length - 1].body;
}

describe('Интеграция: регистрация (FR-4.1, batch1 TS-001..TS-005, TS-013)', () => {
  let env: AuthProfileEnv;

  beforeEach(async () => {
    spyOn(console, 'error');
    env = await startAuthProfileEnv({ initialUrl: '/register' });
  });

  afterEach(() => {
    stopAuthProfileEnv(env);
  });

  it('TS-001: успешная регистрация — автологин студентом и редирект на /my-submissions', async () => {
    fillRegisterForm(
      env,
      validRegister({ '#full-name-input': 'Иванов Иван Иванович' }),
    );
    submitButton(env).click();
    respond(env, 'POST', '/auth/register', { body: ME_FIXTURES.student01 });
    await waitForUrl(env, '/my-submissions');

    expect(env.auth.isAuthenticated())
      .withContext('сессия установлена (автологин, память)')
      .toBeTrue();
    expect(env.auth.currentUser()?.role)
      .withContext('currentUser.role')
      .toBe('student');

    // DTO регистрации на границе HTTP: ровно пять полей формы, триммированы.
    expect(lastRegisterBody(env)).toEqual({
      fullName: 'Иванов Иван Иванович',
      login: 'newuser',
      email: 'newuser@test.ru',
      password: 'Abcdef1!',
      repeatPassword: 'Abcdef1!',
    });
  }, 20000);

  it('TS-002: регистрация с занятым логином — дословный 409-текст, сессии нет', async () => {
    fillRegisterForm(env, validRegister({ '#login-input': 'student01' }));
    submitButton(env).click();
    respond(env, 'POST', '/auth/register', {
      status: 409,
      body: { message: 'Пользователь с таким логином уже существует' },
    });
    await settle(env);

    expect(env.notifications.desktopMessage()?.text)
      .withContext('баннер с текстом из контракта IF-101')
      .toBe('Пользователь с таким логином уже существует');
    expect(env.auth.isAuthenticated()).withContext('сессия НЕ установлена').toBeFalse();
    expect(env.router.url).withContext('экран регистрации остаётся').toBe('/register');
    env.notifications.dismissMobile();
  }, 20000);

  it('TS-003: регистрация с занятым email в другом регистре — дословный 409, аккаунт не создаётся клиентом', async () => {
    fillRegisterForm(
      env,
      validRegister({ '#email-input': 'STUDENT01@EXAMPLE.COM' }),
    );
    submitButton(env).click();
    respond(env, 'POST', '/auth/register', {
      status: 409,
      body: { message: 'Пользователь с таким email уже существует' },
    });
    await settle(env);

    expect(lastRegisterBody(env))
      .withContext('email уходит как набран (ci — зона бэкенда)')
      .toEqual(jasmine.objectContaining({ email: 'STUDENT01@EXAMPLE.COM' }));
    expect(env.notifications.desktopMessage()?.text)
      .withContext('баннер дословно')
      .toBe('Пользователь с таким email уже существует');
    expect(env.auth.isAuthenticated()).withContext('сессии нет').toBeFalse();
    env.notifications.dismissMobile();
  }, 20000);

  it('TS-004: клиентская валидация регистрации без отправки запроса', async () => {
    // Пустая форма: тексты «Заполните поле» у всех обязательных полей.
    submitButton(env).click();
    await settle(env);
    expect(env.notifications.desktopMessage()?.text).toBe('Данные заполнены неверно');
    expect(setFieldError(env, '#full-name-input')).toBe('Заполните поле');
    expect(setFieldError(env, '#login-input')).toBe('Заполните поле');
    expect(setFieldError(env, '#email-input')).toBe('Заполните поле');
    expect(setFieldError(env, '#password-input')).toBe('Заполните поле');
    expect(setFieldError(env, '#repeat-password-input')).toBe('Заполните поле');
    expect(requestsOf(env, 'POST', '/auth/register'))
      .withContext('запрос не отправлен')
      .toBe(0);
    env.notifications.dismissMobile();

    // Некорректный email.
    setInput(env, '#email-input', 'abc');
    submitButton(env).click();
    await settle(env);
    expect(setFieldError(env, '#email-input')).toBe('Введите корректный email');
    env.notifications.dismissMobile();

    // Слабый пароль (первый нарушенный класс — текст из словаря ERROR_TEXTS).
    fillRegisterForm(env, { '#password-input': 'abc', '#repeat-password-input': 'abc' });
    submitButton(env).click();
    await settle(env);
    expect(setFieldError(env, '#password-input')).toBe(
      'Пароль должен содержать не менее 8 символов',
    );
    env.notifications.dismissMobile();

    // Несовпадающий повтор — текст у поля повтора.
    fillRegisterForm(env, {
      '#password-input': 'Abcdef1!',
      '#repeat-password-input': 'Abcdef2!',
    });
    submitButton(env).click();
    await settle(env);
    expect(setFieldError(env, '#repeat-password-input')).toBe('Пароли не совпадают');
    env.notifications.dismissMobile();

    // Исправление поля: текст исчезает сразу, без повторной отправки.
    setInput(env, '#repeat-password-input', 'Abcdef1!');
    expect(setFieldError(env, '#repeat-password-input'))
      .withContext('текст исчезает сразу при исправлении')
      .toBeNull();

    expect(requestsOf(env, 'POST', '/auth/register'))
      .withContext('auth/register НЕ отправлен ни разу')
      .toBe(0);
    expect(env.httpMock.match({ method: 'POST', url: `${env.apiBase}/auth/register` }).length)
      .withContext('незапрограммированных запросов нет')
      .toBe(0);
  }, 20000);

  it('TS-005: границы правила пароля §8 (ровно на границе и за ней)', async () => {
    const fill = (password: string, repeat: string): void =>
      fillRegisterForm(env, {
        '#password-input': password,
        '#repeat-password-input': repeat,
      });
    const submitAndCheck = async (): Promise<void> => {
      submitButton(env).click();
      await settle(env);
    };

    fill('Abcd12!', 'Abcd12!'); // 7 символов — за границей
    await submitAndCheck();
    expect(setFieldError(env, '#password-input')).toBe(
      'Пароль должен содержать не менее 8 символов',
    );
    env.notifications.dismissMobile();

    fill('Abcdefg!', 'Abcdefg!'); // без цифры
    await submitAndCheck();
    expect(setFieldError(env, '#password-input')).toBe(
      'Пароль должен содержать хотя бы одну цифру',
    );
    env.notifications.dismissMobile();

    fill('12345678', '12345678'); // без буквы
    await submitAndCheck();
    expect(setFieldError(env, '#password-input')).toBe(
      'Пароль должен содержать хотя бы одну букву',
    );
    env.notifications.dismissMobile();

    fill('Abcd1234', 'Abcd1234'); // без спецзнака (буква+цифра — только это правило нарушено)
    await submitAndCheck();
    expect(setFieldError(env, '#password-input')).toBe(
      'Пароль должен содержать хотя бы один специальный знак',
    );
    env.notifications.dismissMobile();

    fill('Abcd123!', 'Abcd1234'); // корректный пароль, несовпадающий повтор
    await submitAndCheck();
    expect(setFieldError(env, '#password-input'))
      .withContext('сам пароль корректен — ошибки нет')
      .toBeNull();
    expect(setFieldError(env, '#repeat-password-input'))
      .withContext('несовпадение подсвечено у поля повтора')
      .toBe('Пароли не совпадают');
    env.notifications.dismissMobile();

    expect(requestsOf(env, 'POST', '/auth/register'))
      .withContext('заблокированные варианты не отправлялись')
      .toBe(0);

    // Ровно 8 символов со всеми классами — единственный проходной вариант;
    // остальные обязательные поля заполняются валидными значениями.
    fillRegisterForm(env, validRegister());
    fill('Abcd123!', 'Abcd123!');
    submitButton(env).click();
    respond(env, 'POST', '/auth/register', { body: ME_FIXTURES.student01 });
    await waitForUrl(env, '/my-submissions');
    expect(requestsOf(env, 'POST', '/auth/register')).toBe(1);
    expect(env.router.url).withContext('регистрация прошла').toBe('/my-submissions');
  }, 20000);

  it('TS-013: двойной сабмит во время запроса — один вызов login', async () => {
    await env.harness.navigateByUrl('/login');
    await settle(env);

    setInput(env, '#login-input', 'teacher');
    // Ветвь 401: экран /login остаётся открытым — разблокировка кнопки
    // проверяется на живом DOM после ответа (без ухода со страницы).
    setInput(env, '#password-input', 'wrong-password');

    const button = submitButton(env);
    button.click(); // первый клик
    env.harness.fixture.detectChanges();
    expect(button.disabled)
      .withContext('кнопка заблокирована на время запроса')
      .toBeTrue();
    expect(button.className)
      .withContext('состояние loading')
      .toContain('p-button-loading');

    button.click(); // повторный клик во время запроса
    env.harness.fixture.detectChanges();
    // «Ровно один запрос»: httpMock.match() ПОТРЕБЛЯЕТ открытые запросы
    // (удаляет их из списка), поэтому здесь он не используется — единственность
    // утверждают expectOne() внутри respond() (упадёт с «found 2 requests» при
    // параллельном вызове) и контрольный журнал запросов после ответа.

    respond(env, 'POST', '/auth/login', {
      status: 401,
      body: { message: 'Неверный логин или пароль' },
    });
    await settle(env);
    expect(env.notifications.desktopMessage()?.text)
      .withContext('отказ входа — экран /login остаётся открытым')
      .toBe('Неверный логин или пароль');
    expect(env.router.url).toBe('/login');
    expect(button.disabled)
      .withContext('после ответа кнопка разблокирована')
      .toBeFalse();
    expect(button.className)
      .withContext('loading-состояние снято')
      .not.toContain('p-button-loading');
    expect(requestsOf(env, 'POST', '/auth/login')).toBe(1);
    env.notifications.dismissMobile();
  }, 20000);

  /** Текст полевой ошибки под полем (div.field__error соседнего .field). */
  function setFieldError(localEnv: AuthProfileEnv, inputSelector: string): string | null {
    const input = root(localEnv).querySelector(inputSelector);
    if (input === null) {
      return null;
    }
    const error = input
      .closest('.field')
      ?.querySelector<HTMLElement>('.field__error');
    return error === undefined || error === null ? null : error.textContent!.trim();
  }
});
