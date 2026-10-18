/**
 * Юнит-тесты нормализации HTTP-отказов (C-013, контракт IF-014, FR-025):
 * произвольный отказ цепочки HttpClient приводится к существующей форме
 * ApiError {status, body: {message, errors?}} — баннеры body.message и
 * распознавание полевых ошибок body.errors работают без изменений:
 *  - HttpErrorResponse с телом {message} / {message, errors} — по статусу
 *    и телу (AC «ApiError»: 409 {message} → ApiError {status:409, ...});
 *  - тело не по форме (null, строка, объект без message) — запасное
 *    сообщение, статус сохранён;
 *  - уже готовая ApiError возвращается как есть;
 *  - отказ вне HTTP (Error, произвольное значение) — ApiError со статусом
 *    NON_HTTP_ERROR_STATUS (вне набора HTTP-кодов).
 */
import { HttpErrorResponse } from '@angular/common/http';

import { NON_HTTP_ERROR_STATUS } from '../shared/api-error';
import { ApiError } from '../shared/models';

import { normalizeHttpError } from './http-errors';

/** HttpErrorResponse с заданным статусом и телом (как от HttpClient). */
function httpError(status: number, body?: unknown): HttpErrorResponse {
  return new HttpErrorResponse({ status, statusText: 'Error', url: '/x', error: body });
}

describe('normalizeHttpError — контракт IF-014 (FR-025)', () => {
  it('409 {message} → ApiError {status: 409, body: {message}} — баннер body.message работает', () => {
    const normalized = normalizeHttpError(httpError(409, { message: 'Конфликт' }));

    expect(normalized).toEqual({ status: 409, body: { message: 'Конфликт' } });
  });

  it('400 {message, errors} → ApiError с полевыми ошибками (механизм body.errors сохранён)', () => {
    const normalized = normalizeHttpError(
      httpError(400, {
        message: 'Данные заполнены неверно',
        errors: { password: ['Минимум 8 символов'] },
      }),
    );

    expect(normalized).toEqual({
      status: 400,
      body: {
        message: 'Данные заполнены неверно',
        errors: { password: ['Минимум 8 символов'] },
      },
    });
  });

  it('errors переносится только при наличии — без errors поля в ApiError нет', () => {
    const normalized = normalizeHttpError(httpError(404, { message: 'Не найдено' }));

    expect(normalized.status).toBe(404);
    expect(normalized.body.message).toBe('Не найдено');
    expect('errors' in normalized.body ? normalized.body.errors : undefined).toBeUndefined();
  });

  it('тело null (401 без тела) — запасное сообщение, статус сохранён', () => {
    const normalized = normalizeHttpError(httpError(401, null));

    expect(normalized).toEqual({ status: 401, body: { message: 'Неизвестная ошибка' } });
  });

  it('тело-строка (500 text/html) — запасное сообщение, статус сохранён', () => {
    const normalized = normalizeHttpError(httpError(500, 'Internal Server Error'));

    expect(normalized).toEqual({ status: 500, body: { message: 'Неизвестная ошибка' } });
  });

  it('тело-объект без message — запасное сообщение, статус сохранён', () => {
    const normalized = normalizeHttpError(httpError(502, { details: 'bad gateway' }));

    expect(normalized).toEqual({ status: 502, body: { message: 'Неизвестная ошибка' } });
  });

  it('уже готовая ApiError возвращается как есть (тот же объект)', () => {
    const existing: ApiError = { status: 429, body: { message: 'Слишком много попыток' } };

    expect(normalizeHttpError(existing)).toBe(existing);
  });

  it('Error вне HTTP — ApiError со статусом NON_HTTP_ERROR_STATUS и message ошибки', () => {
    const normalized = normalizeHttpError(new Error('программная ошибка'));

    expect(normalized).toEqual({
      status: NON_HTTP_ERROR_STATUS,
      body: { message: 'программная ошибка' },
    });
  });

  it('произвольное значение (не Error, не HttpErrorResponse) — запасное сообщение', () => {
    const normalized = normalizeHttpError('что-то упало');

    expect(normalized).toEqual({ status: NON_HTTP_ERROR_STATUS, body: { message: 'Неизвестная ошибка' } });
  });
});
