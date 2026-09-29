/**
 * WorksPage — экран «Лабораторные работы» /works (C-111, SCR-007,
 * T-112, FR-4.3/FR-4.9). Контракты: IF-103 (LabsService) и IF-109
 * (уведомления экрана без формы — якорь 'header').
 *
 * Поведение по макету design/mockups/works.html:
 *  - h1 «Лабораторные работы» + «Добавить…» → /works/new (форма — T-113);
 *  - тулбар «Семестр»: p-select «Все семестры» (null) + семестры, в которых
 *    есть работы (labs.getSemesters, §4.4); смена фильтра → страница 1 и
 *    перезагрузка (FR-012);
 *  - p-table: сортировка «Номер»/«Семестр» в двух состояниях ▲/▼ повторными
 *    кликами — состояния «без сортировки» нет, дефолт semester↑ (IF-103);
 *  - «Задание»: ссылка — текстом URL без схемы, href через DomSanitizer,
 *    target="_blank", rel="noopener noreferrer"; без ссылки — прочерк «—»;
 *  - «Нужна защита» — p-checkbox read-only (FR-011);
 *  - пагинация p-paginator (pageSize=10) с подписью «Показать записи с X
 *    по Y из Z», вычисляемой из PagedResult (ADR-109, FR-011);
 *  - удаление — p-confirmDialog (ADR-111) «Вы точно хотите удалить
 *    лабораторную №N в семестре №M?» с «Yes»/«No» (primeng-ru); пока диалог
 *    открыт, список заблокирован (opacity + pointer-events, FR-014);
 *  - успех удаления — «Удалено» и перезагрузка текущей страницы (если она
 *    опустела и page > 1 — предыдущей, ADR-109); ошибки — дословный текст
 *    ApiError якорем 'header' (IF-109).
 */
