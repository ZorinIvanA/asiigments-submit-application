/**
 * Юнит-тесты общих моделей и констант (контракт IF-011).
 *
 * Интерфейсы стираются при компиляции, поэтому их форма фиксируется двумя
 * способами: литеральными образцами (несовпадение полей не скомпилируется —
 * tsconfig.spec включён в strict-сборку тестов) и точечными проверками
 * значений. Константы (ключи хранилищ, задержка мока) проверяются дословно:
 * их значения зафиксированы спекой и переименование молча ломает персистентность.
 */
import {
  ApiError,
  LabDto,
  MOCK_DELAY_MS,
  MySubmissionsDto,
  PagedResult,
  ProfileDto,
  STORAGE_KEYS,
  StudentDto,
  Submission,
  SubmissionsGridDto,
  User,
} from './models';

describe('Константы и модели DTO (IF-011)', () => {
  it('ключи хранилищ — дословно по спеке: mock.db.v1 / mock.session.userId / recovery.flow.v1', () => {
    expect(STORAGE_KEYS.mockDb).toBe('mock.db.v1');
    expect(STORAGE_KEYS.session).toBe('mock.session.userId');
    expect(STORAGE_KEYS.recoveryFlow).toBe('recovery.flow.v1');
  });

  it('задержка мок-вызовов — ровно 500 мс (имитация бэкенда, FR-002/FR-003)', () => {
    expect(MOCK_DELAY_MS).toBe(500);
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

  it('StudentDto — строка списка студентов раздела «Доступ»: groupName string|null', () => {
    const student: StudentDto = {
      id: '0b0b0b0b-0b0b-4b0b-8b0b-0b0b0b0b0b0b',
      fullName: 'Иванов Иван Иванович 26',
      login: 'student26',
      email: 'student26@example.com',
      groupName: 'ИК-222',
    };
    expect(student.groupName).toBe('ИК-222');

    const withoutGroup: StudentDto = { ...student, groupName: null };
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
});
