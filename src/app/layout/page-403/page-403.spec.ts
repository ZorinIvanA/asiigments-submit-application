/**
 * Unit-тесты страницы 403 (C-109, T-109, OQ-004 — макета нет, минимальная
 * карточка): заголовок «Доступ запрещён», кнопка «На главную» ведёт на
 * домашний маршрут роли из ROLE_HOME (IF-108: teacher → /works, student →
 * /my-submissions); крайний случай — без пользователя в кэше → /login.
 *
 * AuthService замещается стабом с настоящим сигналом currentUser (mock-
 * состояние роли); навигации завершаются на маршрутах-заглушках.
 */
import { Component, WritableSignal, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { AuthService, MeDto } from '../../core/services/auth.service';
import { Page403 } from './page-403';

/** Минимальный маршрут-заглушка для завершённых навигаций в тестах. */
@Component({ template: '' })
class EmptyPage {}

const TEACHER_ME: MeDto = {
  login: 'teacher',
  fullName: 'Сидоров Семён Семёнович',
  role: 'teacher',
  groupName: null,
};

const STUDENT_ME: MeDto = {
  login: 'student01',
  fullName: 'Иванов Иван Иванович 01',
  role: 'student',
  groupName: 'ИК-221',
};

describe('Page403 — «Доступ запрещён» (T-109, OQ-004)', () => {
  let currentUser: WritableSignal<MeDto | null>;
  let fixture: ComponentFixture<Page403>;

  beforeEach(async () => {
    currentUser = signal(null);
    await TestBed.configureTestingModule({
      imports: [Page403],
      providers: [
        { provide: AuthService, useValue: { currentUser } },
        provideRouter([
          { path: 'login', component: EmptyPage },
          { path: 'works', component: EmptyPage },
          { path: 'my-submissions', component: EmptyPage },
        ]),
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(Page403);
  });

  afterEach(() => {
    fixture.destroy();
  });

  /** Кнопка перехода на домашний раздел роли. */
  function homeButton(): HTMLButtonElement {
    return fixture.nativeElement.querySelector(
      '.page-403__home',
    ) as HTMLButtonElement;
  }

  it('рендерит карточку с заголовком «Доступ запрещён» и кнопкой «На главную»', () => {
    currentUser.set(TEACHER_ME);
    fixture.detectChanges();

    const title = fixture.nativeElement.querySelector('.page-403__title')!;
    expect(title.textContent!.trim()).toBe('Доступ запрещён');
    expect(homeButton().textContent!.trim()).toBe('На главную');
  });

  it('teacher: кнопка ведёт на домашний маршрут /works (ROLE_HOME)', async () => {
    currentUser.set(TEACHER_ME);
    fixture.detectChanges();

    homeButton().click();
    await fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/works');
  });

  it('student: кнопка ведёт на домашний маршрут /my-submissions (ROLE_HOME)', async () => {
    currentUser.set(STUDENT_ME);
    fixture.detectChanges();

    homeButton().click();
    await fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/my-submissions');
  });

  it('без пользователя в кэше кнопка ведёт на /login (крайний случай)', async () => {
    fixture.detectChanges();

    homeButton().click();
    await fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/login');
  });
});
