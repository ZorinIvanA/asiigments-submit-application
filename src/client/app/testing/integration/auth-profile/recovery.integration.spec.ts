/**
 * Интеграционные тесты потока восстановления пароля (batch1, FR-4.2,
 * §4.2, IF-101/IF-102): TS-014 полный поток; TS-015 неотличимость
 * неизвестного email на клиенте (всегда 200 на шаге 1, единый 400 на
 * шаге 2); TS-016 неверный код; TS-017 серия неверных попыток и рестарт
 * через «Переотправить код»; TS-019 терминальная ветка resetToken; TS-021
 * переотправка кода и 429 лимита; TS-022 F5 сохраняет шаг потока;
 * TS-023 полевая ошибка не гасит токен; TS-024 сброс пароля отзывает
 * сессию (401 → неудачный refresh → сброс памяти → /login).
 *
 * Значения кодов/tokens задаются сценариями (программируемый
 * HttpTestingController, FR-026): серверные свойства потока — TTL кода
 * 10 минут (TS-018), одноразовость кода/токена (TS-020), аннулирование
 * кода 5-й неверной попыткой — зона бэкенд-домена Auth; клиентская
 * поверхность этих свойств (те же 400/баннеры) покрыта здесь, а TS-024
 * проверяет HTTP-цепочку отзыва сессии end-to-end.
 */
import {
  AuthProfileEnv,
  ME_FIXTURES,
  linkByLabel,
  requestsOf,
  restartApp,
  respond,
  root,
  settle,
  setInput,
  startAuthProfileEnv,
  stopAuthProfileEnv,
  buttonByLabel,
  waitForUrl,
} from './auth-profile.env';

const KNOWN_EMAIL = 'student01@example.com';
const GHOST_EMAIL = 'ghost@nowhere.zz';
const CODE_REJECTED = 'Код восстановления не подходит';
const LINK_INVALID = 'Ссылка восстановления недействительна или истекла';
const TOO_MANY = 'Слишком много попыток. Повторите позже';

