/**
 * Интеграционный тест границ области v1 (batch1, FR-4.1/FR-4.7, §4.1/§4.7):
 * TS-040 out-of-scope возможности отсутствуют — в UI нет выбора роли и шага
 * подтверждения email; переданное в auth.register поле role игнорируется
 * (создаётся только student); профиль не содержит редактируемых полей
 * роли/группы/логина (логин read-only).
 *
 * Механизм уведомлений (FR-4.9) и адаптивность (FR-4.10) — зоны других
 * батчей и визуальных сценариев VS-*, здесь не проверяются.
 */
import {
  AuthProfileEnv,
  flushMock,
  mockDbData,
  required,
  restartApp,
  root,
  sessionUserId,
  startAuthProfileEnv,
  stopAuthProfileEnv,
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

    // Прямой вызов auth.register с полем role='teacher': поле игнорируется.
    const me = await env.client.call<{ role: string }>('auth.register', {
      fullName: 'Скоуп Скоупов',
      login: 'scopeuser',
      email: 'scopeuser@test.ru',
      password: 'Scope1234!',
      repeatPassword: 'Scope1234!',
      role: 'teacher',
    });
    expect(me.role).withContext('ответ — student').toBe('student');
    expect(mockDbData().users.find((u) => u.login === 'scopeuser')!.role)
      .withContext('создаётся только student')
      .toBe('student');

    // /profile: редактируемы только ФИО и email; логин read-only; полей
    // роли/группы к редактированию нет. «F5»: id созданного пользователя
    // берётся из mock.db.v1 ДО рестарта (в статическом сиде его нет), сессия
    // уже поставлена автологином регистрации — keepStorage: true.
    const scopeUserId = mockDbData().users.find((u) => u.login === 'scopeuser')!.id;
    env = await restartApp(env, { keepStorage: true });
    expect(sessionUserId()).withContext('сессия autologin пережила рестарт').toBe(scopeUserId);
    await env.harness.navigateByUrl('/profile');
    await flushMock(env, 1);

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
  }, 20000);
});
