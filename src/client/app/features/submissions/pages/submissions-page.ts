/**
 * Страница «Сдача работ» — ведомость преподавателя (C-112, SCR-009, FR-4.4).
 *
 * Данные — только через сервисы core (IF-104/IF-103/IF-106):
 *  - селектор «Группа» — groups.getList, по умолчанию первая по порядку мока
 *    (name↑ без учёта регистра);
 *  - селектор «Семестр» — labs.getSemesters, по умолчанию наименьший
 *    (список asc); смена любого селектора сбрасывает страницу на 1 и
 *    перезагружает ведомость (ADR-109);
 *  - таблица «ФИО × Лабы» — submissions.getGrid: двухуровневая шапка
 *    «Лаб N» (colspan 2) → «Сдача»/«Защита» у КАЖДОЙ работы независимо
 *    от defenseRequired; пагинация по студентам SUBMISSIONS_PAGE_SIZE
 *    с подписью «Показать записи с X по Y из Z» из нормализованного
 *    page ответа (аддендум 1 к ADR-109).
 *
 * Даты в ячейках — p-datepicker (ru, отображение дд.мм.гггг) в изолированной
 * OnPush-ячейке SubmissionDateCell: страница передаёт в ячейку только
 * примитивы (строка 'YYYY-MM-DD', boolean, number), Date кэшируется внутри
 * ячейки computed'ом — тождественно стабильное значение [ngModel] исключает
 * «шторм» DatePicker.writeControlValue (updateUI + markForCheck) на всех
 * ячейках при каждом проходе CD. Первопричина VBUG-003: связывание
 * [ngModel] с новой Date на каждый проход при 200 инстансах (5 студентов ×
 * 20 работ × 2 колонки) зацикливало CD — ~100% CPU рендерера, вкладка
 * не отвечала. Внимание: в паттерне PrimeNG четырёхзначный год — токен
 * 'yy' (не 'yyyy', как в SimpleDateFormat: 'yyyy' рендерит год дважды —
 * «20262026»), поэтому явный dateFormat="dd.mm.yy" на контролах.
 * Выбор/очистка даты шлёт submissions.update с актуальными значениями
 * ОБЕИХ дат пары (null = сброс, upsert IF-106); сохраняемая ячейка
 * блокируется на время своего сохранения (RSK-006), при ошибке —
 * notifyError якорем 'header', перезагрузка грида и ревизия ячейки
 * (сброс контрола на серверную истину; сервер — истина).
 *
 * Вырожденные состояния (FR-015, макет SCR-009): пустая группа — строка
 * «В группе нет студентов» и подпись «Показать записи с 1 по 0 из 0»;
 * нет семестров с работами — селектор заблокирован, подпись «Нет семестров
 * с работами», таблица скрыта; нет групп — селектор заблокирован, подпись
 * «Нет групп», элемент селектора семестра скрыт целиком (макет SCR-009,
 * ревью CR-002 T-114), таблица скрыта. Пустая дата — пустая ячейка без
 * прочерка.
 */
import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Paginator, PaginatorState } from 'primeng/paginator';
import { Select } from 'primeng/select';
import { TableModule } from 'primeng/table';

import { toApiError } from '../../../shared/api-error';
import { toIsoDate } from '../../../shared/dates';
import { NotificationService } from '../../../shared/notifications/notification-service';
import { GroupsService } from '../../../core/services/groups.service';
import { LabsService } from '../../../core/services/labs.service';
import {
  SUBMISSIONS_PAGE_SIZE,
  SubmissionsGridResult,
  SubmissionsService,
} from '../../../core/services/submissions.service';
import { GroupDto } from '../../../shared/models';
import { SubmissionDateCell } from './submission-date-cell';

/** Поле даты ячейки ведомости: сдача либо защита. */
export type SubmissionsDateField = 'submitDate' | 'defenseDate';

/** Актуальные даты пары студент×работа — 'YYYY-MM-DD' | null (IF-011). */
interface CellDates {
  submitDate: string | null;
  defenseDate: string | null;
}

