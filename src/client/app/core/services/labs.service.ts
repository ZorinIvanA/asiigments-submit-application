/**
 * LabsService — core-сервис домена лабораторных работ (C-102, контракт
 * IF-103, FR-4.3/FR-4.4). Единственная точка доступа страниц к мок-методам
 * labs.* (FR-002: наружу мок-слой импортируют только сервисы core); отказы
 * приходят как ApiError {status, body: {message, errors?}} (FR-003), коды
 * ошибок — дословно IF-103:
 *  - 401 «Не авторизован» — нет/битая сессия;
 *  - 403 «Доступ запрещён» — роль не teacher (кроме getSemesters);
 *  - 400 «Данные заполнены неверно» + errors {number, semester, content,
 *    assignmentUrl} — нарушение правил §8 (мок валидирует validators.ts);
 *  - 409 «Лабораторная с таким номером уже есть в семестре» — существующая
 *    пара (semester, number); при update своя запись не конфликтует;
 *  - 404 «Лабораторная не найдена» — getById/update/remove несуществующего id.
 */

import { Injectable } from '@angular/core';

import { LabDto, PagedResult } from '../../shared/models';
import {
  LabInput,
  LabsGetListParams,
  LabsSortDir,
  LabsSortField,
  LabsUpdateParams,
} from '../../mock/labs/handlers';
import { MockApiClient } from '../../mock/mock-api-client';

/**
 * Размер страницы списка лабораторных — контракт IF-103/ADR-109 (§4.3:
 * pageSize=10). Реэкспорт из домена мока (единственный источник значения —
 * обработчик labs.getList); страницы импортируют константу отсюда.
 */
export { LABS_PAGE_SIZE } from '../../mock/labs/handlers';

/** Типы контракта IF-103 — реэкспорт для страниц фичи works (T-112/T-113). */
export type { LabInput, LabsGetListParams, LabsSortDir, LabsSortField, LabsUpdateParams };

@Injectable({ providedIn: 'root' })
export class LabsService {
  constructor(private readonly client: MockApiClient) {}

  /**
   * Страница списка лабораторных: фильтр по семестру (null — все) и
   * двухколоночная сортировка применяются моком до нарезки (ADR-109),
   * pageSize фиксирован (LABS_PAGE_SIZE). Подпись «Показать записи с X по Y
   * из Z» страницы вычисляют из page/pageSize/total.
   */
  getList(params: LabsGetListParams): Promise<PagedResult<LabDto>> {
    return this.client.call<PagedResult<LabDto>>('labs.getList', params);
  }

  /** Лабораторная по id — deep-link/refresh формы редактирования (ADR-104). */
  getById(id: string): Promise<LabDto> {
    return this.client.call<LabDto>('labs.getById', { id });
  }

  /** Создание лабораторной; успех — созданная запись (uuid id). */
  create(input: LabInput): Promise<LabDto> {
    return this.client.call<LabDto>('labs.create', input);
  }

  /** Обновление лабораторной; success — обновлённая запись. */
  update(id: string, input: LabInput): Promise<LabDto> {
    const params: LabsUpdateParams = { ...input, id };
    return this.client.call<LabDto>('labs.update', params);
  }

  /** Удаление лабораторной; мок каскадно удаляет её записи ведомости. */
  remove(id: string): Promise<void> {
    return this.client.call<void>('labs.remove', { id });
  }

  /** Семестры, в которых есть работы: distinct, по возрастанию (§4.4). */
  getSemesters(): Promise<number[]> {
    return this.client.call<number[]>('labs.semesters', null);
  }
}
