/**
 * Интеграционные тесты профиля (batch1, FR-4.7/QG-005, §4.7, IF-107/IF-109):
 * TS-025 просмотр обеих ролей; TS-026 редактирование с триммингом и
 * персистентностью; TS-027 занятый email (409 ci, свой не конфликтует);
 * TS-028 неверный текущий пароль (400, ввод сохраняется); TS-029 приоритет
 * клиентской валидации; TS-030 смена пароля: сессия жива, старый пароль
 * недействителен; TS-031 идемпотентность сохранения; TS-032 валидации без
 * запроса; TS-033 единый тримминг паролей на всех auth-формах.
 *
 * Реальная страница /profile внутри оболочки, реальный домен Profile
 * (мок-обработчики + сервис) на общем MockApiClient; уведомления читаются
 * через NotificationService (дословные тексты), якоря форм — в мобильном
 * режиме MockBreakpointObserver.
 */
import {
  AuthProfileEnv,
  flushMock,
  required,
  mockDbData,
  restartApp,
  root,
  setInput,
  startAuthProfileEnv,
  stopAuthProfileEnv,
} from './auth-profile.env';
import { loginAs, seedUserIdByLogin } from '../../integration-env';

const STUDENT_LOGIN = 'student01';
const STUDENT_PASSWORD = 'student123!';
const TEACHER_LOGIN = 'teacher';

