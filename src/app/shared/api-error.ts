/**
 * Утилиты работы с отказами мок-вызовов (C-010, IF-001/IF-101): проверка
 * «это транспортная форма ошибки бэкенда?» и приведение произвольного
 * отказа к ApiError. Потребители — сервисы core и страницы: catch-блоки
 * вокруг client.call единообразно различают эмулируемый HTTP-отказ
 * (ApiError из shared/models, FR-003) и прочие исключения (дефекты кода,
 * не являющиеся ответами «бэкенда» — см. mock-api-client.ts).
 */
import { ApiError } from './models';

/** Ошибка формы ApiError: числовой статус + тело {message, errors?}. */
export function isApiError(value: unknown): value is ApiError {
  if (typeof value !== 'object' || value === null) {
    return false;
  }
  const candidate = value as Partial<ApiError>;
  return (
    typeof candidate.status === 'number' &&
    Number.isFinite(candidate.status) &&
    typeof candidate.body === 'object' &&
    candidate.body !== null &&
    typeof candidate.body.message === 'string' &&
    (candidate.body.errors === undefined ||
      typeof candidate.body.errors === 'object')
  );
}

/**
 * Статус ApiError для отказа, не являющегося ответом «бэкенда»
 * (не пойманное транспортной формой исключение): вне набора эмулируемых
 * HTTP-кодов, чтобы не совпадать ни с одной проверкой status === 4xx/5xx.
 */
export const NON_HTTP_ERROR_STATUS = 0;

/**
 * Приводит произвольный отказ к ApiError: распознанная транспортная форма
 * возвращается как есть; Error — с его message; прочее — с запасным
 * сообщением (по умолчанию «Неизвестная ошибка»).
 */
export function toApiError(
  value: unknown,
  fallbackMessage = 'Неизвестная ошибка',
): ApiError {
  if (isApiError(value)) {
    return value;
  }
  if (value instanceof Error) {
    return { status: NON_HTTP_ERROR_STATUS, body: { message: value.message } };
  }
  return { status: NON_HTTP_ERROR_STATUS, body: { message: fallbackMessage } };
}
