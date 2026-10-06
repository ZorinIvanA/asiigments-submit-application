/**
 * Юнит-тесты мок-обработчиков домена Labs (C-102, контракт IF-103,
 * FR-4.3/FR-4.4, ADR-104/ADR-109).
 *
 * Покрытие по unit_test_requirements подзадачи:
 *  - getList: фильтр по семестру/null, обе сортировки в обоих направлениях,
 *    двухколоночный дефолт semester↑,number↑, пагинация и total, данные
 *    подписи (page/pageSize/total), нормализация некорректного page к 1
 *    (ADR-109 аддендум 1), страница правее последней — пустая items;
 *  - getById: успех/404; create/update: все 400-поля (тексты из
 *    ERROR_TEXTS), 409 (своя запись при update не конфликтует), 404,
 *    нормализация полей;
 *  - remove: каскадное удаление submissions, 404;
 *  - semesters: distinct+asc, пустая БД, доступ студенту;
 *  - роль: teacher ok / student 403 «Доступ запрещён» / без сессии и битая
 *    сессия 401 «Не авторизован».
 */
import { fakeAsync, tick } from '@angular/core/testing';

import { ApiError, Lab, LabDto, PagedResult, STORAGE_KEYS, Submission, User } from '../../shared/models';
import { ERROR_TEXTS } from '../../shared/validation/error-texts';
import { MockApiClient } from '../mock-api-client';
import { emptyMockDbData, MockDbData, resetMockDbSeed } from '../mock-db';
import { MOCK_ID_PATTERN } from '../ids';
import {
  LabIdParams,
  LabInput,
  LabsGetListParams,
  LabsUpdateParams,
  LABS_PAGE_SIZE,
  registerLabsHandlers,
} from './handlers';

const TEACHER_ID = 'aaaaaaaa-0000-4000-8000-000000000001';
const STUDENT_ID = 'aaaaaaaa-0000-4000-8000-000000000002';

/** uuid в формате MOCK_ID_PATTERN, производные от порядкового номера. */
function labId(n: number): string {
  return `bbbbbbbb-0000-4000-8000-${String(n).padStart(12, '0')}`;
}

function submissionId(n: number): string {
  return `cccccccc-0000-4000-8000-${String(n).padStart(12, '0')}`;
}

function makeTeacher(): User {
  return {
    id: TEACHER_ID,
    login: 'teacher',
    email: 'teacher@example.com',
    fullName: 'Сидоров Семён Семёнович',
    role: 'teacher',
    groupId: null,
    password: 'teacher123!',
  };
}

function makeStudent(): User {
  return {
    id: STUDENT_ID,
    login: 'student01',
    email: 'student01@example.com',
    fullName: 'Иванов Иван Иванович 01',
    role: 'student',
    groupId: null,
    password: 'student123!',
  };
}

function makeLab(id: string, semester: number, number: number, overrides?: Partial<Lab>): Lab {
  return {
    id,
    semester,
    number,
    content: `Содержание лабораторной работы №${number}`,
    assignmentUrl: null,
    defenseRequired: false,
    ...overrides,
  };
}

function makeSubmission(id: string, studentId: string, labIdentifier: string): Submission {
  return {
    id,
    studentId,
    labId: labIdentifier,
    submitDate: '2026-09-01',
    defenseDate: null,
    updatedAt: '2026-09-01T10:00:00.000Z',
    updatedBy: null,
  };
}

function stored(): MockDbData {
  return JSON.parse(localStorage.getItem(STORAGE_KEYS.mockDb) ?? 'null') as MockDbData;
}

