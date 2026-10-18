/**
 * Интеграционный тест границ области v1 (batch1, FR-4.1/FR-4.7, §4.1/§4.7):
 * TS-040 out-of-scope возможности отсутствуют — в UI нет выбора роли и шага
 * подтверждения email; DTO auth.register содержит ровно пять полей формы
 * (поле role клиентом не передаётся — роль учётной записи назначает
 * бэкенд); профиль не содержит редактируемых полей роли/группы/логина
 * (логин read-only).
 *
 * Механизм уведомлений (FR-4.9) и адаптивность (FR-4.10) — зоны других
 * батчей и визуальных сценариев VS-*, здесь не проверяются.
 */
import {
  AuthProfileEnv,
  ME_FIXTURES,
  respond,
  required,
  restartApp,
  root,
  settle,
  setInput,
  startAuthProfileEnv,
  stopAuthProfileEnv,
  submitButton,
} from './auth-profile.env';

describe('Интеграция: границы области v1 (FR-4.1/FR-4.7, batch1 TS-040)', () => {
  let env: AuthProfileEnv;

  beforeEach(async () => {
    spyOn(console, 'error');
    env = await startAuthProfileEnv({ initialUrl: '/register' });
  });

  afterEach(() => {
    stopAuthProfileEnv(env);
  });

  it('TS-040: out-of-scope возможности v1 отсутствуют', async () => {
    // /register: ровно пять полей макета, без выбора роли и подтверждения email.
    const labels = Array.from(root(env).querySelectorAll('label')).map((l) =>
      l.textContent!.trim(),
    );
    expect(labels).withContext('поля формы регистрации (SCR-002)').toEqual([
      'ФИО',
      'Логин',
      'Email',
      'Пароль',
      'Повторите пароль',
    ]);
    expect(root(env).querySelectorAll('select, input[type="radio"]').length)
      .withContext('выбора роли в UI нет')
      .toBe(0);
    expect(root(env).textContent).not.toContain('Подтвердит');

    // Отправка формы: DTO регистрации содержит ровно пять полей формы —
    // роль клиентом не передаётся (создаётся только student — зона бэкенда).
    setInput(env, '#full-name-input', 'Скоуп Скоупов');
    setInput(env, '#login-input', 'scopeuser');
    setInput(env, '#email-input', 'scopeuser@test.ru');
    setInput(env, '#password-input', 'Scope1234!');
    setInput(env, '#repeat-password-input', 'Scope1234!');
    submitButton(env).click();
    respond(env, 'POST', '/auth/register', { body: ME_FIXTURES.student01 });
    await settle(env);
    const registerCall = env.requests.find((r) => r.path === '/auth/register');
    expect(Object.keys(registerCall!.body as Record<string, unknown>).sort()).toEqual([
      'email',
      'fullName',
      'login',
      'password',
      'repeatPassword',
    ]);
    expect(env.auth.isAuthenticated()).withContext('автологин регистрации').toBeTrue();

    // /profile: редактируемы только ФИО и email; логин read-only; полей
    // роли/группы к редактированию нет. «F5»: сессия в памяти не переживает
    // рестарт — восстанавливается валидным /auth/me (cookie-семантика).
    env = await restartApp(env, { keepStorage: true, session: 'student01' });
    await env.harness.navigateByUrl('/profile');
    respond(env, 'GET', '/me/profile', {
      body: {
        login: 'student01',
        email: 'student01@example.com',
        fullName: 'Иванов Иван Иванович 01',
        role: 'student',
        groupName: 'ИК-221',
      },
    });
    await settle(env);

    // Ровно два редактируемых поля — в форме «Основные данные» (селектор
    // ограничен формой: поля «Смена пароля» — отдельная карточка, CR-011).
    const inputs = Array.from(
      required(env, 'form[data-test="profile-form"]').querySelectorAll<HTMLInputElement>('input'),
    );
    expect(inputs.map((input) => input.id).sort())
      .withContext('редактируемые поля профиля — только email и ФИО')
      .toEqual(['profile-email', 'profile-full-name']);
    const loginField = required(env, '[data-test="login-field"]');
    expect(loginField.querySelector('input, textarea'))
      .withContext('логин read-only')
      .toBeNull();
    expect(root(env).querySelector('[data-test="role-field"] input, [data-test="group-field"] input'))
      .withContext('роль и группа — не поля ввода')
      .toBeNull();
    expect(console.error).not.toHaveBeenCalled();
  }, 20000);
});
