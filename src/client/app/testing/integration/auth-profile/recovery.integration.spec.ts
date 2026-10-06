/**
 * Интеграционные тесты потока восстановления пароля (batch1, FR-4.2,
 * §4.2, IF-101/IF-102): TS-014 полный поток; TS-015 неотличимость
 * неизвестного email; TS-016 неверный код; TS-017 аннулирование на 5-й
 * попытке и рестарт через «Переотправить код»; TS-018 TTL кода 10 минут;
 * TS-019 TTL resetToken 15 минут (терминальная ветка); TS-020 одноразовость
 * кода и токена; TS-021 переотправка инвалидирует код, лимит 3/час;
 * TS-022 F5 сохраняет шаг потока; TS-023 полевая ошибка не гасит токен;
 * TS-024 сброс пароля отзывает сессию.
 *
 * Код восстановления берётся из console.info (демо-замена SMTP: строка
 * «[mock-email] Код восстановления для <email>: <код>»); TTL/лимиты — на
 * управляемых часах обработчиков auth (clock.now). TS-022 — перезапуск
 * приложения (новый инжектор, storage сохранён) как аналог F5.
 */
import {
  AuthProfileEnv,
  buttonByLabel,
  fieldErrorText,
  flushMock,
  linkByLabel,
  mockDbData,
  restartApp,
  sessionUserId,
  setInput,
  startAuthProfileEnv,
  stopAuthProfileEnv,
  submitButton,
} from './auth-profile.env';
import { loginAs, seedUserIdByLogin } from '../../integration-env';

const KNOWN_EMAIL = 'student01@example.com';
const GHOST_EMAIL = 'ghost@nowhere.zz';
const MINUTE_MS = 60 * 1000;

