/**
 * Юнит-тесты ProfileService (C-106, IF-107): проксирование мок-методов
 * 'profile.get' / 'profile.update' / 'profile.changePassword' с параметрами
 * контракта, пробрасывание ApiError отказов мока как есть и сквозной прогон
 * с реальным реестром (регистрация обработчиков домена — защита от
 * расхождения имён методов сервиса и обработчиков).
 */
import { fakeAsync, TestBed, tick } from '@angular/core/testing';

import { MockApiClient } from '../../mock/mock-api-client';
import { emptyMockDbData } from '../../mock/mock-db';
import { registerProfileHandlers } from '../../mock/profile/handlers';
import { ApiError, ProfileDto, STORAGE_KEYS, User } from '../../shared/models';
import { ProfileChangePasswordInput, ProfileService, ProfileUpdateInput } from './profile.service';

const SESSION_KEY = STORAGE_KEYS.session;
const DB_KEY = STORAGE_KEYS.mockDb;

let userSeq = 0;

function makeUser(overrides: Partial<User> = {}): User {
  userSeq += 1;
  return {
    id: `0b0b0b0b-0b0b-4b0b-8b0b-${String(userSeq).padStart(12, '0')}`,
    login: 'student01',
    email: 'student01@example.com',
    fullName: 'Иванов Иван Иванович 01',
    role: 'student',
    groupId: null,
    password: 'Student#2026',
    ...overrides,
  };
}

describe('ProfileService (C-106, IF-107)', () => {
  describe('проксирование мок-методов (подмена клиента spy)', () => {
    let service: ProfileService;
    let callSpy: jasmine.Spy;

    beforeEach(() => {
      callSpy = jasmine.createSpy('MockApiClient.call');
      const client = { call: callSpy } as unknown as MockApiClient;
      TestBed.configureTestingModule({ providers: [{ provide: MockApiClient, useValue: client }] });
      service = TestBed.inject(ProfileService);
    });

    it('get() вызывает profile.get без параметров и резолвит ProfileDto', async () => {
      const dto: ProfileDto = {
        login: 'student01',
        email: 'student01@example.com',
        fullName: 'Иванов Иван Иванович 01',
        role: 'student',
        groupName: 'ИК-221',
      };
      callSpy.and.resolveTo(dto);

      await expectAsync(service.get()).toBeResolvedTo(dto);
      expect(callSpy).toHaveBeenCalledWith('profile.get', null);
      expect(callSpy).toHaveBeenCalledTimes(1);
    });

    it('update() передаёт {fullName, email} как есть и резолвит обновлённый ProfileDto', async () => {
      const input: ProfileUpdateInput = { fullName: 'Петров Пётр Петрович', email: 'petr@example.com' };
      const dto: ProfileDto = {
        login: 'student01',
        email: input.email,
        fullName: input.fullName,
        role: 'student',
        groupName: null,
      };
      callSpy.and.resolveTo(dto);

      await expectAsync(service.update(input)).toBeResolvedTo(dto);
      expect(callSpy).toHaveBeenCalledWith('profile.update', input);
    });

    it('changePassword() передаёт {currentPassword, password, confirmPassword} и резолвит void', async () => {
      const input: ProfileChangePasswordInput = {
        currentPassword: 'Student#2026',
        password: 'NewPass#2027',
        confirmPassword: 'NewPass#2027',
      };
      callSpy.and.resolveTo(undefined);

      await expectAsync(service.changePassword(input)).toBeResolvedTo(undefined);
      expect(callSpy).toHaveBeenCalledWith('profile.changePassword', input);
    });

    it('отказ мока пробрасывается как есть — ApiError {status, body} дословно', async () => {
      const apiError: ApiError = {
        status: 409,
        body: { message: 'Пользователь с таким email уже существует' },
      };
      callSpy.and.rejectWith(apiError);

      await expectAsync(service.get()).toBeRejectedWith(apiError);
    });
  });

  describe('сквозной прогон с реальным моком (методы сервиса совпадают с реестром)', () => {
    beforeEach(() => {
      localStorage.clear();
      sessionStorage.clear();
      TestBed.configureTestingModule({});
      registerProfileHandlers(TestBed.inject(MockApiClient));
    });

    afterEach(() => {
      localStorage.clear();
      sessionStorage.clear();
    });

    it('get() возвращает профиль пользователя сессии', fakeAsync(() => {
      const teacher = makeUser({
        login: 'teacher',
        email: 'teacher@example.com',
        fullName: 'Сидоров Семён Семёнович',
        role: 'teacher',
        password: 'Teacher#2026',
      });
      localStorage.setItem(DB_KEY, JSON.stringify({ ...emptyMockDbData(), users: [teacher] }));
      localStorage.setItem(SESSION_KEY, teacher.id);
      const service = TestBed.inject(ProfileService);

      let resolved: ProfileDto | undefined;
      service.get().then((value) => {
        resolved = value;
      });
      tick(500);

      expect(resolved).toEqual({
        login: 'teacher',
        email: 'teacher@example.com',
        fullName: 'Сидоров Семён Семёнович',
        role: 'teacher',
        groupName: null,
      });
    }));

    it('update() с занятым email получает отказ ApiError 409 с дословным текстом', fakeAsync(() => {
      const teacher = makeUser({
        login: 'teacher',
        email: 'teacher@example.com',
        role: 'teacher',
        password: 'Teacher#2026',
      });
      const student = makeUser();
      localStorage.setItem(DB_KEY, JSON.stringify({ ...emptyMockDbData(), users: [teacher, student] }));
      localStorage.setItem(SESSION_KEY, student.id);
      const service = TestBed.inject(ProfileService);

      let error: ApiError | undefined;
      service.update({ fullName: student.fullName, email: 'TEACHER@example.com' }).then(
        () => fail('ожидался отказ 409'),
        (rejected: unknown) => {
          error = rejected as ApiError;
        },
      );
      tick(500);

      expect(error?.status).toBe(409);
      expect(error?.body.message).toBe('Пользователь с таким email уже существует');
    }));
  });
});
