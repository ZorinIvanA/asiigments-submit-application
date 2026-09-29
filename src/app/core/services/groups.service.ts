/**
 * GroupsService — API раздела «Группы» (C-103, контракт IF-104, US-16 §4.5):
 * список групп с вычисляемым studentCount, создание/переименование/удаление
 * и состав группы с пагинацией. Единственный источник данных — мок-слой
 * (FR-002, ADR-101): каждый метод — один мок-вызов через MockApiClient
 * с задержкой и трансляцией отказов в ApiError {status, body}.
 *
 * Все методы — teacher-only на стороне мока: не-преподаватель получает
 * 403 «Доступ запрещён», отсутствующая сессия — 401 (IF-101).
 *
 * Коды ошибок — дословно IF-104:
 *  - 400 «Данные заполнены неверно» + errors {name} — имя пустое/пробелы
 *    или длиннее 100 символов;
 *  - 409 «Группа с таким названием уже существует» — дубликат без учёта
 *    регистра;
 *  - 404 «Группа не найдена» — rename/remove/getStudents несуществующего id;
 *  - 403 «Доступ запрещён» — роль отлична от teacher;
 *  - 401 «Не авторизован» — нет сессии (контракт IF-101).
 */
import { Injectable, inject } from '@angular/core';

import { GroupDto, PagedResult, StudentDto } from '../../shared/models';
import { MockApiClient } from '../../mock/mock-api-client';

/**
 * Размер страницы состава группы (ADR-109: группы/студенты/состав группы — 10,
 * страницы 1-based; нарезка выполняется моком над полной выборкой).
 */
export const GROUPS_PAGE_SIZE = 10;

@Injectable({ providedIn: 'root' })
export class GroupsService {
  private readonly api = inject(MockApiClient);

  /** Список групп, порядок name↑ без учёта регистра; studentCount вычисляется. */
  getList(): Promise<GroupDto[]> {
    return this.api.call<GroupDto[]>('groups.getList', {});
  }

  /** Создаёт группу с уникальным (ci) названием 1–100 символов. */
  create(name: string): Promise<GroupDto> {
    return this.api.call<GroupDto>('groups.create', { name });
  }

  /** Переименовывает группу; дубликат (ci) чужого названия — 409. */
  rename(id: string, name: string): Promise<GroupDto> {
    return this.api.call<GroupDto>('groups.rename', { id, name });
  }

  /**
   * Удаляет группу; студенты группы сохраняются с groupId=null (§4.5 —
   * теряют доступ к сдаче, попадают в фильтр «без группы»).
   */
  remove(id: string): Promise<void> {
    return this.api.call<void>('groups.remove', { id });
  }

  /** Страница состава группы: pageSize=10, порядок fullName↑ затем login↑. */
  getStudents(id: string, page: number): Promise<PagedResult<StudentDto>> {
    return this.api.call<PagedResult<StudentDto>>('groups.getStudents', { id, page });
  }
}
