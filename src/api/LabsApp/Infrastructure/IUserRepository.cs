using LabsApp.Domain.Entities;

namespace LabsApp.Infrastructure;

/// <summary>
/// Репозиторий пользователей (IF-015, FR-024). Контракт асинхронный: реализации
/// могут выполнять ввод-вывод (EF Core/PostgreSQL); контроллеры и сервисы
/// зависят только от интерфейса, конкретный класс регистрируется в
/// композиция-корне (Program.cs).
///
/// Договорённости реализаций:
/// - ci-уникальность login и email — по правилу коллации <see cref="Domain.Collation"/>
///   (нижний регистр + ordinal); проверка и вставка атомарны (IF-015);
/// - операции чтения возвращают копии-снимки: изменение полученной сущности не
///   влияет на хранилище, мутации — только через узкие атомарные мутаторы
///   <see cref="SetGroupAsync"/> (группа, SEC-001) и <see cref="UpdateProfileAsync"/>
///   (профиль) — каждый меняет ТОЛЬКО
///   свои поля и не откатывает конкурентные мутации соседних полей (CR-001).
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Атомарно проверяет ci-уникальность Login и Email и вставляет запись.
    /// Конфликт — <see cref="StorageConflictException"/> (запись не вставляется).
    /// </summary>
    Task AddAsync(User user);

    /// <summary>Пользователь по uuid либо null.</summary>
    Task<User?> GetByIdAsync(Guid id);

    /// <summary>Пользователь по логину без учёта регистра либо null.</summary>
    Task<User?> GetByLoginAsync(string login);

    /// <summary>Пользователь по email без учёта регистра либо null.</summary>
    Task<User?> GetByEmailAsync(string email);

    /// <summary>
    /// ПОЛНАЯ замена записи по <see cref="User.Id"/> (запись обязана существовать —
    /// иначе <see cref="InvalidOperationException"/>) с перепривязкой ci-индексов;
    /// конфликт Login/Email с другим пользователем — <see cref="StorageConflictException"/>.
    /// <para>
    /// Контракт СУЖЕН (аменда CR-001/SEC-001, OQ-002): полная замена записи
    /// устаревшим снимком откатывает конкурентные изменения соседних полей, поэтому
    /// метод НЕ применяется для правки профиля
    /// (<see cref="UpdateProfileAsync"/>) и группы студента (<see cref="SetGroupAsync"/>).
    /// Прод-контроллеры его не вызывают; легитимные применения — тестовые фикстуры
    /// (сборка/исправление записей).
    /// </para>
    /// </summary>
    Task UpdateAsync(User user);

    /// <summary>
    /// Узкая атомарная мутация группы студента (SEC-001, IF-015): проверяет
    /// существование пользователя и его роль (= student), затем для ненулевой
    /// цели — существование группы вызовом <paramref name="groupExists"/>,
    /// и меняет ТОЛЬКО <see cref="User.GroupId"/>. Остальные поля хранимой записи
    /// (PasswordHash, Login, Email, FullName) не перечитываются и не перезаписываются,
    /// поэтому конкурентная смена пароля/профиля студента не откатывается, а группа,
    /// удалённая между проверкой и записью, не оставляет висячего GroupId.
    /// <para>
    /// <paramref name="groupExists"/> — асинхронный делегат (реализации хранилищ
    /// асинхронны); для <paramref name="groupId"/> = null проверка группы не
    /// выполняется, делегат может быть null; ненулевая цель с null-делегатом —
    /// ArgumentNullException.
    /// </para>
    /// </summary>
    Task<SetGroupResult> SetGroupAsync(Guid userId, Guid? groupId, Func<Guid, Task<bool>>? groupExists);

    /// <summary>
    /// Узкая атомарная мутация профиля (CR-001/SEC-001, IF-015): проверяет
    /// существование записи, ci-занятость lower(email) ДРУГИМ пользователем
    /// (<see cref="Domain.Collation.Key"/>; совпадение с собственной записью — не
    /// конфликт) и меняет ТОЛЬКО <see cref="User.FullName"/> и <see cref="User.Email"/>
    /// с перепривязкой ci-индекса byEmail. Остальные поля (Login, Role, GroupId,
    /// PasswordHash, CreatedAt) не перечитываются и не перезаписываются, поэтому
    /// конкурентные смена пароля и группы не откатываются; гонка двух
    /// UpdateProfile на один email — ровно один Success (EMAIL_CONFLICT).
    /// <para>
    /// Запись отсутствует — <see cref="UpdateProfileResult.UserNotFound"/>;
    /// email занят другим — <see cref="UpdateProfileResult.EmailConflict"/> (запись
    /// не изменяется); <paramref name="fullName"/>/<paramref name="email"/> null —
    /// <see cref="ArgumentNullException"/> (валидация до вызова). Полевые правила
    /// длины/формата — зона контроллера (FR-006).
    /// </para>
    /// </summary>
    Task<UpdateProfileResult> UpdateProfileAsync(Guid userId, string fullName, string email);

    /// <summary>
    /// Студенты с поиском и фильтром группы (FR-020): все пользователи с ролью
    /// student, удовлетворяющие обоим фильтрам (порядок не определён; сортировка
    /// fullName↑ затем login↑ — в сервисе, AR-005).
    /// <para>
    /// <paramref name="search"/> — многословный одно-полевой ci-поиск: после трима
    /// запрос разбивается на токены по пробелам; запись проходит, если ВСЕ токены —
    /// подстроки без учёта регистра ОДНОГО И ТОГО ЖЕ поля (FullName ИЛИ Login ИЛИ
    /// Email), порядок токенов не значим; пустой/отсутствующий search — без фильтра
    /// (валидация длины >200 — зона контроллера, FR-020).
    /// </para>
    /// <para>
    /// <paramref name="groupIdFilter"/>: null/пустой — без фильтра; «none» — только
    /// студенты без группы; uuid — только эта группа (неизвестный uuid — пустая
    /// выборка, НЕ 404, FR-020); иная нечитаемая строка — также пустая выборка.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<User>> ListStudentsAsync(string? search = null, string? groupIdFilter = null);

    /// <summary>Студенты группы (порядок не определён; сортировка — в сервисе, FR-019).</summary>
    Task<IReadOnlyList<User>> ListByGroupAsync(Guid groupId);

    /// <summary>Число студентов группы (studentCount GroupDto вычисляется подсчётом, data_design).</summary>
    Task<int> CountByGroupAsync(Guid groupId);
}
