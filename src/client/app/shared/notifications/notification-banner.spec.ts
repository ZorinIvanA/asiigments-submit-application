/**
 * Unit-тесты презентационного баннера уведомления (FR-021): текст
 * дословно, иконка и раскраска по severity, role alert/status,
 * закрытие крестиком.
 */
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { NotificationBanner } from './notification-banner';

describe('NotificationBanner', () => {
  let fixture: ComponentFixture<NotificationBanner>;

  function createComponent(severity: 'error' | 'success', text: string): HTMLElement {
    fixture = TestBed.createComponent(NotificationBanner);
    fixture.componentRef.setInput('severity', severity);
    fixture.componentRef.setInput('text', text);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('показывает текст уведомления дословно', () => {
    const element = createComponent('error', 'Лабораторная с таким номером уже есть в семестре');
    expect(element.textContent).toContain('Лабораторная с таким номером уже есть в семестре');
  });

  it('ошибка: красный вариант с иконкой ошибки и role=alert', () => {
    const element = createComponent('error', 'Текст ошибки');
    const banner = element.querySelector('.app-notification-banner')!;
    expect(banner.classList).toContain('app-notification-banner--error');
    expect(banner.classList).not.toContain('app-notification-banner--success');
    expect(banner.getAttribute('role')).toBe('alert');
    expect(banner.querySelector('.app-notification-banner__icon')!.classList).toContain(
      'pi-times-circle',
    );
  });

  it('успех: зелёный вариант с иконкой успеха и role=status', () => {
    const element = createComponent('success', 'Сохранено');
    const banner = element.querySelector('.app-notification-banner')!;
    expect(banner.classList).toContain('app-notification-banner--success');
    expect(banner.classList).not.toContain('app-notification-banner--error');
    expect(banner.getAttribute('role')).toBe('status');
    expect(banner.querySelector('.app-notification-banner__icon')!.classList).toContain(
      'pi-check-circle',
    );
  });

  it('клик по крестику эмитит closed', () => {
    let closed = false;
    const element = createComponent('error', 'Текст');
    fixture.componentInstance.closed.subscribe(() => (closed = true));

    (element.querySelector<HTMLButtonElement>('.app-notification-banner__close')!).click();

    expect(closed).toBe(true);
  });

  it('кнопка закрытия имеет доступное имя и не сабмитит форму', () => {
    const element = createComponent('error', 'Текст');
    const button = element.querySelector<HTMLButtonElement>('.app-notification-banner__close')!;
    expect(button.getAttribute('aria-label')).toBe('Закрыть');
    expect(button.getAttribute('type')).toBe('button');
  });
});
