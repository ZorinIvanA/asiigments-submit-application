/**
 * Интеграционные тесты регистрации (batch1, FR-4.1/QG-005, §4.1):
 * TS-001 автологин студентом и редирект; TS-002/TS-003 дословные 409;
 * TS-004 клиентская валидация без запроса; TS-005 границы правила пароля §8;
 * TS-006 rate limit 5/час (счётчик всех вызовов — аменда 8/ISS-006);
 * TS-007 идемпотентность повторной регистрации; TS-012 параллельная
 * регистрация одного логина; TS-013 двойной сабмит во время мок-задержки.
 *
 * Уровень — интеграционный: реальная страница внутри полного дерева
 * маршрутов, реальный мок-слой (setupMockLayer) на свежем клиенте, реальный
 * localStorage (mock.db.v1, mock.session.userId); состояние проверяется
 * через границы компонентов (сессия, мок-БД, текущий URL), а не мимо них.
 */
import {
  AuthProfileEnv,
  fieldErrorText,
  flushMock,
  mockDbData,
  sessionUserId,
  setInput,
  startAuthProfileEnv,
  stopAuthProfileEnv,
  submitButton,
  T0,
} from './auth-profile.env';
import { seedFixtures } from '../../../mock/seed';

const HOUR_MS = 60 * 60 * 1000;

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

/** Число вызовов мок-метода через клиент (по журналу spy). */
function callsOf(callSpy: jasmine.Spy, method: string): number {
  return callSpy.calls.all().filter((c) => c.args[0] === method).length;
}