describe('Интеграция: восстановление пароля (FR-4.2, batch1 TS-014..TS-024)', () => {
  let env: AuthProfileEnv;
  let infoSpy: jasmine.Spy;

  beforeEach(async () => {
    spyOn(console, 'error');
    env = await startAuthProfileEnv({ initialUrl: '/recovery' });
    infoSpy = spyOn(console, 'info');
  });

  afterEach(() => {
    stopAuthProfileEnv(env);
  });

  /** Последний код из «письма» console.info для email. */
  function codeFor(email: string): string {
    const prefix = `[mock-email] Код восстановления для ${email}: `;
    const line = infoSpy.calls
      .all()
      .map((c) => String(c.args[0]))
      .reverse()
      .find((l) => l.startsWith(prefix));
    expect(line).withContext(`строка «[mock-email] …» для ${email}`).toBeDefined();
    return line!.slice(prefix.length);
  }

  /** Шаг 1: запрос кода через форму /recovery (успех → /recovery/code). */
  async function requestCode(email: string): Promise<string> {
    await env.harness.navigateByUrl('/recovery');
    env.harness.fixture.detectChanges();
    setInput(env, '#email-input', email);
    buttonByLabel(env, 'Отправить код').click();
    await flushMock(env, 1);
    expect(env.router.url).withContext('шаг 2 открыт').toBe('/recovery/code');
    return codeFor(email);
  }

  /** Шаг 2: ввод кода (успех → /reset-password). */
  async function enterCode(code: string): Promise<void> {
    setInput(env, '#code-input', code);
    buttonByLabel(env, 'Ввести код').click();
    await flushMock(env, 1);
  }

  /** Шаг 3: смена пароля на /reset-password. */
  async function submitNewPassword(password: string): Promise<void> {
    setInput(env, '#password-input', password);
    setInput(env, '#repeat-password-input', password);
    buttonByLabel(env, 'Сменить пароль').click();
    await flushMock(env, 1);
  }

  it('TS-014: полный поток — email → код → новый пароль', async () => {
    const code = await requestCode(KNOWN_EMAIL);
    await enterCode(code);
    expect(env.router.url).withContext('шаг 3 открыт').toBe('/reset-password');
    expect(env.flow.resetToken())
      .withContext('confirm возвращает resetToken (в store)')
      .not.toBeNull();

    await submitNewPassword('NewPass1!');
    expect(env.router.url).withContext('сброс успешен — редирект на /login').toBe('/login');

    // Вход со старым паролем больше не работает.
    setInput(env, '#login-input', 'student01');
    setInput(env, '#password-input', 'student123!');
    submitButton(env).click();
    await flushMock(env, 1);
    expect(env.notifications.desktopMessage()?.text).toBe('Неверный логин или пароль');
    env.notifications.dismissMobile();

    // Вход с новым паролем успешен.
    setInput(env, '#password-input', 'NewPass1!');
    submitButton(env).click();
    await flushMock(env, 1);
    expect(env.router.url).toBe('/my-submissions');
    expect(sessionUserId()).not.toBeNull();
  }, 20000);

  it('TS-015: неизвестный email неотличим на шаге 1 и всегда 400 на шаге 2', async () => {
    await requestCode(KNOWN_EMAIL); // ответ и лог для существующего
    const ghostCode = await requestCode(GHOST_EMAIL); // поведение идентично

    const pattern = /^\[mock-email\] Код восстановления для .+: \d{6}$/;
    const knownLine = `[mock-email] Код восстановления для ${KNOWN_EMAIL}: ${codeFor(KNOWN_EMAIL)}`;
    const ghostLine = `[mock-email] Код восстановления для ${GHOST_EMAIL}: ${ghostCode}`;
    expect(knownLine).withContext('формат лога существующего').toMatch(pattern);
    expect(ghostLine).withContext('код пишется в console.info одинаково').toMatch(pattern);

    // Confirm с кодом для неизвестного email — всегда 400.
    setInput(env, '#code-input', ghostCode);
    buttonByLabel(env, 'Ввести код').click();
    await flushMock(env, 1);
    expect(env.notifications.desktopMessage()?.text)
      .withContext('единый текст отказа')
      .toBe('Код восстановления не подходит');
    env.notifications.dismissMobile();

    let caught: { status: number; body: { message: string; errors?: unknown } } | undefined;
    try {
      await env.client.call('auth.recovery.confirm', {
        email: GHOST_EMAIL,
        code: ghostCode,
      });
    } catch (error) {
      caught = error as typeof caught;
    }
    expect(caught?.status).withContext('статус 400').toBe(400);
    expect(caught?.body.message).toBe('Код восстановления не подходит');
    expect(caught?.body.errors).withContext('существование email нигде не раскрывается').toBeUndefined();

    // Код для неизвестного email не сохранялся — только код student01.
    expect(mockDbData().recoveryCodes.length).toBe(1);
    expect(mockDbData().recoveryCodes[0].code).toBe(codeFor(KNOWN_EMAIL));
  }, 20000);

  it('TS-016: неверный код восстановления', async () => {
    const callSpy = spyOn(env.client, 'call').and.callThrough();
    await requestCode(KNOWN_EMAIL);

    // Не-6-цифровая строка блокируется клиентским валидатором — форматного
    // 400 у confirm нет (запрос не уходит).
    setInput(env, '#code-input', '12a456');
    buttonByLabel(env, 'Ввести код').click();
    env.harness.fixture.detectChanges();
    expect(fieldErrorText(env, '#code-input')).toBe('Код должен состоять из 6 цифр');
    expect(callSpy.calls.all().filter((c) => c.args[0] === 'auth.recovery.confirm').length)
      .withContext('форматного 400 у confirm нет — запрос не отправлен')
      .toBe(0);

    // Неверный 6-значный код — 400 дословно, экран остаётся, попытка засчитана.
    setInput(env, '#code-input', '000000');
    buttonByLabel(env, 'Ввести код').click();
    await flushMock(env, 1);
    expect(env.notifications.desktopMessage()?.text).toBe('Код восстановления не подходит');
    expect(env.router.url).withContext('экран /recovery/code остаётся открытым').toBe('/recovery/code');
    expect(mockDbData().recoveryCodes[0].attempts)
      .withContext('попытка засчитана')
      .toBe(1);
    env.notifications.dismissMobile();

    // Прямой confirm неформатной строки — тот же единый 400 (без errors).
    await expectAsync(
      env.client.call('auth.recovery.confirm', { email: KNOWN_EMAIL, code: '12a456' }),
    ).toBeRejectedWith(
      jasmine.objectContaining({
        status: 400,
        body: jasmine.objectContaining({ message: 'Код восстановления не подходит' }),
      }),
    );
  }, 20000);

  it('TS-017: 5 неверных попыток аннулируют код; store сохраняет email; resend восстанавливает поток', async () => {
    const code = await requestCode(KNOWN_EMAIL);

    for (let i = 1; i <= 5; i++) {
      setInput(env, '#code-input', '111111');
      buttonByLabel(env, 'Ввести код').click();
      await flushMock(env, 1);
      expect(env.notifications.desktopMessage()?.text)
        .withContext(`попытка ${i} — 400`)
        .toBe('Код восстановления не подходит');
      expect(env.router.url).withContext('экран остаётся').toBe('/recovery/code');
      env.notifications.dismissMobile();
    }

    // Верный код тоже 400 — код аннулирован (usedAt на 5-й неверной).
    await enterCode(code);
    expect(env.notifications.desktopMessage()?.text)
      .withContext('верный код после аннулирования — тот же 400')
      .toBe('Код восстановления не подходит');
    env.notifications.dismissMobile();

    // RecoveryFlowStore НЕ очищен — email сохранён, resetToken нет.
    expect(env.flow.email()).toBe(KNOWN_EMAIL);
    expect(env.flow.resetToken()).toBeNull();
    expect(sessionStorage.getItem('recovery.flow.v1')).toContain(KNOWN_EMAIL);

    // «Переотправить код» выдаёт новый код, которым поток завершается успешно.
    buttonByLabel(env, 'Переотправить код').click();
    await flushMock(env, 1);
    const freshCode = codeFor(KNOWN_EMAIL);
    expect(freshCode).withContext('новый код выдан').not.toBe(code);

    await enterCode(freshCode);
    expect(env.router.url).withContext('новым кодом поток продолжился').toBe('/reset-password');
    await submitNewPassword('Restart1!pass');
    expect(env.router.url).withContext('поток завершён успешно').toBe('/login');
  }, 30000);

  it('TS-018: TTL кода 10 минут (9:59 принимается, 10:01 — отказ)', async () => {
    const code = await requestCode(KNOWN_EMAIL);
    env.clock.now += 9 * MINUTE_MS + 59 * 1000;
    await enterCode(code);
    expect(env.router.url).withContext('до TTL код принимается').toBe('/reset-password');

    // Новый код, вводимый через 10 минут 1 секунду после выдачи.
    const freshCode = await requestCode(KNOWN_EMAIL);
    env.clock.now += 10 * MINUTE_MS + 1 * 1000;
    await enterCode(freshCode);
    expect(env.notifications.desktopMessage()?.text)
      .withContext('просроченный неотличим от неверного')
      .toBe('Код восстановления не подходит');
    expect(env.router.url).withContext('экран остаётся').toBe('/recovery/code');
  }, 30000);

  it('TS-019: TTL resetToken 15 минут — терминальная ветка с рестартом', async () => {
    const code = await requestCode(KNOWN_EMAIL);
    await enterCode(code);
    expect(env.router.url).toBe('/reset-password');

    env.clock.now += 15 * MINUTE_MS + 1000;
    await submitNewPassword('TooLate1!pass');

    expect(env.notifications.desktopMessage()?.text)
      .withContext('400 дословно')
      .toBe('Ссылка восстановления недействительна или истекла');
    expect(env.router.url)
      .withContext('экран /reset-password остаётся открытым (автоперехода нет)')
      .toBe('/reset-password');
    expect(linkByLabel(env, 'Запросить код заново').getAttribute('href'))
      .withContext('ссылка рестарта потока')
      .toContain('/recovery');
    expect(env.flow.resetToken()).withContext('resetToken удалён из store').toBeNull();
    expect(env.flow.email()).withContext('email сохранён').toBe(KNOWN_EMAIL);
    env.notifications.dismissMobile();

    // Повторная отправка формы — та же терминальная ошибка.
    await submitNewPassword('TooLate1!pass');
    expect(env.notifications.desktopMessage()?.text).toBe('Ссылка восстановления недействительна или истекла');
    expect(env.router.url).toBe('/reset-password');
  }, 20000);

  it('TS-020: одноразовость кода и resetToken', async () => {
    // Код подтверждается ровно один раз.
    const code = await requestCode(KNOWN_EMAIL);
    await enterCode(code);
    expect(env.router.url).toBe('/reset-password');

    await env.harness.navigateByUrl('/recovery/code');
    env.harness.fixture.detectChanges();
    await enterCode(code);
    expect(env.notifications.desktopMessage()?.text)
      .withContext('второй confirm с тем же кодом — 400')
      .toBe('Код восстановления не подходит');
    env.notifications.dismissMobile();

    // Второй поток: один resetToken используется дважды.
    const freshCode = await requestCode(KNOWN_EMAIL);
    await enterCode(freshCode);
    const token = env.flow.resetToken();
    expect(token).withContext('resetToken получен').not.toBeNull();

    await submitNewPassword('Once1!aaa');
    expect(env.router.url).withContext('первый reset успешен').toBe('/login');

    let caught: { status: number; body: { message: string } } | undefined;
    try {
      await env.client.call('auth.reset-password', {
        resetToken: token,
        password: 'Twice2!bb',
        confirmPassword: 'Twice2!bb',
      });
    } catch (error) {
      caught = error as typeof caught;
    }
    expect(caught?.body.message).toBe('Ссылка восстановления недействительна или истекла');

    // Эффект применён ровно один раз.
    await expectAsync(
      env.client.call('auth.login', { login: 'student01', password: 'Twice2!bb' }),
    ).withContext('пароль второй попытки не применился')
      .toBeRejectedWith(jasmine.objectContaining({ status: 401 }));
    const me = await env.client.call<{ login: string }>('auth.login', {
      login: 'student01',
      password: 'Once1!aaa',
    });
    expect(me.login).withContext('пароль сменился ровно один раз').toBe('student01');
  }, 30000);

  it('TS-021: переотправка инвалидирует старый код; 4-й запрос в час — 429', async () => {
    const oldCode = await requestCode(KNOWN_EMAIL); // запрос 1

    buttonByLabel(env, 'Переотправить код').click();
    await flushMock(env, 1);
    const newCode = codeFor(KNOWN_EMAIL); // запрос 2
    expect(newCode).not.toBe(oldCode);

    await enterCode(oldCode);
    expect(env.notifications.desktopMessage()?.text)
      .withContext('старый код — 400')
      .toBe('Код восстановления не подходит');
    env.notifications.dismissMobile();

    await enterCode(newCode);
    expect(env.router.url).withContext('новый код — успех').toBe('/reset-password');

    // 3-й запрос кода в окне часа — разрешён.
    await requestCode(KNOWN_EMAIL);
    expect(env.router.url).toBe('/recovery/code');

    // 4-й запрос — 429 дословно, экран /recovery остаётся.
    await env.harness.navigateByUrl('/recovery');
    env.harness.fixture.detectChanges();
    setInput(env, '#email-input', KNOWN_EMAIL);
    buttonByLabel(env, 'Отправить код').click();
    await flushMock(env, 1);
    expect(env.notifications.desktopMessage()?.text)
      .withContext('429 дословно')
      .toBe('Слишком много попыток. Повторите позже');
    expect(env.router.url).withContext('шаг 2 не открыт').toBe('/recovery');
  }, 30000);

  it('TS-022: F5 в пределах вкладки сохраняет шаг потока восстановления', async () => {
    const code = await requestCode(KNOWN_EMAIL);
    expect(env.router.url).toBe('/recovery/code');

    // F5 на шаге кода: новый инжектор, sessionStorage сохранён.
    env = await restartApp(env, { keepStorage: true });
    await env.harness.navigateByUrl('/recovery/code');
    expect(env.router.url)
      .withContext('шаг восстановлен — редиректа recoveryStepGuard нет')
      .toBe('/recovery/code');
    expect(env.flow.email()).withContext('email из recovery.flow.v1').toBe(KNOWN_EMAIL);

    await enterCode(code);
    expect(env.router.url).toBe('/reset-password');

    // F5 на шаге смены пароля.
    env = await restartApp(env, { keepStorage: true });
    await env.harness.navigateByUrl('/reset-password');
    expect(env.router.url)
      .withContext('шаг восстановлен без повторного ввода email')
      .toBe('/reset-password');
    expect(env.flow.resetToken())
      .withContext('resetToken из recovery.flow.v1')
      .not.toBeNull();

    await submitNewPassword('AfterF51!pass');
    expect(env.router.url).withContext('поток продолжается до успеха').toBe('/login');
  }, 30000);

  it('TS-023: полевая ошибка на /reset-password не гасит resetToken', async () => {
    const code = await requestCode(KNOWN_EMAIL);
    await enterCode(code);
    const tokenBefore = env.flow.resetToken();
    expect(tokenBefore).not.toBeNull();

    const callSpy = spyOn(env.client, 'call').and.callThrough();
    setInput(env, '#password-input', 'abc');
    setInput(env, '#repeat-password-input', 'abc');
    buttonByLabel(env, 'Сменить пароль').click();
    env.harness.fixture.detectChanges();

    expect(env.notifications.desktopMessage()?.text)
      .withContext('баннер клиентской валидации')
      .toBe('Данные заполнены неверно');
    expect(fieldErrorText(env, '#password-input')).toBe(
      'Пароль должен содержать не менее 8 символов',
    );
    expect(callSpy.calls.all().filter((c) => c.args[0] === 'auth.reset-password').length)
      .withContext('запрос не списал токен')
      .toBe(0);
    expect(env.flow.resetToken()).toBe(tokenBefore);
    env.notifications.dismissMobile();

    // Повторный сабмит с валидным паролем — успех тем же resetToken.
    await submitNewPassword('Valid1!pass');
    expect(env.router.url).withContext('сброс успешен').toBe('/login');
  }, 20000);

  it('TS-024: сброс пароля отзывает сессию пользователя', async () => {
    // Сессия установлена (аналог второй вкладки того же пользователя).
    loginAs(seedUserIdByLogin('student01'));
    expect(sessionUserId()).not.toBeNull();

    // Гостевые страницы восстановления авторизованному недоступны (guestGuard),
    // поэтому полный поток до успеха выполняется через границу мок-API —
    // UI-механика потока покрыта TS-014..TS-023.
    await env.client.call('auth.recovery.request', { email: KNOWN_EMAIL });
    const prefix = `[mock-email] Код восстановления для ${KNOWN_EMAIL}: `;
    const line = infoSpy.calls
      .all()
      .map((c) => String(c.args[0]))
      .reverse()
      .find((l) => l.startsWith(prefix));
    expect(line).withContext('код в console.info').toBeDefined();
    const code = line!.slice(prefix.length);
    const { resetToken } = await env.client
      .call<{ resetToken: string }>('auth.recovery.confirm', {
        email: KNOWN_EMAIL,
        code,
      });
    await env.client.call('auth.reset-password', {
      resetToken,
      password: 'Revoked1!pass',
      confirmPassword: 'Revoked1!pass',
    });

    // §4.2 «отзывает все refresh-токены пользователя»: сессия отозвана —
    // продолжить работу без повторного входа невозможно (решение TS-024).
    expect(env.auth.isAuthenticated())
      .withContext('после сброса isAuthenticated()=false')
      .toBeFalse();
    await env.harness.navigateByUrl('/profile');
    expect(env.router.url).withContext('редирект на /login').toBe('/login');
    expect(sessionUserId()).withContext('ключ сессии очищен').toBeNull();
  }, 20000);
});
