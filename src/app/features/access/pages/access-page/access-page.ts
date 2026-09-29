/**
 * AccessPage — раздел «Доступ» (/access, C-115, SCR-013, FR-4.6/US-17):
 * постраничный список студентов (10 на страницу, ADR-109) с ci-поиском по
 * ФИО/логину/email (дебаунс 300 мс, смена условия → страница 1), фильтром
 * «Без группы» (groupId='none') и сменой группы селектором в строке
 * (p-select): включение/перевод выполняются немедленным students.setGroup,
 * выбор «Без группы» подтверждается диалогом «Исключить студента <ФИО> из
 * группы?» («Yes»/«No»; «No» возвращает селектор на прежнее значение, а
 * setGroup не вызывается). Значение селектора строки — student.groupId
 * из DTO (аменда 6), справочник групп нужен только для списка опций.
 * Состояние сохранения строки — блокировка селектора строки и спиннер в
 * подписи пагинации на время запроса (макет SCR-013); подпись «Показать
 * записи с X по Y из Z» строится из PagedResult (ADR-109). Если страница
 * списка опустела после изменения данных (например, включён последний
 * студент страницы под фильтром), выполняется откат на предыдущую
 * существующую страницу (по образцу works/group, ADR-109). Ошибки сервисов
 * (IF-105/IF-104) — notifyError с якорем 'header' (экран без формы,
 * IF-109) и перезагрузкой текущей страницы списка; при ошибке справочника
 * групп селекторы строк не рендерятся — в ячейке группы выводится DTO-шное
 * groupName вместо пустой подписи селектора (аменда 6, деградация; ревью
 * CR-007 iter2 T-117). Успехи setGroup
 * без тостов: фидбек — обновлённая строка после перезагрузки (аменда 6).
 *
 * Раздел «Группы» — отдельная фича (T-116); маршруты фичи объявлены в
 * access.routes.ts (ADR-110).
 */
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Subject } from 'rxjs';
import { debounceTime, distinctUntilChanged } from 'rxjs/operators';

import { ConfirmationService } from 'primeng/api';
import { ConfirmDialog } from 'primeng/confirmdialog';
import { Select } from 'primeng/select';
import { TableModule } from 'primeng/table';

import { GroupsService } from '../../../../core/services/groups.service';
import {
  STUDENTS_PAGE_SIZE,
  StudentListQuery,
  StudentsService,
} from '../../../../core/services/students.service';
import { toApiError } from '../../../../shared/api-error';
import { GroupDto, StudentDto } from '../../../../shared/models';
import { NotificationService } from '../../../../shared/notifications/notification-service';

/** Задержка дебаунса поиска, мс (SCR-013). */
const SEARCH_DEBOUNCE_MS = 300;

/** Подпись варианта «без группы» в селекторе строки (SCR-013). */
const NO_GROUP_LABEL = 'Без группы';

/** Значение groupId запроса списка для фильтра «только без группы» (IF-105). */
const GROUP_FILTER_NONE = 'none';

/** Опция селектора группы в строке: «Без группы» (null) либо группа (uuid). */
interface GroupOption {
  label: string;
  value: string | null;
}

/** Строка таблицы студентов: данные студента + локальное состояние селектора. */
interface AccessRow {
  readonly student: StudentDto;
  /** Значение селектора строки: uuid группы либо null = «Без группы». */
  groupId: string | null;
  /** true — по строке идёт students.setGroup: селектор строки заблокирован. */
  saving: boolean;
}

@Component({
  selector: 'app-access-page',
  imports: [FormsModule, TableModule, Select, ConfirmDialog],
  templateUrl: './access-page.html',
  styleUrl: './access-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [ConfirmationService],
})
export class AccessPage implements OnInit {
  /** Размер страницы списка студентов (IF-105, ADR-109) — реэкспорт сервиса. */
  protected readonly pageSize = STUDENTS_PAGE_SIZE;

  protected readonly searchTerm = signal('');
  protected readonly onlyWithoutGroup = signal(false);
  protected readonly page = signal(1);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly rows = signal<AccessRow[]>([]);
  protected readonly groups = signal<GroupDto[]>([]);
  /** Справочник групп недоступен: селекторы строк заблокированы (аменда 6). */
  protected readonly groupsFailed = signal(false);

  private readonly studentsService = inject(StudentsService);
  private readonly groupsService = inject(GroupsService);
  private readonly notifications = inject(NotificationService);
  private readonly confirmation = inject(ConfirmationService);

  /** Поток ввода поиска: дебаунс 300 мс → страница 1 и перезагрузка (SCR-013). */
  private readonly searchInput = new Subject<string>();

  /** Номер запроса списка: ответы устаревших запросов отбрасываются. */
  private loadSeq = 0;

  constructor() {
    this.searchInput
      .pipe(
        debounceTime(SEARCH_DEBOUNCE_MS),
        distinctUntilChanged(),
        takeUntilDestroyed(),
      )
      .subscribe(() => {
        this.page.set(1);
        void this.loadStudents();
      });
  }

  ngOnInit(): void {
    void this.init();
  }

  /** Опции селектора строки: «Без группы» + все группы (IF-104, SCR-013). */
  protected readonly groupOptions = computed<GroupOption[]>(() => [
    { label: NO_GROUP_LABEL, value: null },
    ...this.groups().map((group) => ({ label: group.name, value: group.id })),
  ]);

  /** true — идёт загрузка списка либо сохранение хотя бы одной строки. */
  protected readonly busy = computed(
    () => this.loading() || this.rows().some((row) => row.saving),
  );

