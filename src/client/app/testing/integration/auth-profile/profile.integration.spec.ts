/**
 * Интеграционные тесты профиля (batch1, FR-4.7/QG-005, §4.7, IF-107/IF-109):
 * TS-025 просмотр обеих ролей; TS-026 редактирование с триммингом DTO и
 * персистентностью (значение «помнит» программируемый бэкенд сценария);
 * TS-027 занятый email (409; значения уходят как набрано — ci на бэкенде);
 * TS-028 неверный текущий пароль (400, ввод сохраняется); TS-029 приоритет
 * клиентской валидации; TS-030 смена пароля: сессия жива, старый пароль
 * недействителен; TS-031 идемпотентность сохранения; TS-032 валидации без
 * запроса; TS-033 единый тримминг паролей на всех auth-формах.
 *
 * Реальная страница /profile внутри оболочки, реальный домен Profile
 * (ProfileService поверх HttpClient + authInterceptor) на программируемом
 * HttpTestingController (FR-026); уведомления читаются через
 * NotificationService (дословные тексты), якоря форм — в мобильном режиме
 * MockBreakpointObserver. Персистентность изменений «на сервере» моделирует
 * мутируемая фикстура сценария: PUT-ответы и последующие GET возвращают
 * сохранённое состояние, тела PUT проверяются на границе HTTP.
 */
import { ProfileDto } from '../../../shared/models';

import {
  apiUrl,
  AuthProfileEnv,
  DemoLogin,
  ME_FIXTURES,
  PROFILE_FIXTURES,
  requestsOf,
  respond,
  required,
  restartApp,
  root,
  settle,
  setInput,
  startAuthProfileEnv,
  stopAuthProfileEnv,
  waitForUrl,
} from './auth-profile.env';

const STUDENT_LOGIN: DemoLogin = 'student01';
const STUDENT_PASSWORD = 'student123!';
const TEACHER_LOGIN: DemoLogin = 'teacher';

