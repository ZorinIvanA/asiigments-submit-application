/**
 * Интеграционные тесты навигации auth-семейства (batch1, IF-108/FR-4.1,
 * §4.8): TS-034 guestGuard уводит авторизованных со всех auth-маршрутов;
 * TS-035 homeGuard на '' и '**'; TS-036 recoveryStepGuard на deep-link без
 * данных потока; TS-037 холодный старт без сессии (401 от me + неудачный
 * refresh — HTTP-аналог «битой сессии» прошлого контракта) трактуется как
 * гость; TS-039 сессия переживает F5 — рестарт приложения повторно
 * валидирует /auth/me (cookie-семантика на границе HTTP).
 *
 * Полное скомпонованное дерево маршрутов (app.routes) с реальными guards;
 * сессия — память AuthService (FR-092), заполняется инициализацией
 * /auth/me; «F5» — перезапуск приложения (новый инжектор с fresh
 * root-синглтонами) при сохранённом storage.
 */
import {
  AuthProfileEnv,
  requestsOf,
  required,
  restartApp,
  respond,
  settle,
  startAuthProfileEnv,
  stopAuthProfileEnv,
} from './auth-profile.env';

const AUTH_ROUTES = ['/login', '/register', '/recovery', '/recovery/code', '/reset-password'];

/** Ответ GET /me/profile для сценариев с заходом на /profile. */
const STUDENT_PROFILE = {
  login: 'student01',
  email: 'student01@example.com',
  fullName: 'Иванов Иван Иванович 01',
  role: 'student',
  groupName: 'ИК-221',
};