describe('Интеграция: восстановление пароля (FR-4.2, batch1 TS-014..TS-017, TS-019, TS-021..TS-024)', () => {
  let env: AuthProfileEnv;
  /** Счётчик «выданных» кодов: каждый запрос кода получает новый. */
  let issuedCodes: number;

  beforeEach(async () => {
    spyOn(console, 'error');
    issuedCodes = 0;
    env = await startAuthProfileEnv({ initialUrl: '/recovery' });
  });

  afterEach(() => {
    stopAuthProfileEnv(env);
  });

  /** Очередной 6-значный код, который «бэкенд» выдаёт на запрос. */
  function nextCode(): string {
    issuedCodes += 1;
    return String(100000 + issuedCodes);
  }

  /** Шаг 1: запрос кода через форму /recovery (всегда 200 → /recovery/code). */
  async function requestCode(email: string): Promise<string> {
    if (env.router.url !== '/recovery') {
      await env.harness.navigateByUrl('/recovery');
      await settle(env);
    }
    setInput(env, '#email-input', email);
    buttonByLabel(env, 'Отправить код').click();
    respond(env, 'POST', '/auth/recovery/request', { body: null });
    await waitForUrl(env, '/recovery/code');
    expect(requestsOf(env, 'POST', '/auth/recovery/request')).toBeGreaterThan(0);
    return nextCode();
  }

  /** Шаг 2: ввод кода с программируемым ответом confirm. */
  async function enterCode(
    code: string,
    status: number,
    body: unknown,
    expectedUrl?: string,
  ): Promise<void> {
    setInput(env, '#code-input', code);
    buttonByLabel(env, 'Ввести код').click();
    respond(env, 'POST', '/auth/recovery/confirm', { status, body });
    if (expectedUrl === undefined) {
      await settle(env);
    } else {
      await waitForUrl(env, expectedUrl);
    }
  }

  /** Шаг 3: смена пароля на /reset-password с программируемым ответом. */
  async function submitNewPassword(
    password: string,
    status: number,
    body: unknown = null,
    expectedUrl?: string,
  ): Promise<void> {
    setInput(env, '#password-input', password);
    setInput(env, '#repeat-password-input', password);
    buttonByLabel(env, 'Сменить пароль').click();
    respond(env, 'POST', '/auth/reset-password', { status, body });
    if (expectedUrl === undefined) {
      await settle(env);
    } else {
      await waitForUrl(env, expectedUrl);
    }
  }

  /** Переотправка кода кнопкой экрана шага 2. */
  async function resend(
    status: number,
    body: unknown = null,
  ): Promise<string> {
    buttonByLabel(env, 'Переотправить код').click();
    respond(env, 'POST', '/auth/recovery/request', { status, body });
    await settle(env);
    return nextCode();
  }

  it('TS-014: полный поток — email → код → новый пароль → вход новым паролем', async () => {
    const code = await requestCode(KNOWN_EMAIL);
    await enterCode(code, 200, { resetToken: 'reset-token-1' }, '/reset-password');
    expect(env.flow.resetToken())
      .withContext('confirm возвращает resetToken (в store)')
      .toBe('reset-token-1');

    await submitNewPassword('NewPass1!', 204, null, '/login');
    expect(env.router.url).withContext('сброс успешен — редирект на /login').toBe('/login');
    // DTO сброса на границе HTTP.
    expect(env.requests.find((r) => r.path === '/auth/reset-password')?.body).toEqual({
      resetToken: 'reset-token-1',
      password: 'NewPass1!',
      confirmPassword: 'NewPass1!',
    });

    // Вход со старым паролем больше не работает (ответ бэкенда 401).
    setInput(env, '#login-input', 'student01');
    setInput(env, '#password-input', 'student123!');
    buttonByLabel(env, 'Войти').click();
    respond(env, 'POST', '/auth/login', {
      status: 401,
      body: { message: 'Неверный логин или пароль' },
    });
    await settle(env);
    expect(env.notifications.desktopMessage()?.text).toBe('Неверный логин или пароль');
    env.notifications.dismissMobile();

    // Вход с новым паролем успешен.
    setInput(env, '#password-input', 'NewPass1!');
    buttonByLabel(env, 'Войти').click();
    respond(env, 'POST', '/auth/login', { body: ME_FIXTURES.student01 });
    await waitForUrl(env, '/my-submissions');
    expect(env.auth.isAuthenticated()).toBeTrue();
  }, 20000);

  it('TS-015: неизвестный email неотличим на клиенте: всегда 200 на шаге 1, единый 400 на шаге 2', async () => {
    await requestCode(KNOWN_EMAIL); // ответ и переход для существующего
    await requestCode(GHOST_EMAIL); // поведение идентично

    // Confirm с кодом для неизвестного email — единый 400.
    const ghostCode = nextCode();
    setInput(env, '#code-input', ghostCode);
    buttonByLabel(env, 'Ввести код').click();
    respond(env, 'POST', '/auth/recovery/confirm', {
      status: 400,
      body: { message: CODE_REJECTED },
    });
    await settle(env);

    expect(env.notifications.desktopMessage()?.text)
      .withContext('единый текст отказа')
      .toBe(CODE_REJECTED);
    env.notifications.dismissMobile();

    // Тела запросов шага 1 идентичны по форме (различие только в значении
    // email; существует ли он — клиенту не раскрывается).
    const requests = env.requests.filter((r) => r.path === '/auth/recovery/request');
    expect(requests.map((r) => (r.body as { email: string }).email)).toEqual([
      KNOWN_EMAIL,
      GHOST_EMAIL,
    ]);
    // Полевых ошибок нет (400 без errors).
    expect(env.harness.fixture.nativeElement.querySelector('.field__error')).toBeNull();
  }, 20000);

  it('TS-016: неверный код восстановления — формат ловится клиентом, 400 оставляет экран', async () => {
    const code = await requestCode(KNOWN_EMAIL);

    // Не-6-цифровая строка блокируется клиентским валидатором — запроса нет.
    setInput(env, '#code-input', '12a456');
    buttonByLabel(env, 'Ввести код').click();
    await settle(env);
    expect(
      root(env)
        .querySelector('#code-input')
        ?.closest('.field')
        ?.querySelector<HTMLElement>('.field__error')?.textContent?.trim(),
    ).toBe('Код должен состоять из 6 цифр');
    expect(requestsOf(env, 'POST', '/auth/recovery/confirm'))
      .withContext('форматного 400 у confirm нет — запрос не отправлен')
      .toBe(0);

    // Неверный 6-значный код — 400 дословно, экран остаётся.
    await enterCode('000000', 400, { message: CODE_REJECTED });
    expect(env.notifications.desktopMessage()?.text).toBe(CODE_REJECTED);
    expect(env.router.url)
      .withContext('экран /recovery/code остаётся открытым')
      .toBe('/recovery/code');
    expect(env.flow.email()).withContext('email сохранён').toBe(KNOWN_EMAIL);
    expect(env.flow.resetToken()).toBeNull();
    env.notifications.dismissMobile();
  }, 20000);

  it('TS-017: серия неверных попыток (аннулирование — зона бэкенда) не сбрасывает email; resend восстанавливает поток', async () => {
    const code = await requestCode(KNOWN_EMAIL);

    for (let i = 1; i <= 5; i++) {
      await enterCode('111111', 400, { message: CODE_REJECTED });
      expect(env.notifications.desktopMessage()?.text)
        .withContext(`попытка ${i} — 400`)
        .toBe(CODE_REJECTED);
      expect(env.router.url).withContext('экран остаётся').toBe('/recovery/code');
      env.notifications.dismissMobile();
    }

    // Верный код тоже 400 — код аннулирован 5-й неверной попыткой (бэкенд).
    await enterCode(code, 400, { message: CODE_REJECTED });
    expect(env.notifications.desktopMessage()?.text)
      .withContext('верный код после аннулирования — тот же 400')
      .toBe(CODE_REJECTED);
    env.notifications.dismissMobile();

    // RecoveryFlowStore НЕ очищен — email сохранён, resetToken нет.
    expect(env.flow.email()).toBe(KNOWN_EMAIL);
    expect(env.flow.resetToken()).toBeNull();
    expect(sessionStorage.getItem('recovery.flow.v1')).toContain(KNOWN_EMAIL);

    // «Переотправить код» выдаёт новый код, которым поток завершается успешно.
    const freshCode = await resend(200);
    expect(freshCode).withContext('новый код выдан').not.toBe(code);

    await enterCode(freshCode, 200, { resetToken: 'reset-token-2' }, '/reset-password');
    await submitNewPassword('Restart1!pass', 204, null, '/login');
    expect(env.router.url).withContext('поток завершён успешно').toBe('/login');
  }, 30000);

  it('TS-019: терминальная ветка resetToken (400 «Ссылка…») — экран остаётся, токен погашен, email сохранён', async () => {
    const code = await requestCode(KNOWN_EMAIL);
    await enterCode(code, 200, { resetToken: 'reset-token-1' }, '/reset-password');

    await submitNewPassword('TooLate1!pass', 400, { message: LINK_INVALID });

    expect(env.notifications.desktopMessage()?.text)
      .withContext('400 дословно')
      .toBe(LINK_INVALID);
    expect(env.router.url)
      .withContext('экран /reset-password остаётся открытым (автоперехода нет)')
      .toBe('/reset-password');
    const link = linkByLabel(env, 'Запросить код заново');
    expect(link.getAttribute('href'))
      .withContext('ссылка рестарта потока')
      .toContain('/recovery');
    expect(env.flow.resetToken()).withContext('resetToken удалён из store').toBeNull();
    expect(env.flow.email()).withContext('email сохранён').toBe(KNOWN_EMAIL);
    env.notifications.dismissMobile();

    // Повторная отправка формы — та же терминальная ошибка.
    await submitNewPassword('TooLate1!pass', 400, { message: LINK_INVALID });
    expect(env.notifications.desktopMessage()?.text).toBe(LINK_INVALID);
    expect(env.router.url).toBe('/reset-password');
    env.notifications.dismissMobile();
  }, 20000);

  it('TS-021: переотправка выдаёт новый код; 429 лимита — дословный баннер, экран /recovery остаётся', async () => {
    const oldCode = await requestCode(KNOWN_EMAIL); // запрос 1

    const newCode = await resend(200); // запрос 2
    expect(newCode).not.toBe(oldCode);

    await enterCode(oldCode, 400, { message: CODE_REJECTED });
    expect(env.notifications.desktopMessage()?.text)
      .withContext('старый код — 400')
      .toBe(CODE_REJECTED);
    env.notifications.dismissMobile();

    await enterCode(newCode, 200, { resetToken: 'reset-token-1' }, '/reset-password');

    // 3-й запрос кода в окне часа — разрешён (ответ бэкенда 200).
    await env.harness.navigateByUrl('/recovery');
    await settle(env);
    await requestCode(KNOWN_EMAIL);
    expect(env.router.url).toBe('/recovery/code');

    // 4-й запрос — 429 дословно, шаг 2 не открывается.
    await env.harness.navigateByUrl('/recovery');
    await settle(env);
    setInput(env, '#email-input', KNOWN_EMAIL);
    buttonByLabel(env, 'Отправить код').click();
    respond(env, 'POST', '/auth/recovery/request', {
      status: 429,
      body: { message: TOO_MANY },
    });
    await settle(env);
    expect(env.notifications.desktopMessage()?.text)
      .withContext('429 дословно')
      .toBe(TOO_MANY);
    expect(env.router.url).withContext('шаг 2 не открыт').toBe('/recovery');
    env.notifications.dismissMobile();
  }, 30000);

  it('TS-022: F5 в пределах вкладки сохраняет шаг потока восстановления (recovery.flow.v1)', async () => {
    const code = await requestCode(KNOWN_EMAIL);
    expect(env.router.url).toBe('/recovery/code');

    // F5 на шаге кода: новый инжектор, sessionStorage сохранён.
    env = await restartApp(env, { keepStorage: true });
    await env.harness.navigateByUrl('/recovery/code');
    expect(env.router.url)
      .withContext('шаг восстановлен — редиректа recoveryStepGuard нет')
      .toBe('/recovery/code');
    expect(env.flow.email()).withContext('email из recovery.flow.v1').toBe(KNOWN_EMAIL);

    await enterCode(code, 200, { resetToken: 'reset-token-1' }, '/reset-password');

    // F5 на шаге смены пароля.
    env = await restartApp(env, { keepStorage: true });
    await env.harness.navigateByUrl('/reset-password');
    expect(env.router.url)
      .withContext('шаг восстановлен без повторного ввода email')
      .toBe('/reset-password');
    expect(env.flow.resetToken())
      .withContext('resetToken из recovery.flow.v1')
      .toBe('reset-token-1');

    await submitNewPassword('AfterF51!pass', 204, null, '/login');
    expect(env.router.url).withContext('поток продолжается до успеха').toBe('/login');
  }, 30000);

  it('TS-023: полевая ошибка на /reset-password не гасит resetToken', async () => {
    const code = await requestCode(KNOWN_EMAIL);
    await enterCode(code, 200, { resetToken: 'reset-token-1' }, '/reset-password');
    const tokenBefore = env.flow.resetToken();
    expect(tokenBefore).not.toBeNull();

    setInput(env, '#password-input', 'abc');
    setInput(env, '#repeat-password-input', 'abc');
    buttonByLabel(env, 'Сменить пароль').click();
    await settle(env);

    expect(env.notifications.desktopMessage()?.text)
      .withContext('баннер клиентской валидации')
      .toBe('Данные заполнены неверно');
    expect(
      root(env)
        .querySelector('#password-input')
        ?.closest('.field')
        ?.querySelector<HTMLElement>('.field__error')?.textContent?.trim(),
    ).toBe('Пароль должен содержать не менее 8 символов');
    expect(requestsOf(env, 'POST', '/auth/reset-password'))
      .withContext('запрос не списал токен')
      .toBe(0);
    expect(env.flow.resetToken()).toBe(tokenBefore);
    env.notifications.dismissMobile();

    // Повторный сабмит с валидным паролем — успех тем же resetToken.
    await submitNewPassword('Valid1!pass', 204, null, '/login');
    expect(env.router.url).withContext('сброс успешен').toBe('/login');
    expect(env.requests.find((r) => r.path === '/auth/reset-password')?.body).toEqual(
      jasmine.objectContaining({ resetToken: tokenBefore }),
    );
  }, 20000);

  it('TS-024: сброс пароля отзывает сессию — 401 → неудачный refresh → сброс памяти → /login', async () => {
    // Сессия установлена (аналог второй вкладки того же пользователя).
    env = await restartApp(env, { session: 'student01' });
    expect(env.auth.isAuthenticated()).toBeTrue();

    // §4.2 «отзывает все refresh-токены пользователя»: следующий защищённый
    // вызов получает 401, тихий refresh тоже 401 — сессия отозвана,
    // продолжить работу без повторного входа невозможно.
    await env.harness.navigateByUrl('/profile');
    respond(env, 'GET', '/me/profile', {
      status: 401,
      body: { message: 'Не авторизован' },
    });
    respond(env, 'POST', '/auth/refresh', {
      status: 401,
      body: { message: 'Не авторизован' },
    });
    await settle(env);

    expect(env.auth.isAuthenticated())
      .withContext('после отзыва isAuthenticated()=false')
      .toBeFalse();
    expect(env.router.url).withContext('редирект на /login').toBe('/login');
    env.notifications.dismissMobile();
  }, 20000);
});
