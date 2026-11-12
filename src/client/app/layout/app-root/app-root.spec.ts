/**
 * Smoke-тесты корневого компонента AppRoot (требование T-001):
 * компонент создаётся и рендерится без ошибок в ChromeHeadless-раннере.
 */
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AppRoot } from './app-root';

describe('AppRoot — корневой компонент-каркас', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppRoot],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  it('создаётся без ошибок', () => {
    const fixture = TestBed.createComponent(AppRoot);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('рендерит router-outlet (пустой каркас до появления экранов)', () => {
    const fixture = TestBed.createComponent(AppRoot);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('router-outlet')).not.toBeNull();
  });

  it('содержит глобальный хост уведомлений перед router-outlet (IF-109, T-109)', () => {
    const fixture = TestBed.createComponent(AppRoot);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const toastHost = element.querySelector('app-notification-toast');
    const outlet = element.querySelector('router-outlet');
    expect(toastHost).not.toBeNull();
    expect(outlet).not.toBeNull();
    // Хост объявлен ДО outlet: Toast смонтирован для всех экранов,
    // включая страницы auth вне app-shell.
    expect(
      toastHost!.compareDocumentPosition(outlet!) &
        Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
  });

  it('рендерится без ошибок шаблона', () => {
    const fixture = TestBed.createComponent(AppRoot);
    expect(() => fixture.detectChanges()).not.toThrow();
  });
});
