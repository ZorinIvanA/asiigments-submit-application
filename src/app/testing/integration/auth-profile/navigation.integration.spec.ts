/**
 * Интеграционные тесты навигации auth-семейства (batch1, IF-108/FR-4.1,
 * §4.8): TS-034 guestGuard уводит авторизованных со всех auth-маршрутов;
 * TS-035 homeGuard на '' и '**'; TS-036 recoveryStepGuard на deep-link без
 * данных потока; TS-037 битая сессия трактуется как гость; TS-039 сессия
 * переживает F5 (перезагрузка страницы).
 *
 * Полное скомпонованное дерево маршрутов (app.routes) с реальными guards;
 * сессия — реальный ключ mock.session.userId; «F5» — перезапуск приложения
 * (новый инжектор с fresh root-синглтонами) при сохранённом storage.
 */
import {
  AuthProfileEnv,
  flushMock,
  required,
  restartApp,
  root,
  sessionUserId,
  startAuthProfileEnv,
  stopAuthProfileEnv,
} from './auth-profile.env';
import { loginAs, seedUserIdByLogin } from '../../integration-env';

const AUTH_ROUTES = ['/login', '/register', '/recovery', '/recovery/code', '/reset-password'];

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
    const cases: Array<{ login: string; home: string }> = [
      { login: 'student01', home: '/my-submissions' },
      { login: 'teacher', home: '/works' },
    ];

    for (const { login, home } of cases) {
      env = await restartApp(env, { keepStorage: false });
      loginAs(seedUserIdByLogin(login));
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
    env = await restartApp(env, { keepStorage: false });
    loginAs(seedUserIdByLogin('student01'));
    await env.harness.navigateByUrl('/');
    expect(env.router.url).withContext('student на корне').toBe('/my-submissions');
    await env.harness.navigateByUrl('/some/unknown/page');
    expect(env.router.url).withContext('student на **').toBe('/my-submissions');

    // Преподаватель → /works.
    env = await restartApp(env, { keepStorage: false });
    loginAs(seedUserIdByLogin('teacher'));
    await env.harness.navigateByUrl('/');
    expect(env.router.url).withContext('teacher на корне').toBe('/works');
    await env.harness.navigateByUrl('/some/unknown/page');
    expect(env.router.url).withContext('teacher на **').toBe('/works');
    expect(console.error).not.toHaveBeenCalled();
  }, 30000);

  it('TS-036: recoveryStepGuard на deep-link без данных потока — безопасный рестарт', async () => {
    const callSpy = spyOn(env.client, 'call').and.callThrough();

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

    // Страницы не шлют запросов с пустыми значениями; ошибок консоли нет.
    expect(
      callSpy.calls.all().filter((c) => String(c.args[0]).startsWith('auth.recovery')).length,
    ).withContext('recovery-запросов нет').toBe(0);
    expect(console.error).not.toHaveBeenCalled();
  }, 20000);

  it('TS-037: битая сессия (несуществующий/пустой userId) трактуется как гость', async () => {
    // userId, которого нет в mock.db.v1.
    loginAs('ffffffff-ffff-4fff-8fff-ffffffffffff');
    await env.harness.navigateByUrl('/profile');
    expect(env.router.url)
      .withContext('после 401 me — редирект /login')
      .toBe('/login');
    expect(sessionUserId()).withContext('битая сессия очищена').toBeNull();
    expect(env.auth.currentUser()).toBeNull();

    // Дальнейшая навигация ведёт себя как гостевая.
    await env.harness.navigateByUrl('/register');
    expect(env.router.url).withContext('auth-страницы доступны гостю').toBe('/register');
    await env.harness.navigateByUrl('/profile');
    expect(env.router.url).withContext('защищённые — редирект').toBe('/login');

    // Отдельный случай: пустая строка как userId сессии.
    env = await restartApp(env, { keepStorage: false });
    loginAs('');
    await env.harness.navigateByUrl('/profile');
    expect(env.router.url).withContext('пустой userId — тоже битая сессия').toBe('/login');
    expect(sessionUserId()).toBeNull();
    expect(console.error).not.toHaveBeenCalled();
  }, 20000);

  it('TS-039: сессия переживает перезагрузку страницы (F5)', async () => {
    loginAs(seedUserIdByLogin('student01'));
    const callSpy = spyOn(env.client, 'call').and.callThrough();
    const meCalls = (): number =>
      callSpy.calls.all().filter((c) => c.args[0] === 'auth.me').length;

    // Первый переход после F5 догружает me — один мок-вызов. Гарантия
    // длительности мока [500; 800] мс (NFR-§10.1) проверяется юнит-тестами
    // MockApiClient; здесь — верхняя граница с запасом на навигацию и CD
    // (ревью CR-012: 800 мс без запаса флакало).
    const startedAt = Date.now();
    await env.harness.navigateByUrl('/profile');
    const elapsedMs = Date.now() - startedAt;
    expect(env.router.url).withContext('без редиректа на /login').toBe('/profile');
    expect(meCalls()).withContext('ровно один вызов me').toBe(1);
    expect(elapsedMs)
      .withContext('первый переход — мок-вызов [500; 800] мс + запас на навигацию')
      .toBeLessThan(2000);

    await flushMock(env, 1);
    expect(required(env, '[data-test="login-value"]').textContent!.trim())
      .withContext('профиль отображает корректные данные')
      .toBe('student01');

    // Повторные переходы мгновенны — без вызовов me.
    const meAfterFirst = meCalls();
    await env.harness.navigateByUrl('/my-submissions');
    await env.harness.navigateByUrl('/profile');
    expect(meCalls()).withContext('повторные переходы без вызовов me').toBe(meAfterFirst);

    // Перезагрузка страницы (новый инжектор, storage сохранён).
    env = await restartApp(env, { keepStorage: true });
    expect(sessionUserId()).withContext('сессия восстановлена из localStorage').not.toBeNull();

    const freshSpy = spyOn(env.client, 'call').and.callThrough();
    await env.harness.navigateByUrl('/profile');
    expect(env.router.url).withContext('после F5 — по-прежнему /profile').toBe('/profile');
    await flushMock(env, 1); // profile.get после догрузки me (ревью CR-009)
    expect(
      freshSpy.calls.all().filter((c) => c.args[0] === 'auth.me').length,
    ).withContext('новое приложение догружает me одним вызовом').toBe(1);
    expect(required(env, '[data-test="login-value"]').textContent!.trim()).toBe(
      'student01',
    );
    expect(console.error).not.toHaveBeenCalled();
  }, 30000);
});
