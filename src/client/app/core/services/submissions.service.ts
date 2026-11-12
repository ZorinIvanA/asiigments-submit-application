/**
 * SubmissionsService — клиент REST-домена сдач (C-014, FR-091):
 * ведомость группы×семестр с пагинацией по студентам (US-10), проставление/
 * сброс дат сдачи/защиты (upsert) и сдачи текущего студента (US-11).
 *
 * Страницы (C-112/C-113) обращаются к данным сдач только через этот сервис:
 * каждый метод — вызов HttpClient к эндпойнтам REST ведомости (submissions
 * и me/submissions); базовый префикс — токен API_BASE_URL (ADR-009),
 * запросы идут с withCredentials: true через authInterceptor (C-013),
 * HTTP-отказы нормализуются им в ApiError {status, body: {message, errors?}}
 * (http-errors.ts) — прежняя форма отказов страниц не меняется. Коды ошибок
 * — дословно контрактам REST /submissions и /me/submissions:
 *  - 401 «Не авторизован» — нет/битая сессия;
 *  - 403 «Доступ запрещён» (getGrid/update — не teacher, getMy — не student);
 *  - 400 «Данные заполнены неверно» + errors {submitDate, defenseDate} —
 *    неконтрактная строка даты в update (проверяется раньше 404);
 *  - 404 «Группа не найдена» (getGrid), «Студент не найден» /
 *    «Лабораторная не найдена» (update).
 * Даты — строки 'YYYY-MM-DD' либо null (контракт PUT /submissions: обе даты
 * всегда передаются целиком, null = сброс); конвертация в Date и отображение
 * — на стороне контролов p-datepicker и shared/dates (ADR-007).
 */
import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { API_BASE_URL } from '../api-base-url';
import { MySubmissionsDto, Submission, SubmissionsGridDto } from '../../shared/models';

/** Студентов на страницу ведомости (§4.4: пагинация по 5, ADR-109). */
export const SUBMISSIONS_PAGE_SIZE = 5;

/** Параметры выборки ведомости (GET /submissions: getGrid({groupId, semester, page})). */
export interface SubmissionsGridQuery {
  /** uuid группы. */
  groupId: string;
  /** Номер семестра, чьи работы образуют колонки ведомости. */
  semester: number;
  /** Номер страницы студентов, 1-based (ADR-109). */
  page: number;
}

/**
 * Ответ getGrid (GET /submissions + ADR-109 аддендум 1): DTO ведомости из
 * models.ts с обязательным полем page — номер страницы из ответа бэкенда
 * (page<1/нечисловой → 1; эхо исходного некорректного значения запрещено).
 */
export type SubmissionsGridResult = SubmissionsGridDto & { page: number };

/** Параметры update (PUT /submissions): обе даты передаются целиком, null = сброс. */
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
  private readonly http = inject(HttpClient);
  private readonly apiBase = inject(API_BASE_URL);

  /**
   * Ведомость группы по семестру (GET /submissions): страница студентов
   * группы (SUBMISSIONS_PAGE_SIZE на страницу, порядок fullName↑, затем
   * login↑), работы семестра (number↑), записи сдач только для пар
   * студент-работа текущей страницы, total — полное число студентов группы
   * и page — нормализованный номер страницы. Query собирается в порядке
   * groupId, semester, page — все три параметра обязательны (ADR-109:
   * query.page 1-based).
   */
  getGrid(query: SubmissionsGridQuery): Promise<SubmissionsGridResult> {
    const params = new HttpParams()
      .set('groupId', query.groupId)
      .set('semester', query.semester)
      .set('page', query.page);
    return firstValueFrom(
      this.http.get<SubmissionsGridResult>(`${this.apiBase}/submissions`, { params }),
    );
  }

  /**
   * Проставление/сброс дат (PUT /submissions): upsert по паре (studentId,
   * labId) — записи нет: создаётся; есть: обе даты заменяются переданными
   * значениями (null = сброс). Тело всегда содержит обе даты целиком
   * (контракт PUT /submissions — частичной формы нет). Бэкенд фиксирует
   * updatedBy (преподаватель сессии) и updatedAt; возвращает сохранённую
   * запись.
   */
  update(params: SubmissionUpdateParams): Promise<Submission> {
    return firstValueFrom(this.http.put<Submission>(`${this.apiBase}/submissions`, params));
  }

  /**
   * Сдачи текущего студента по семестру (GET /me/submissions?semester):
   * hasGroup=false и пустые массивы, если студент не включён в группу
   * (§4.4 — предупреждение вместо таблицы); иначе работы семестра (number↑)
   * и только собственные сдачи.
   */
  getMy(semester: number): Promise<MySubmissionsDto> {
    return firstValueFrom(
      this.http.get<MySubmissionsDto>(`${this.apiBase}/me/submissions`, {
        params: new HttpParams().set('semester', semester),
      }),
    );
  }
}