describe('Интеграция: профиль (FR-4.7, batch1 TS-025..TS-033)', () => {
  let env: AuthProfileEnv;
  /** Состояние профиля student01 «на бэкенде» (мутирует успешными PUT). */
  let storedStudent: ProfileDto;

  beforeEach(async () => {
    spyOn(console, 'error');
    storedStudent = { ...PROFILE_FIXTURES.student01 };
    env = await startAuthProfileEnv({ session: STUDENT_LOGIN });
  });

  afterEach(() => {
    stopAuthProfileEnv(env);
  });

  /** Открытие /profile авторизованной сессией с ответом GET профиля. */
  async function openProfileAs(login: DemoLogin): Promise<void> {
    await env.harness.navigateByUrl('/profile');
    const profile = login === STUDENT_LOGIN ? storedStudent : PROFILE_FIXTURES[TEACHER_LOGIN];
    respond(env, 'GET', '/me/profile', { body: profile });
    await settle(env);
  }

  function profileSave(): HTMLButtonElement {
    return required<HTMLButtonElement>(env, '[data-test="profile-submit"] button');
  }

  function passwordSave(): HTMLButtonElement {
    return required<HTMLButtonElement>(env, '[data-test="password-submit"] button');
  }

  function logoutClick(): void {
    required<HTMLButtonElement>(env, '.app-topbar__logout').click();
  }

  /** Текст баннера, смонтированного в якоре формы (мобильный режим). */
  function bannerIn(anchorSelector: string): string | null {
    const banner = root(env).querySelector(
      `${anchorSelector} app-notification-banner`,
    );
    return banner === null ? null : banner.textContent!.replace(/\s+/g, ' ').trim();
  }

  /** Тело последнего вызова path (граница HTTP). */
  function lastBody(path: string): unknown {
    const calls = env.requests.filter((r) => r.path === path);
    return calls[calls.length - 1].body;
  }

  /**
   * Сохранение профиля с ответом «бэкенда», вычисленным ИЗ тела PUT:
   * клик, захват единственного открытого PUT /me/profile (match()
   * потребляет запрос — тело читается с границы HTTP), журналирование и
   * ответ 200 значением buildResponse(putBody).
   */
  async function saveProfileResponding(
    buildResponse: (putBody: { fullName: string; email: string }) => ProfileDto,
  ): Promise<{ fullName: string; email: string }> {
    profileSave().click();
    const pending = env.httpMock.match({ method: 'PUT', url: apiUrl(env, '/me/profile') });
    expect(pending.length).withContext('ровно один PUT /me/profile').toBe(1);
    const putBody = pending[0].request.body as { fullName: string; email: string };
    env.requests.push({ method: 'PUT', path: '/me/profile', body: putBody });
    pending[0].flush(buildResponse(putBody), { status: 200, statusText: 'OK' });
    await settle(env);
    return putBody;
  }

  it('TS-025: просмотр профиля — обе роли', async () => {
    // Студент: логин read-only, роль по-русски, группа фикстуры.
    await openProfileAs(STUDENT_LOGIN);
    expect(env.router.url).toBe('/profile');

    const loginField = required(env, '[data-test="login-field"]');
    expect(loginField.querySelector('[data-test="login-value"]')!.textContent!.trim())
      .withContext('логин отображается')
      .toBe(STUDENT_LOGIN);
    expect(loginField.querySelector('input, textarea'))
      .withContext('логин недоступен вводу (read-only)')
      .toBeNull();
    expect(required(env, '[data-test="role-value"]').textContent!.trim()).toBe('Студент');
    expect(required(env, '[data-test="group-value"]').textContent!.trim()).toBe('ИК-221');
    expect(required<HTMLInputElement>(env, '#profile-email').value)
      .withContext('email учётки')
      .toBe('student01@example.com');
    expect(required<HTMLInputElement>(env, '#profile-full-name').value)
      .withContext('ФИО учётки')
      .toBe('Иванов Иван Иванович 01');

    // Преподаватель: роль «Преподаватель», группа отсутствует (F5-рестарт
    // с сессией teacher).
    env = await restartApp(env, { session: TEACHER_LOGIN });
    await openProfileAs(TEACHER_LOGIN);
    expect(required(env, '[data-test="login-value"]').textContent!.trim()).toBe(
      TEACHER_LOGIN,
    );
    expect(required(env, '[data-test="role-value"]').textContent!.trim()).toBe(
      'Преподаватель',
    );
    expect(root(env).querySelector('[data-test="group-field"]'))
      .withContext('у преподавателя группы нет')
      .toBeNull();
    expect(required<HTMLInputElement>(env, '#profile-email').value).toBe(
      'teacher@example.com',
    );
    expect(required<HTMLInputElement>(env, '#profile-full-name').value).toBe(
      'Сидоров Семён Семёнович',
    );
  }, 30000);

  it('TS-026: редактирование ФИО и email — «Сохранено», тримминг DTO, персистентность после F5', async () => {
    await openProfileAs(STUDENT_LOGIN);

    setInput(env, '#profile-email', '  petrov.petrov@example.ru ');
    setInput(env, '#profile-full-name', '  Петров Пётр Петрович  ');
    // «Бэкенд» применяет триммированные значения из PUT и отвечает ими.
    const putBody = await saveProfileResponding((body) => {
      storedStudent = { ...storedStudent, fullName: body.fullName, email: body.email };
      return storedStudent;
    });

    // Успех «Сохранено» без якоря формы: desktop — Toast (не mobileMessage).
    expect(env.notifications.desktopMessage()).toEqual(
      jasmine.objectContaining({ severity: 'success', text: 'Сохранено' }),
    );
    expect(env.notifications.mobileMessage()).withContext('успех без якоря формы').toBeNull();

    // Значения ушли в PUT триммированными; login/роль/группа не в DTO.
    expect(putBody).toEqual({
      fullName: 'Петров Пётр Петрович',
      email: 'petrov.petrov@example.ru',
    });

    // Мобильная ширина: успех фиксированно под шапкой (formId = null).
    env.notifications.dismissMobile();
    env.breakpoints.simulate(true);
    setInput(env, '#profile-email', '  petrov.petrov2@example.ru ');
    await saveProfileResponding((body) => {
      storedStudent = { ...storedStudent, email: body.email };
      return storedStudent;
    });
    expect(env.notifications.mobileMessage()).toEqual(
      jasmine.objectContaining({ severity: 'success', text: 'Сохранено', formId: null }),
    );
    env.breakpoints.simulate(false);
    env.notifications.dismissMobile();

    // Перезагрузка: значения видны (персистентность состояния «бэкенда»).
    env = await restartApp(env, { keepStorage: true, session: STUDENT_LOGIN });
    await openProfileAs(STUDENT_LOGIN);
    expect(required<HTMLInputElement>(env, '#profile-email').value).toBe(
      'petrov.petrov2@example.ru',
    );
    expect(required<HTMLInputElement>(env, '#profile-full-name').value).toBe(
      'Петров Пётр Петрович',
    );
  }, 30000);

  it('TS-027: занятый email в профиле — 409 с дословным текстом; свой email не конфликтует', async () => {
    await openProfileAs(STUDENT_LOGIN);
    env.breakpoints.simulate(true); // якорь ошибок — под формой «Основные данные»

    // Чужой email в другом регистре: значение уходит как набрано (ci —
    // зона бэкенда), ответ 409 — под своей формой.
    setInput(env, '#profile-email', 'TEACHER@EXAMPLE.COM');
    profileSave().click();
    respond(env, 'PUT', '/me/profile', {
      status: 409,
      body: { message: 'Пользователь с таким email уже существует' },
    });
    await settle(env);
    expect(lastBody('/me/profile'))
      .withContext('email уходит без изменения регистра')
      .toEqual({ fullName: 'Иванов Иван Иванович 01', email: 'TEACHER@EXAMPLE.COM' });
    expect(env.notifications.mobileMessage()?.text)
      .withContext('баннер дословно')
      .toBe('Пользователь с таким email уже существует');
    expect(env.notifications.mobileMessage()?.formId).withContext('якорь формы').toBe('profile-form');
    expect(bannerIn('[data-test="profile-anchor"]')).toBe('Пользователь с таким email уже существует');
    // Отказ PUT кэш формы не обновляет: значение email осталось введённым.
    expect(required<HTMLInputElement>(env, '#profile-email').value).toBe('TEACHER@EXAMPLE.COM');
    env.notifications.dismissMobile();

    // Собственный текущий email — сохранение успешно (сам исключён из проверки).
    setInput(env, '#profile-email', 'student01@example.com');
    profileSave().click();
    respond(env, 'PUT', '/me/profile', { body: storedStudent });
    await settle(env);
    expect(env.notifications.mobileMessage()).toEqual(
      jasmine.objectContaining({ severity: 'success', text: 'Сохранено' }),
    );
    env.notifications.dismissMobile();
  }, 20000);

  it('TS-028: смена пароля с неверным текущим — 400 и сохранение ввода', async () => {
    await openProfileAs(STUDENT_LOGIN);
    env.breakpoints.simulate(true);

    setInput(env, '#profile-current-password', 'WrongPass999!');
    setInput(env, '#profile-new-password', 'NewPass1!');
    setInput(env, '#profile-repeat-password', 'NewPass1!');
    passwordSave().click();
    respond(env, 'PUT', '/me/password', {
      status: 400,
      body: { message: 'Неверный текущий пароль' },
    });
    await settle(env);

    expect(env.notifications.mobileMessage()?.text)
      .withContext('400 дословно')
      .toBe('Неверный текущий пароль');
    expect(env.notifications.mobileMessage()?.formId)
      .withContext('баннер под формой «Смена пароля»')
      .toBe('password-form');
    expect(bannerIn('[data-test="password-anchor"]')).toBe('Неверный текущий пароль');

    // Все три поля сохраняют введённое (сброс только при успехе).
    expect(required<HTMLInputElement>(env, '#profile-current-password').value).toBe(
      'WrongPass999!',
    );
    expect(required<HTMLInputElement>(env, '#profile-new-password').value).toBe(
      'NewPass1!',
    );
    expect(required<HTMLInputElement>(env, '#profile-repeat-password').value).toBe(
      'NewPass1!',
    );
    env.notifications.dismissMobile();
  }, 20000);

  it('TS-029: приоритет валидаций при смене пароля — клиент раньше запроса', async () => {
    await openProfileAs(STUDENT_LOGIN);

    // Слабый новый + неверный текущий: клиентская валидация, запроса нет.
    setInput(env, '#profile-current-password', 'WrongPass1!');
    setInput(env, '#profile-new-password', 'abc');
    setInput(env, '#profile-repeat-password', 'abc');
    passwordSave().click();
    await settle(env);
    expect(env.notifications.desktopMessage()?.text).toBe('Данные заполнены неверно');
    expect(required(env, '[data-test="new-password-error"]').textContent!.trim()).toBe(
      'Пароль должен содержать не менее 8 символов',
    );
    expect(requestsOf(env, 'PUT', '/me/password'))
      .withContext('запрос changePassword НЕ отправлен')
      .toBe(0);
    env.notifications.dismissMobile();

    // Валидный новый + неверный текущий: запрос отправлен — 400 дословно.
    setInput(env, '#profile-new-password', 'NewPass1!');
    setInput(env, '#profile-repeat-password', 'NewPass1!');
    passwordSave().click();
    respond(env, 'PUT', '/me/password', {
      status: 400,
      body: { message: 'Неверный текущий пароль' },
    });
    await settle(env);
    expect(requestsOf(env, 'PUT', '/me/password'))
      .withContext('запрос отправлен')
      .toBe(1);
    expect(env.notifications.desktopMessage()?.text).toBe('Неверный текущий пароль');
    env.notifications.dismissMobile();
  }, 20000);

  it('TS-030: после смены пароля сессия жива, старый пароль недействителен', async () => {
    await openProfileAs(STUDENT_LOGIN);

    setInput(env, '#profile-current-password', STUDENT_PASSWORD);
    setInput(env, '#profile-new-password', 'NewPass1!');
    setInput(env, '#profile-repeat-password', 'NewPass1!');
    passwordSave().click();
    respond(env, 'PUT', '/me/password', { status: 204 });
    await settle(env);

    expect(env.notifications.desktopMessage()).toEqual(
      jasmine.objectContaining({ severity: 'success', text: 'Пароль изменён' }),
    );
    env.notifications.dismissMobile();
    expect(required<HTMLInputElement>(env, '#profile-current-password').value)
      .withContext('поля пароля очищены при успехе')
      .toBe('');
    expect(required<HTMLInputElement>(env, '#profile-new-password').value).toBe('');

    // Текущая сессия сохранена — повторный вход не требуется (память AuthService).
    expect(env.auth.isAuthenticated()).withContext('сессия жива').toBeTrue();
    // Повторная навигация на ТЕКУЩИЙ URL игнорируется роутером (новой
    // активации страницы и GET профиля не будет), поэтому живую сессию
    // доказываем уходом на домашний маршрут роли (страница на заглушках
    // сервисов — без HTTP) и возвратом с новой активацией /profile.
    await env.harness.navigateByUrl('/my-submissions');
    await env.harness.navigateByUrl('/profile');
    respond(env, 'GET', '/me/profile', { body: storedStudent });
    await settle(env);
    expect(env.router.url).withContext('профиль доступен без повторного входа').toBe('/profile');

    // Выход: старый пароль больше не работает, новый — работает.
    logoutClick();
    respond(env, 'POST', '/auth/logout', { status: 204 });
    await waitForUrl(env, '/login');
    setInput(env, '#login-input', STUDENT_LOGIN);
    setInput(env, '#password-input', STUDENT_PASSWORD);
    required<HTMLButtonElement>(env, 'button[type="submit"]').click();
    respond(env, 'POST', '/auth/login', {
      status: 401,
      body: { message: 'Неверный логин или пароль' },
    });
    await settle(env);
    expect(env.notifications.desktopMessage()?.text).toBe('Неверный логин или пароль');
    env.notifications.dismissMobile();

    setInput(env, '#password-input', 'NewPass1!');
    required<HTMLButtonElement>(env, 'button[type="submit"]').click();
    respond(env, 'POST', '/auth/login', { body: ME_FIXTURES.student01 });
    await waitForUrl(env, '/my-submissions');
    expect(env.router.url).withContext('вход с новым паролем успешен').toBe('/my-submissions');
  }, 30000);

  it('TS-031: повторное сохранение профиля идемпотентно, роль/группа неизменны', async () => {
    await openProfileAs(STUDENT_LOGIN);

    for (let attempt = 1; attempt <= 2; attempt++) {
      setInput(env, '#profile-full-name', 'Иванов Иван Иванович 01');
      setInput(env, '#profile-email', 'student01@example.com');
      profileSave().click();
      respond(env, 'PUT', '/me/profile', { body: storedStudent });
      await settle(env);
      expect(env.notifications.desktopMessage())
        .withContext(`сохранение ${attempt} успешно (свой email не даёт 409)`)
        .toEqual(jasmine.objectContaining({ severity: 'success', text: 'Сохранено' }));
      env.notifications.dismissMobile();
      // DTO сохранения — только fullName/email: роль/группа клиентом не меняются.
      expect(lastBody('/me/profile')).toEqual({
        fullName: 'Иванов Иван Иванович 01',
        email: 'student01@example.com',
      });
    }

    // Отображение после сохранений: роль и группа без изменений.
    expect(required(env, '[data-test="role-value"]').textContent!.trim()).toBe('Студент');
    expect(required(env, '[data-test="group-value"]').textContent!.trim()).toBe('ИК-221');
  }, 20000);

  it('TS-032: валидации email/ФИО в профиле без отправки запроса', async () => {
    await openProfileAs(STUDENT_LOGIN);

    setInput(env, '#profile-email', 'abc');
    profileSave().click();
    await settle(env);
    expect(required(env, '[data-test="email-error"]').textContent!.trim()).toBe(
      'Введите корректный email',
    );
    env.notifications.dismissMobile();

    setInput(env, '#profile-email', 'student01@example.com');
    setInput(env, '#profile-full-name', '');
    profileSave().click();
    await settle(env);
    expect(required(env, '[data-test="full-name-error"]').textContent!.trim()).toBe(
      'Заполните поле',
    );
    env.notifications.dismissMobile();

    setInput(env, '#profile-full-name', '   ');
    profileSave().click();
    await settle(env);
    expect(required(env, '[data-test="full-name-error"]').textContent!.trim()).toBe(
      'Заполните поле',
    );

    expect(requestsOf(env, 'PUT', '/me/profile'))
      .withContext('profile.update не вызван ни разу')
      .toBe(0);
    expect(
      env.httpMock.match({ method: 'PUT', url: `${env.apiBase}/me/profile` }).length,
    ).withContext('незапрограммированных запросов нет')
      .toBe(0);
  }, 20000);

  it('TS-033: единый тримминг паролей на всех auth-формах', async () => {
    const loginSubmit = (): HTMLButtonElement =>
      required<HTMLButtonElement>(env, 'button[type="submit"]');

    // (1) Вход: пароль с крайними пробелами.
    env = await restartApp(env, { session: null });
    await env.harness.navigateByUrl('/login');
    await settle(env);
    setInput(env, '#login-input', '  student01  ');
    setInput(env, '#password-input', '  student123!  ');
    loginSubmit().click();
    respond(env, 'POST', '/auth/login', { body: ME_FIXTURES.student01 });
    await waitForUrl(env, '/my-submissions');
    expect(lastBody('/auth/login'))
      .withContext('вход: значения DTO триммированы')
      .toEqual({ login: 'student01', password: 'student123!' });

    // (2) «Смена пароля»: текущий и новый с крайними пробелами.
    await env.harness.navigateByUrl('/profile');
    respond(env, 'GET', '/me/profile', { body: storedStudent });
    await settle(env);
    setInput(env, '#profile-current-password', '  student123!  ');
    setInput(env, '#profile-new-password', '  Change1!x  ');
    setInput(env, '#profile-repeat-password', '  Change1!x  ');
    passwordSave().click();
    respond(env, 'PUT', '/me/password', { status: 204 });
    await settle(env);
    expect(env.notifications.desktopMessage())
      .withContext('смена успешна')
      .toEqual(jasmine.objectContaining({ text: 'Пароль изменён' }));
    expect(lastBody('/me/password')).toEqual({
      currentPassword: 'student123!',
      password: 'Change1!x',
      confirmPassword: 'Change1!x',
    });
    env.notifications.dismissMobile();

    logoutClick();
    respond(env, 'POST', '/auth/logout', { status: 204 });
    await waitForUrl(env, '/login');

    // (3) /reset-password: новый пароль с крайними пробелами.
    await env.harness.navigateByUrl('/recovery');
    await settle(env);
    setInput(env, '#email-input', 'student01@example.com');
    Array.from(root(env).querySelectorAll<HTMLButtonElement>('button'))
      .find((b) => (b.textContent ?? '').trim() === 'Отправить код')!
      .click();
    respond(env, 'POST', '/auth/recovery/request', { body: null });
    await waitForUrl(env, '/recovery/code');
    setInput(env, '#code-input', '123456');
    Array.from(root(env).querySelectorAll<HTMLButtonElement>('button'))
      .find((b) => (b.textContent ?? '').trim() === 'Ввести код')!
      .click();
    respond(env, 'POST', '/auth/recovery/confirm', { body: { resetToken: 'reset-token-1' } });
    await waitForUrl(env, '/reset-password');
    setInput(env, '#password-input', '  NewPass1!  ');
    setInput(env, '#repeat-password-input', '  NewPass1!  ');
    Array.from(root(env).querySelectorAll<HTMLButtonElement>('button'))
      .find((b) => (b.textContent ?? '').trim() === 'Сменить пароль')!
      .click();
    respond(env, 'POST', '/auth/reset-password', { status: 204 });
    await waitForUrl(env, '/login');
    expect(lastBody('/auth/reset-password'))
      .withContext('сброс: пароль в DTO триммирован')
      .toEqual(
        jasmine.objectContaining({ password: 'NewPass1!', confirmPassword: 'NewPass1!' }),
      );

    // (4) Вход новым паролем с пробелами — согласовано со значением DTO.
    setInput(env, '#login-input', '  student01  ');
    setInput(env, '#password-input', '  NewPass1!  ');
    loginSubmit().click();
    respond(env, 'POST', '/auth/login', { body: ME_FIXTURES.student01 });
    await waitForUrl(env, '/my-submissions');

    // (5) Регистрация с паролем с пробелами; вход нового пользователя с пробелами.
    logoutClick();
    respond(env, 'POST', '/auth/logout', { status: 204 });
    await waitForUrl(env, '/login');
    await env.harness.navigateByUrl('/register');
    await settle(env);
    setInput(env, '#full-name-input', 'Тримм Триммов');
    setInput(env, '#login-input', 'trimuser');
    setInput(env, '#email-input', 'trimuser@test.ru');
    setInput(env, '#password-input', '  Abcdef1!  ');
    setInput(env, '#repeat-password-input', '  Abcdef1!  ');
    loginSubmit().click();
    respond(env, 'POST', '/auth/register', { body: ME_FIXTURES.student01 });
    await waitForUrl(env, '/my-submissions');
    expect(lastBody('/auth/register'))
      .withContext('в DTO уходит триммированный пароль')
      .toEqual(jasmine.objectContaining({ password: 'Abcdef1!', repeatPassword: 'Abcdef1!' }));

    logoutClick();
    respond(env, 'POST', '/auth/logout', { status: 204 });
    await waitForUrl(env, '/login');
    setInput(env, '#login-input', '  trimuser  ');
    setInput(env, '#password-input', '  Abcdef1!  ');
    loginSubmit().click();
    respond(env, 'POST', '/auth/login', { body: ME_FIXTURES.student01 });
    await waitForUrl(env, '/my-submissions');
    expect(requestsOf(env, 'POST', '/auth/login'))
      .withContext('все входы через единый HTTP-стек')
      .toBe(3);
  }, 30000);
});
