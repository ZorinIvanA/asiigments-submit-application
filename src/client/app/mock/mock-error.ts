/**
 * Сигнал отказа мок-обработчика (C-002, контракт IF-001).
 *
 * Доменные обработчики (T-006/T-012/T-015/T-018/T-021) сообщают об отказе
 * выбросом MockValidationError с нужным HTTP-кодом и телом; MockApiClient
 * транслирует исключение в транспортную форму ApiError {status, body:
 * {message, errors?}} и reject'ит промис вызова после задержки.
 *
 * Прочие исключения обработчиков НЕ являются ответами «бэкенда» — это дефекты
 * кода, клиент пробрасывает их наружу как есть (см. mock-api-client.ts).
 */

/** Эмулируемые HTTP-коды отказов — закрытый набор из контракта IF-001. */
export type MockErrorStatus = 400 | 401 | 403 | 404 | 409 | 429;

/**
 * Ошибка мок-обработчика: статус + сообщение + ошибки по полям.
 * Имя сохранено по тексту контракта IF-001 («обработчик бросил
 * MockValidationError с кодом и errors по полям»).
 */
export class MockValidationError extends Error {
  /** Ошибки по полям — обязательны только для 400 «Данные заполнены неверно». */
  readonly errors?: Record<string, string[]>;

  constructor(
    /** HTTP-код отказа из допустимого набора MockErrorStatus. */
    readonly status: MockErrorStatus,
    message: string,
    errors?: Record<string, string[]>,
  ) {
    super(message);
    this.name = 'MockValidationError';
    this.errors = errors;
  }
}
