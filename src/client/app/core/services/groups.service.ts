/**
 * GroupsService — клиент REST-домена групп (C-014, FR-091, FR-4.5/US-16):
 * список групп с вычисляемым studentCount, создание/переименование/удаление
 * и состав группы с пагинацией. Единственная точка доступа страниц к
 * эндпойнтам groups (снаружи сервисы core). Базовый префикс — токен
 * API_BASE_URL (ADR-009); HTTP-отказы нормализуются в ApiError
 * {status, body: {message, errors?}} (http-errors.ts) — форма отказов
 * страниц не меняется. Коды ошибок — дословно контрактам REST домена
 * groups (анонимная консоль преподавателя, без 401/403):
 *  - 400 «Данные заполнены неверно» + errors {name} — имя пустое/пробелы
 *    или длиннее 100 символов;
 *  - 409 «Группа с таким названием уже существует» — дубликат без учёта
 *    регистра;
 *  - 404 «Группа не найдена» — rename/remove/getStudents несуществующего id.
 */
import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';

import { GroupDto, PagedResult, StudentDto } from '../../shared/models';
import { firstValueFromApi } from '../http-errors';
import { API_BASE_URL } from '../api-base-url';

/**
 * Размер страницы состава группы (ADR-109: группы/студенты/состав группы — 10,
 * страницы 1-based; фильтр/сортировка/нарезка выполняются бэкендом над полной
 * выборкой). Реэкспортирующий импорт сохранён: константу импортируют отсюда
 * страница карточки группы и мок-обработчик домена groups.
 */
export const GROUPS_PAGE_SIZE = 10;

@Injectable({ providedIn: 'root' })
export class GroupsService {
  private readonly http = inject(HttpClient);
  private readonly apiBase = inject(API_BASE_URL);

  /** Список групп, порядок name↑ без учёта регистра; studentCount вычисляется. */
  getList(): Promise<GroupDto[]> {
    return firstValueFromApi(this.http.get<GroupDto[]>(`${this.apiBase}/groups`));
  }

  /** Создаёт группу с уникальным (ci) названием 1–100 символов → 201 GroupDto. */
  create(name: string): Promise<GroupDto> {
    return firstValueFromApi(this.http.post<GroupDto>(`${this.apiBase}/groups`, { name }));
  }

  /** Переименовывает группу; дубликат (ci) чужого названия — 409. */
  rename(id: string, name: string): Promise<GroupDto> {
    return firstValueFromApi(this.http.put<GroupDto>(`${this.apiBase}/groups/${id}`, { name }));
  }

  /**
   * Удаляет группу (DELETE → 204); студенты группы сохраняются с groupId=null
   * (§4.5 — теряют доступ к сдаче, попадают в фильтр «без группы»).
   */
  remove(id: string): Promise<void> {
    return firstValueFromApi(this.http.delete<void>(`${this.apiBase}/groups/${id}`));
  }

  /** Страница состава группы (GET /groups/{id}/students?page=): pageSize=10,
   *  порядок fullName↑ затем login↑; фильтрация/нарезка — на бэкенде. */
  getStudents(id: string, page: number): Promise<PagedResult<StudentDto>> {
    const query = new HttpParams().set('page', page);
    return firstValueFromApi(
      this.http.get<PagedResult<StudentDto>>(`${this.apiBase}/groups/${id}/students`, {
        params: query,
      }),
    );
  }
}
