/**
 * Интеграционные тесты входа и выхода (batch1, FR-4.1/IF-108, §4.1):
 * TS-008 вход обеими демо-ролями (ответ {role, fullName}, ci-логин,
 * редирект по роли); TS-009 единое сообщение при любых неверных данных;
 * TS-010 rate limit входа 5 неуспешных/мин (успехи лимит не расходуют);
 * TS-011 выход очищает сессию и возвращает на /login.
 *
 * Реальная страница /login, реальный мок-слой и guards; тексты — дословно
 * из контракта IF-101 (QG-005).
 */
import {
  AuthProfileEnv,
  flushMock,
  required,
  sessionUserId,
  setInput,
  startAuthProfileEnv,
  stopAuthProfileEnv,
  submitButton,
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

  async function submitLogin(calls = 1): Promise<void> {
    submitButton(env).click();
    await flushMock(env, calls);
  }

  it('TS-008: вход обеими демо-ролями — ответ, ci-логин, редирект на домашнюю', async () => {
    // Преподаватель.
    fillCredentials(TEACHER.login, TEACHER.password);
    await submitLogin();
    expect(env.auth.currentUser()).withContext('ответ содержит {role, fullName}').toEqual(
      jasmine.objectContaining({
        role: 'teacher',
        fullName: 'Сидоров Семён Семёнович',
      }),
    );
    expect(env.router.url).withContext('teacher → редирект /works').toBe('/works');
    expect(sessionUserId()).withContext('сессия установлена').not.toBeNull();

    // Выход через шапку.
    required<HTMLButtonElement>(env, '.app-topbar__logout').click();
    await flushMock(env, 1);
    expect(env.router.url).toBe('/login');
    expect(sessionUserId()).withContext('сессия очищена').toBeNull();

    // Студент — логин в другом регистре (ci).
    fillCredentials('Student01', STUDENT.password);
    await submitLogin();
    expect(env.auth.currentUser()).toEqual(
      jasmine.objectContaining({
        role: 'student',
        fullName: 'Иванов Иван Иванович 01',
      }),
    );
    expect(env.router.url).withContext('student → редирект /my-submissions').toBe('/my-submissions');
    expect(sessionUserId()).withContext('сессия установлена').not.toBeNull();
  }, 20000);

  it('TS-009: единое сообщение при любых неверных данных входа', async () => {
    const attempts: Array<[string, string]> = [
      ['ghost', 'no-such-user'], // несуществующий логин
      [STUDENT.login, 'wrong-password'], // существующий логин + неверный пароль
      [STUDENT.login, TEACHER.password], // пароль существующего другого пользователя
    ];
    const texts: Array<string | null | undefined> = [];

    for (const [login, password] of attempts) {
      fillCredentials(login, password);
      await submitLogin();
      texts.push(env.notifications.desktopMessage()?.text);
      expect(sessionUserId()).withContext(`сессия не установлена (${login})`).toBeNull();
      env.notifications.dismissMobile();
    }

    expect(texts).withContext('один и тот же текст во всех трёх случаях').toEqual([
      'Неверный логин или пароль',
      'Неверный логин или пароль',
      'Неверный логин или пароль',
    ]);
  }, 20000);

  it('TS-010: rate limit входа — 5 неуспешных в минуту, 6-я 429; успехи лимит не расходуют', async () => {
    const expectButtonEnabled = (): void =>
      expect(submitButton(env).disabled)
        .withContext('кнопка «Войти» не блокируется')
        .toBeFalse();

    // 2 неуспешные попытки под student01.
    for (let i = 0; i < 2; i++) {
      fillCredentials(STUDENT.login, `nope-${i}`);
      await submitLogin();
      expect(env.notifications.desktopMessage()?.text).toBe('Неверный логин или пароль');
      expectButtonEnabled();
      env.notifications.dismissMobile();
    }

    // Успешная попытка тем же логином: лимит не расходует.
    fillCredentials(STUDENT.login, STUDENT.password);
    await submitLogin();
    expect(env.router.url).withContext('вход успешен').toBe('/my-submissions');
    required<HTMLButtonElement>(env, '.app-topbar__logout').click();
    await flushMock(env, 1);
    expect(env.router.url).toBe('/login');

    // Ещё 3 неуспешные: всего 5 неуспешных в окне минуты.
    for (let i = 0; i < 3; i++) {
      fillCredentials(STUDENT.login, `nope-more-${i}`);
      await submitLogin();
      expect(env.notifications.desktopMessage()?.text).toBe('Неверный логин или пароль');
      expectButtonEnabled();
      env.notifications.dismissMobile();
    }

    // 6-я неуспешная попытка в окне — 429 дословно.
    fillCredentials(STUDENT.login, 'nope-6th');
    await submitLogin();
    expect(env.notifications.desktopMessage()?.text)
      .withContext('429 дословно')
      .toBe('Слишком много попыток. Повторите позже');
    expectButtonEnabled();
    env.notifications.dismissMobile();

    // После окна минуты счётчик сброшен — вход с верным паролем успешен.
    env.clock.now += 61 * 1000;
    fillCredentials(STUDENT.login, STUDENT.password);
    await submitLogin();
    expect(env.router.url).withContext('вход успешен после окна').toBe('/my-submissions');
    expect(sessionUserId()).not.toBeNull();
  }, 30000);

  it('TS-011: выход очищает сессию и возвращает на /login', async () => {
    fillCredentials(STUDENT.login, STUDENT.password);
    await submitLogin();
    expect(env.router.url).toBe('/my-submissions');
    expect(sessionUserId()).not.toBeNull();

    required<HTMLButtonElement>(env, '.app-topbar__logout').click();
    await flushMock(env, 1);

    expect(sessionUserId())
      .withContext('mock.session.userId удалён')
      .toBeNull();
    expect(env.auth.currentUser()).withContext('currentUser=null').toBeNull();
    expect(env.router.url).withContext('возврат на /login').toBe('/login');

    // /profile закрыт authGuard'ом.
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