  /**
   * Подпись «Показать записи с X по Y из Z» (ADR-109). Аменда 7: подпись
   * видима всегда, при total=0 — «Показать записи с 1 по 0 из 0» рядом
   * со строкой пустого состояния.
   */
  protected readonly caption = computed(() => {
    const from = (this.page() - 1) * this.pageSize + 1;
    const to = Math.min(this.page() * this.pageSize, this.total());
    return `Показать записи с ${from} по ${to} из ${this.total()}`;
  });

  /** Число страниц по факту полной выборки после фильтра/поиска. */
  protected readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.total() / this.pageSize)),
  );

  /** Номера страниц для кнопок пагинации (1..N). */
  protected readonly pageNumbers = computed(() =>
    Array.from({ length: this.totalPages() }, (_, index) => index + 1),
  );

  protected onSearchInput(value: string): void {
    this.searchTerm.set(value);
    this.searchInput.next(value);
  }

  /** Кнопка очистки поля поиска (SCR-013): пустой ввод → страница 1. */
  protected clearSearch(): void {
    if (this.searchTerm() !== '') {
      this.onSearchInput('');
    }
  }

  protected onOnlyWithoutGroupChange(checked: boolean): void {
    this.onlyWithoutGroup.set(checked);
    this.page.set(1);
    void this.loadStudents();
  }

  protected goToPage(target: number): void {
    const clamped = Math.min(Math.max(1, target), this.totalPages());
    if (this.loading() || clamped === this.page()) {
      return;
    }
    this.page.set(clamped);
    void this.loadStudents();
  }

  /**
   * Смена группы селектором строки (IF-105): включение/перевод — немедленный
   * setGroup; «Без группы» у студента с группой — подтверждение; «No»
   * возвращает селектор на прежнее значение без вызова setGroup. Прежним
   * значением служит student.groupId из DTO (аменда 6).
   */
  protected onGroupChange(row: AccessRow, value: string | null): void {
    row.groupId = value;
    const previous = row.student.groupId;
    if (row.saving || value === previous) {
      return;
    }
    if (value === null) {
      this.confirmation.confirm({
        message: `Исключить студента ${row.student.fullName} из группы?`,
        accept: () => void this.applyGroup(row, null),
        reject: () => {
          row.groupId = previous;
          this.refreshRows();
        },
      });
      return;
    }
    void this.applyGroup(row, value);
  }

  /** Открытие экрана: справочник групп, затем первая страница списка. */
  private async init(): Promise<void> {
    try {
      this.groups.set(await this.groupsService.getList());
      this.groupsFailed.set(false);
    } catch (error) {
      // Деградация (аменда 6): баннер + блокировка селекторов; строки
      // рендерятся с корректными данными DTO без справочника.
      this.groupsFailed.set(true);
      this.notifications.notifyError(this.errorMessage(error), 'header');
    }
    await this.loadStudents();
  }

  /**
   * Загрузка текущей страницы списка (IF-105). Последовательные запросы
   * упорядочиваются счётчиком: ответ устаревшего запроса не применяется.
   * Если запрошенная страница опустела (данные изменились между действиями —
   * например, включён последний студент последней страницы под фильтром),
   * выполняется откат на предыдущую страницу, но не раньше первой
   * (ADR-109: страницы 1-based; по образцу works/group).
   */
  private async loadStudents(): Promise<void> {
    const seq = ++this.loadSeq;
    this.loading.set(true);
    const query: StudentListQuery = {
      search: this.searchTerm(),
      groupId: this.onlyWithoutGroup() ? GROUP_FILTER_NONE : null,
      page: this.page(),
    };
    try {
      const result = await this.studentsService.getList(query);
      if (seq !== this.loadSeq) {
        return;
      }
      this.total.set(result.total);
      this.rows.set(result.items.map((student) => this.toRow(student)));
      if (result.items.length === 0 && this.page() > 1) {
        this.page.set(this.page() - 1);
        await this.loadStudents();
      }
    } catch (error) {
      if (seq !== this.loadSeq) {
        return;
      }
      this.total.set(0);
      this.rows.set([]);
      this.notifications.notifyError(this.errorMessage(error), 'header');
    } finally {
      if (seq === this.loadSeq) {
        this.loading.set(false);
      }
    }
  }

  /**
   * Включение/перевод/исключение (IF-105): setGroup, затем перезагрузка
   * текущей страницы списка — она же возвращает строкам фактическое
   * состояние при отказе и откатывает опустевшую страницу (контракт T-117,
   * CR-001). Успех без тостов: фидбек — обновлённая строка (аменда 6).
   */
  private async applyGroup(row: AccessRow, groupId: string | null): Promise<void> {
    if (row.saving) {
      return;
    }
    row.saving = true;
    this.refreshRows();
    try {
      await this.studentsService.setGroup(row.student.id, groupId);
    } catch (error) {
      this.notifications.notifyError(this.errorMessage(error), 'header');
    } finally {
      await this.loadStudents();
    }
  }

  private toRow(student: StudentDto): AccessRow {
    return { student, groupId: student.groupId, saving: false };
  }

  /** Пересборка массива строк с теми же объектами — сигнал для OnPush-CD. */
  private refreshRows(): void {
    this.rows.update((rows) => [...rows]);
  }

  /** Текст ошибки для notifyError — дословно body.message мока (IF-109). */
  private errorMessage(error: unknown): string {
    return toApiError(error).body.message;
  }
}
