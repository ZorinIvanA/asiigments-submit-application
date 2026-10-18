/**
 * Юнит-тесты RecoveryFlowStore (C-101, IF-102, ADR-106): сигналы email/
 * resetToken, предикаты hasEmail()/hasResetToken(), персистентность в
 * sessionStorage[recovery.flow.v1], переживание пересоздания сервиса,
 * clear (полная очистка) и clearResetToken (терминальная ветка: email
 * сохраняется), защитное чтение повреждённого значения.
 */
import { Injector } from '@angular/core';

import { STORAGE_KEYS } from '../../shared/models';
import { RecoveryFlowStore } from './recovery-flow-store';

describe('RecoveryFlowStore — поток восстановления recovery.flow.v1 (IF-102)', () => {
  beforeEach(() => {
    sessionStorage.clear();
    localStorage.clear();
  });

  afterEach(() => sessionStorage.clear());

  function freshStore(): RecoveryFlowStore {
    // Отдельный инжектор — экземпляр, независимый от любого TestBed/синглтона:
    // состояние должно приезжать из sessionStorage, а не из памяти.
    return Injector.create({ providers: [RecoveryFlowStore] }).get(RecoveryFlowStore);
  }

  it('новый поток пуст: email/resetToken — null, предикаты false', () => {
    const store = freshStore();
    expect(store.email()).toBeNull();
    expect(store.resetToken()).toBeNull();
    expect(store.hasEmail()).toBeFalse();
    expect(store.hasResetToken()).toBeFalse();
    expect(sessionStorage.getItem(STORAGE_KEYS.recoveryFlow)).toBeNull();
  });

  it('setEmail обновляет сигнал, пишет sessionStorage и включает hasEmail', () => {
    const store = freshStore();
    store.setEmail('student01@example.com');

    expect(store.email()).toBe('student01@example.com');
    expect(store.hasEmail()).toBeTrue();
    expect(store.hasResetToken()).toBeFalse();
    expect(JSON.parse(sessionStorage.getItem(STORAGE_KEYS.recoveryFlow) ?? 'null')).toEqual({
      email: 'student01@example.com',
      resetToken: null,
    });
  });

  it('setResetToken сохраняет email шага и включает hasResetToken', () => {
    const store = freshStore();
    store.setEmail('student01@example.com');
    store.setResetToken('reset-token-1');

    expect(store.email()).toBe('student01@example.com');
    expect(store.resetToken()).toBe('reset-token-1');
    expect(store.hasResetToken()).toBeTrue();
  });

  it('поток переживает пересоздание сервиса в пределах вкладки (F5)', () => {
    const first = freshStore();
    first.setEmail('student01@example.com');
    first.setResetToken('reset-token-1');

    const second = freshStore();
    expect(second.email()).toBe('student01@example.com');
    expect(second.resetToken()).toBe('reset-token-1');
    expect(second.hasEmail()).toBeTrue();
    expect(second.hasResetToken()).toBeTrue();
  });

  it('clear очищает поток полностью: сигналы, предикаты и sessionStorage', () => {
    const store = freshStore();
    store.setEmail('student01@example.com');
    store.setResetToken('reset-token-1');

    store.clear();
    expect(store.email()).toBeNull();
    expect(store.resetToken()).toBeNull();
    expect(store.hasEmail()).toBeFalse();
    expect(store.hasResetToken()).toBeFalse();
    expect(JSON.parse(sessionStorage.getItem(STORAGE_KEYS.recoveryFlow) ?? 'null')).toEqual({
      email: null,
      resetToken: null,
    });
  });

  it('clearResetToken — терминальная ветка: удаляется только resetToken, email сохраняется', () => {
    const store = freshStore();
    store.setEmail('student01@example.com');
    store.setResetToken('reset-token-1');

    store.clearResetToken();
    expect(store.email()).withContext('email сохраняется').toBe('student01@example.com');
    expect(store.resetToken()).toBeNull();
    expect(store.hasResetToken()).toBeFalse();
    expect(store.hasEmail()).toBeTrue();
  });

  it('защитное чтение: повреждённый JSON не роняет сервис — поток пуст', () => {
    sessionStorage.setItem(STORAGE_KEYS.recoveryFlow, '{это не json');
    const store = freshStore();
    expect(store.email()).toBeNull();
    expect(store.resetToken()).toBeNull();
  });

  it('защитное чтение: поля не-строкового типа отбрасываются', () => {
    sessionStorage.setItem(
      STORAGE_KEYS.recoveryFlow,
      JSON.stringify({ email: 42, resetToken: { x: 1 }, лишнее: true }),
    );
    const store = freshStore();
    expect(store.email()).toBeNull();
    expect(store.resetToken()).toBeNull();
  });
});
