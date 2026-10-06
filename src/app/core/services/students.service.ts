/**
 * StudentsService — фасад домена Students поверх мок-слоя (C-104, IF-105,
 * FR-4.6/US-17): список студентов раздела «Доступ» и назначение группы.
 *
 * Единственный слой, который страницы вызывают для работы со студентами:
 * наружу мок-слой импортируют только сервисы core (ADR-003). Все коды
 * ошибок контракта приходят от мока как ApiError {status, body} и
 * пробрасываются вызывающей странице как есть: 400 «Данные заполнены
 * неверно» + errors {search}, 401 «Не авторизован», 403 «Доступ запрещён»,
 * 404 «Студент не найден» / «Группа не найдена».
 */
import { Injectable, inject } from '@angular/core';

import { MockApiClient } from '../../mock/mock-api-client';
import {
  STUDENTS_PAGE_SIZE,
  StudentsGetListParams as StudentListQuery,
  StudentsSetGroupParams,
} from '../../mock/students/handlers';
import { PagedResult, StudentDto } from '../../shared/models';

/**
 * Размер страницы списка студентов (IF-105, ADR-109). Реэкспорт константы
 * мок-обработчиков — единый источник значения для мока, сервиса и страниц.
 */
export { STUDENTS_PAGE_SIZE };

/**
 * Типы контракта IF-105 — реэкспорт единого источника (мок-обработчики
 * домена) вместо дублирующих определений; имя StudentListQuery сохранено
 * для страниц фич (T-114/T-115).
 */
export type { StudentListQuery };

@Injectable({ providedIn: 'root' })
export class StudentsService {
  private readonly client = inject(MockApiClient);

  /**
   * Постраничный список студентов (IF-105): поиск/фильтр/порядок ФИО↑ login↑
   * мок применяет к полной выборке до нарезки страницы; порядок от поиска
   * и фильтра не меняется.
   */
  getList(query: StudentListQuery): Promise<PagedResult<StudentDto>> {
    return this.client.call<PagedResult<StudentDto>>('students.getList', query);
  }

  /**
   * Назначение группы студенту (IF-105): uuid — включение/перевод,
   * null — исключение из группы; единственная точка изменения users.group_id,
   * включение в группу немедленно даёт доступ к данным ведомости (§4.6).
   */
  setGroup(studentId: string, groupId: string | null): Promise<void> {
    const params: StudentsSetGroupParams = { studentId, groupId };
    return this.client.call<void>('students.setGroup', params);
  }
}
