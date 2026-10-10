/**
 * Интеграционные тесты входа и выхода (batch1, FR-4.1/IF-108, §4.1):
 * TS-008 вход обеими демо-ролями (ответ {role, fullName}, значение логина
 * уходит как набрано — ci-нормализация на бэкенде, редирект по роли);
 * TS-009 единое сообщение при любых неверных данных (401); TS-010
 * 429 «Слишком много попыток» — клиентская поверхность лимита входа
 * (окно/счётчик — зона бэкенд-домена); TS-011 выход очищает сессию
 * (память AuthService) и возвращает на /login.
 *
 * Реальная страница /login, реальный HTTP-стек и guards на программируемом
 * HttpTestingController (FR-026); тексты — дословно из контракта IF-101
 * (QG-005). Признак сессии — память AuthService (FR-092): сохранение/очистка
 * проверяются по auth.currentUser()/isAuthenticated().
 */
import {
  AuthProfileEnv,
  ME_FIXTURES,
  respond,
  required,
  settle,
  setInput,
  startAuthProfileEnv,
  stopAuthProfileEnv,
  submitButton,
  waitForUrl,
} from './auth-profile.env';

const TEACHER = { login: 'teacher', password: 'teacher123!' };
const STUDENT = { login: 'student01', password: 'student123!' };

