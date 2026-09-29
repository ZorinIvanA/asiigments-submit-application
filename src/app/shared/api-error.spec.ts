/**
 * Юнит-тесты утилит ApiError (C-101/C-010, IF-001): распознавание
 * транспортной формы ошибки, приведение произвольных отказов
 * (ApiError / Error / мусор) к ApiError, пограничные формы.
 */
import { ApiError } from './models';
import { NON_HTTP_ERROR_STATUS, isApiError, toApiError } from './api-error';

function apiError(status: number, message: string, errors?: Record<string, string[]>): ApiError {
  return errors === undefined
    ? { status, body: { message } }
    : { status, body: { message, errors } };
}

describe('shared/api-error — isApiError/toApiError (IF-001)', () => {
  describe('isApiError', () => {
    it('распознаёт транспортную форму ошибки с message', () => {
      expect(isApiError(apiError(401, 'Неверный логин или пароль'))).toBeTrue();
    });

    it('распознаёт форму с errors по полям', () => {
      expect(
        isApiError(apiError(400, 'Данные заполнены неверно', { login: ['Заполните поле'] })),
      ).toBeTrue();
    });

    it('распознаёт все статусы набора IF-001', () => {
      for (const status of [400, 401, 403, 404, 409, 429]) {
        expect(isApiError(apiError(status, `Отказ ${status}`))).withContext(`статус ${status}`).toBeTrue();
      }
    });

    it('отвергает не-объекты и объекты без обязательных полей', () => {
      const notApiErrors: unknown[] = [
        null,
        undefined,
        42,
        'ошибка',
        {},
        { status: 401 },
        { body: { message: 'текст' } },
        { status: '401', body: { message: 'текст' } },
        { status: Number.NaN, body: { message: 'текст' } },
        { status: 401, body: null },
        { status: 401, body: { message: 42 } },
        { status: 401, body: {} },
      ];
      for (const value of notApiErrors) {
        expect(isApiError(value)).withContext(JSON.stringify(value) ?? 'значение').toBeFalse();
      }
    });

    it('errors не-объектного типа отвергается', () => {
      expect(isApiError({ status: 400, body: { message: 'т', errors: 'нет' } })).toBeFalse();
    });
  });

  describe('toApiError', () => {
    it('распознанная транспортная форма возвращается той же ссылкой', () => {
      const error = apiError(409, 'Пользователь с таким логином уже существует');
      expect(toApiError(error)).toBe(error);
    });

    it('Error → ApiError со статусом вне HTTP-набора и message ошибки', () => {
      const converted = toApiError(new Error('взрыв обработчика'));
      expect(converted.status).toBe(NON_HTTP_ERROR_STATUS);
      expect(converted.body.message).toBe('взрыв обработчика');
      expect(converted.body.errors).toBeUndefined();
    });

    it('произвольное значение → запасное сообщение «Неизвестная ошибка»', () => {
      const converted = toApiError('что-то иное');
      expect(converted.status).toBe(NON_HTTP_ERROR_STATUS);
      expect(converted.body.message).toBe('Неизвестная ошибка');
    });

    it('запасное сообщение переопределяется', () => {
      expect(toApiError(undefined, 'Сбой сети').body.message).toBe('Сбой сети');
    });

    it('результат toApiError всегда проходит isApiError', () => {
      for (const value of [null, new Error('x'), { a: 1 }, 'строка'] as unknown[]) {
        expect(isApiError(toApiError(value))).toBeTrue();
      }
    });
  });
});
