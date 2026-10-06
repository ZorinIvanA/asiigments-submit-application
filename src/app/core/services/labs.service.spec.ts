/**
 * Юнит-тесты LabsService (C-102, контракт IF-103, FR-4.3/FR-4.4):
 *  - делегирование мок-методам: имя метода и параметры вызова client.call
 *    (update склеивает id и поля формы в плоские params);
 *  - константы контракта: LABS_PAGE_SIZE=10, MAX_SEMESTER=10 (shared/models);
 *  - сквозной прогон через реальный мок (create → getById → update →
 *    getList → remove) на teacher-сессии;
 *  - проброс ApiError (403 «Доступ запрещён» студенту) через сервис.
 */
import { fakeAsync, tick } from '@angular/core/testing';
import { TestBed } from '@angular/core/testing';

import {
  LabInput,
  LabsGetListParams,
  LABS_PAGE_SIZE,
  registerLabsHandlers,
} from '../../mock/labs/handlers';
import { MockApiClient } from '../../mock/mock-api-client';
import { emptyMockDbData, resetMockDbSeed } from '../../mock/mock-db';
import { ApiError, LabDto, MAX_SEMESTER, PagedResult, STORAGE_KEYS } from '../../shared/models';
import { LabsService } from './labs.service';

const TEACHER_ID = 'aaaaaaaa-0000-4000-8000-000000000001';
const STUDENT_ID = 'aaaaaaaa-0000-4000-8000-000000000002';
const LAB_ID = 'bbbbbbbb-0000-4000-8000-000000000001';

const teacher = {
  id: TEACHER_ID,
  login: 'teacher',
  email: 'teacher@example.com',
  fullName: 'Сидоров Семён Семёнович',
  role: 'teacher' as const,
  groupId: null,
  password: 'teacher123!',
};

const student = {
  id: STUDENT_ID,
  login: 'student01',
  email: 'student01@example.com',
  fullName: 'Иванов Иван Иванович 01',
  role: 'student' as const,
  groupId: null,
  password: 'student123!',
};

const labInput = (overrides?: Partial<LabInput>): LabInput => ({
  number: 21,
  semester: 1,
  content: 'Содержание новой работы',
  assignmentUrl: null,
  defenseRequired: false,
  ...overrides,
});

describe('LabsService — контракт IF-103', () => {
  let service: LabsService;
  let client: MockApiClient;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    resetMockDbSeed();
    client = new MockApiClient();
    registerLabsHandlers(client);
    TestBed.configureTestingModule({ providers: [{ provide: MockApiClient, useValue: client }] });
    service = TestBed.inject(LabsService);
  });

  afterEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    resetMockDbSeed();
  });

  describe('делегирование client.call', () => {
    it('getList передаёт метод labs.getList и параметры как есть', async () => {
      const params: LabsGetListParams = { semester: 2, page: 3, sortField: 'number', sortDir: 'desc' };
      const page: PagedResult<LabDto> = { items: [], total: 0, page: 3, pageSize: LABS_PAGE_SIZE };
      const spy = spyOn(client, 'call').and.resolveTo(page);

      const result = await service.getList(params);

      expect(spy).toHaveBeenCalledOnceWith('labs.getList', params);
      expect(result).toBe(page);
    });

    it('getById передаёт {id}; create — вход формы без изменений', async () => {
      const spy = spyOn(client, 'call').and.resolveTo(null);

      await service.getById(LAB_ID);
      expect(spy).toHaveBeenCalledWith('labs.getById', { id: LAB_ID });

      await service.create(labInput());
      expect(spy).toHaveBeenCalledWith('labs.create', labInput());
    });

    it('update склеивает id и поля формы в плоские params', async () => {
      const spy = spyOn(client, 'call').and.resolveTo(null);

      await service.update(LAB_ID, labInput({ number: 7, semester: 3 }));

      expect(spy).toHaveBeenCalledOnceWith('labs.update', {
        number: 7,
        semester: 3,
        content: 'Содержание новой работы',
        assignmentUrl: null,
        defenseRequired: false,
        id: LAB_ID,
      });
    });

    it('remove передаёт {id}; getSemesters — labs.semesters с params null', async () => {
      const spy = spyOn(client, 'call').and.resolveTo(null);

      await service.remove(LAB_ID);
      expect(spy).toHaveBeenCalledWith('labs.remove', { id: LAB_ID });

      await service.getSemesters();
      expect(spy).toHaveBeenCalledWith('labs.semesters', null);
    });

    it('константы контракта: LABS_PAGE_SIZE=10, MAX_SEMESTER=10', () => {
      expect(LABS_PAGE_SIZE).toBe(10);
      expect(MAX_SEMESTER).toBe(10);
    });
  });

  describe('сквозной прогон через реальный мок', () => {
    const seedTeacher = (): void => {
      localStorage.setItem(
        STORAGE_KEYS.mockDb,
        JSON.stringify({ ...emptyMockDbData(), users: [teacher] }),
      );
      localStorage.setItem(STORAGE_KEYS.session, TEACHER_ID);
    };

    it('teacher: create → getById → update → getList → remove без отказов', fakeAsync(() => {
      seedTeacher();

      let created: LabDto | undefined;
      void service.create(labInput({ assignmentUrl: 'https://git.example.com/a' })).then((r) => (created = r));
      tick(500);
      expect(created?.id).toBeDefined();

      let loaded: LabDto | undefined;
      void service.getById(created?.id ?? '').then((r) => (loaded = r));
      tick(500);
      expect(loaded).toEqual(created);

      let updated: LabDto | undefined;
      void service
        .update(created?.id ?? '', labInput({ number: 21, content: 'Изменено', defenseRequired: true }))
        .then((r) => (updated = r));
      tick(500);
      expect(updated?.content).toBe('Изменено');
      expect(updated?.defenseRequired).toBeTrue();

      let page: PagedResult<LabDto> | undefined;
      void service.getList({ semester: 1, page: 1 }).then((r) => (page = r));
      tick(500);
      expect(page?.total).toBe(1);
      expect(page?.pageSize).toBe(10);

      let removed = false;
      void service.remove(created?.id ?? '').then(() => (removed = true));
      tick(500);
      expect(removed).toBeTrue();

      let semesters: number[] | undefined;
      void service.getSemesters().then((r) => (semesters = r));
      tick(500);
      expect(semesters).toEqual([]);
    }));

    it('студенту create отказывает 403 «Доступ запрещён» — ApiError пробрасывается сервисом', fakeAsync(() => {
      localStorage.setItem(
        STORAGE_KEYS.mockDb,
        JSON.stringify({ ...emptyMockDbData(), users: [teacher, student] }),
      );
      localStorage.setItem(STORAGE_KEYS.session, STUDENT_ID);

      let error: ApiError | undefined;
      service.create(labInput()).catch((captured: unknown) => (error = captured as ApiError));
      tick(500);

      expect(error?.status).toBe(403);
      expect(error?.body.message).toBe('Доступ запрещён');
    }));
  });
});
