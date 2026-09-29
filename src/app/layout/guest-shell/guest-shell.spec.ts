/**
 * Unit-тесты гостевой оболочки GuestShell (§4.8, VS-006, фикс VBUG-004):
 * ровно три вкладки «Вход», «Регистрация», «Восстановление пароля» с
 * ссылками /login, /register, /recovery, бренд «Сдача лабораторных»,
 * активная вкладка routerLinkActive + aria-current, --app-header-height:
 * 56px и высота шапки 56px, маршрутизируемая страница под шапкой.
 *
 * Тесты не зависят от медиазапросов: мобильную вёрстку (<768px, как у
 * AppShell) проверяет визуальный прогон по макетам, здесь — только DOM
 * и вычисляемые стили ChromeHeadless.
 */
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { GuestShell } from './guest-shell';

/** Минимальный маршрут-заглушка для гостевых страниц в тестах. */
@Component({ selector: 'app-empty-page', template: '' })
class EmptyPage {}

describe('GuestShell — топбар гостя (§4.8, VS-006, VBUG-004)', () => {
  let fixture: ComponentFixture<GuestShell>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [GuestShell],
      providers: [
        provideRouter([
          { path: 'login', component: EmptyPage },
          { path: 'register', component: EmptyPage },
          { path: 'recovery', component: EmptyPage },
          { path: '**', redirectTo: 'login' },
        ]),
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(GuestShell);
  });

  afterEach(() => {
    (fixture.nativeElement as HTMLElement).remove();
    fixture.destroy();
  });

  /** Ссылки-вкладки гостевого топбара в порядке рендера. */
  function tabs(): HTMLAnchorElement[] {
    return Array.from(
      fixture.nativeElement.querySelectorAll(
        'a.guest-topbar__tab',
      ) as NodeListOf<HTMLAnchorElement>,
    );
  }

  it('ровно 3 вкладки с подписями и ссылками §4.8', () => {
    fixture.detectChanges();

    expect(tabs().map((tab) => tab.textContent!.trim())).toEqual([
      'Вход',
      'Регистрация',
      'Восстановление пароля',
    ]);
    expect(tabs().map((tab) => tab.getAttribute('href'))).toEqual([
      '/login',
      '/register',
      '/recovery',
    ]);
  });

  it('бренд «Сдача лабораторных» в шапке, вкладки в nav «Разделы»', () => {
    fixture.detectChanges();

    expect(
      fixture.nativeElement.querySelector('.guest-topbar__brand')!.textContent!.trim(),
    ).toBe('Сдача лабораторных');
    expect(
      fixture.nativeElement.querySelector('nav.guest-topbar__tabs')!.getAttribute('aria-label'),
    ).toBe('Разделы');
  });

  it('активная вкладка подсвечивается routerLinkActive и помечена aria-current', async () => {
    fixture.detectChanges();

    await TestBed.inject(Router).navigate(['/recovery']);
    fixture.detectChanges();

    const active = tabs().filter((tab) =>
      tab.classList.contains('guest-topbar__tab--active'),
    );
    expect(active.length).withContext('активна ровно одна вкладка').toBe(1);
    expect(active[0].getAttribute('href')).toBe('/recovery');
    expect(active[0].getAttribute('aria-current')).toBe('page');
  });

  it('объявляет --app-header-height: 56px, высота шапки 56px', () => {
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(
      getComputedStyle(host).getPropertyValue('--app-header-height').trim(),
    ).toBe('56px');
    const header = host.querySelector<HTMLElement>('.guest-topbar')!;
    expect(getComputedStyle(header).height).toBe('56px');
  });

  it('маршрутизируемая страница рендерится под шапкой (router-outlet)', async () => {
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();

    await TestBed.inject(Router).navigate(['/login']);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    const header = host.querySelector('.guest-topbar')!;
    const page = host.querySelector('app-empty-page');
    expect(page).withContext('страница активирована внутри оболочки').not.toBeNull();
    expect(header.compareDocumentPosition(page!) & Node.DOCUMENT_POSITION_FOLLOWING)
      .withContext('страница после шапки в порядке документа')
      .toBeTruthy();
  });
});
