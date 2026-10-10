/**
 * Интеграционные сценарии механизма уведомлений FR-4.9 на экранах works
 * (IF-109, NotificationService + NotificationToast / HeaderNotification /
 * NotificationAnchor, NFR-§4.9):
 *  - TS-222: desktop-тост ошибки на /works — top-right, дословный текст,
 *    единый глобальный хост, красная семантика, иконка и крестик;
 *  - TS-223: автозакрытие ровно через 5 с (desktop и mobile);
 *  - TS-224: закрытие крестиком до таймаута, без повторного появления;
 *  - TS-225: мобильный inline-баннер — под формой-инициатором vs под шапкой;
 *    граница 768px (inline ↔ toast top-right);
 *  - TS-226: баннер не переживает уход с экрана-инициатора; новая ошибка —
 *    резервно под шапкой (formId без якоря → 'header');
 *  - TS-227: успешные уведомления — литералы «Сохранено»/«Удалено» и
 *    размещение (desktop — тост, mobile — только под шапкой, аменда 2).
 *
 * Уровень — интеграционный: ошибка провоцируется через реальные страницы
 * и HttpTestingController-бэкенд WorksBackendStub (remove-404 по TS-210,
 * 409 формы; без мок-слоя, FR-026), показ — через настоящие хосты.
 */
import { fakeAsync, tick } from '@angular/core/testing';

import { NOTIFICATION_AUTO_CLOSE_MS } from '../../../shared/notifications/notification-model';
import {
  WorksIntegrationHarness,
} from './works-integration-harness';

const NOT_FOUND_MESSAGE = 'Лабораторная не найдена';
const DUPLICATE_MESSAGE = 'Лабораторная с таким номером уже есть в семестре';

