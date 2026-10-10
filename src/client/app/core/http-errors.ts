/**
 * Нормализация HTTP-отказов в существующую форму ApiError (C-013, контракт
 * IF-014, FR-025): отказы HttpClient (HttpErrorResponse) преобразуются к
 * ApiError {status, body: {message, errors?}} — существующая логика показа
 * баннеров (body.message) и распознавания полевых ошибок (body.errors)
 * работает без изменений. Сетевой отказ (нет ответа бэкенда) получает
 * статус NON_HTTP_ERROR_STATUS — вне набора HTTP-кодов, чтобы не совпадать
 * ни с одной проверкой status === 4xx/5xx (та же семантика, что у
 * toApiError в shared/api-error).
 */
import { HttpErrorResponse } from '@angular/common/http';

import { isApiError, toApiError } from '../shared/api-error';
import { ApiError } from '../shared/models';

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

/**
 * Тело ApiError из транспортного тела ответа: message — строка; errors
 * переносится при наличии (400 «Данные заполнены неверно»). Тело не по
 * форме (текст, объект без message) — запасное сообщение toApiError.
 */
function extractErrorBody(raw: unknown): ApiError['body'] {
  if (isRecord(raw) && typeof raw['message'] === 'string') {
    const body: ApiError['body'] = { message: raw['message'] };
    if (isRecord(raw['errors'])) {
      body.errors = raw['errors'] as Record<string, string[]>;
    }
    return body;
  }
  return { message: toApiError(raw).body.message };
}

/**
 * Приводит произвольный отказ цепочки HttpClient к ApiError: уже готовая
 * транспортная форма возвращается как есть; HttpErrorResponse — по статусу
 * и телу ответа; прочее (программные ошибки) — как нетранспортный отказ
 * со статусом NON_HTTP_ERROR_STATUS.
 */
export function normalizeHttpError(error: unknown): ApiError {
  if (isApiError(error)) {
    return error;
  }
  if (error instanceof HttpErrorResponse) {
    return { status: error.status, body: extractErrorBody(error.error) };
  }
  return toApiError(error);
}
