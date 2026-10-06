/**
 * Юнит-тесты SubmissionsService (C-105, IF-106): делегирование мок-вызовам
 * MockApiClient с дословными именами методов и параметрами контракта,
 * константа SUBMISSIONS_PAGE_SIZE=5 (§4.4, ADR-109). Сам MockApiClient
 * подменяется шпионом — конвейер задержки и обработчики покрыты
 * в mock/submissions/handlers.spec.ts и mock/mock-api-client.spec.ts.
 */
import { TestBed } from '@angular/core/testing';

import { MockApiClient } from '../../mock/mock-api-client';
import { MySubmissionsDto, Submission } from '../../shared/models';
import {
  SUBMISSIONS_PAGE_SIZE,
  SubmissionsGridQuery,
  SubmissionsGridResult,
  SubmissionsService,
} from './submissions.service';

describe('SubmissionsService — клиент мок-домена сдач (IF-106)', () => {
  let service: SubmissionsService;
  let api: MockApiClient;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    TestBed.configureTestingModule({});
    service = TestBed.inject(SubmissionsService);
    api = TestBed.inject(MockApiClient);
  });

  afterEach(() => {
    localStorage.clear();
    sessionStorage.clear();
  });

  it('SUBMISSIONS_PAGE_SIZE = 5 (§4.4, ADR-109)', () => {
    expect(SUBMISSIONS_PAGE_SIZE).toBe(5);
  });

  it('getGrid → call("submissions.getGrid", query) и возвращает ведомость с нормализованным page', async () => {
    const dto: SubmissionsGridResult = {
      students: [{ id: 'a0000000-0000-4000-8000-000000000001', fullName: 'Фамилия А' }],
      labs: [{ id: 'd0000000-0000-4000-8000-000000000001', number: 1, defenseRequired: true }],
      submissions: [
        {
          studentId: 'a0000000-0000-4000-8000-000000000001',
          labId: 'd0000000-0000-4000-8000-000000000001',
          submitDate: '2026-09-01',
          defenseDate: null,
        },
      ],
      total: 1,
      page: 2,
    };
    const spy = spyOn(api, 'call').and.resolveTo(dto);

    const query: SubmissionsGridQuery = {
      groupId: 'a0000000-0000-4000-8000-000000000001',
      semester: 1,
      page: 2,
    };
    const result = await service.getGrid(query);

    expect(spy).toHaveBeenCalledOnceWith('submissions.getGrid', query);
    expect(result).toBe(dto);
    expect(result.page).toBe(2);
  });

  it('update → call("submissions.update", params) с параметрами как есть и возвращает запись', async () => {
    const params = {
      studentId: 'b0000000-0000-4000-8000-000000000002',
      labId: 'd0000000-0000-4000-8000-000000000002',
      submitDate: '2026-09-15' as string | null,
      defenseDate: null as string | null,
    };
    const saved: Submission = {
      id: 'f0000000-0000-4000-8000-000000000001',
      studentId: params.studentId,
      labId: params.labId,
      submitDate: '2026-09-15',
      defenseDate: null,
      updatedAt: '2026-09-27T12:00:00.000Z',
      updatedBy: 'b0000000-0000-4000-8000-000000000001',
    };
    const spy = spyOn(api, 'call').and.resolveTo(saved);

    const result = await service.update(params);

    expect(spy).toHaveBeenCalledOnceWith('submissions.update', params);
    expect(result).toBe(saved);
  });

  it('getMy → call("submissions.getMy", {semester}) и возвращает MySubmissionsDto', async () => {
    const dto: MySubmissionsDto = { hasGroup: false, labs: [], submissions: [] };
    const spy = spyOn(api, 'call').and.resolveTo(dto);

    const result = await service.getMy(3);

    expect(spy).toHaveBeenCalledOnceWith('submissions.getMy', { semester: 3 });
    expect(result).toBe(dto);
  });
});