describe('Интеграция: уведомления на экранах works (FR-4.9, IF-109)', () => {
  let harness: WorksIntegrationHarness;

  beforeEach(() => {
    harness = WorksIntegrationHarness.setup();
  });

  afterEach(() => {
    harness.detach();
    WorksIntegrationHarness.restoreSeed();
    localStorage.clear();
  });

  /** Монтирование, сессия teacher, открытие /works (desktop-режим). */
  function openWorks(): void {
    harness.attach();
    harness.loginTeacher();
    harness.navigate('/works');
    harness.flush();
  }

  /**
   * Спровоцировать ошибку бэкенда на /works (remove-404 по TS-210): работа,
   * реально отображённая в первой строке списка, удаляется прямой мутацией
   * серверного состояния (эмуляция второй вкладки), затем в UI
   * подтверждается удаление её строки. Повторные вызовы корректны и после
   * фикса реализации по аменде 5 (страница перезагружается → rows()[0] — уже
   * другая работа): id каждый раз резолвится из состояния бэкенда по паре
   * (семестр, номер) ячеек строки. Для устаревшей строки уже удалённой
   * работы (реализация без перезагрузки) мутация не нужна — remove в UI
   * ответит 404 сам.
   */
  function triggerRemove404(): void {
    const cells = harness.rowCells(harness.rows()[0]!);
    const number = Number(cells[0]);
    const semester = Number(cells[2]);
    const displayed = harness
      .readDb()
      .labs.find((lab) => lab.semester === semester && lab.number === number);
    if (displayed !== undefined) {
      harness.removeLabDirect(displayed.id);
      harness.flush();
    }
    harness.openRemoveDialog(harness.rows()[0]!);
    harness.dialogButton('accept').click();
    harness.flush(2); // волна 1 — remove → 404; волна 2 — перезагрузка страницы (аменда 5)
  }

  /** Отправка формы создания с дубликатом пары (семестр 1, номер 1) — 409. */
  function submitDuplicateForm(): void {
    harness.setInput('#lab-number', '1');
    harness.openSemesterSelect();
    harness.chooseSemesterOption('1');
    harness.setInput('#lab-content', 'Дубль пары');
    harness.buttonByLabel('Сохранить').click();
    harness.flush();
  }

  it('TS-222: desktop-тост ошибки на /works — top-right, дословный текст, единый глобальный хост', fakeAsync(() => {
    openWorks();
    triggerRemove404();

    // Хост app-notification-toast един для всех экранов и объявлен один раз.
    expect(document.querySelectorAll('app-notification-toast').length).toBe(1);
    const toastHost = document.querySelector('app-notification-toast')!;
    // Позиция справа сверху поверх страницы.
    const toast = toastHost.querySelector('.p-toast')!;
    expect(toast.classList).toContain('p-toast-top-right');
    // Красная семантика, текст дословно без префиксов.
    const message = toast.querySelector('.p-toast-message-error')!;
    expect(message).not.toBeNull();
    expect(message.querySelector('.p-toast-summary')!.textContent!.trim()).toBe(
      NOT_FOUND_MESSAGE,
    );
    // Иконка и кнопка закрытия присутствуют.
    expect(message.querySelector('.p-toast-message-icon')).not.toBeNull();
    expect(message.querySelector('.p-toast-close-button')).not.toBeNull();
    // Вне глобального хоста тостов нет.
    expect(document.querySelectorAll('.p-toast').length).toBe(1);
    harness.finalize(); // таймер автозакрытия тоста ошибки
  }));

  it('TS-223: автозакрытие уведомления ровно через 5 с — desktop и mobile', fakeAsync(() => {
    openWorks();
    triggerRemove404();

    // К моменту измерения с момента уведомления прожиты только микрозадачи
    // волн ответов (задержек бэкенда нет), поэтому до автозакрытия
    // (NOTIFICATION_AUTO_CLOSE_MS) остаётся 4999 + 1 мс.
    const measure = (): void => {
      // На границе «5 с минус 1 мс» уведомление ещё видно, на «5 с» — исчезло.
      tick(NOTIFICATION_AUTO_CLOSE_MS - 1);
      harness.uiSettle();
      expect(harness.notifications.isMobile() ? harness.headerBannerTexts() : harness.toastTexts())
        .toContain(NOT_FOUND_MESSAGE);
      tick(1);
      harness.uiSettle();
      expect(harness.headerBannerTexts()).not.toContain(NOT_FOUND_MESSAGE);
      expect(harness.toastTexts()).not.toContain(NOT_FOUND_MESSAGE);
    };

    // Desktop: тост ошибки исчезает ровно через 5 с (NFR-§4.9).
    expect(document.querySelector('.p-toast-message-error')).not.toBeNull();
    measure();
    expect(document.querySelector('.p-toast-message-error')).toBeNull();
    expect(harness.notifications.desktopMessage()).toBeNull();

    // Mobile: смена режима гасит показанное уведомление — показываем заново.
    harness.breakpoints.simulate(true);
    harness.uiSettle();
    triggerRemove404();
    expect(harness.headerBannerTexts()).toContain(NOT_FOUND_MESSAGE);
    measure();
    expect(harness.headerBannerTexts()).not.toContain(NOT_FOUND_MESSAGE);
    expect(harness.notifications.mobileMessage()).toBeNull();
  }));

  it('TS-224: закрытие уведомления крестиком до таймаута — немедленно и без повторного появления', fakeAsync(() => {
    openWorks();
    triggerRemove404();

    // Desktop: крестик тоста.
    const closeToast = document.querySelector<HTMLButtonElement>(
      '.p-toast-message-error .p-toast-close-button',
    )!;
    expect(closeToast).not.toBeNull();
    closeToast.click();
    harness.uiSettle();
    expect(document.querySelector('.p-toast-message')).toBeNull();
    tick(5000);
    harness.uiSettle();
    expect(document.querySelector('.p-toast-message')).toBeNull(); // самопроизвольного появления нет

    // Mobile: крестик inline-баннера под шапкой.
    harness.breakpoints.simulate(true);
    harness.uiSettle();
    triggerRemove404();
    expect(harness.headerBannerTexts()).toContain(NOT_FOUND_MESSAGE);
    document
      .querySelector<HTMLButtonElement>('app-header-notification .app-notification-banner__close')!
      .click();
    harness.uiSettle();
    expect(harness.headerBannerTexts()).not.toContain(NOT_FOUND_MESSAGE);
    expect(harness.notifications.mobileMessage()).toBeNull();
    tick(5000);
    harness.uiSettle();
    expect(harness.headerBannerTexts()).not.toContain(NOT_FOUND_MESSAGE); // повторного появления нет
  }));

  it('TS-225: мобильный inline-баннер — под формой vs под шапкой; граница 768px', fakeAsync(() => {
    harness.breakpoints.simulate(true); // 767px
    harness.attach();
    harness.loginTeacher();

    // 409 формы создания → inline-баннер непосредственно под формой #lab-form.
    harness.navigate('/works/new');
    submitDuplicateForm();
    expect(harness.formAnchorBannerTexts()).toContain(DUPLICATE_MESSAGE);
    expect(harness.headerBannerTexts()).toEqual([]); // НЕ под шапкой
    const anchorUnderForm = harness.native.querySelector('#lab-form ~ app-notification-anchor')!;
    expect(anchorUnderForm.querySelector('.app-notification-banner')).not.toBeNull();

    // remove-404 списка → inline-баннер под шапкой (якорь 'header'):
    // две волны — монтирование /works и его стартовая загрузка.
    harness.buttonByLabel('Назад').click();
    harness.flush(2);
    expect(harness.formAnchorBannerTexts()).toEqual([]); // баннер формы не пережил уход
    triggerRemove404();
    expect(harness.headerBannerTexts()).toContain(NOT_FOUND_MESSAGE);

    // Граница 768px: inline сменяется тостом top-right (смена режима гасит
    // показанное уведомление — провоцируем ошибку заново).
    harness.breakpoints.simulate(false);
    harness.uiSettle();
    expect(harness.headerBannerTexts()).toEqual([]);
    triggerRemove404();
    expect(harness.headerBannerTexts()).toEqual([]); // inline больше не используется
    const toast = document.querySelector('.p-toast')!;
    expect(toast.classList).toContain('p-toast-top-right');
    expect(document.querySelector('.p-toast-summary')!.textContent!.trim()).toBe(
      NOT_FOUND_MESSAGE,
    );
    harness.finalize(); // таймер автозакрытия тоста ошибки
  }));

  it('TS-226: баннер не переживает уход с экрана-инициатора; новая ошибка показывается под шапкой', fakeAsync(() => {
    harness.breakpoints.simulate(true); // mobile
    harness.attach();
    harness.loginTeacher();

    harness.navigate('/works/new');
    submitDuplicateForm();
    expect(harness.formAnchorBannerTexts()).toContain(DUPLICATE_MESSAGE);

    // «Назад» → /works: якорь unregistered, «призрака» нет
    // (две волны — монтирование списка и его загрузка).
    harness.buttonByLabel('Назад').click();
    harness.flush(2);
    expect(harness.router.url).toBe('/works');
    expect(harness.formAnchorBannerTexts()).toEqual([]);
    expect(harness.headerBannerTexts()).toEqual([]);

    // Новая ошибка на /works — заново, резервно под шапкой (IF-109).
    triggerRemove404();
    expect(harness.headerBannerTexts()).toContain(NOT_FOUND_MESSAGE);
    harness.finalize(); // таймер автозакрытия мобильного баннера
  }));

  it('TS-227: успешные уведомления — литералы «Сохранено»/«Удалено» и размещение', fakeAsync(() => {
    // Desktop: тост справа сверху.
    openWorks();
    harness.openRemoveDialog(harness.rowByNumber(1));
    harness.dialogButton('accept').click();
    harness.flush(3); // remove → getList → getSemesters
    expect(harness.lastToastText()).toBe('Удалено');
    expect(document.querySelector('.p-toast-message-success')).not.toBeNull();
    expect(document.querySelector('.p-toast')!.classList).toContain('p-toast-top-right');

    harness.navigate('/works/new');
    harness.setInput('#lab-number', '30');
    harness.openSemesterSelect();
    harness.chooseSemesterOption('1');
    harness.setInput('#lab-content', 'Успешное сохранение');
    harness.buttonByLabel('Сохранить').click();
    harness.flush(); // create
    harness.flush(); // редирект + загрузка /works
    expect(harness.lastToastText()).toBe('Сохранено');

    // Mobile: успех фиксированно под шапкой, НИКОГДА под формой (аменда 2),
    // даже если инициирован формой.
    harness.breakpoints.simulate(true);
    harness.uiSettle();
    harness.navigate('/works/new');
    harness.setInput('#lab-number', '31');
    harness.openSemesterSelect();
    harness.chooseSemesterOption('1');
    harness.setInput('#lab-content', 'Мобильный успех');
    harness.buttonByLabel('Сохранить').click();
    harness.flush(); // create
    harness.flush(); // редирект + загрузка /works
    expect(harness.formAnchorBannerTexts()).toEqual([]);
    expect(harness.headerBannerTexts()).toContain('Сохранено');
    expect(
      document.querySelector('app-header-notification .app-notification-banner--success'),
    ).not.toBeNull();

    // «Удалено» на мобильном — тоже под шапкой: экран уже /works после
    // успешного сохранения (отдельный клик «Назад» не нужен).
    harness.openRemoveDialog(harness.rows()[0]!);
    harness.dialogButton('accept').click();
    harness.flush(3); // remove → getList → getSemesters
    expect(harness.headerBannerTexts()).toContain('Удалено');
    harness.finalize(); // таймеры автозакрытия уведомлений
  }));
});