describe('Интеграция: навигация auth-семейства (IF-108, batch1 TS-034..TS-037, TS-039)', () => {
  let env: AuthProfileEnv;

  beforeEach(async () => {
    spyOn(console, 'error');
    env = await startAuthProfileEnv();
  });

  afterEach(() => {
    stopAuthProfileEnv(env);
  });

  it('TS-034: guestGuard уводит авторизованных со всех auth-маршрутов на домашний маршрут роли', async () => {
    const cases = [
      { login: 'student01' as const, home: '/my-submissions' },
      { login: 'teacher' as const, home: '/works' },
    ];

    for (const { login, home } of cases) {
      // F5-рестарт с валидной cookie-сессией: /auth/me → 200.
      env = await restartApp(env, { keepStorage: false, session: login });
      expect(env.auth.isAuthenticated()).toBeTrue();
      for (const route of AUTH_ROUTES) {
        await env.harness.navigateByUrl(route);
        expect(env.router.url)
          .withContext(`${login}: ${route} → домашний маршрут роли`)
          .toBe(home);
      }
    }
    expect(console.error).withContext('ошибок консоли нет').not.toHaveBeenCalled();
  }, 30000);

  it('TS-035: homeGuard на \'\' и \'**\' по наличию сессии и роли', async () => {
    // Гость → /login.
    await env.harness.navigateByUrl('/');
    expect(env.router.url).withContext('гость на корне').toBe('/login');
    await env.harness.navigateByUrl('/some/unknown/page');
    expect(env.router.url).withContext('гость на несуществующем маршруте').toBe('/login');

    // Студент → /my-submissions.
    env = await restartApp(env, { keepStorage: false, session: 'student01' });
    await env.harness.navigateByUrl('/');
    expect(env.router.url).withContext('student на корне').toBe('/my-submissions');
    await env.harness.navigateByUrl('/some/unknown/page');
    expect(env.router.url).withContext('student на **').toBe('/my-submissions');

    // Преподаватель → /works.
    env = await restartApp(env, { keepStorage: false, session: 'teacher' });
    await env.harness.navigateByUrl('/');
    expect(env.router.url).withContext('teacher на корне').toBe('/works');
    await env.harness.navigateByUrl('/some/unknown/page');
    expect(env.router.url).withContext('teacher на **').toBe('/works');
    expect(console.error).not.toHaveBeenCalled();
  }, 30000);

  it('TS-036: recoveryStepGuard на deep-link без данных потока — безопасный рестарт', async () => {
    // Пустой store: оба шага → /recovery.
    await env.harness.navigateByUrl('/recovery/code');
    expect(env.router.url).withContext('/recovery/code без email → /recovery').toBe('/recovery');
    await env.harness.navigateByUrl('/reset-password');
    expect(env.router.url).withContext('/reset-password без resetToken → /recovery').toBe('/recovery');

    // Store только с email: код открыт, /reset-password → безопасный рестарт.
    env.flow.setEmail('student01@example.com');
    await env.harness.navigateByUrl('/recovery/code');
    expect(env.router.url).withContext('с email шаг кода разрешён').toBe('/recovery/code');
    await env.harness.navigateByUrl('/reset-password');
    expect(env.router.url).withContext('с email без resetToken → /recovery').toBe('/recovery');

    // Страницы не шлют recovery-запросов с пустыми значениями (журнал HTTP).
    expect(requestsOf(env, 'POST', '/auth/recovery/request'))
      .withContext('запросов кода нет')
      .toBe(0);
    expect(requestsOf(env, 'POST', '/auth/recovery/confirm'))
      .withContext('подтверждений нет')
      .toBe(0);
    expect(console.error).not.toHaveBeenCalled();
  }, 20000);

  it('TS-037: холодный старт без сессии (401 me + неудачный refresh) трактуется как гость', async () => {
    // Гостевой старт уже выполнен в beforeEach: /auth/me → 401, refresh → 401.
    expect(env.auth.currentUser()).withContext('аноним').toBeNull();
    expect(env.auth.isAuthenticated()).toBeFalse();
    expect(requestsOf(env, 'POST', '/auth/refresh'))
      .withContext('тихий refresh выполнен ровно один раз')
      .toBe(1);

    // /profile закрыт; guard решает по пустому кэшу, без HTTP.
    await env.harness.navigateByUrl('/profile');
    expect(env.router.url).withContext('аноним на /profile → редирект /login').toBe('/login');
    expect(requestsOf(env, 'POST', '/auth/refresh'))
      .withContext('новых refresh нет (фиксатор неудачного refresh)')
      .toBe(1);

    // Дальнейшая навигация ведёт себя как гостевая.
    await env.harness.navigateByUrl('/register');
    expect(env.router.url).withContext('auth-страницы доступны гостю').toBe('/register');
    await env.harness.navigateByUrl('/profile');
    expect(env.router.url).withContext('защищённые — редирект').toBe('/login');
    expect(console.error).not.toHaveBeenCalled();
  }, 20000);

  it('TS-039: сессия переживает перезагрузку страницы (F5) — повторная валидация /auth/me', async () => {
    // F5-рестарт с валидной cookie: cold-start me → 200, начальная навигация /profile.
    env = await restartApp(env, { keepStorage: true, session: 'student01' });
    expect(requestsOf(env, 'GET', '/auth/me'))
      .withContext('новое приложение валидирует me одним вызовом')
      .toBe(1);

    // Первый переход догружает профиль страницы.
    await env.harness.navigateByUrl('/profile');
    respond(env, 'GET', '/me/profile', { body: STUDENT_PROFILE });
    await settle(env);
    expect(env.router.url).withContext('без редиректа на /login').toBe('/profile');
    expect(required(env, '[data-test="login-value"]').textContent!.trim())
      .withContext('профиль отображает корректные данные')
      .toBe('student01');

    // Повторные переходы мгновенны — без повторных вызовов me (кэш в памяти).
    // Возврат на /profile — новая активация страницы: она снова запрашивает
    // GET /me/profile, сценарий программирует ответ (иначе запрос останется
    // открытым и будет пойман verify() в afterEach).
    await env.harness.navigateByUrl('/my-submissions');
    await env.harness.navigateByUrl('/profile');
    respond(env, 'GET', '/me/profile', { body: STUDENT_PROFILE });
    await settle(env);
    expect(requestsOf(env, 'GET', '/auth/me'))
      .withContext('повторные переходы без вызовов me')
      .toBe(1);
    expect(console.error).not.toHaveBeenCalled();
  }, 30000);
});
