/**
 * SubmissionsService — клиент мок-домена сдач (C-105, контракт IF-106, FR-4.4):
 * ведомость группы×семестр с пагинацией по студентам (US-10), проставление/
 * сброс дат сдачи/защиты (upsert) и сдачи текущего студента (US-11).
 *
 * Страницы (C-112/C-113) обращаются к данным сдач только через этот сервис:
 * каждый метод — мок-вызов MockApiClient (задержка ~500 мс, NFR-001), отказ
 * приходит как ApiError {status, body: {message, errors?}} (FR-003):
 *  - 404 «Группа не найдена» / «Студент не найден» / «Лабораторная не найдена»;
 *  - 403 «Доступ запрещён» (getGrid/update — не teacher, getMy — не student);
 *  - 400 «Данные заполнены неверно» — неконтрактная строка даты в update.
 * Даты — строки 'YYYY-MM-DD' либо null (IF-011); конвертация в Date и
 * отображение — на стороне контролов p-datepicker и shared/dates (ADR-007).
 */
import { Injectable, inject } from '@angular/core';

import { MockApiClient } from '../../mock/mock-api-client';
import { MySubmissionsDto, Submission, SubmissionsGridDto } from '../../shared/models';

/** Студентов на страницу ведомости (§4.4: пагинация по 5, ADR-109). */
export const SUBMISSIONS_PAGE_SIZE = 5;

/** Параметры выборки ведомости (IF-106: getGrid({groupId, semester, page})). */
export interface SubmissionsGridQuery {
  /** uuid группы. */
  groupId: string;
  /** Номер семестра, чьи работы образуют колонки ведомости. */
  semester: number;
  /** Номер страницы студентов, 1-based (ADR-109). */
  page: number;
}

/**
 * Ответ getGrid (IF-106 + ADR-109 аддендум 1): DTO ведомости из models.ts
 * с обязательным полем page — НОРМАЛИЗОВАННЫЙ номер страницы из мока
 * (page<1/нечисловой → 1; эхо исходного некорректного значения запрещено).
 */
export type SubmissionsGridResult = SubmissionsGridDto & { page: number };

/** Параметры update (IF-106): обе даты передаются целиком, null = сброс. */
export interface SubmissionUpdateParams {
  /** uuid студента (User с role = student). */
  studentId: string;
  /** uuid лабораторной работы. */
  labId: string;
  /** Дата сдачи 'YYYY-MM-DD' либо null = сброс. */
  submitDate: string | null;
  /** Дата защиты 'YYYY-MM-DD' либо null = сброс. */
  defenseDate: string | null;
}

@Injectable({ providedIn: 'root' })
export class SubmissionsService {
  private readonly api = inject(MockApiClient);

  /**
   * Ведомость группы по семестру (мок GET /api/v1/submissions): страница
   * студентов группы (SUBMISSIONS_PAGE_SIZE на страницу, порядок fullName↑,
   * затем login↑), работы семестра (number↑), записи сдач только для пар
   * студент-работа текущей страницы и total — полное число студентов группы.
   * query.page — 1-based (ADR-109); в ответе page — нормализованное значение
   * (ADR-109 аддендум 1: page<1/нечисловой → 1, без эха исходного).
   */
  getGrid(query: SubmissionsGridQuery): Promise<SubmissionsGridResult> {
    return this.api.call<SubmissionsGridResult>('submissions.getGrid', query);
  }

  /**
   * Проставление/сброс дат (мок PUT /api/v1/submissions): upsert по паре
   * (studentId, labId) — записи нет: создаётся; есть: обе даты заменяются
   * переданными значениями (null = сброс). Мок фиксирует updated_by
   * (преподаватель сессии) и updated_at. Возвращает сохранённую запись.
   */
  update(params: SubmissionUpdateParams): Promise<Submission> {
    return this.api.call<Submission>('submissions.update', params);
  }

  /**
   * Сдачи текущего студента по семестру (мок GET /api/v1/me/submissions):
   * hasGroup=false и пустые массивы, если студент не включён в группу
   * (§4.4 — предупреждение вместо таблицы); иначе работы семестра (number↑)
   * и только собственные сдачи.
   */
  getMy(semester: number): Promise<MySubmissionsDto> {
    return this.api.call<MySubmissionsDto>('submissions.getMy', { semester });
  }
}