describe('Интеграция: регистрация (FR-4.1, batch1 TS-001..TS-007, TS-012, TS-013)', () => {
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
    await flushMock(env, 1);

    expect(env.router.url)
      .withContext('редирект на сдачи студента')
      .toBe('/my-submissions');
    expect(sessionUserId())
      .withContext('сессия установлена (mock.session.userId)')
      .not.toBeNull();
    expect(env.auth.currentUser()?.role)
      .withContext('currentUser.role')
      .toBe('student');

    const created = mockDbData().users.find((u) => u.login === 'newuser');
    expect(created).withContext('пользователь в mock.db.v1').toBeDefined();
    expect(created!.role)
      .withContext('роль созданной учётной записи')
      .toBe('student');
    expect(created!.fullName).toBe('Иванов Иван Иванович');
    expect(created!.email).toBe('newuser@test.ru');
  }, 20000);

  it('TS-002: регистрация с занятым логином — дословный 409-текст', async () => {
    // До первого мок-вызова ключа mock.db.v1 ещё нет — исходная длина берётся
    // из сида (детерминирован, ADR-107).
    const usersBefore = seedFixtures().users.length;
    fillRegisterForm(env, validRegister({ '#login-input': 'student01' }));
    submitButton(env).click();
    await flushMock(env, 1);

    expect(env.notifications.desktopMessage()?.text)
      .withContext('баннер с текстом из контракта IF-101')
      .toBe('Пользователь с таким логином уже существует');
    expect(sessionUserId()).withContext('сессия НЕ установлена').toBeNull();
    expect(mockDbData().users.length)
      .withContext('новый пользователь не создан')
      .toBe(usersBefore);
  }, 20000);

  it('TS-003: регистрация с занятым email в другом регистре (ci-проверка)', async () => {
    fillRegisterForm(
      env,
      validRegister({ '#email-input': 'STUDENT01@EXAMPLE.COM' }),
    );
    submitButton(env).click();
    await flushMock(env, 1);

    expect(env.notifications.desktopMessage()?.text)
      .withContext('баннер дословно')
      .toBe('Пользователь с таким email уже существует');
    expect(sessionUserId()).withContext('сессии нет').toBeNull();
    expect(mockDbData().users.find((u) => u.login === 'newuser'))
      .withContext('аккаунт не создан — дубликат email в другом регистре не прошёл')
      .toBeUndefined();
  }, 20000);

  it('TS-004: клиентская валидация регистрации без отправки запроса', async () => {
    const callSpy = spyOn(env.client, 'call').and.callThrough();

    // Пустая форма: тексты «Заполните поле» у всех обязательных полей.
    submitButton(env).click();
    env.harness.fixture.detectChanges();
    expect(env.notifications.desktopMessage()?.text).toBe('Данные заполнены неверно');
    expect(fieldErrorText(env, '#full-name-input')).toBe('Заполните поле');
    expect(fieldErrorText(env, '#login-input')).toBe('Заполните поле');
    expect(fieldErrorText(env, '#email-input')).toBe('Заполните поле');
    expect(fieldErrorText(env, '#password-input')).toBe('Заполните поле');
    expect(fieldErrorText(env, '#repeat-password-input')).toBe('Заполните поле');
    expect(callsOf(callSpy, 'auth.register'))
      .withContext('запрос не отправлен')
      .toBe(0);
    env.notifications.dismissMobile();

    // Некорректный email.
    setInput(env, '#email-input', 'abc');
    submitButton(env).click();
    env.harness.fixture.detectChanges();
    expect(fieldErrorText(env, '#email-input')).toBe('Введите корректный email');
    env.notifications.dismissMobile();

    // Слабый пароль (первый нарушенный класс — текст из словаря ERROR_TEXTS).
    fillRegisterForm(env, { '#password-input': 'abc', '#repeat-password-input': 'abc' });
    submitButton(env).click();
    env.harness.fixture.detectChanges();
    expect(fieldErrorText(env, '#password-input')).toBe(
      'Пароль должен содержать не менее 8 символов',
    );
    env.notifications.dismissMobile();

    // Несовпадающий повтор — текст у поля повтора.
    fillRegisterForm(env, {
      '#password-input': 'Abcdef1!',
      '#repeat-password-input': 'Abcdef2!',
    });
    submitButton(env).click();
    env.harness.fixture.detectChanges();
    expect(fieldErrorText(env, '#repeat-password-input')).toBe('Пароли не совпадают');
    env.notifications.dismissMobile();

    // Исправление поля: текст исчезает сразу, без повторной отправки.
    setInput(env, '#repeat-password-input', 'Abcdef1!');
    expect(fieldErrorText(env, '#repeat-password-input'))
      .withContext('текст исчезает сразу при исправлении')
      .toBeNull();

    expect(callsOf(callSpy, 'auth.register'))
      .withContext('auth.register НЕ вызван ни разу')
      .toBe(0);
  }, 20000);

  it('TS-005: границы правила пароля §8 (ровно на границе и за ней)', async () => {
    const callSpy = spyOn(env.client, 'call').and.callThrough();
    const fill = (password: string, repeat: string): void =>
      fillRegisterForm(env, {
        '#password-input': password,
        '#repeat-password-input': repeat,
      });
    const submitAndCheck = (): void => {
      submitButton(env).click();
      env.harness.fixture.detectChanges();
    };

    fill('Abcd12!', 'Abcd12!'); // 7 символов — за границей
    submitAndCheck();
    expect(fieldErrorText(env, '#password-input')).toBe(
      'Пароль должен содержать не менее 8 символов',
    );
    env.notifications.dismissMobile();

    fill('Abcdefg!', 'Abcdefg!'); // без цифры
    submitAndCheck();
    expect(fieldErrorText(env, '#password-input')).toBe(
      'Пароль должен содержать хотя бы одну цифру',
    );
    env.notifications.dismissMobile();

    fill('12345678', '12345678'); // без буквы
    submitAndCheck();
    expect(fieldErrorText(env, '#password-input')).toBe(
      'Пароль должен содержать хотя бы одну букву',
    );
    env.notifications.dismissMobile();

    fill('Abcd1234', 'Abcd1234'); // без спецзнака (буква+цифра — только это правило нарушено)
    submitAndCheck();
    expect(fieldErrorText(env, '#password-input')).toBe(
      'Пароль должен содержать хотя бы один специальный знак',
    );
    env.notifications.dismissMobile();

    fill('Abcd123!', 'Abcd1234'); // корректный пароль, несовпадающий повтор
    submitAndCheck();
    expect(fieldErrorText(env, '#password-input'))
      .withContext('сам пароль корректен — ошибки нет')
      .toBeNull();
    expect(fieldErrorText(env, '#repeat-password-input'))
      .withContext('несовпадение подсвечено у поля повтора')
      .toBe('Пароли не совпадают');
    env.notifications.dismissMobile();

    expect(callsOf(callSpy, 'auth.register'))
      .withContext('заблокированные варианты не отправлялись')
      .toBe(0);

    // Ровно 8 символов со всеми классами — единственный проходной вариант;
    // остальные обязательные поля заполняются валидными значениями.
    fillRegisterForm(env, validRegister());
    fill('Abcd123!', 'Abcd123!');
    submitAndCheck();
    await flushMock(env, 1);
    expect(callsOf(callSpy, 'auth.register')).toBe(1);
    expect(env.router.url).withContext('регистрация прошла').toBe('/my-submissions');
  }, 20000);

  it('TS-006: rate limit регистраций — 6-й вызов в час 429; после окна — успех', async () => {
    // 5 вызовов auth.register, завершившихся валидационным 400: счётчик
    // учитывает ВСЕ вызовы (аменда 8/ISS-006) — лимит исчерпан.
    const invalid = {
      fullName: 'Лимит Лимитов',
      login: 'ratelimit',
      email: 'ratelimit@test.ru',
      password: 'weak',
      repeatPassword: 'weak',
    };
    for (let i = 0; i < 5; i++) {
      await expectAsync(env.client.call('auth.register', invalid))
        .withContext(`вызов ${i + 1} — валидационный 400`)
        .toBeRejectedWith(jasmine.objectContaining({ status: 400 }));
    }

    fillRegisterForm(env, validRegister({ '#login-input': 'freshuser' }));
    submitButton(env).click();
    await flushMock(env, 1);

    expect(env.notifications.desktopMessage()?.text)
      .withContext('429 дословно')
      .toBe('Слишком много попыток. Повторите позже');
    const button = submitButton(env);
    expect(button.disabled)
      .withContext('кнопки формы не блокируются')
      .toBeFalse();
    expect(button.className).not.toContain('p-button-loading');
    expect(sessionUserId()).toBeNull();
    env.notifications.dismissMobile();

    // Сдвиг времени за окно часа — регистрация снова успешна.
    env.clock.now = T0 + HOUR_MS + 60 * 1000;
    submitButton(env).click();
    await flushMock(env, 1);

    expect(env.router.url)
      .withContext('регистрация прошла после окна')
      .toBe('/my-submissions');
    expect(sessionUserId()).withContext('сессия установлена').not.toBeNull();
  }, 20000);

  it('TS-007: повторная идентичная регистрация не создаёт дубликат', async () => {
    const params = {
      fullName: 'Дубль Дублев',
      login: 'dupuser',
      email: 'dupuser@test.ru',
      password: 'Dupl1234!',
      repeatPassword: 'Dupl1234!',
    };
    fillRegisterForm(env, {
      '#full-name-input': params.fullName,
      '#login-input': params.login,
      '#email-input': params.email,
      '#password-input': params.password,
      '#repeat-password-input': params.repeatPassword,
    });
    submitButton(env).click();
    await flushMock(env, 1);
    const sessionAfterFirst = sessionUserId();
    expect(sessionAfterFirst).withContext('первая регистрация успешна').not.toBeNull();

    // Повтор той же регистрации (те же логин и email).
    await expectAsync(env.client.call('auth.register', params))
      .withContext('второй ответ — 409')
      .toBeRejectedWith(
        jasmine.objectContaining({
          status: 409,
          body: jasmine.objectContaining({
            message: 'Пользователь с таким логином уже существует',
          }),
        }),
      );

    expect(mockDbData().users.filter((u) => u.login === 'dupuser').length)
      .withContext('ровно один пользователь dupuser')
      .toBe(1);
    expect(sessionUserId())
      .withContext('сессия после второй попытки не перезаписана')
      .toBe(sessionAfterFirst);
  }, 20000);

  it('TS-012: параллельная регистрация с одним логином — ровно один пользователь', async () => {
    const params = {
      fullName: 'Гонка Гонкиных',
      login: 'raceuser',
      email: 'race@test.ru',
      password: 'Race1234!',
      repeatPassword: 'Race1234!',
    };
    const settled = await Promise.allSettled([
      env.client.call('auth.register', params),
      env.client.call('auth.register', params),
    ]);

    const fulfilled = settled.filter((r) => r.status === 'fulfilled');
    const rejected = settled.filter(
      (r): r is PromiseRejectedResult => r.status === 'rejected',
    );
    expect(fulfilled.length).withContext('один ответ успешен').toBe(1);
    expect(rejected.length).withContext('второй — конфликт').toBe(1);
    expect(rejected[0].reason)
      .withContext('409 дословно')
      .toEqual(
        jasmine.objectContaining({
          status: 409,
          body: jasmine.objectContaining({
            message: 'Пользователь с таким логином уже существует',
          }),
        }),
      );
    expect(mockDbData().users.filter((u) => u.login === 'raceuser').length)
      .withContext('ровно одна учётная запись (транзакционный db.mutate)')
      .toBe(1);
  }, 20000);

  it('TS-013: двойной сабмит во время мок-задержки — один вызов login', async () => {
    await env.harness.navigateByUrl('/login');
    env.harness.fixture.detectChanges();

    const callSpy = spyOn(env.client, 'call').and.callThrough();
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
    expect(callsOf(callSpy, 'auth.login'))
      .withContext('обработчик login вызван ровно один раз')
      .toBe(1);

    await flushMock(env, 1);
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
    expect(callsOf(callSpy, 'auth.login')).toBe(1);
  }, 20000);
});