describe('Интеграция: вход и выход (FR-4.1/IF-108, batch1 TS-008..TS-011)', () => {
  let env: AuthProfileEnv;

  beforeEach(async () => {
    spyOn(console, 'error');
    env = await startAuthProfileEnv({ initialUrl: '/login' });
  });

  afterEach(() => {
    stopAuthProfileEnv(env);
  });

  function fillCredentials(login: string, password: string): void {
    setInput(env, '#login-input', login);
    setInput(env, '#password-input', password);
  }

  /**
   * Сабмит входа с программируемым ответом POST /auth/login; для успешных
   * исходов дожидается фактического редиректа (ленивый чанк домашней
   * страницы асинхронен).
   */
  async function submitLogin(
    status: number,
    body: unknown,
    expectedUrl?: string,
  ): Promise<void> {
    submitButton(env).click();
    respond(env, 'POST', '/auth/login', { status, body });
    if (expectedUrl === undefined) {
      await settle(env);
    } else {
      await waitForUrl(env, expectedUrl);
    }
  }

  /** Тело последнего login-запроса (граница HTTP). */
  function lastLoginBody(): unknown {
    const logins = env.requests.filter((r) => r.path === '/auth/login');
    return logins[logins.length - 1].body;
  }

  it('TS-008: вход обеими демо-ролями — ответ, редирект на домашнюю, значение логина без искажений', async () => {
    // Преподаватель.
    fillCredentials(TEACHER.login, TEACHER.password);
    await submitLogin(200, ME_FIXTURES.teacher, '/works');
    expect(env.auth.currentUser()).withContext('ответ содержит {role, fullName}').toEqual(
      jasmine.objectContaining({
        role: 'teacher',
        fullName: 'Сидоров Семён Семёнович',
      }),
    );
    expect(env.router.url).withContext('teacher → редирект /works').toBe('/works');
    expect(env.auth.isAuthenticated()).withContext('сессия установлена (память)').toBeTrue();

    // Выход через шапку.
    required<HTMLButtonElement>(env, '.app-topbar__logout').click();
    respond(env, 'POST', '/auth/logout', { status: 204 });
    await waitForUrl(env, '/login');
    expect(env.auth.isAuthenticated()).withContext('сессия очищена').toBeFalse();

    // Студент — логин в другом регистре: клиент отправляет значение как
    // набрано; нормализация регистра — ci-проверка бэкенда.
    fillCredentials('Student01', STUDENT.password);
    await submitLogin(200, ME_FIXTURES.student01, '/my-submissions');
    expect(lastLoginBody()).withContext('логин уходит без изменения регистра').toEqual({
      login: 'Student01',
      password: STUDENT.password,
    });
    expect(env.auth.currentUser()).toEqual(
      jasmine.objectContaining({
        role: 'student',
        fullName: 'Иванов Иван Иванович 01',
      }),
    );
    expect(env.router.url).withContext('student → редирект /my-submissions').toBe('/my-submissions');
    expect(env.auth.isAuthenticated()).toBeTrue();
  }, 20000);

  it('TS-009: единое сообщение при любых неверных данных входа (401)', async () => {
    const attempts: Array<[string, string]> = [
      ['ghost', 'no-such-user'], // несуществующий логин
      [STUDENT.login, 'wrong-password'], // существующий логин + неверный пароль
      [STUDENT.login, TEACHER.password], // пароль существующего другого пользователя
    ];
    const texts: Array<string | null | undefined> = [];

    for (const [login, password] of attempts) {
      fillCredentials(login, password);
      await submitLogin(401, { message: 'Неверный логин или пароль' });
      texts.push(env.notifications.desktopMessage()?.text);
      expect(env.auth.isAuthenticated())
        .withContext(`сессия не установлена (${login})`)
        .toBeFalse();
      env.notifications.dismissMobile();
    }

    expect(texts).withContext('один и тот же текст во всех трёх случаях').toEqual([
      'Неверный логин или пароль',
      'Неверный логин или пароль',
      'Неверный логин или пароль',
    ]);
    expect(env.router.url).withContext('экран входа остаётся открытым').toBe('/login');
  }, 20000);

  it('TS-010: 429 входа — дословный баннер, форма не блокируется (окно лимита — зона бэкенда)', async () => {
    const expectButtonEnabled = (): void =>
      expect(submitButton(env).disabled)
        .withContext('кнопка «Войти» не блокируется')
        .toBeFalse();

    // Две неуспешные попытки подряд.
    for (let i = 0; i < 2; i++) {
      fillCredentials(STUDENT.login, `nope-${i}`);
      await submitLogin(401, { message: 'Неверный логин или пароль' });
      expect(env.notifications.desktopMessage()?.text).toBe('Неверный логин или пароль');
      expectButtonEnabled();
      env.notifications.dismissMobile();
    }

    // Лимит бэкенда исчерпан: 429 дословно, экран входа остаётся.
    fillCredentials(STUDENT.login, STUDENT.password);
    await submitLogin(429, { message: 'Слишком много попыток. Повторите позже' });
    expect(env.notifications.desktopMessage()?.text)
      .withContext('429 дословно')
      .toBe('Слишком много попыток. Повторите позже');
    expect(env.auth.isAuthenticated()).toBeFalse();
    expectButtonEnabled();
    expect(submitButton(env).className).not.toContain('p-button-loading');
    env.notifications.dismissMobile();
  }, 20000);

  it('TS-011: выход очищает сессию (память) и возвращает на /login', async () => {
    fillCredentials(STUDENT.login, STUDENT.password);
    await submitLogin(200, ME_FIXTURES.student01, '/my-submissions');
    expect(env.router.url).toBe('/my-submissions');
    expect(env.auth.isAuthenticated()).toBeTrue();

    required<HTMLButtonElement>(env, '.app-topbar__logout').click();
    respond(env, 'POST', '/auth/logout', { status: 204 });
    await waitForUrl(env, '/login');

    expect(env.auth.currentUser()).withContext('currentUser=null').toBeNull();
    expect(env.auth.isAuthenticated()).withContext('сессия очищена').toBeFalse();
    expect(env.router.url).withContext('возврат на /login').toBe('/login');

    // /profile закрыт authGuard'ом (проверка по памяти, без HTTP).
    await env.harness.navigateByUrl('/profile');
    expect(env.router.url).withContext('/profile → редирект /login').toBe('/login');

    // Страницы гостя снова доступны.
    await env.harness.navigateByUrl('/login');
    expect(env.router.url).toBe('/login');
    await env.harness.navigateByUrl('/register');
    expect(env.router.url).toBe('/register');
    await env.harness.navigateByUrl('/recovery');
    expect(env.router.url).toBe('/recovery');
  }, 20000);
});
