/**
 * Юнит-тесты SessionStore (C-101, ADR-105, ключ mock.session.userId):
 * чтение/запись/очистка, синхронный hasUser, персистентность между
 * экземплярами (переживание пересоздания сервиса после F5).
 */
import { STORAGE_KEYS } from '../../shared/models';
import { SessionStore } from './session-store';

describe('SessionStore — сессия mock.session.userId (C-101, ADR-105)', () => {
  beforeEach(() => localStorage.clear());

  afterEach(() => localStorage.clear());

  it('без ключа getUserId → null и hasUser → false', () => {
    const store = new SessionStore();
    expect(store.getUserId()).toBeNull();
    expect(store.hasUser()).toBeFalse();
  });

  it('setUserId пишет ключ как есть, getUserId/hasUser видят значение', () => {
    const store = new SessionStore();
    const userId = '0b0b0b0b-0b0b-4b0b-8b0b-0b0b0b0b0b0b';
    store.setUserId(userId);
    expect(store.getUserId()).toBe(userId);
    expect(store.hasUser()).toBeTrue();
    expect(localStorage.getItem(STORAGE_KEYS.session)).toBe(userId);
  });

  it('clear удаляет ключ: getUserId → null, hasUser → false', () => {
    const store = new SessionStore();
    store.setUserId('0b0b0b0b-0b0b-4b0b-8b0b-0b0b0b0b0b0b');
    store.clear();
    expect(store.getUserId()).toBeNull();
    expect(store.hasUser()).toBeFalse();
    expect(localStorage.getItem(STORAGE_KEYS.session)).toBeNull();
  });

  it('clear без сессии — тоже успех (ключа не было)', () => {
    const store = new SessionStore();
    expect(() => store.clear()).not.toThrow();
    expect(store.hasUser()).toBeFalse();
  });

  it('перезозданный экземпляр видит сессию первого (состояние — в localStorage)', () => {
    const first = new SessionStore();
    first.setUserId('0b0b0b0b-0b0b-4b0b-8b0b-0b0b0b0b0b0b');

    const second = new SessionStore(); // например, после пересоздания сервиса
    expect(second.getUserId()).toBe('0b0b0b0b-0b0b-4b0b-8b0b-0b0b0b0b0b0b');
    expect(second.hasUser()).toBeTrue();

    second.clear();
    expect(first.hasUser()).toBeFalse(); // тот же ключ, а не состояние экземпляра
  });
});
