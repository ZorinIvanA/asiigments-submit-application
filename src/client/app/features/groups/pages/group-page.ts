/**
 * Карточка группы «/groups/:id» (C-114, SCR-012, FR-4.9): крошки
 * «Группы / <имя>», заголовок с именем, счётчик «Студентов: N» и состав
 * группы с пагинацией 10 (ADR-109) и подписью
 * «Показать записи с X по Y из Z».
 *
 * Контракты IF-104/IF-105 и IF-109: имя группы берётся из
 * GroupsService.getList (работает и для пустой группы), состав —
 * GroupsService.getStudents; исключение студента —
 * StudentsService.setGroup(id, null) после подтверждения
 * p-confirmDialog «Исключить студента <ФИО> из группы?» с кнопками
 * «Yes»/«No» (ADR-111); успех «Сохранено» — счётчик и страница
 * перечитываются (сервер — истина). Отсутствующая группа — «Группа не
 * найдена» под шапкой (якорь 'header') и возврат к /groups.
 */
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ConfirmationService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { ConfirmDialogModule } from 'primeng/confirmdialog';
import { TableModule } from 'primeng/table';

import { GROUPS_PAGE_SIZE, GroupsService } from '../../../core/services/groups.service';
import { StudentsService } from '../../../core/services/students.service';
import { toApiError } from '../../../shared/api-error';
import { submitting } from '../../../shared/loading/submitting';
import { NotificationService } from '../../../shared/notifications/notification-service';
import { StudentDto } from '../../../shared/models';

/** Текст отказа для несуществующей группы — дословно IF-104. */
const MESSAGE_GROUP_NOT_FOUND = 'Группа не найдена';

@Component({
  selector: 'app-group-page',
  templateUrl: './group-page.html',
  styleUrl: './group-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TableModule, ButtonModule, ConfirmDialogModule],
  viewProviders: [ConfirmationService],
})
export class GroupPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly groupsService = inject(GroupsService);
  private readonly studentsService = inject(StudentsService);
  private readonly notifications = inject(NotificationService);
  private readonly confirmationService = inject(ConfirmationService);

  /** Размер страницы состава группы — константа контракта IF-104. */
  readonly pageSize = GROUPS_PAGE_SIZE;

  readonly groupId = signal<string | null>(null);
  readonly groupName = signal('');
  /** Страница состава группы и полный размер выборки (счётчик «Студентов»). */
  readonly students = signal<StudentDto[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);

  /** Счётчик загрузок: ответ устаревшего запроса (id/страница) не применяется. */
  private loadSeq = 0;

  readonly pagesCount = computed(() =>
    Math.max(1, Math.ceil(this.total() / this.pageSize)),
  );

  readonly pageNumbers = computed(() =>
    Array.from({ length: this.pagesCount() }, (_, index) => index + 1),
  );

  /**
   * Подпись пагинации FR-012: «Показать записи с X по Y из Z» (аменда 7,
   * ADR-111): выводится всегда при отрисованном списке, включая total=0 —
   * X=(page−1)·pageSize+1, поэтому пустая группа даёт дословно
   * «Показать записи с 1 по 0 из 0» (эталон — SubmissionsPage, FR-015).
   */
  readonly rangeLabel = computed(() => {
    const total = this.total();
    const from = (this.page() - 1) * this.pageSize + 1;
    const to = Math.min(this.page() * this.pageSize, total);
    return `Показать записи с ${from} по ${to} из ${total}`;
  });

  /** Индикатор исключения: блокирует кнопки строк на время запроса. */
  readonly excludeSubmit = submitting();

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      this.groupId.set(params.get('id'));
      this.page.set(1);
      this.groupName.set('');
      this.students.set([]);
      this.total.set(0);
      void this.load();
    });
  }

  goToPage(target: number): void {
    if (target < 1 || target > this.pagesCount() || target === this.page()) {
      return;
    }
    this.page.set(target);
    void this.loadStudents();
  }

  /**
   * «Исключить из группы» — подтверждение (единообразно с FR-017):
   * «Исключить студента <ФИО> из группы?», кнопки «Yes»/«No».
   */
  confirmExclude(student: StudentDto): void {
    this.confirmationService.confirm({
      header: 'Исключение из группы',
      message: `Исключить студента ${student.fullName} из группы?`,
      accept: () => void this.exclude(student),
    });
  }

  /** Имя группы (getList) и первая страница состава; 404 — баннер + /groups. */
  private async load(): Promise<void> {
    const id = this.groupId();
    if (id === null) {
      return;
    }
    const seq = ++this.loadSeq;
    try {
      const groups = await this.groupsService.getList();
      if (seq !== this.loadSeq) {
        return; // устаревший ответ: сменился параметр маршрута или страница
      }
      const group = groups.find((candidate) => candidate.id === id);
      if (group === undefined) {
        this.handleMissingGroup();
        return;
      }
      this.groupName.set(group.name);
    } catch (error) {
      if (seq !== this.loadSeq) {
        return;
      }
      this.notifications.notifyError(toApiError(error).body.message, 'header');
      return;
    }
    await this.loadStudents();
  }

  /** Страница состава; пустая страница не на первой — шаг назад (ADR-109). */
  private async loadStudents(): Promise<void> {
    const id = this.groupId();
    if (id === null) {
      return;
    }
    const seq = ++this.loadSeq;
    const page = this.page();
    try {
      const result = await this.groupsService.getStudents(id, page);
      if (seq !== this.loadSeq) {
        return; // устаревший ответ: сменился параметр маршрута или страница
      }
      this.students.set(result.items);
      this.total.set(result.total);
      if (result.items.length === 0 && this.page() > 1) {
        this.page.set(this.page() - 1);
        await this.loadStudents();
      }
    } catch (error) {
      if (seq !== this.loadSeq) {
        return;
      }
      const apiError = toApiError(error);
      if (apiError.status === 404) {
        this.handleMissingGroup();
        return;
      }
      this.notifications.notifyError(apiError.body.message, 'header');
    }
  }

  /** Группа исчезла (или её не было): баннер под шапкой и возврат к списку. */
  private handleMissingGroup(): void {
    this.notifications.notifyError(MESSAGE_GROUP_NOT_FOUND, 'header');
    void this.router.navigate(['/groups']);
  }

  /** «Yes» в подтверждении: setGroup(id, null) → «Сохранено» → перечитывание. */
  private async exclude(student: StudentDto): Promise<void> {
    await this.excludeSubmit.run(async () => {
      try {
        await this.studentsService.setGroup(student.id, null);
        this.notifications.notifySuccess('Сохранено');
      } catch (error) {
        this.notifications.notifyError(toApiError(error).body.message, 'header');
      }
      // Сервер — истина: счётчик и страница обновляются при любом исходе.
      await this.loadStudents();
    });
  }
}
