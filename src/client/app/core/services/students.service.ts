/**
 * StudentsService — клиент REST-домена студентов (C-014, FR-091, FR-4.6/US-17):
 * список студентов раздела «Доступ» и назначение группы. Единственная точка
 * доступа страниц к эндпойнтам students (снаружи сервисы core). Базовый
 * префикс — токен API_BASE_URL (ADR-009); запросы идут с withCredentials:
 * true через authInterceptor (C-013), HTTP-отказы нормализуются им в ApiError
 * {status, body: {message, errors?}} (http-errors.ts) — прежняя форма отказов
 * страниц не меняется. Коды ошибок — дословно контрактам REST домена students:
 *  - 401 «Не авторизован» — нет/битая сессия;
 *  - 403 «Доступ запрещён» — роль не teacher;
 *  - 400 «Данные заполнены неверно» + errors {search} — search длиннее
 *    200 символов;
 *  - 404 «Студент не найден» / «Группа не найдена» — setGroup.
 */
import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { API_BASE_URL } from '../api-base-url';
import {
  PagedResult,
  STUDENTS_PAGE_SIZE,
  StudentDto,
  StudentsGetListParams as StudentListQuery,
} from '../../shared/models';

/**
 * Размер страницы списка студентов (IF-105, ADR-109). Реэкспорт константы
 * из shared/models — единый источник значения для сервиса и страниц
 * (FR-090).
 */
export { STUDENTS_PAGE_SIZE };

/**
 * Типы контракта списка студентов — реэкспорт единого источника
 * (shared/models) вместо дублирующих определений; имя StudentListQuery
 * сохранено для страниц фич (доступ/карточка группы).
 */
export type { StudentListQuery };

@Injectable({ providedIn: 'root' })
export class StudentsService {
  private readonly http = inject(HttpClient);
  private readonly apiBase = inject(API_BASE_URL);

  /**
   * Постраничный список студентов (GET /students): поиск/фильтр/сортировка
   * и нарезка страницы — на бэкенде. Query собирается в порядке search,
   * groupId, page; отсутствующие значения в query не попадают: search
   * без строки (null/undefined) или без непробельных символов — «без
   * поиска» (трим и токенизация — правила бэкенда, значение уходит как
   * есть, включая пробелы многословного запроса); groupId null/undefined —
   * «без фильтра», 'none' — только студенты без группы, uuid — только эта
   * группа (несуществующий uuid — пустая выборка, не 404).
   */
  getList(query: StudentListQuery): Promise<PagedResult<StudentDto>> {
    let params = new HttpParams();
    if (
      query.search !== undefined &&
      query.search !== null &&
      query.search.trim() !== ''
    ) {
      params = params.set('search', query.search);
    }
    if (query.groupId !== undefined && query.groupId !== null) {
      params = params.set('groupId', query.groupId);
    }
    params = params.set('page', query.page);
    return firstValueFrom(
      this.http.get<PagedResult<StudentDto>>(`${this.apiBase}/students`, { params }),
    );
  }

  /**
   * Назначение группы студенту (PUT /students/{id}/group, тело {groupId}):
   * uuid — включение/перевод, null — исключение из группы; успех → 204.
   * Идентификатор студента уходит в путь запроса, не в тело.
   */
  setGroup(studentId: string, groupId: string | null): Promise<void> {
    return firstValueFrom(
      this.http.put<void>(`${this.apiBase}/students/${studentId}/group`, { groupId }),
    );
  }
}
