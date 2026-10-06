/**
 * Интеграционный тест вкладок шапки (batch1, IF-108/FR-4.1, §4.8, IF-111):
 * TS-038 набор вкладок появляется по роли при входе, меняется сразу после
 * входа/выхода.
 *
 * Наборы роли — ровно по IF-111 (§4.8): преподаватель «Работы», «Сдача
 * работ», «Группы», «Доступ», «Профиль»; студент «Сдача работ», «Профиль».
 * Для гостя §4.8 («Неавторизованный: Вход, Регистрация, Восстановление
 * пароля») реализовано экранами-карточками SCR-001..SCR-003: у гостя нет
 * топбара с вкладками, а навигацию образуют экраны с заголовками «Вход»,
 * «Регистрация», «Восстановление пароля» и ссылки карточки входа —
 * интерпретация по макетам и IF-111 (набор NAV_ITEMS определён только для
 * ролей); визуальная сторона — VS-006 (ручная зона).
 */
import {
  AuthProfileEnv,
  flushMock,
  required,
  root,
  setInput,
  shellTabs,
  startAuthProfileEnv,
  stopAuthProfileEnv,
  submitButton,
} from './auth-profile.env';

describe('Интеграция: вкладки шапки по ролям (IF-111, batch1 TS-038)', () => {
  let env: AuthProfileEnv;

  beforeEach(async () => {
    spyOn(console, 'error');
    env = await startAuthProfileEnv({ initialUrl: '/' });
  });

  afterEach(() => {
    stopAuthProfileEnv(env);
  });

  function loginViaForm(login: string, password: string): void {
    setInput(env, '#login-input', login);
    setInput(env, '#password-input', password);
    submitButton(env).click();
  }

  it('TS-038: набор вкладок шапки появляется по роли при входе', async () => {
    // Гость: топбарных вкладок нет; навигация — экраны «Вход»,
    // «Регистрация», «Восстановление пароля».
    expect(env.router.url).withContext('гость на корне — экран входа').toBe('/login');
    expect(shellTabs(env).length).withContext('у гостя нет вкладок шапки').toBe(0);
    expect(required(env, 'h1').textContent!.trim()).toBe('Вход');
    await env.harness.navigateByUrl('/register');
    expect(required(env, 'h1').textContent!.trim()).toBe('Регистрация');
    await env.harness.navigateByUrl('/recovery');
    expect(required(env, 'h1').textContent!.trim()).toBe('Восстановление пароля');

    // Вход студентом: вкладки меняются сразу, без перезагрузки.
    await env.harness.navigateByUrl('/login');
    env.harness.fixture.detectChanges();
    loginViaForm('student01', 'student123!');
    await flushMock(env, 1);
    expect(env.router.url).toBe('/my-submissions');
    expect(shellTabs(env).map((tab) => tab.textContent!.trim())).toEqual([
      'Сдача работ',
      'Профиль',
    ]);
    expect(shellTabs(env).map((tab) => tab.getAttribute('href'))).toEqual([
      '/my-submissions',
      '/profile',
    ]);
    expect(required(env, '.app-topbar__who').textContent!.trim())
      .withContext('ФИО в шапке')
      .toBe('Иванов Иван Иванович 01');

    // Выход: вкладки исчезают сразу, гость снова на экране входа.
    required<HTMLButtonElement>(env, '.app-topbar__logout').click();
    await flushMock(env, 1);
    expect(env.router.url).toBe('/login');
    expect(shellTabs(env).length).withContext('после выхода вкладок нет').toBe(0);

    // Вход преподавателем: ровно 5 вкладок по IF-111.
    loginViaForm('teacher', 'teacher123!');
    await flushMock(env, 1);
    expect(env.router.url).toBe('/works');
    expect(shellTabs(env).map((tab) => tab.textContent!.trim())).toEqual([
      'Работы',
      'Сдача работ',
      'Группы',
      'Доступ',
      'Профиль',
    ]);
    expect(shellTabs(env).map((tab) => tab.getAttribute('href'))).toEqual([
      '/works',
      '/submissions',
      '/groups',
      '/access',
      '/profile',
    ]);
  }, 30000);
});
