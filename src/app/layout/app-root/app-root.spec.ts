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

  it('рендерится без ошибок шаблона', () => {
    const fixture = TestBed.createComponent(AppRoot);
    expect(() => fixture.detectChanges()).not.toThrow();
  });
});
