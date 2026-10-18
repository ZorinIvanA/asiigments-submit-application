using LabsApp.Domain.Entities;

namespace LabsApp.Storage;

/// <summary>
/// Репозиторий пользователей (IF-015, FR-024). Контроллеры и сервисы зависят
/// только от интерфейса; конкретная in-memory реализация регистрируется в
/// композиция-корне (Program.cs).
///
/// Договорённости in-memory реализации:
/// - ci-уникальность login и email — по правилу коллации <see cref="Domain.Collation"/>
///   (нижний регистр + ordinal); проверка и занятие индексов атомарны в одной
///   критической секции с вставкой/заменой (IF-015);
/// - операции чтения возвращают копии-снимки: изменение полученной сущности не
///   влияет на хранилище и ci-индексы, мутации — только через узкие атомарные
///   мутаторы <see cref="SetGroup"/> (группа, SEC-001), <see cref="SetPassword"/>
///   (пароль) и <see cref="UpdateProfile"/> (профиль) — каждый меняет ТОЛЬКО свои
///   поля и не откатывает конкурентные мутации соседних полей (CR-001).
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Атомарно проверяет ci-уникальность Login и Email и вставляет запись.
    /// Конфликт — <see cref="StorageConflictException"/> (запись не вставляется).
    /// </summary>
    void Add(User user);

    /// <summary>Пользователь по uuid либо null.</summary>
    User? GetById(Guid id);

    /// <summary>Пользователь по логину без учёта регистра либо null.</summary>
    User? GetByLogin(string login);

    /// <summary>Пользователь по email без учёта регистра либо null.</summary>
    User? GetByEmail(string email);

    /// <summary>
    /// ПОЛНАЯ замена записи по <see cref="User.Id"/> (запись обязана существовать —
    /// иначе <see cref="InvalidOperationException"/>) с перепривязкой ci-индексов;
    /// конфликт Login/Email с другим пользователем — <see cref="StorageConflictException"/>.
    /// <para>
    /// Контракт СУЖЕН (аменда CR-001/SEC-001, OQ-002): полная замена записи
    /// устаревшим снимком откатывает конкурентные изменения соседних полей, поэтому
    /// метод НЕ применяется для смены пароля (<see cref="SetPassword"/>), профиля
    /// (<see cref="UpdateProfile"/>) и группы студента (<see cref="SetGroup"/>).
    /// Прод-контроллеры его не вызывают (гейт QG-006: 0 call-сайтов в Controllers/);
    /// легитимные применения — тестовые фикстуры (сборка/исправление записей) и
    /// паритет с будущей EF Core-итерацией, где метод будет удалён.
    /// </para>
    /// </summary>
    void Update(User user);

    /// <summary>
    /// Узкая атомарная мутация группы студента (SEC-001, IF-015): в ОДНОЙ критической
    /// секции проверяет существование пользователя и его роль (= student), затем для
    /// ненулевой цели — существование группы вызовом <paramref name="groupExists"/>,
    /// и меняет ТОЛЬКО <see cref="User.GroupId"/>. Остальные поля хранимой записи
    /// (PasswordHash, Login, Email, FullName) не перечитываются и не перезаписываются,
    /// поэтому конкурентная смена пароля/профиля студента не откатывается, а группа,
    /// удалённая между проверкой и записью, не оставляет висячего GroupId.
    /// <para>
    /// <paramref name="groupExists"/> вызывается внутри критической секции: реализациям
    /// с единой блокировкой хранилища повторный вход реентерабельного монитора не
    /// мешает. Для <paramref name="groupId"/> = null проверка группы не выполняется,
    /// делегат может быть null; ненулевая цель с null-делегатом — ArgumentNullException.
    /// </para>
    /// </summary>
    SetGroupResult SetGroup(Guid userId, Guid? groupId, Func<Guid, bool>? groupExists);

    /// <summary>
    /// Узкая атомарная мутация пароля (CR-001/SEC-001, IF-015): в ОДНОЙ критической
    /// секции проверяет существование записи и меняет ТОЛЬКО
    /// <see cref="User.PasswordHash"/>. Остальные поля хранимой записи (Login, Email,
    /// FullName, Role, GroupId, CreatedAt) не перечитываются и не перезаписываются,
    /// поэтому конкурентные смена профиля и назначение группы студента не откатываются
    /// устаревшим снимком; ci-индексы не затрагиваются.
    /// <para>
    /// Запись отсутствует — false без исключения (вызывающая сторона отдаёт
    /// 401 «Не авторизован», IF-015 NOT_FOUND). <paramref name="passwordHash"/> null
    /// либо пустой — <see cref="ArgumentNullException"/> (валидация вне секции).
    /// </para>
    /// </summary>
    bool SetPassword(Guid userId, string passwordHash);

    /// <summary>
    /// Узкая атомарная мутация профиля (CR-001/SEC-001, IF-015): в ОДНОЙ критической
    /// секции проверяет существование записи, ci-занятость lower(email) ДРУГИМ
    /// пользователем (<see cref="Domain.Collation.Key"/>; совпадение с собственной
    /// записью — не конфликт) и меняет ТОЛЬКО <see cref="User.FullName"/> и
    /// <see cref="User.Email"/> с перепривязкой ci-индекса byEmail. Остальные поля
    /// (Login, Role, GroupId, PasswordHash, CreatedAt) не перечитываются и не
    /// перезаписываются, поэтому конкурентная смена пароля и группы не откатываются;
    /// гонка двух UpdateProfile на один email — ровно один Success (EMAIL_CONFLICT).
    /// <para>
    /// Запись отсутствует — <see cref="UpdateProfileResult.UserNotFound"/>;
    /// email занят другим — <see cref="UpdateProfileResult.EmailConflict"/> (запись
    /// не изменяется); <paramref name="fullName"/>/<paramref name="email"/> null —
    /// <see cref="ArgumentNullException"/> (валидация вне секции). Полевые правила
    /// длины/формата — зона контроллера (FR-006).
    /// </para>
    /// </summary>
    UpdateProfileResult UpdateProfile(Guid userId, string fullName, string email);

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
    IReadOnlyList<User> ListStudents(string? search = null, string? groupIdFilter = null);

    /// <summary>Студенты группы (порядок не определён; сортировка — в сервисе, FR-019).</summary>
    IReadOnlyList<User> ListByGroup(Guid groupId);

    /// <summary>Число студентов группы (studentCount GroupDto вычисляется подсчётом, data_design).</summary>
    int CountByGroup(Guid groupId);
}