/** Ключ пары студент×работа в карте дат и в наборе сохраняемых ячеек. */
function pairKey(studentId: string, labId: string): string {
  return `${studentId}:${labId}`;
}

@Component({
  selector: 'app-submissions-page',
  imports: [FormsModule, Select, TableModule, Paginator, SubmissionDateCell],
  templateUrl: './submissions-page.html',
  styleUrl: './submissions-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SubmissionsPage implements OnInit {
  private readonly groupsService = inject(GroupsService);
  private readonly labsService = inject(LabsService);
  private readonly submissionsService = inject(SubmissionsService);
  private readonly notifications = inject(NotificationService);

  /** Размер страницы студентов ведомости (§4.4, ADR-109) — для шаблона. */
  readonly pageSize = SUBMISSIONS_PAGE_SIZE;

  /** Группы для селектора (name↑ без учёта регистра — порядок задаёт мок). */
  readonly groups = signal<GroupDto[]>([]);
  /** Семестры с работами (asc) — labs.getSemesters. */
  readonly semesters = signal<number[]>([]);
  /** Выбранная группа; null — выбор ещё не сделан/групп нет. */
  readonly selectedGroupId = signal<string | null>(null);
  /** Выбранный семестр; null — семестров с работами нет. */
  readonly selectedSemester = signal<number | null>(null);

  /** Текущая страница ведомости (страница из ответа — нормализованная). */
  readonly grid = signal<SubmissionsGridResult | null>(null);
  /** Таблица в состоянии загрузки, пока выполняется getGrid (по образцу works-page, ревью CR-001 T-114). */
  readonly loading = signal(false);
  /** Даты пар студент×работа текущего грида: pairKey → обе даты. */
  private readonly records = signal<Map<string, CellDates>>(new Map());
  /** Запрошенная страница (1-based); селекторы сбрасывают её на 1. */
  private readonly requestedPage = signal(1);
  /** Ключи ячеек, сохранение которых выполняется прямо сейчас. */
  private readonly savingCells = signal<ReadonlySet<string>>(new Set());
  /**
   * Ревизии ячеек (cellKey → счётчик): растёт при сбросе ячейки после
   * ошибки сохранения — заставляет OnPush-ячейку пересоздать Date и
   * перезаписать значение p-datepicker серверной истиной (VBUG-003).
   */
  private readonly cellRevisions = signal<Map<string, number>>(new Map());

  /** Номер скользящего запроса getGrid: ответы устаревших запросов отбрасываются. */
  private gridRequestSeq = 0;

  /** Дефолт группы — первая по порядку мока; false — состояние «Нет групп». */
  readonly hasGroups = computed(() => this.groups().length > 0);
  /** Дефолт семестра — наименьший; false — состояние «Нет семестров с работами». */
  readonly hasSemesters = computed(() => this.semesters().length > 0);

  /** Студенты текущей страницы — строки таблицы. */
  readonly students = computed(() => this.grid()?.students ?? []);
  /** Работы семестра — колонки таблицы (пара «Сдача/Защита» у каждой). */
  readonly labs = computed(() => this.grid()?.labs ?? []);

  /**
   * Таблица видна только с загруженной ведомостью: без выбранных селекторов,
   * в состояниях «Нет групп»/«Нет семестров с работами» и до первого
   * ответа getGrid таблица скрыта (макет SCR-009).
   */
  readonly tableVisible = computed(() => this.grid() !== null);

  /**
   * Подпись пагинации «Показать записи с X по Y из Z» (ADR-109): вычисляется
   * из нормализованного page ответа и полного total группы. Пустая группа
   * даёт «Показать записи с 1 по 0 из 0» (FR-015).
   */
  readonly rangeCaption = computed<string | null>(() => {
    const grid = this.grid();
    if (grid === null) {
      return null;
    }
    const from = (grid.page - 1) * SUBMISSIONS_PAGE_SIZE + 1;
    const to = Math.min(grid.page * SUBMISSIONS_PAGE_SIZE, grid.total);
    return `Показать записи с ${from} по ${to} из ${grid.total}`;
  });

  /** Позиция пагинатора — от нормализованной страницы ответа (ADR-109). */
  readonly firstIndex = computed(
    () => ((this.grid()?.page ?? 1) - 1) * SUBMISSIONS_PAGE_SIZE,
  );

  ngOnInit(): void {
    void this.initSelectors();
  }

  /**
   * Загрузка селекторов (группы и семестры — параллельно, отказ одного
   * не блокирует другой), проставление дефолтов и первая загрузка грида.
   * Отказ — notifyError якорем 'header' (экран без формы, IF-109).
   */
  private async initSelectors(): Promise<void> {
    const [groupsResult, semestersResult] = await Promise.allSettled([
      this.groupsService.getList(),
      this.labsService.getSemesters(),
    ]);

    if (groupsResult.status === 'fulfilled') {
      this.groups.set(groupsResult.value);
      const first = groupsResult.value[0];
      if (first !== undefined) {
        this.selectedGroupId.set(first.id);
      }
    } else {
      this.notifications.notifyError(toApiError(groupsResult.reason).body.message, 'header');
    }

    if (semestersResult.status === 'fulfilled') {
      this.semesters.set(semestersResult.value);
      const smallest = semestersResult.value[0];
      if (smallest !== undefined) {
        this.selectedSemester.set(smallest);
      }
    } else {
      this.notifications.notifyError(toApiError(semestersResult.reason).body.message, 'header');
    }

    await this.loadGrid();
  }

  /**
   * Смена группы: страница сбрасывается на 1, ведомость перезагружается
   * (ключ_специфики T-114, ADR-109).
   */
  onGroupChange(groupId: string | null): void {
    if (groupId === null || groupId === this.selectedGroupId()) {
      return;
    }
    this.selectedGroupId.set(groupId);
    this.requestedPage.set(1);
    void this.loadGrid();
  }

  /** Смена семестра: аналогично смене группы — страница 1 + перезагрузка. */
  onSemesterChange(semester: number | null): void {
    if (semester === null || semester === this.selectedSemester()) {
      return;
    }
    this.selectedSemester.set(semester);
    this.requestedPage.set(1);
    void this.loadGrid();
  }

  /** Листание пагинатора: 0-based index события → 1-based страница. */
  onPageChange(event: PaginatorState): void {
    if (event.page === undefined || event.page + 1 === this.requestedPage()) {
      return;
    }
    this.requestedPage.set(event.page + 1);
    void this.loadGrid();
  }

  /**
   * Загрузка ведомости для текущих селекторов и страницы: на время запроса
   * таблица в состоянии загрузки ([loading], по образцу works-page — ревью
   * CR-001 T-114). Ответ устаревшего запроса (после смены селектора/страницы)
   * отбрасывается без применения. Ошибка: грид сбрасывается (таблица
   * скрывается), текст — notifyError якорем 'header' (IF-106/IF-109:
   * «сервер — истина»).
   */
  private async loadGrid(): Promise<void> {
    const groupId = this.selectedGroupId();
    const semester = this.selectedSemester();
    if (groupId === null || semester === null) {
      return;
    }
    const requestId = ++this.gridRequestSeq;
    this.loading.set(true);
    try {
      const result = await this.submissionsService.getGrid({
        groupId,
        semester,
        page: this.requestedPage(),
      });
      if (requestId !== this.gridRequestSeq) {
        return;
      }
      this.grid.set(result);
      this.records.set(this.buildRecords(result));
    } catch (error) {
      if (requestId !== this.gridRequestSeq) {
        return;
      }
      this.grid.set(null);
      this.notifications.notifyError(toApiError(error).body.message, 'header');
    } finally {
      if (requestId === this.gridRequestSeq) {
        this.loading.set(false);
      }
    }
  }

  /** Карта дат пар из ответа getGrid (записи есть не у всех пар). */
  private buildRecords(grid: SubmissionsGridResult): Map<string, CellDates> {
    const records = new Map<string, CellDates>();
    for (const submission of grid.submissions) {
      records.set(pairKey(submission.studentId, submission.labId), {
        submitDate: submission.submitDate,
        defenseDate: submission.defenseDate,
      });
    }
    return records;
  }

  /** Увеличивает ревизию ячейки — форс перезаписи значения контрола. */
  private bumpCellRevision(
    studentId: string,
    labId: string,
    field: SubmissionsDateField,
  ): void {
    const key = `${pairKey(studentId, labId)}:${field}`;
    this.cellRevisions.update((revisions) => {
      const next = new Map(revisions);
      next.set(key, (revisions.get(key) ?? 0) + 1);
      return next;
    });
  }

  /**
   * Контрактная дата ячейки 'YYYY-MM-DD' | null — ПРИМИТИВ для входа
   * OnPush-ячейки: строка из карты записей, без создания объектов
   * (антидождь VBUG-003). Конвертация в Date — внутри ячейки.
   */
  recordDate(
    studentId: string,
    labId: string,
    field: SubmissionsDateField,
  ): string | null {
    const record = this.records().get(pairKey(studentId, labId));
    return record === undefined ? null : record[field];
  }

  /**
   * Ревизия ячейки: число меняется только при сбросе после ошибки —
   * OnPush-ячейка пересчитывает кэшированную Date и перезаписывает контрол.
   */
  cellRevision(studentId: string, labId: string, field: SubmissionsDateField): number {
    return this.cellRevisions().get(`${pairKey(studentId, labId)}:${field}`) ?? 0;
  }

  /** Блокировка своей ячейки на время её сохранения (RSK-006). */
  isCellSaving(
    studentId: string,
    labId: string,
    field: SubmissionsDateField,
  ): boolean {
    return this.savingCells().has(`${pairKey(studentId, labId)}:${field}`);
  }

  /**
   * Выбор/очистка даты ячейки: submissions.update получает АКТУАЛЬНЫЕ
   * значения ОБЕИХ дат пары — вторая дата берётся из текущего состояния,
   * выбранное поле конвертируется Date → 'YYYY-MM-DD' (null = сброс).
   * Успех: даты пары заменяются ответом мока; ошибка: notifyError
   * якорем 'header' + перезагрузка грида (IF-106: сервер — истина).
   */
  async onCellDateChange(
    studentId: string,
    labId: string,
    field: SubmissionsDateField,
    value: Date | null,
  ): Promise<void> {
    const savingKey = `${pairKey(studentId, labId)}:${field}`;
    if (this.savingCells().has(savingKey)) {
      return; // сохранение этой ячейки уже выполняется — дубля запроса нет
    }

    const record =
      this.records().get(pairKey(studentId, labId)) ??
      ({ submitDate: null, defenseDate: null } satisfies CellDates);
    const isoDate = value === null ? null : toIsoDate(value);
    const payload: CellDates =
      field === 'submitDate'
        ? { submitDate: isoDate, defenseDate: record.defenseDate }
        : { submitDate: record.submitDate, defenseDate: isoDate };

    this.savingCells.update((cells) => new Set(cells).add(savingKey));
    try {
      const saved = await this.submissionsService.update({
        studentId,
        labId,
        submitDate: payload.submitDate,
        defenseDate: payload.defenseDate,
      });
      this.records.update((records) => {
        const next = new Map(records);
        next.set(pairKey(studentId, labId), {
          submitDate: saved.submitDate,
          defenseDate: saved.defenseDate,
        });
        return next;
      });
    } catch (error) {
      this.notifications.notifyError(toApiError(error).body.message, 'header');
      // Сброс ячейки на серверную истину: без ревизии OnPush-ячейка с тем же
      // примитивным value не перезапишет значение контрола (VBUG-003).
      this.bumpCellRevision(studentId, labId, field);
      await this.loadGrid();
    } finally {
      this.savingCells.update((cells) => {
        const next = new Set(cells);
        next.delete(savingKey);
        return next;
      });
    }
  }
}