describe('Интеграция: профиль (FR-4.7, batch1 TS-025..TS-033)', () => {
  let env: AuthProfileEnv;

  beforeEach(async () => {
    spyOn(console, 'error');
    env = await startAuthProfileEnv();
  });

  afterEach(() => {
    stopAuthProfileEnv(env);
  });

  /** Вход «как пользователь существующей сессии» и открытие /profile. */
  async function openProfileAs(login: string): Promise<void> {
    loginAs(seedUserIdByLogin(login));
    await env.harness.navigateByUrl('/profile');
    await flushMock(env, 1); // loadMe (первый переход) + profile.get
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

  it('TS-025: просмотр профиля — обе роли', async () => {
    // Студент: логин read-only, роль по-русски, группа сида.
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

    // Преподаватель: роль «Преподаватель», группа отсутствует.
    env = await restartApp(env, { keepStorage: false });
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

  it('TS-026: редактирование ФИО и email — «Сохранено» и персистентность', async () => {
    await openProfileAs(STUDENT_LOGIN);

    setInput(env, '#profile-email', '  petrov.petrov@example.ru ');
    setInput(env, '#profile-full-name', '  Петров Пётр Петрович  ');
    profileSave().click();
    await flushMock(env, 1);

    // Успех «Сохранено» без якоря формы: desktop — Toast (не mobileMessage).
    expect(env.notifications.desktopMessage()).toEqual(
      jasmine.objectContaining({ severity: 'success', text: 'Сохранено' }),
    );
    expect(env.notifications.mobileMessage()).withContext('успех без якоря формы').toBeNull();

    // Значения сохранены в mock.db.v1 с триммингом; login/роль/группа не менялись.
    const user = mockDbData().users.find((u) => u.login === STUDENT_LOGIN)!;
    expect(user.fullName).toBe('Петров Пётр Петрович');
    expect(user.email).toBe('petrov.petrov@example.ru');
    expect(user.role).toBe('student');
    expect(user.groupId).not.toBeNull();

    // Мобильная ширина: успех фиксированно под шапкой (formId = null).
    env.notifications.dismissMobile();
    env.breakpoints.simulate(true);
    setInput(env, '#profile-email', '  petrov.petrov2@example.ru ');
    profileSave().click();
    await flushMock(env, 1);
    expect(env.notifications.mobileMessage()).toEqual(
      jasmine.objectContaining({ severity: 'success', text: 'Сохранено', formId: null }),
    );
    env.breakpoints.simulate(false);

    // Перезагрузка: значения видны (персистентность в mock.db.v1).
    env = await restartApp(env, { keepStorage: true });
    loginAs(seedUserIdByLogin(STUDENT_LOGIN));
    await env.harness.navigateByUrl('/profile');
    await flushMock(env, 1);
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

    // Чужой email в другом регистре (ci) — 409 под своей формой.
    setInput(env, '#profile-email', 'TEACHER@EXAMPLE.COM');
    profileSave().click();
    await flushMock(env, 1);
    expect(env.notifications.mobileMessage()?.text)
      .withContext('баннер дословно')
      .toBe('Пользователь с таким email уже существует');
    expect(env.notifications.mobileMessage()?.formId).withContext('якорь формы').toBe('profile-form');
    expect(bannerIn('[data-test="profile-anchor"]')).toBe('Пользователь с таким email уже существует');
    expect(mockDbData().users.find((u) => u.login === STUDENT_LOGIN)!.email)
      .withContext('профиль не изменён')
      .toBe('student01@example.com');

    // Собственный текущий email — сохранение успешно (сам исключён из проверки).
    setInput(env, '#profile-email', 'student01@example.com');
    profileSave().click();
    await flushMock(env, 1);
    expect(env.notifications.mobileMessage()).toEqual(
      jasmine.objectContaining({ severity: 'success', text: 'Сохранено' }),
    );
  }, 20000);

  it('TS-028: смена пароля с неверным текущим — 400 и сохранение ввода', async () => {
    await openProfileAs(STUDENT_LOGIN);
    env.breakpoints.simulate(true);

    setInput(env, '#profile-current-password', 'WrongPass999!');
    setInput(env, '#profile-new-password', 'NewPass1!');
    setInput(env, '#profile-repeat-password', 'NewPass1!');
    passwordSave().click();
    await flushMock(env, 1);

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

    // Пароль пользователя не изменился.
    await expectAsync(
      env.client.call('auth.login', { login: STUDENT_LOGIN, password: STUDENT_PASSWORD }),
    ).withContext('старый пароль по-прежнему действует')
      .toBeResolved();
  }, 20000);

  it('TS-029: приоритет валидаций при смене пароля — клиент раньше запроса', async () => {
    await openProfileAs(STUDENT_LOGIN);
    const callSpy = spyOn(env.client, 'call').and.callThrough();

    // Слабый новый + неверный текущий: клиентская валидация, запроса нет.
    setInput(env, '#profile-current-password', 'WrongPass1!');
    setInput(env, '#profile-new-password', 'abc');
    setInput(env, '#profile-repeat-password', 'abc');
    passwordSave().click();
    env.harness.fixture.detectChanges();
    expect(env.notifications.desktopMessage()?.text).toBe('Данные заполнены неверно');
    expect(required(env, '[data-test="new-password-error"]').textContent!.trim()).toBe(
      'Пароль должен содержать не менее 8 символов',
    );
    expect(callSpy.calls.all().filter((c) => c.args[0] === 'profile.changePassword').length)
      .withContext('запрос changePassword НЕ отправлен')
      .toBe(0);
    env.notifications.dismissMobile();

    // Валидный новый + неверный текущий: запрос отправлен — 400 дословно.
    setInput(env, '#profile-new-password', 'NewPass1!');
    setInput(env, '#profile-repeat-password', 'NewPass1!');
    passwordSave().click();
    await flushMock(env, 1);
    expect(callSpy.calls.all().filter((c) => c.args[0] === 'profile.changePassword').length)
      .withContext('запрос отправлен')
      .toBe(1);
    expect(env.notifications.desktopMessage()?.text).toBe('Неверный текущий пароль');
  }, 20000);

  it('TS-030: после смены пароля сессия жива, старый пароль недействителен', async () => {
    await openProfileAs(STUDENT_LOGIN);

    setInput(env, '#profile-current-password', STUDENT_PASSWORD);
    setInput(env, '#profile-new-password', 'NewPass1!');
    setInput(env, '#profile-repeat-password', 'NewPass1!');
    passwordSave().click();
    await flushMock(env, 1);

    expect(env.notifications.desktopMessage()).toEqual(
      jasmine.objectContaining({ severity: 'success', text: 'Пароль изменён' }),
    );
    env.notifications.dismissMobile();
    expect(required<HTMLInputElement>(env, '#profile-current-password').value)
      .withContext('поля пароля очищены при успехе')
      .toBe('');
    expect(required<HTMLInputElement>(env, '#profile-new-password').value).toBe('');

    // Текущая сессия сохранена — повторный вход не требуется.
    expect(env.auth.isAuthenticated()).withContext('сессия жива').toBeTrue();
    await env.harness.navigateByUrl('/profile');
    expect(env.router.url).withContext('профиль доступен без повторного входа').toBe('/profile');

    // Выход: старый пароль больше не работает, новый — работает.
    logoutClick();
    await flushMock(env, 1);
    await env.harness.navigateByUrl('/login');
    setInput(env, '#login-input', STUDENT_LOGIN);
    setInput(env, '#password-input', STUDENT_PASSWORD);
    required<HTMLButtonElement>(env, 'button[type="submit"]').click();
    await flushMock(env, 1);
    expect(env.notifications.desktopMessage()?.text).toBe('Неверный логин или пароль');
    env.notifications.dismissMobile();

    setInput(env, '#password-input', 'NewPass1!');
    required<HTMLButtonElement>(env, 'button[type="submit"]').click();
    await flushMock(env, 1);
    expect(env.router.url).withContext('вход с новым паролем успешен').toBe('/my-submissions');
  }, 30000);

  it('TS-031: повторное сохранение профиля идемпотентно, роль/группа неизменны', async () => {
    await openProfileAs(STUDENT_LOGIN);
    const before = mockDbData().users.find((u) => u.login === STUDENT_LOGIN)!;

    for (let attempt = 1; attempt <= 2; attempt++) {
      setInput(env, '#profile-full-name', 'Иванов Иван Иванович 01');
      setInput(env, '#profile-email', 'student01@example.com');
      profileSave().click();
      await flushMock(env, 1);
      expect(env.notifications.desktopMessage())
        .withContext(`сохранение ${attempt} успешно (свой email не даёт 409)`)
        .toEqual(jasmine.objectContaining({ severity: 'success', text: 'Сохранено' }));
      env.notifications.dismissMobile();
    }

    const after = mockDbData().users.find((u) => u.login === STUDENT_LOGIN)!;
    expect(after.login).withContext('login не меняется').toBe(before.login);
    expect(after.role).withContext('update не меняет роль').toBe(before.role);
    expect(after.groupId).withContext('update не меняет группу').toBe(before.groupId);
    expect(required(env, '[data-test="role-value"]').textContent!.trim()).toBe('Студент');
    expect(required(env, '[data-test="group-value"]').textContent!.trim()).toBe('ИК-221');
  }, 20000);

  it('TS-032: валидации email/ФИО в профиле без отправки запроса', async () => {
    await openProfileAs(STUDENT_LOGIN);
    const callSpy = spyOn(env.client, 'call').and.callThrough();

    setInput(env, '#profile-email', 'abc');
    profileSave().click();
    env.harness.fixture.detectChanges();
    expect(required(env, '[data-test="email-error"]').textContent!.trim()).toBe(
      'Введите корректный email',
    );
    env.notifications.dismissMobile();

    setInput(env, '#profile-email', 'student01@example.com');
    setInput(env, '#profile-full-name', '');
    profileSave().click();
    env.harness.fixture.detectChanges();
    expect(required(env, '[data-test="full-name-error"]').textContent!.trim()).toBe(
      'Заполните поле',
    );
    env.notifications.dismissMobile();

    setInput(env, '#profile-full-name', '   ');
    profileSave().click();
    env.harness.fixture.detectChanges();
    expect(required(env, '[data-test="full-name-error"]').textContent!.trim()).toBe(
      'Заполните поле',
    );

    expect(callSpy.calls.all().filter((c) => c.args[0] === 'profile.update').length)
      .withContext('profile.update не вызван ни разу')
      .toBe(0);
  }, 20000);

  it('TS-033: единый тримминг паролей на всех auth-формах', async () => {
    const infoSpy = spyOn(console, 'info');
    const codeFromLog = (): string => {
      const line = infoSpy.calls
        .all()
        .map((c) => String(c.args[0]))
        .reverse()
        .find((l) => l.startsWith('[mock-email] Код восстановления для student01@example.com: '));
      expect(line).withContext('строка кода в console.info').toBeDefined();
      return line!.slice(line!.lastIndexOf(': ') + 2);
    };
    const loginSubmit = (): HTMLButtonElement =>
      required<HTMLButtonElement>(env, 'button[type="submit"]');

    await env.harness.navigateByUrl('/login');
    const callSpy = spyOn(env.client, 'call').and.callThrough();
    const methodCalls = (method: string): number =>
      callSpy.calls.all().filter((c) => c.args[0] === method).length;

    // (1) Вход: пароль с крайними пробелами.
    setInput(env, '#login-input', '  student01  ');
    setInput(env, '#password-input', '  student123!  ');
    loginSubmit().click();
    await flushMock(env, 1);
    expect(env.router.url).withContext('вход успешен, как с очищенным значением').toBe('/my-submissions');
    const loginParams = callSpy.calls
      .all()
      .find((c) => c.args[0] === 'auth.login')!.args[1] as { login: string; password: string };
    expect(loginParams).toEqual({ login: 'student01', password: 'student123!' });

    // (2) «Смена пароля»: текущий и новый с крайними пробелами.
    await env.harness.navigateByUrl('/profile');
    await flushMock(env, 1);
    setInput(env, '#profile-current-password', '  student123!  ');
    setInput(env, '#profile-new-password', '  Change1!x  ');
    setInput(env, '#profile-repeat-password', '  Change1!x  ');
    passwordSave().click();
    await flushMock(env, 1);
    expect(env.notifications.desktopMessage())
      .withContext('смена успешна')
      .toEqual(jasmine.objectContaining({ text: 'Пароль изменён' }));
    const changeParams = callSpy.calls
      .all()
      .find((c) => c.args[0] === 'profile.changePassword')!.args[1] as Record<string, string>;
    expect(changeParams).toEqual({
      currentPassword: 'student123!',
      password: 'Change1!x',
      confirmPassword: 'Change1!x',
    });
    env.notifications.dismissMobile();

    logoutClick();
    await flushMock(env, 1);

    // (3) /reset-password: новый пароль с крайними пробелами.
    await env.harness.navigateByUrl('/recovery');
    env.harness.fixture.detectChanges();
    setInput(env, '#email-input', 'student01@example.com');
    Array.from(root(env).querySelectorAll<HTMLButtonElement>('button'))
      .find((b) => (b.textContent ?? '').trim() === 'Отправить код')!
      .click();
    await flushMock(env, 1);
    setInput(env, '#code-input', codeFromLog());
    Array.from(root(env).querySelectorAll<HTMLButtonElement>('button'))
      .find((b) => (b.textContent ?? '').trim() === 'Ввести код')!
      .click();
    await flushMock(env, 1);
    expect(env.router.url).toBe('/reset-password');
    setInput(env, '#password-input', '  NewPass1!  ');
    setInput(env, '#repeat-password-input', '  NewPass1!  ');
    Array.from(root(env).querySelectorAll<HTMLButtonElement>('button'))
      .find((b) => (b.textContent ?? '').trim() === 'Сменить пароль')!
      .click();
    await flushMock(env, 1);
    expect(env.router.url).withContext('сброс успешен').toBe('/login');
    const resetParams = callSpy.calls
      .all()
      .find((c) => c.args[0] === 'auth.reset-password')!.args[1] as Record<string, string>;
    expect(resetParams['password']).toBe('NewPass1!');

    // (4) Вход новым паролем с пробелами — согласовано с моком.
    setInput(env, '#login-input', '  student01  ');
    setInput(env, '#password-input', '  NewPass1!  ');
    loginSubmit().click();
    await flushMock(env, 1);
    expect(env.router.url).toBe('/my-submissions');

    // (5) Регистрация с паролем с пробелами; вход нового пользователя с пробелами.
    logoutClick();
    await flushMock(env, 1);
    await env.harness.navigateByUrl('/register');
    env.harness.fixture.detectChanges();
    setInput(env, '#full-name-input', 'Тримм Триммов');
    setInput(env, '#login-input', 'trimuser');
    setInput(env, '#email-input', 'trimuser@test.ru');
    setInput(env, '#password-input', '  Abcdef1!  ');
    setInput(env, '#repeat-password-input', '  Abcdef1!  ');
    required<HTMLButtonElement>(env, 'button[type="submit"]').click();
    await flushMock(env, 1);
    expect(env.router.url).withContext('регистрация успешна').toBe('/my-submissions');
    const registerParams = callSpy.calls
      .all()
      .find((c) => c.args[0] === 'auth.register')!.args[1] as Record<string, string>;
    expect(registerParams['password'])
      .withContext('в DTO уходит триммированный пароль')
      .toBe('Abcdef1!');

    logoutClick();
    await flushMock(env, 1);
    await env.harness.navigateByUrl('/login');
    setInput(env, '#login-input', '  trimuser  ');
    setInput(env, '#password-input', '  Abcdef1!  ');
    loginSubmit().click();
    await flushMock(env, 1);
    expect(env.router.url)
      .withContext('сохранённый пароль согласован с триммированным значением')
      .toBe('/my-submissions');
    expect(methodCalls('auth.login')).withContext('все входы через единый клиент').toBe(3);
  }, 30000);
});