import { ChangeDetectionStrategy, Component, OnInit, SecurityContext, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { DomSanitizer } from '@angular/platform-browser';
import { ConfirmationService } from 'primeng/api';
import { Button } from 'primeng/button';
import { Checkbox } from 'primeng/checkbox';
import { ConfirmDialog } from 'primeng/confirmdialog';
import { Paginator, PaginatorState } from 'primeng/paginator';
import { Select } from 'primeng/select';
import { TableModule } from 'primeng/table';

import { toApiError } from '../../../../shared/api-error';
import { submitting } from '../../../../shared/loading/submitting';
import { LabDto, PagedResult } from '../../../../shared/models';
import { NotificationService } from '../../../../shared/notifications/notification-service';
import { LABS_PAGE_SIZE, LabsService, LabsSortDir, LabsSortField } from '../../../../core/services/labs.service';

/** Опция селекта «Семестр»: value null — «Все семестры». */
interface SemesterOption {
  label: string;
  value: number | null;
}

@Component({
  selector: 'app-works-page',
  templateUrl: './works-page.html',
  styleUrl: './works-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  // В PrimeNG 20 ConfirmationService больше не providedIn: 'root' — даётся
  // здесь, его же инжектирует вложенный p-confirmdialog.
  providers: [ConfirmationService],
  imports: [FormsModule, Button, Checkbox, ConfirmDialog, Paginator, Select, TableModule],
})
export class WorksPage implements OnInit {
  private readonly labs = inject(LabsService);
  private readonly notifications = inject(NotificationService);
  private readonly confirmation = inject(ConfirmationService);
  private readonly router = inject(Router);
  private readonly sanitizer = inject(DomSanitizer);

  /** Контроллер удаления — без параллельного повторного запуска (FR-025). */
  protected readonly removeState = submitting();

  /** Текущий ответ labs.getList (null — первая загрузка ещё не завершилась). */
  private readonly result = signal<PagedResult<LabDto> | null>(null);

  /** Семестры, в которых есть работы — labs.getSemesters (§4.4). */
  private readonly semesters = signal<number[]>([]);

  /** Фильтр семестра: null — «Все семестры» (FR-012). */
  protected readonly filterSemester = signal<number | null>(null);

  /** Номер страницы, начиная с 1 (ADR-109). */
  private readonly page = signal(1);

  /** Первичный ключ сортировки; дефолт — 'semester' (IF-103: semester↑, number↑). */
  protected readonly sortField = signal<LabsSortField>('semester');
  protected readonly sortDir = signal<LabsSortDir>('asc');

  /** Таблица в состоянии загрузки, пока выполняется labs.getList (FR-002). */
  protected readonly loading = signal(false);

  /** Диалог подтверждения открыт — список заблокирован (FR-014). */
  protected readonly dialogOpen = signal(false);

  /** Счётчик загрузок: ответ устаревшего запроса не применяется к экрану. */
  private loadSeq = 0;

  /** Размер страницы списка — контракт IF-103 (10). */
  readonly pageSize = LABS_PAGE_SIZE;

  /** Записи текущей страницы (пусто до загрузки или при «Записей нет»). */
  readonly rows = computed(() => this.result()?.items ?? []);

  /** Полный размер выборки после фильтра — total из PagedResult (IF-103). */
  readonly total = computed(() => this.result()?.total ?? 0);

  /** Смещение пагинатора (0-based first) из 1-based страницы. */
  readonly first = computed(() => (this.page() - 1) * this.pageSize);

  /** Опции селекта: «Все семестры» (null) + семестры с работами по возрастанию. */
  readonly semesterOptions = computed<SemesterOption[]>(() => [
    { label: 'Все семестры', value: null },
    ...this.semesters().map((semester) => ({ label: String(semester), value: semester })),
  ]);

  /**
   * Подпись пагинации «Показать записи с X по Y из Z» (FR-011, ADR-109,
   * CR-003 вариант «а» — единая норма всех списков): видима всегда при
   * отрисованном списке, включая total = 0 («с 1 по 0 из 0» рядом с
   * «Записей нет»); X = (page−1)·pageSize+1, Y = min(page·pageSize, total).
   */
  readonly pageReport = computed<string | null>(() => {
    const current = this.result();
    if (current === null) {
      return null;
    }
    const start = (current.page - 1) * current.pageSize + 1;
    const end = Math.min(current.page * current.pageSize, current.total);
    return `Показать записи с ${start} по ${end} из ${current.total}`;
  });

  ngOnInit(): void {
    void this.load();
    void this.loadSemesters();
  }

  /**
   * Смена фильтра семестра: страница сбрасывается к 1 и список
   * перезагружается (AC filter-reload, FR-012).
   */
  onSemesterChange(value: number | null): void {
    if (value === this.filterSemester()) {
      return;
    }
    this.filterSemester.set(value);
    this.page.set(1);
    void this.load();
  }

  /**
   * Двухпозиционная сортировка (SCR-007): повторный клик по активной колонке
   * переключает asc↔desc — третьего состояния нет; клик по другой колонке
   * включает её по возрастанию.
   */
  toggleSort(field: LabsSortField): void {
    if (this.sortField() === field) {
      this.sortDir.set(this.sortDir() === 'asc' ? 'desc' : 'asc');
    } else {
      this.sortField.set(field);
      this.sortDir.set('asc');
    }
    void this.load();
  }

  /** Листание: p-paginator отдаёт 0-based first — страница = first/rows + 1. */
  onPageChange(event: PaginatorState): void {
    const requested = (event.page ?? 0) + 1;
    if (requested === this.page()) {
      return;
    }
    this.page.set(requested);
    void this.load();
  }

  /** «Добавить…» → форма создания (T-113). */
  add(): void {
    void this.router.navigateByUrl('/works/new');
  }

  /** «Редактировать» → форма правки записи (T-113). */
  edit(lab: LabDto): void {
    void this.router.navigate(['/works', lab.id, 'edit']);
  }

  /**
   * «Удалить»: диалог подтверждения (FR-014) с текстом дословно — номер
   * работы и семестра из записи; кнопки «Yes»/«No» приходят из primeng-ru
   * (ADR-111). Пока диалог открыт, список заблокирован; «Yes» — remove,
   * «Удалено» и перезагрузка; «No» — без вызова; отказ remove — баннер
   * 'header' и перезагрузка текущей страницы без редиректа (аменда 5).
   */
  requestRemove(lab: LabDto): void {
    this.confirmation.confirm({
      header: 'Удаление лабораторной',
      message: `Вы точно хотите удалить лабораторную №${lab.number} в семестре №${lab.semester}?`,
      accept: () => {
        this.dialogOpen.set(false);
        void this.removeState.run(async () => {
          try {
            await this.labs.remove(lab.id);
            this.notifications.notifySuccess('Удалено');
            await this.reloadAfterRemove();
          } catch (error) {
            this.notifications.notifyError(toApiError(error).body.message, 'header');
            // Аменда 5 (IF-103, remove-404): запись могла исчезнуть в другом
            // сеансе — перезагружаем текущее состояние списка (с откатом на
            // предыдущую страницу, если текущая опустела); редиректа нет.
            await this.reloadAfterRemove();
          }
        });
      },
      reject: () => this.dialogOpen.set(false),
    });
    this.dialogOpen.set(true);
  }

  /** Текст ссылки задания — URL без схемы http/https (макет SCR-007). */
  assignmentLabel(url: string): string {
    return url.replace(/^https?:\/\//, '');
  }

  /**
   * href ссылки задания через DomSanitizer (макет SCR-007): безопасные
   * http/https возвращаются как есть; прочие схемы нейтрализуются
   * (null либо префикс «unsafe:») — исполнение такой ссылки невозможно.
   */
  assignmentHref(url: string): string | null {
    return this.sanitizer.sanitize(SecurityContext.URL, url);
  }

  /**
   * Перезагрузка текущего состояния (фильтр/сортировка/страница): таблица
   * в состоянии загрузки; ответ более раннего запроса (после более свежего)
   * отбрасывается; отказ мока — дословный текст якорем 'header' (IF-109).
   */
  async load(): Promise<void> {
    const seq = ++this.loadSeq;
    this.loading.set(true);
    try {
      const current = await this.labs.getList({
        semester: this.filterSemester(),
        page: this.page(),
        sortField: this.sortField(),
        sortDir: this.sortDir(),
      });
      if (seq !== this.loadSeq) {
        return;
      }
      this.result.set(current);
    } catch (error) {
      if (seq !== this.loadSeq) {
        return;
      }
      this.notifications.notifyError(toApiError(error).body.message, 'header');
    } finally {
      if (seq === this.loadSeq) {
        this.loading.set(false);
      }
    }
  }

  /**
   * Перезагрузка после удаления: текущая страница, а если она опустела
   * (удалена последняя запись страницы) — предыдущая, но не раньше первой
   * (ADR-109: страницы 1-based).
   */
  private async reloadAfterRemove(): Promise<void> {
    await this.load();
    if ((this.result()?.items.length ?? 0) === 0 && this.page() > 1) {
      this.page.set(this.page() - 1);
      await this.load();
    }
    // Селект «Семестр» должен отражать текущее состояние: семестр, в котором
    // не осталось работ, исчезает из опций (CR-004); если исчез выбранный
    // семестр — фильтр сбрасывается к «Все семестрам» с перезагрузкой списка
    // и без ошибки (аменда 7, следствие CR-002).
    await this.loadSemesters();
    const selected = this.filterSemester();
    if (selected !== null && !this.semesters().includes(selected)) {
      this.filterSemester.set(null);
      this.page.set(1);
      await this.load();
    }
  }

  /** Семестры для селекта; отказ мока — дословный текст якорем 'header'. */
  private async loadSemesters(): Promise<void> {
    try {
      this.semesters.set(await this.labs.getSemesters());
    } catch (error) {
      this.notifications.notifyError(toApiError(error).body.message, 'header');
    }
  }
}
