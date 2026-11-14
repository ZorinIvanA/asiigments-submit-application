/**
 * Страница «Сдача работ» студента (C-113, SCR-010, US-11, /my-submissions):
 * своя строка ведомости только для чтения (календарь не открывается,
 * вызовов submissions.update нет). Данные — SubmissionsService.getMy
 * (IF-106) и LabsService.getSemesters (IF-103); ошибки мока — дословно
 * через NotificationService с якорем 'header' (IF-109: экран без формы).
 *
 * Вёрстки переключаются BreakpointObserver'ом (FR-022, единственная
 * калитка 768px): десктоп ≥768px — p-table (ADR-111) с двухуровневой
 * шапкой «Студент» + пары «Сдача»/«Защита» каждой работы; <768px —
 * карточки «Лаб N» без горизонтального скролла страницы. Даты отображаются
 * toDisplayDate (дд.мм.гггг, ADR-007); пустая дата — пустая ячейка.
 *
 * Признак «в группе» — синхронно из кэша профиля groupName (аменда 3 к
 * tech-solution, CR-002: MeDto содержит groupName по IF-101, загружено
 * guards'ами к моменту рендера); submissions.getMy при пустом списке
 * семестров НЕ вызывается — вызов с семестром вне 1..10 запрещён решением.
 *
 * Вырожденные состояния:
 *  - студент без группы — постоянный жёлтый баннер role=status «Вы не
 *    включены в группу…» вместо таблицы, селектор семестра остаётся
 *    (активен при наличии семестров);
 *  - «Нет семестров с работами» (ISS-108/ASM-015) — единый пустой
 *    компонент для двух случаев: семестров нет вовсе (селектор пуст и
 *    заблокирован) и у выбранного семестра нет работ, labs: [] (CR-004,
 *    селектор остаётся активным, смена семестра перезагружает экран).
 */
import { BreakpointObserver, BreakpointState } from '@angular/cdk/layout';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { TableModule } from 'primeng/table';

import { AuthService } from '../../../core/services/auth.service';
import { LabsService } from '../../../core/services/labs.service';
import { SubmissionsService } from '../../../core/services/submissions.service';
import { toApiError } from '../../../shared/api-error';
import { toDisplayDate } from '../../../shared/dates';
import { NOTIFICATION_MOBILE_MEDIA_QUERY } from '../../../shared/notifications/notification-model';
import { NotificationService } from '../../../shared/notifications/notification-service';
import { MySubmissionsDto } from '../../../shared/models';

/** Предупреждение «без группы» — дословно макету SCR-010 (FR-016). */
export const TEXT_NO_GROUP =
  'Вы не включены в группу, доступ к сдаче лабораторных не выдан';

/** Сообщение пустого состояния (ISS-108/ASM-015, CR-004) — единый текст. */
export const TEXT_NO_SEMESTERS = 'Нет семестров с работами';

/** Строка «Лаб N» таблицы/карточки с датами в формате отображения. */
interface SubmissionRow {
  readonly labId: string;
  readonly number: number;
  /** 'дд.мм.гггг' либо null — пустая ячейка (ISS-102: без прочерка). */
  readonly submitDate: string | null;
  readonly defenseDate: string | null;
}

