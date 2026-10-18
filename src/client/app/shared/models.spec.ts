/**
 * Юнит-тесты общих моделей и констант (контракт IF-011).
 *
 * Интерфейсы стираются при компиляции, поэтому их форма фиксируется двумя
 * способами: литеральными образцами (несовпадение полей не скомпилируется —
 * tsconfig.spec включён в strict-сборку тестов) и точечными проверками
 * значений. Константы (ключи хранилищ) проверяются дословно:
 * их значения зафиксированы спекой и переименование молча ломает персистентность.
 */
import {
  ApiError,
  AUTH_ERRORS,
  ConfirmRecoveryResult,
  LabDto,
  LABS_PAGE_SIZE,
  LabInput,
  LabsGetListParams,
  LabsUpdateParams,
  LoginParams,
  MAX_SEMESTER,
  MySubmissionsDto,
  PagedResult,
  ProfileDto,
  RegisterParams,
  STORAGE_KEYS,
  STUDENTS_PAGE_SIZE,
  StudentDto,
  StudentsGetListParams,
  StudentsSetGroupParams,
  Submission,
  SubmissionsGridDto,
  User,
} from './models';

describe('Константы и модели DTO (IF-011)', () => {
  it('ключи хранилищ — только recovery.flow.v1; mock-ключи удалены вместе с мок-слоем (FR-026(4))', () => {
    expect(Object.keys(STORAGE_KEYS)).toEqual(['recoveryFlow']);
    expect(STORAGE_KEYS.recoveryFlow).toBe('recovery.flow.v1');
  });

  it('User — образец студента без группы: uuid-id, role, groupId/groupName = null, пароль только внутри модели', () => {
    const student: User = {
      id: '0b0b0b0b-0b0b-4b0b-8b0b-0b0b0b0b0b0b',
      login: 'student31',
      email: 'student31@example.com',
      fullName: 'Иванов Иван Иванович 31',
      role: 'student',
      groupId: null,
      groupName: null,
      password: 'Student#2026',
    };
    expect(student.id).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i,
    );
    expect(student.role).toBe('student');
    expect(student.groupId).toBeNull();
    expect(student.groupName).toBeNull();
  });

  it('Submission — даты строками YYYY-MM-DD либо null, служебные поля updated* присутствуют', () => {
    const submission: Submission = {
      id: '11111111-2222-4333-8444-555555555555',
      studentId: '0b0b0b0b-0b0b-4b0b-8b0b-0b0b0b0b0b0b',
      labId: '66666666-7777-4888-9999-aaaaaaaaaaaa',
      submitDate: '2026-09-01',
      defenseDate: null,
      updatedAt: '2026-09-26T10:00:00.000Z',
      updatedBy: null,
    };
    expect(submission.submitDate).toBe('2026-09-01');
    expect(submission.defenseDate).toBeNull();
    expect(submission.updatedBy).toBeNull();
  });

  it('PagedResult<LabDto> — items/total/page/pageSize (подпись «Показать записи с X по Y из Z»)', () => {
    const page: PagedResult<LabDto> = {
      items: [
        {
          id: '66666666-7777-4888-9999-aaaaaaaaaaaa',
          semester: 1,
          number: 1,
          content: 'Содержание лабораторной работы №1',
          assignmentUrl: null,
          defenseRequired: true,
        },
      ],
      total: 200,
      page: 1,
      pageSize: 10,
    };
    expect(page.items.length).toBe(1);
    expect(page.items[0].assignmentUrl).toBeNull();
    expect(page.items[0].defenseRequired).toBe(true);
    expect(page.total).toBe(200);
    expect(page.pageSize).toBe(10);
  });

  it('StudentDto — строка списка студентов раздела «Доступ»: groupId и groupName string|null', () => {
    const student: StudentDto = {
      id: '0b0b0b0b-0b0b-4b0b-8b0b-0b0b0b0b0b0b',
      fullName: 'Иванов Иван Иванович 26',
      login: 'student26',
      email: 'student26@example.com',
      groupId: 'c0c0c0c0-0000-4000-8000-000000000222',
      groupName: 'ИК-222',
    };
    expect(student.groupId).toBe('c0c0c0c0-0000-4000-8000-000000000222');
    expect(student.groupName).toBe('ИК-222');

    // Аменда 6: у безгруппного студента null и ключевое groupId, и отображаемое groupName.
    const withoutGroup: StudentDto = { ...student, groupId: null, groupName: null };
    expect(withoutGroup.groupId).toBeNull();
    expect(withoutGroup.groupName).toBeNull();
  });

  it('SubmissionsGridDto — студенты × работы × сдачи + total ведомости', () => {
    const grid: SubmissionsGridDto = {
      students: [{ id: '0b0b0b0b-0b0b-4b0b-8b0b-0b0b0b0b0b0b', fullName: 'Иванов Иван Иванович 01' }],
      labs: [
        {
          id: '66666666-7777-4888-9999-aaaaaaaaaaaa',
          number: 1,
          defenseRequired: true,
        },
      ],
      submissions: [
        {
          studentId: '0b0b0b0b-0b0b-4b0b-8b0b-0b0b0b0b0b0b',
          labId: '66666666-7777-4888-9999-aaaaaaaaaaaa',
          submitDate: '2026-09-01',
          defenseDate: null,
        },
      ],
      total: 25,
    };
    expect(grid.students.length).toBe(1);
    expect(grid.labs[0].number).toBe(1);
    expect(grid.submissions[0].defenseDate).toBeNull();
    expect(grid.total).toBe(25);
  });

  it('MySubmissionsDto — hasGroup, работы и сдачи только текущего студента', () => {
    const mine: MySubmissionsDto = {
      hasGroup: false,
      labs: [],
      submissions: [],
    };
    expect(mine.hasGroup).toBe(false);
    expect(mine.labs).toEqual([]);
    expect(mine.submissions).toEqual([]);
  });

  it('ProfileDto — login/email/fullName/role и groupName, отсутствующий у преподавателя', () => {
    const teacher: ProfileDto = {
      login: 'teacher',
      email: 'teacher@example.com',
      fullName: 'Сидоров Семён Семёнович',
      role: 'teacher',
    };
    expect(teacher.groupName).toBeUndefined();

    const student: ProfileDto = { ...teacher, role: 'student', groupName: 'ИК-221' };
    expect(student.groupName).toBe('ИК-221');
  });

  it('ApiError — форма status + body.message + необязательные ошибки по полям', () => {
    const validation: ApiError = {
      status: 400,
      body: {
        message: 'Данные заполнены неверно',
        errors: { number: ['Номер должен быть положительным числом'] },
      },
    };
    expect(validation.status).toBe(400);
    expect(validation.body.message).toBe('Данные заполнены неверно');
    expect(validation.body.errors?.['number']).toEqual([
      'Номер должен быть положительным числом',
    ]);

    const unauthorized: ApiError = { status: 401, body: { message: 'Не авторизован' } };
    expect(unauthorized.body.errors).toBeUndefined();
  });

  it('AUTH_ERRORS — тексты отказов IF-101 дословно (страницы сравнивают с ними ветки)', () => {
    expect(AUTH_ERRORS.wrongCredentials).toBe('Неверный логин или пароль');
    expect(AUTH_ERRORS.invalidData).toBe('Данные заполнены неверно');
    expect(AUTH_ERRORS.loginTaken).toBe('Пользователь с таким логином уже существует');
    expect(AUTH_ERRORS.emailTaken).toBe('Пользователь с таким email уже существует');
    expect(AUTH_ERRORS.tooManyAttempts).toBe('Слишком много попыток. Повторите позже');
    expect(AUTH_ERRORS.recoveryCodeRejected).toBe('Код восстановления не подходит');
    expect(AUTH_ERRORS.resetLinkInvalid).toBe(
      'Ссылка восстановления недействительна или истекла',
    );
    expect(AUTH_ERRORS.unauthorized).toBe('Не авторизован');
  });

  it('LABS_PAGE_SIZE и STUDENTS_PAGE_SIZE — ровно 10 (§4.3, ADR-109)', () => {
    expect(LABS_PAGE_SIZE).toBe(10);
    expect(STUDENTS_PAGE_SIZE).toBe(10);
  });

  it('RegisterParams/LoginParams — форма полей форм auth (IF-101)', () => {
    const register: RegisterParams = {
      fullName: 'Иванов Иван Иванович',
      login: 'student31',
      email: 'student31@example.com',
      password: 'Student#2026',
      repeatPassword: 'Student#2026',
    };
    expect(Object.keys(register).sort()).toEqual([
      'email',
      'fullName',
      'login',
      'password',
      'repeatPassword',
    ]);

    const login: LoginParams = { login: 'student31', password: 'Student#2026' };
    expect(login.login).toBe('student31');
  });

  it('ConfirmRecoveryResult — краткоживущий resetToken (IF-101)', () => {
    const result: ConfirmRecoveryResult = { resetToken: 'runtime-id' };
    expect(typeof result.resetToken).toBe('string');
  });

  it('LabInput/LabsUpdateParams — вход формы лабораторной (IF-103)', () => {
    const input: LabInput = {
      number: 21,
      semester: MAX_SEMESTER,
      content: 'Содержание работы',
      assignmentUrl: null,
      defenseRequired: false,
    };
    expect(input.assignmentUrl).toBeNull();

    const update: LabsUpdateParams = { ...input, id: 'lab-uuid' };
    expect(update.id).toBe('lab-uuid');

    const query: LabsGetListParams = { page: 2, semester: 1, sortField: 'number', sortDir: 'desc' };
    expect(query.page).toBe(2);
    expect(query.sortField).toBe('number');
    expect(query.sortDir).toBe('desc');
  });

  it('StudentsGetListParams/StudentsSetGroupParams — поиск и назначение группы (IF-105)', () => {
    const query: StudentsGetListParams = { search: 'иванов', groupId: 'none', page: 1 };
    expect(query.groupId).toBe('none');

    const setGroup: StudentsSetGroupParams = { studentId: 'student-uuid', groupId: null };
    expect(setGroup.groupId).toBeNull();
  });
});
