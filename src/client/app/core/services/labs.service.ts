/**
 * LabsService — клиент REST-домена лабораторных работ (C-014, FR-091,
 * FR-4.3/FR-4.4): список с фильтром/сортировкой/пагинацией, карточка,
 * создание/редактирование/удаление и перечень семестров с работами.
 * Единственная точка доступа страниц к эндпойнтам labs (снаружи сервисы
 * core). Базовый префикс — токен API_BASE_URL (ADR-009); запросы идут
 * с withCredentials: true через authInterceptor (C-013), HTTP-отказы
 * нормализуются им в ApiError {status, body: {message, errors?}}
 * (http-errors.ts) — прежняя форма отказов страниц не меняется.
 * Коды ошибок — дословно контрактам REST эндпойнтов labs* и semesters:
 *  - 401 «Не авторизован» — нет/битая сессия;
 *  - 403 «Доступ запрещён» — роль не teacher (кроме GET /semesters);
 *  - 400 «Данные заполнены неверно» + errors {number, semester, content,
 *    assignmentUrl} — нарушение правил §8;
 *  - 409 «Лабораторная с таким номером уже есть в семестре» — занятая
 *    пара (semester, number);
 *  - 404 «Лабораторная не найдена» — getById/update/remove чужого id.
 */
import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { API_BASE_URL } from '../api-base-url';
import {
  LabDto,
  LabInput,
  LabsGetListParams,
  LabsSortDir,
  LabsSortField,
  LabsUpdateParams,
  PagedResult,
} from '../../shared/models';

/**
 * Размер страницы списка лабораторных — контракт §4.3 (pageSize=10).
 * Реэкспорт из shared/models (новое место константы, T-018/FR-090:
 * единый источник значения); страницы импортируют константу отсюда.
 */
export { LABS_PAGE_SIZE } from '../../shared/models';

/** Типы контракта labs — реэкспорт для страниц фичи works (T-112/T-113). */
export type { LabInput, LabsGetListParams, LabsSortDir, LabsSortField, LabsUpdateParams };

@Injectable({ providedIn: 'root' })
export class LabsService {
  private readonly http = inject(HttpClient);
  private readonly apiBase = inject(API_BASE_URL);

  /**
   * Страница списка лабораторных (GET /labs): фильтр/сортировка/нарезка —
   * на бэкенде, pageSize фиксирован (LABS_PAGE_SIZE). Query собирается в
   * порядке semester, page, sortField, sortDir; отсутствующие значения
   * (semester null — «все семестры», сортировка не выбрана) в query не
   * попадают — бэкенд применяет дефолты контракта (без фильтра,
   * semester↑,number↑). Подпись «Показать записи с X по Y из Z» страницы
   * вычисляют из page/pageSize/total.
   */
  getList(params: LabsGetListParams): Promise<PagedResult<LabDto>> {
    let query = new HttpParams();
    if (params.semester !== null && params.semester !== undefined) {
      query = query.set('semester', params.semester);
    }
    query = query.set('page', params.page);
    if (params.sortField !== undefined) {
      query = query.set('sortField', params.sortField);
    }
    if (params.sortDir !== undefined) {
      query = query.set('sortDir', params.sortDir);
    }
    return firstValueFrom(
      this.http.get<PagedResult<LabDto>>(`${this.apiBase}/labs`, { params: query }),
    );
  }

  /** Лабораторная по id (GET /labs/{id}) — deep-link/refresh формы. */
  getById(id: string): Promise<LabDto> {
    return firstValueFrom(this.http.get<LabDto>(`${this.apiBase}/labs/${id}`));
  }

  /** Создание лабораторной (POST /labs, 201) — успех: созданная запись. */
  create(input: LabInput): Promise<LabDto> {
    return firstValueFrom(this.http.post<LabDto>(`${this.apiBase}/labs`, input));
  }

  /** Обновление лабораторной (PUT /labs/{id}, тело как у POST) — успех: обновлённая запись. */
  update(id: string, input: LabInput): Promise<LabDto> {
    return firstValueFrom(this.http.put<LabDto>(`${this.apiBase}/labs/${id}`, input));
  }

  /** Удаление лабораторной (DELETE /labs/{id} → 204); записи ведомости удаляются каскадом. */
  remove(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.apiBase}/labs/${id}`));
  }

  /** Семестры, в которых есть работы (GET /semesters): distinct, по возрастанию (§4.4). */
  getSemesters(): Promise<number[]> {
    return firstValueFrom(this.http.get<number[]>(`${this.apiBase}/semesters`));
  }
}