@Component({
  selector: 'app-my-submissions-page',
  imports: [TableModule],
  templateUrl: './my-submissions-page.html',
  styleUrl: './my-submissions-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MySubmissionsPage {
  private readonly labsService = inject(LabsService);
  private readonly submissionsService = inject(SubmissionsService);
  private readonly notifications = inject(NotificationService);
  private readonly auth = inject(AuthService);
  private readonly breakpointObserver = inject(BreakpointObserver);
  private readonly destroyRef = inject(DestroyRef);

  /** Мобильная вёрстка (<768px) — карточки вместо таблицы (FR-022). */
  readonly isMobile = signal(false);

  /** Семестры с работами (IF-103: distinct, по возрастанию). */
  readonly semesters = signal<number[]>([]);
  /** Выбранный семестр; null — семестров нет (селектор пуст, заблокирован). */
  readonly selectedSemester = signal<number | null>(null);
  /** Ответ getMy текущего семестра; null — данные семестра не загружены. */
  readonly data = signal<MySubmissionsDto | null>(null);
  /** true, пока выполняется запрос данных — индикатор [loading] p-table. */
  readonly loading = signal(false);

  /**
   * true после УСПЕШНОГО ответа getSemesters: пустой компонент показывается
   * только когда семестры действительно загружены и их нет — при отказе
   * getSemesters экран не выдаёт «Нет семестров с работами» (ошибка
   * сигнализируется уведомлением IF-109).
   */
  readonly semestersLoaded = signal(false);

  /**
   * Признак «включён в группу» — синхронно из кэша профиля (аменда 3,
   * CR-002): groupName !== null. Не зависит от ответов getMy.
   */
  readonly inGroup = computed(() => this.auth.currentUser()?.groupName != null);

  /**
   * Предупреждение «без группы» — вместо таблицы, постоянно (макет
   * SCR-010); приоритетнее любых других состояний экрана.
   */
  readonly showNoGroupWarning = computed(() => !this.inGroup());

  /**
   * Единый пустой компонент «Нет семестров с работами» (текст один,
   * CR-004): (а) семестров с работами нет вовсе — селектор пуст и
   * заблокирован; (б) у выбранного семестра работ нет (getMy вернул
   * labs: []). Пока запрос выполняется, пустой текст не мигает.
   */
  readonly showEmptyState = computed(
    () =>
      this.semestersLoaded() &&
      this.inGroup() &&
      !this.loading() &&
      (this.semesters().length === 0 ||
        (this.data() !== null && this.data()!.labs.length === 0)),
  );

  /** Таблица/карточки — студент в группе и по выбранному семестру есть работы. */
  readonly showTable = computed(
    () => this.inGroup() && this.data() !== null && this.rows().length > 0,
  );

  /** ФИО студента для колонки «Студент» (кэш профиля AuthService). */
  readonly fullName = computed(() => this.auth.currentUser()?.fullName ?? '');

  /** Тексты макета SCR-010 — единый источник для шаблона и тестов. */
  readonly noGroupText = TEXT_NO_GROUP;
  readonly noSemestersText = TEXT_NO_SEMESTERS;

  /**
   * Строки «Лаб N» текущего семестра: пара «Сдача»/«Защита» у КАЖДОЙ
   * работы независимо от defenseRequired (ASM-010), даты — формат
   * отображения, пустая дата — null.
   */
  readonly rows = computed<SubmissionRow[]>(() => {
    const data = this.data();
    if (data === null || !this.inGroup()) {
      return [];
    }
    const byLabId = new Map(data.submissions.map((item) => [item.labId, item]));
    return data.labs.map((lab) => {
      const submission = byLabId.get(lab.id);
      return {
        labId: lab.id,
        number: lab.number,
        submitDate: toDisplayDate(submission?.submitDate ?? null),
        defenseDate: toDisplayDate(submission?.defenseDate ?? null),
      };
    });
  });

  /**
   * Единственная строка p-table — своя ведомость: содержимое строки
   * (ФИО + ячейки дат) шаблон #body берёт из rows(), значение value
   * роли не играет.
   */
  readonly tableValue: object[] = [{}];

  /** Номер последнего запрошенного ответа getMy — отбрасывание устаревших. */
  private loadSeq = 0;

  constructor() {
    this.breakpointObserver
      .observe(NOTIFICATION_MOBILE_MEDIA_QUERY)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((state: BreakpointState) => {
        this.isMobile.set(state.matches);
      });
    void this.reload();
  }

  /**
   * Полная загрузка (первичный вход и возврат на страницу): семестры,
   * по умолчанию — НАИМЕНЬШИЙ из возвращённых (макет SCR-010, сид: 1),
   * затем сдачи выбранного семестра. Семестров нет — getMy не вызывается
   * (аменда 3, CR-002): состояние определяется по groupName профиля.
   */
  async reload(): Promise<void> {
    this.loading.set(true);
    try {
      const semesters = await this.labsService.getSemesters();
      this.semestersLoaded.set(true);
      if (semesters.length === 0) {
        this.semesters.set([]);
        this.selectedSemester.set(null);
        this.data.set(null);
        this.loading.set(false);
        return;
      }
      this.semesters.set(semesters);
      const initial = Math.min(...semesters);
      this.selectedSemester.set(initial);
      await this.loadSubmissions(initial);
    } catch (error: unknown) {
      this.notifications.notifyError(toApiError(error).body.message, 'header');
      this.loading.set(false);
    }
  }

  /** Смена семестра селектором → перезагрузка сдач выбранного семестра. */
  async onSemesterChange(event: Event): Promise<void> {
    const semester = Number((event.target as HTMLSelectElement).value);
    if (!Number.isInteger(semester)) {
      return;
    }
    this.selectedSemester.set(semester);
    await this.loadSubmissions(semester);
  }

  /**
   * Загрузка сдач выбранного семестра (семестр всегда из списка
   * getSemesters, т.е. 1..10 — аменда 3). Ответ предыдущего запроса
   * (смена семестра на лету) отбрасывается.
   */
  private async loadSubmissions(semester: number): Promise<void> {
    const seq = ++this.loadSeq;
    this.loading.set(true);
    try {
      const data = await this.submissionsService.getMy(semester);
      if (seq !== this.loadSeq) {
        return;
      }
      this.data.set(data);
    } catch (error: unknown) {
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
}
