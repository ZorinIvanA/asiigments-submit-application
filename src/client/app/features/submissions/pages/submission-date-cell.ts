/**
 * Ячейка даты ведомости «Сдача/Защита» (C-112, FR-015, RSK-006, VBUG-003).
 *
 * Изолированный OnPush-компонент с ПРИМИТИВНЫМИ входами — защита от
 * «шторма» change detection на больших ведомостях (5 студентов × 20 работ
 * × 2 колонки = 200 p-datepicker): значение [ngModel] кэшируется computed'ом
 * от примитивной строки 'YYYY-MM-DD' и тождественно стабильно между
 * проходами CD, поэтому writeControlValue (updateUI + markForCheck) пикера
 * не вызывается впустую (первопричина VBUG-003).
 *
 * Разделение ввода и коммита (CR-001): p-datepicker на каждый keystroke
 * парсит текст и при неудаче отдаёт в ngModelChange(null) — такие
 * ПРОМЕЖУТОЧНЫЕ события серверу не отправляются и источник не меняют.
 * Коммитятся:
 *  - валидный распарсенный результат ввода (ngModelChange с Date) — включая
 *    выбор в календаре (тоже приходит через updateModel);
 *  - явная очистка: кнопка × (svg clear() эмитит onClear; кнопке button bar
 *    соответствует onClearClick), blur/Enter в пустом поле.
 * Таким образом промежуточный набор текста не стирает прежнюю дату и не
 * порождает серверных вызовов, а guard savingCells страницы не глотает
 * валидный финальный ввод (он больше не идёт следом за отклонённым
 * промежуточным null-сохранением).
 *
 * Ревизия ячейки (CR-002): effect() по revision принудительно вызывает
 * writeValue пикера (viewChild) со значением источника — перезапись
 * работает и для null (ссылочно равный null в [ngModel] NgModel в контрол
 * не пробрасывает, поэтому после 400-отказа в пустой ячейке без форса
 * оставалась фантомная дата).
 */
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  input,
  output,
  untracked,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatePicker } from 'primeng/datepicker';

import { toIsoDate, toLocalDate } from '../../../shared/dates';

@Component({
  selector: 'app-submission-date-cell',
  imports: [FormsModule, DatePicker],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p-datepicker
      [ngModel]="dateModel()"
      (ngModelChange)="onModelValue($event)"
      (onClear)="commitClear()"
      (onClearClick)="commitClear()"
      (onBlur)="onBlur($event)"
      (keydown.enter)="onEnter($event)"
      [disabled]="saving()"
      view="date"
      dateFormat="dd.mm.yy"
      [showIcon]="true"
      [showOnFocus]="true"
      [showClear]="true"
    />
  `,
})
export class SubmissionDateCell {
  /** Контрактная дата ячейки 'YYYY-MM-DD' либо null = пустая ячейка. */
  readonly value = input<string | null>(null);
  /** true — сохранение ячейки выполняется: контрол заблокирован (RSK-006). */
  readonly saving = input<boolean>(false);
  /** Ревизия ячейки: рост форсирует перезапись контрола (сброс после ошибки). */
  readonly revision = input<number>(0);

  /** Коммит значения пользователем: Date либо null = сброс. */
  readonly dateChange = output<Date | null>();

  private readonly pickerRef = viewChild(DatePicker);

  /**
   * Значение p-datepicker: ссылочно стабильно, пока не изменится value, —
   * NgModel не получает новый объект на каждом проходе CD (VBUG-003).
   */
  protected readonly dateModel = computed(() => toLocalDate(this.value()));

  constructor() {
    // CR-002: рост ревизии — принудительная перезапись контрола источником
    // вне зависимости от значения (в т.ч. null): writeValue(null) чистит
    // поле, а ссылочно равный null в [ngModel] NgModel не доставляет.
    effect(() => {
      if (this.revision() === 0) {
        return;
      }
      const picker = this.pickerRef();
      if (picker !== undefined) {
        picker.writeValue(untracked(() => toLocalDate(this.value())));
      }
    });
  }

  /**
   * Отправка коммита родителю: только значение, отличное от текущего
   * источника (дубли событий «календарь + ввод» и повторные blur отсеиваются).
   */
  private commitValue(value: Date | null): void {
    const iso = value === null ? null : toIsoDate(value);
    if (iso === this.value()) {
      return;
    }
    this.dateChange.emit(value);
  }

  /**
   * CR-001: коммитится только ВАЛИДНЫЙ распарсенный результат. null в
   * ngModelChange — это промежуточные неудачные парсинги ('1', '15.') и
   * стирание текста: серверных вызовов он не порождает, очистка коммитится
   * явными путями (commitClear).
   */
  protected onModelValue(value: Date | null): void {
    if (value !== null) {
      this.commitValue(value);
    }
  }

  /**
   * Явная очистка (× / пустой blur / Enter в пустом поле): немедленно чистим
   * контрол (текст и внутреннее значение), затем коммитим null — прежняя
   * дата не «вспыхивает» на время сохранения; при ошибке ревизия вернёт
   * серверную истину.
   */
  protected commitClear(): void {
    this.pickerRef()?.writeValue(null);
    this.commitValue(null);
  }

  /**
   * Blur: пустое поле — явная очистка; валидный ввод уже закоммичен по мере
   * набора; нечитаемый остаток текста (picker.value === null — парс неудачен)
   * откатывается к источнику без серверного вызова.
   */
  protected onBlur(event: Event): void {
    const text = (event.target as HTMLInputElement | null)?.value.trim() ?? '';
    if (text === '') {
      this.commitClear();
      return;
    }
    const picker = this.pickerRef();
    if (picker !== undefined && (picker.value === null || picker.value === undefined)) {
      this.rewriteFromSource();
    }
  }

  /** Enter: в пустом поле — явная очистка; валидный ввод уже закоммичен. */
  protected onEnter(event: Event): void {
    const text = (event.target as HTMLInputElement | null)?.value.trim() ?? '';
    if (text === '') {
      this.commitClear();
    }
  }

  /** Внеконтекстная перезапись значения контрола из источника. */
  private rewriteFromSource(): void {
    this.pickerRef()?.writeValue(toLocalDate(this.value()));
  }
}