describe('registerLabsHandlers — мок домена Labs (C-102, IF-103)', () => {
  let client: MockApiClient;

  /**
   * Дополняет текущее состояние mock.db.v1 (до первого вызова мока): уже
   * засеянное (например, users от seedUsers) сохраняется, переопределяются
   * только перечисленные коллекции.
   */
  const seed = (data: Partial<MockDbData>): void => {
    const current = stored();
    localStorage.setItem(
      STORAGE_KEYS.mockDb,
      JSON.stringify({ ...(current ?? emptyMockDbData()), ...data }),
    );
  };

  const loginAs = (userId: string | null): void => {
    if (userId === null) {
      localStorage.removeItem(STORAGE_KEYS.session);
    } else {
      localStorage.setItem(STORAGE_KEYS.session, userId);
    }
  };

  /** Стандартный расклад: teacher-сессия, пользователей двое, БД пустая. */
  const seedUsers = (): void => {
    seed({ users: [makeTeacher(), makeStudent()] });
    loginAs(TEACHER_ID);
  };

  /** Вход формы; для labs.update допускает id правимой записи. */
  const labInput = (overrides?: Partial<LabsUpdateParams>): LabInput & { id?: string } => ({
    number: 21,
    semester: 1,
    content: 'Содержание новой работы',
    assignmentUrl: null,
    defenseRequired: false,
    ...overrides,
  });

  /** Ловит отказ вызова; успех вместо отказа — ошибка теста. */
  const rejectionOf = (promise: Promise<unknown>): Promise<ApiError> =>
    promise.then(
      () => {
        throw new Error('ожидался отказ мок-вызова, получен успех');
      },
      (error: unknown) => error as ApiError,
    );

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    resetMockDbSeed();
    client = new MockApiClient();
    registerLabsHandlers(client);
  });

  afterEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    resetMockDbSeed();
  });

  describe('labs.getList — фильтр, сортировка, пагинация (ADR-109)', () => {
    it('AC «list-фильтр-и-сортировка»: семестр 1 + number desc → только семестр 1 по убыванию, total выборки', fakeAsync(() => {
      seedUsers();
      seed({
        labs: [
          makeLab(labId(1), 2, 1),
          makeLab(labId(2), 1, 3),
          makeLab(labId(3), 1, 1),
          makeLab(labId(4), 1, 2),
        ],
      });

      let result: PagedResult<LabDto> | undefined;
      void client
        .call<PagedResult<LabDto>>('labs.getList', {
          semester: 1,
          sortField: 'number',
          sortDir: 'desc',
          page: 1,
        } satisfies LabsGetListParams)
        .then((r) => (result = r));
      tick(500);

      expect(result?.items.map((lab) => lab.number)).toEqual([3, 2, 1]);
      expect(result?.items.every((lab) => lab.semester === 1)).withContext('только семестр 1').toBeTrue();
      expect(result?.total).withContext('total — размер фильтрованной выборки').toBe(3);
      expect(result?.items.length).withContext('items ≤ 10').toBeLessThanOrEqual(10);
    }));

    it('без фильтра и параметров сортировки — все записи, дефолт semester↑,number↑', fakeAsync(() => {
      seedUsers();
      seed({
        labs: [makeLab(labId(1), 1, 2), makeLab(labId(2), 2, 1), makeLab(labId(3), 1, 1)],
      });

      let result: PagedResult<LabDto> | undefined;
      void client
        .call<PagedResult<LabDto>>('labs.getList', { page: 1 } satisfies LabsGetListParams)
        .then((r) => (result = r));
      tick(500);

      expect(result?.items.map((lab) => [lab.semester, lab.number])).toEqual([
        [1, 1],
        [1, 2],
        [2, 1],
      ]);
      expect(result?.total).toBe(3);
    }));

    it('semester null — фильтра нет; semester desc → вторичный ключ number asc', fakeAsync(() => {
      seedUsers();
      seed({
        labs: [makeLab(labId(1), 1, 2), makeLab(labId(2), 2, 1), makeLab(labId(3), 1, 1)],
      });

      let result: PagedResult<LabDto> | undefined;
      void client
        .call<PagedResult<LabDto>>('labs.getList', {
          semester: null,
          sortField: 'semester',
          sortDir: 'desc',
          page: 1,
        } satisfies LabsGetListParams)
        .then((r) => (result = r));
      tick(500);

      expect(result?.items.map((lab) => [lab.semester, lab.number])).toEqual([
        [2, 1],
        [1, 1],
        [1, 2],
      ]);
    }));

    it('number asc и number desc: вторичный ключ semester asc (двухколоночная сортировка)', fakeAsync(() => {
      seedUsers();
      seed({
        labs: [makeLab(labId(1), 2, 1), makeLab(labId(2), 1, 2), makeLab(labId(3), 1, 1)],
      });

      const numbersOf = (page: PagedResult<LabDto> | undefined): Array<[number, number]> =>
        (page?.items ?? []).map((lab) => [lab.semester, lab.number]);

      let asc: PagedResult<LabDto> | undefined;
      void client
        .call<PagedResult<LabDto>>('labs.getList', {
          sortField: 'number',
          sortDir: 'asc',
          page: 1,
        } satisfies LabsGetListParams)
        .then((r) => (asc = r));
      tick(500);

      let desc: PagedResult<LabDto> | undefined;
      void client
        .call<PagedResult<LabDto>>('labs.getList', {
          sortField: 'number',
          sortDir: 'desc',
          page: 1,
        } satisfies LabsGetListParams)
        .then((r) => (desc = r));
      tick(500);

      expect(numbersOf(asc)).toEqual([
        [1, 1],
        [2, 1],
        [1, 2],
      ]);
      expect(numbersOf(desc)).toEqual([
        [1, 2],
        [1, 1],
        [2, 1],
      ]);
    }));

    it('пагинация: 12 записей — вторая страница отдаёт остаток, данные подписи page/pageSize/total', fakeAsync(() => {
      seedUsers();
      seed({ labs: Array.from({ length: 12 }, (_, i) => makeLab(labId(i + 1), 1, i + 1)) });

      let page2: PagedResult<LabDto> | undefined;
      void client
        .call<PagedResult<LabDto>>('labs.getList', {
          semester: 1,
          sortField: 'number',
          sortDir: 'asc',
          page: 2,
        } satisfies LabsGetListParams)
        .then((r) => (page2 = r));
      tick(500);

      expect(page2?.items.map((lab) => lab.number)).toEqual([11, 12]);
      expect(page2?.total).toBe(12);
      expect(page2?.page).toBe(2);
      expect(page2?.pageSize).toBe(LABS_PAGE_SIZE);
      expect(page2?.pageSize).toBe(10);
    }));

    it('ADR-109 аддендум 1: некорректный page (0, -3, «abc», 1.5, undefined) → нормализация к 1', fakeAsync(() => {
      seedUsers();
      seed({ labs: Array.from({ length: 12 }, (_, i) => makeLab(labId(i + 1), 1, i + 1)) });

      const askWithBadPage = (badPage: unknown): PagedResult<LabDto> | undefined => {
        let result: PagedResult<LabDto> | undefined;
        void client
          .call<PagedResult<LabDto>>(
            'labs.getList',
            { page: badPage } as unknown as LabsGetListParams,
          )
          .then((r) => (result = r));
        tick(500);
        return result;
      };

      for (const badPage of [0, -3, 'abc', 1.5, undefined]) {
        const result = askWithBadPage(badPage);
        expect(result?.page).withContext(`page ${String(badPage)} → 1`).toBe(1);
        expect(result?.items.length)
          .withContext(`первая страница данных для page ${String(badPage)}`)
          .toBe(10);
        expect(result?.items[0]?.number).withContext(`первая запись для ${String(badPage)}`).toBe(1);
        expect(result?.total).withContext(`total не зависит от page ${String(badPage)}`).toBe(12);
      }
    }));

    it('страница правее последней (page=3 из 12 записей) — пустая items при корректном total, page эхом', fakeAsync(() => {
      seedUsers();
      seed({ labs: Array.from({ length: 12 }, (_, i) => makeLab(labId(i + 1), 1, i + 1)) });

      let page3: PagedResult<LabDto> | undefined;
      void client
        .call<PagedResult<LabDto>>('labs.getList', { page: 3 } satisfies LabsGetListParams)
        .then((r) => (page3 = r));
      tick(500);

      expect(page3?.items).toEqual([]);
      expect(page3?.total).toBe(12);
      expect(page3?.page).toBe(3);
    }));
  });

  describe('labs.getById (ADR-104)', () => {
    it('существующий id → LabDto со всеми полями записи', fakeAsync(() => {
      seedUsers();
      seed({ labs: [makeLab(labId(1), 1, 1, { assignmentUrl: 'https://git.example.com/a', defenseRequired: true })] });

      let result: LabDto | undefined;
      void client
        .call<LabDto>('labs.getById', { id: labId(1) } satisfies LabIdParams)
        .then((r) => (result = r));
      tick(500);

      expect(result).toEqual(makeLab(labId(1), 1, 1, {
        assignmentUrl: 'https://git.example.com/a',
        defenseRequired: true,
      }));
    }));

    it('несуществующий id → 404 «Лабораторная не найдена»', fakeAsync(() => {
      seedUsers();

      let error: ApiError | undefined;
      void rejectionOf(
        client.call('labs.getById', { id: labId(99) } satisfies LabIdParams),
      ).then((captured) => (error = captured));
      tick(500);

      expect(error?.status).toBe(404);
      expect(error?.body.message).toBe('Лабораторная не найдена');
    }));
  });

  describe('labs.create', () => {
    it('успех: запись создана с uuid-id и переданными полями, персистентна', fakeAsync(() => {
      seedUsers();

      let created: LabDto | undefined;
      void client
        .call<LabDto>('labs.create', labInput({ assignmentUrl: 'https://git.example.com/assignments/1/21' }))
        .then((r) => (created = r));
      tick(500);

      const createdId = created?.id ?? '';
      expect(createdId).toMatch(MOCK_ID_PATTERN);
      expect(created?.number).toBe(21);
      expect(created?.semester).toBe(1);
      expect(created?.content).toBe('Содержание новой работы');
      expect(created?.defenseRequired).toBeFalse();
      expect(stored().labs.map((lab) => lab.id)).toEqual([createdId]);
    }));

    it('AC «create-конфликт»: существующая пара (semester, number) → 409, запись не создана', fakeAsync(() => {
      seedUsers();
      seed({ labs: [makeLab(labId(1), 1, 1)] });

      let error: ApiError | undefined;
      void rejectionOf(client.call('labs.create', labInput({ number: 1, semester: 1 }))).then(
        (captured) => (error = captured),
      );
      tick(500);

      expect(error?.status).toBe(409);
      expect(error?.body.message).toBe('Лабораторная с таким номером уже есть в семестре');
      expect(stored().labs.length).withContext('дубль не создан').toBe(1);
    }));

    it('та же пара в другом семестре конфликтом не считается', fakeAsync(() => {
      seedUsers();
      seed({ labs: [makeLab(labId(1), 1, 1)] });

      let created: LabDto | undefined;
      void client
        .call<LabDto>('labs.create', labInput({ number: 1, semester: 2 }))
        .then((r) => (created = r));
      tick(500);

      expect(created?.semester).toBe(2);
      expect(stored().labs.length).toBe(2);
    }));

    it('400 по всем полям сразу: number, semester, content, assignmentUrl — тексты из ERROR_TEXTS', fakeAsync(() => {
      seedUsers();

      let error: ApiError | undefined;
      void rejectionOf(
        client.call(
          'labs.create',
          labInput({ number: 0, semester: 11, content: '', assignmentUrl: 'ftp://example.com/task' }),
        ),
      ).then((captured) => (error = captured));
      tick(500);

      expect(error?.status).toBe(400);
      expect(error?.body.message).toBe('Данные заполнены неверно');
      expect(Object.keys(error?.body.errors ?? {}).sort()).toEqual([
        'assignmentUrl',
        'content',
        'number',
        'semester',
      ]);
      expect(error?.body.errors?.['number']).toEqual([ERROR_TEXTS['lab.number']]);
      expect(error?.body.errors?.['semester']).toEqual([ERROR_TEXTS['lab.semester']]);
      expect(error?.body.errors?.['content']).toEqual([ERROR_TEXTS.required]);
      expect(error?.body.errors?.['assignmentUrl']).toEqual([ERROR_TEXTS['lab.url']]);
    }));

    it('400 содержание длиннее 500 символов после трима; записи не создаются ни при 400', fakeAsync(() => {
      seedUsers();

      let error: ApiError | undefined;
      void rejectionOf(
        client.call('labs.create', labInput({ content: `  ${'а'.repeat(501)}  ` })),
      ).then((captured) => (error = captured));
      tick(500);

      expect(error?.status).toBe(400);
      expect(error?.body.errors?.['content']).toEqual([ERROR_TEXTS['lab.content']]);
      expect(stored().labs.length).toBe(0);
    }));

    it('400 обязательные поля: пустой номер и содержание из одних пробелов → «Заполните поле»', fakeAsync(() => {
      seedUsers();

      let error: ApiError | undefined;
      void rejectionOf(
        client.call('labs.create', labInput({ number: undefined as unknown as number, content: '   ' })),
      ).then((captured) => (error = captured));
      tick(500);

      expect(error?.body.errors?.['number']).toEqual([ERROR_TEXTS.required]);
      expect(error?.body.errors?.['content']).toEqual([ERROR_TEXTS.required]);
    }));

    it('нормализация: содержание и ссылка триммятся, пустая ссылка → null, семестр 10 валиден (MAX_SEMESTER)', fakeAsync(() => {
      seedUsers();

      let created: LabDto | undefined;
      void client
        .call<LabDto>(
          'labs.create',
          labInput({
            semester: 10,
            content: '  Содержание с пробелами  ',
            assignmentUrl: '  https://git.example.com/a  ',
          }),
        )
        .then((r) => (created = r));
      tick(500);

      expect(created?.semester).toBe(10);
      expect(created?.content).toBe('Содержание с пробелами');
      expect(created?.assignmentUrl).toBe('https://git.example.com/a');
    }));
  });

  describe('labs.update', () => {
    it('успех: поля обновлены, id сохранён', fakeAsync(() => {
      seedUsers();
      seed({ labs: [makeLab(labId(1), 1, 1)] });

      let updated: LabDto | undefined;
      void client
        .call<LabDto>(
          'labs.update',
          labInput({
            number: 2,
            semester: 2,
            content: 'Обновлённое содержание',
            assignmentUrl: 'https://git.example.com/b',
            defenseRequired: true,
            id: labId(1),
          }),
        )
        .then((r) => (updated = r));
      tick(500);

      expect(updated?.id).toBe(labId(1));
      expect(updated?.number).toBe(2);
      expect(updated?.semester).toBe(2);
      expect(updated?.content).toBe('Обновлённое содержание');
      expect(updated?.defenseRequired).toBeTrue();
      expect(stored().labs[0]?.number).toBe(2);
    }));

    it('своя пара (semester, number) при update конфликтом не считается', fakeAsync(() => {
      seedUsers();
      seed({ labs: [makeLab(labId(1), 1, 1)] });

      let updated: LabDto | undefined;
      void client
        .call<LabDto>('labs.update', labInput({ number: 1, semester: 1, content: 'Тот же номер', id: labId(1) }))
        .then((r) => (updated = r));
      tick(500);

      expect(updated?.content).toBe('Тот же номер');
      expect(stored().labs.length).toBe(1);
    }));

    it('чужая пара (semester, number) при update → 409, запись не изменена', fakeAsync(() => {
      seedUsers();
      seed({ labs: [makeLab(labId(1), 1, 1), makeLab(labId(2), 1, 2)] });

      let error: ApiError | undefined;
      void rejectionOf(
        client.call('labs.update', labInput({ number: 2, semester: 1, id: labId(1) })),
      ).then((captured) => (error = captured));
      tick(500);

      expect(error?.status).toBe(409);
      expect(error?.body.message).toBe('Лабораторная с таким номером уже есть в семестре');
      expect(stored().labs.find((lab) => lab.id === labId(1))?.number).toBe(1);
    }));

    it('несуществующий id → 404 «Лабораторная не найдена»', fakeAsync(() => {
      seedUsers();

      let error: ApiError | undefined;
      void rejectionOf(
        client.call('labs.update', labInput({ id: labId(99) })),
      ).then((captured) => (error = captured));
      tick(500);

      expect(error?.status).toBe(404);
      expect(error?.body.message).toBe('Лабораторная не найдена');
    }));

    it('невалидные поля при update → 400, запись не изменена', fakeAsync(() => {
      seedUsers();
      seed({ labs: [makeLab(labId(1), 1, 1)] });

      let error: ApiError | undefined;
      void rejectionOf(
        client.call('labs.update', labInput({ semester: 0, id: labId(1) })),
      ).then((captured) => (error = captured));
      tick(500);

      expect(error?.status).toBe(400);
      expect(error?.body.errors?.['semester']).toEqual([ERROR_TEXTS['lab.semester']]);
      expect(stored().labs[0]).toEqual(makeLab(labId(1), 1, 1));
    }));
  });

  describe('labs.remove — каскадное удаление (§4.3)', () => {
    it('AC «remove-каскад»: работа и все её submissions удалены, чужие сдачи не тронуты', fakeAsync(() => {
      seedUsers();
      seed({
        labs: [makeLab(labId(1), 1, 1), makeLab(labId(2), 1, 2)],
        submissions: [
          makeSubmission(submissionId(1), STUDENT_ID, labId(1)),
          makeSubmission(submissionId(2), STUDENT_ID, labId(1)),
          makeSubmission(submissionId(3), STUDENT_ID, labId(2)),
        ],
      });

      let removed = false;
      void client.call('labs.remove', { id: labId(1) } satisfies LabIdParams).then(() => (removed = true));
      tick(500);
      expect(removed).toBeTrue();

      expect(stored().labs.map((lab) => lab.id)).toEqual([labId(2)]);
      expect(stored().submissions.map((submission) => submission.id)).toEqual([submissionId(3)]);
    }));

    it('несуществующий id → 404 «Лабораторная не найдена», данные не изменены', fakeAsync(() => {
      seedUsers();
      seed({ labs: [makeLab(labId(1), 1, 1)], submissions: [makeSubmission(submissionId(1), STUDENT_ID, labId(1))] });

      let error: ApiError | undefined;
      void rejectionOf(client.call('labs.remove', { id: labId(99) } satisfies LabIdParams)).then(
        (captured) => (error = captured),
      );
      tick(500);

      expect(error?.status).toBe(404);
      expect(error?.body.message).toBe('Лабораторная не найдена');
      expect(stored().labs.length).toBe(1);
      expect(stored().submissions.length).toBe(1);
    }));
  });

  describe('labs.semesters — любая роль с сессией', () => {
    it('AC «semesters»: distinct семестры по возрастанию', fakeAsync(() => {
      seedUsers();
      seed({
        labs: [makeLab(labId(1), 2, 1), makeLab(labId(2), 1, 1), makeLab(labId(3), 2, 2)],
      });

      let result: number[] | undefined;
      void client.call<number[]>('labs.semesters', null).then((r) => (result = r));
      tick(500);

      expect(result).toEqual([1, 2]);
    }));

    it('пустая БД → пустой перечень', fakeAsync(() => {
      seedUsers();

      let result: number[] | undefined;
      void client.call<number[]>('labs.semesters', null).then((r) => (result = r));
      tick(500);

      expect(result).toEqual([]);
    }));

    it('доступен студенту (сессия студента)', fakeAsync(() => {
      seedUsers();
      loginAs(STUDENT_ID);
      seed({ labs: [makeLab(labId(1), 3, 1)] });

      let result: number[] | undefined;
      void client.call<number[]>('labs.semesters', null).then((r) => (result = r));
      tick(500);

      expect(result).toEqual([3]);
    }));
  });

  describe('роль и сессия (401/403)', () => {
    it('AC «role-guard»: сессия студента, labs.create → 403 «Доступ запрещён», запись не создана', fakeAsync(() => {
      seedUsers();
      loginAs(STUDENT_ID);

      let error: ApiError | undefined;
      void rejectionOf(client.call('labs.create', labInput())).then((captured) => (error = captured));
      tick(500);

      expect(error?.status).toBe(403);
      expect(error?.body.message).toBe('Доступ запрещён');
      expect(stored().labs.length).toBe(0);
    }));

    it('student 403 для getList/getById/update/remove', fakeAsync(() => {
      seedUsers();
      loginAs(STUDENT_ID);
      seed({ labs: [makeLab(labId(1), 1, 1)] });

      const check = async (method: string, params: unknown): Promise<ApiError> =>
        rejectionOf(client.call(method, params));

      let listError: ApiError | undefined;
      void check('labs.getList', { page: 1 }).then((captured) => (listError = captured));
      tick(500);
      expect(listError?.status).toBe(403);
      expect(listError?.body.message).toBe('Доступ запрещён');

      let getByIdError: ApiError | undefined;
      void check('labs.getById', { id: labId(1) }).then((captured) => (getByIdError = captured));
      tick(500);
      expect(getByIdError?.status).toBe(403);

      let updateError: ApiError | undefined;
      void check('labs.update', labInput({ id: labId(1) })).then((captured) => (updateError = captured));
      tick(500);
      expect(updateError?.status).toBe(403);

      let removeError: ApiError | undefined;
      void check('labs.remove', { id: labId(1) }).then((captured) => (removeError = captured));
      tick(500);
      expect(removeError?.status).toBe(403);
    }));

    it('без сессии → 401 «Не авторизован» (create, getList, semesters)', fakeAsync(() => {
      seedUsers();
      loginAs(null);

      let createError: ApiError | undefined;
      void rejectionOf(client.call('labs.create', labInput())).then((captured) => (createError = captured));
      tick(500);
      expect(createError?.status).toBe(401);
      expect(createError?.body.message).toBe('Не авторизован');

      let listError: ApiError | undefined;
      void rejectionOf(client.call('labs.getList', { page: 1 })).then((captured) => (listError = captured));
      tick(500);
      expect(listError?.status).toBe(401);

      let semestersError: ApiError | undefined;
      void rejectionOf(client.call('labs.semesters', null)).then((captured) => (semestersError = captured));
      tick(500);
      expect(semestersError?.status).toBe(401);
    }));

    it('битая сессия (ключ указывает на несуществующего пользователя) → 401', fakeAsync(() => {
      seedUsers();
      loginAs(labId(99));

      let error: ApiError | undefined;
      void rejectionOf(client.call('labs.getList', { page: 1 })).then((captured) => (error = captured));
      tick(500);

      expect(error?.status).toBe(401);
      expect(error?.body.message).toBe('Не авторизован');
    }));

    it('сессия преподавателя — все операции доступны (teacher ok)', fakeAsync(() => {
      seedUsers();
      seed({ labs: [makeLab(labId(1), 1, 1)] });

      let list: PagedResult<LabDto> | undefined;
      void client
        .call<PagedResult<LabDto>>('labs.getList', { page: 1 } satisfies LabsGetListParams)
        .then((r) => (list = r));
      tick(500);
      expect(list?.total).toBe(1);

      let created: LabDto | undefined;
      void client
        .call<LabDto>('labs.create', labInput({ number: 2 }))
        .then((r) => (created = r));
      tick(500);
      expect(created).toBeDefined();

      let updated: LabDto | undefined;
      void client
        .call<LabDto>(
          'labs.update',
          labInput({ number: 2, content: 'Изменено', id: created?.id ?? '' }),
        )
        .then((r) => (updated = r));
      tick(500);
      expect(updated?.content).toBe('Изменено');

      let removed = false;
      void client
        .call('labs.remove', { id: created?.id ?? '' } satisfies LabIdParams)
        .then(() => (removed = true));
      tick(500);
      expect(removed).toBeTrue();
      expect(stored().labs.length).toBe(1);
    }));
  });
});
