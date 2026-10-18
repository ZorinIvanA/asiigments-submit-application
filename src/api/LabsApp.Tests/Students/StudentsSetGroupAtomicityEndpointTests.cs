using System.Net;
using LabsApp.Domain.Entities;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using LabsApp.Storage.InMemory;
using LabsApp.Tests.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LabsApp.Tests.Students;

// ============================================================================
// Эндпойнт-тесты SEC-001 (доработка T-128): PUT /students/{id}/group обязан
// быть УЗКОЙ атомарной мутацией — IUserRepository.SetGroup: проверка студента
// и группы + смена ТОЛЬКО GroupId в одной критической секции StorageLock.
// Полная замена записи устаревшим снимком (GetById → чтение тела → Update)
// откатывала бы конкурентную смену пароля студента (эксплойт SEC-001: смена
// пароля через PUT /me/password молча откатывается, отзыв refresh-токенов уже
// состоялся) и оставляла висячий GroupId при удалении группы «в полёте».
//
// Окно гонки детерминировано швом-декоратором IUserRepository
// (<see cref="GatedUserRepository"/>): первый GetById отслеживаемого студента —
// это pre-check маршрута в StudentsController (CookieAuthenticationHandler
// хранилище не читает) — сигналит Arrived и удерживает контроллер, пока тест
// меняет хранилище, затем Release продолжает запрос.
// ============================================================================

/// <summary>
/// Фикстура хоста с декоратором-швом IUserRepository (демо-сид включён: нужны
/// группы ИК-222/ИК-223 и студенты student31/student32 без группы).
/// </summary>
public sealed class StudentsSetGroupGateFixture : IDisposable
{
    private readonly TestWebAppFactory _root = new(
        null,
        new Dictionary<string, string?> { [SeedOptions.DemoDataVariable] = "true" });

    /// <summary>Хост с подменённым IUserRepository (запросы и DI-сид — только он).</summary>
    public WebApplicationFactory<Program> Host { get; }

    /// <summary>Шов детерминированной гонки (один на хост, Arm на каждый сценарий).</summary>
    public GatedUserRepository Gate { get; }

    public StudentsSetGroupGateFixture()
    {
        Host = _root.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IUserRepository>();
                services.AddSingleton<GatedUserRepository>();
                services.AddSingleton<IUserRepository>(static sp =>
                    sp.GetRequiredService<GatedUserRepository>());
            });
        });
        Gate = Host.Services.GetRequiredService<GatedUserRepository>();
    }

    public void Dispose()
    {
        Host.Dispose();
        _root.Dispose();
    }
}

/// <summary>
/// Декоратор IUserRepository с одноразовым швом детерминированной гонки:
/// <see cref="Arm"/> выбирает студента и свежую пару замков; следующий GetById
/// этого студента (pre-check маршрута) отдаёт снимок, сигналит Arrived и держит
/// вызвавшую сторону до Release — мутация хранилища из теста гарантированно
/// попадает между чтением студента контроллером и записью группы.
/// </summary>
public sealed class GatedUserRepository(InMemoryUserRepository inner) : IUserRepository
{
    private Guid _watchId;
    private bool _armed;
    private TaskCompletionSource? _arrived;
    private TaskCompletionSource? _release;

    /// <summary>Взводит шов: следующий GetById(watchId) задержится до Release.</summary>
    public (TaskCompletionSource Arrived, TaskCompletionSource Release) Arm(Guid watchId)
    {
        _watchId = watchId;
        _arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _armed = true;
        return (_arrived, _release);
    }

    /// <inheritdoc/>
    public User? GetById(Guid id)
    {
        var user = inner.GetById(id);
        if (_armed && id == _watchId)
        {
            _armed = false;
            _arrived!.TrySetResult();

            // Пауза контроллера в окне гонки; таймаут — страховка прогона от
            // зависания, если тест упал до Release.
            _ = _release!.Task.Wait(TimeSpan.FromSeconds(60));
        }

        return user;
    }

    /// <inheritdoc/>
    public void Add(User user) => inner.Add(user);

    /// <inheritdoc/>
    public User? GetByLogin(string login) => inner.GetByLogin(login);

    /// <inheritdoc/>
    public User? GetByEmail(string email) => inner.GetByEmail(email);

    /// <inheritdoc/>
    public void Update(User user) => inner.Update(user);

    /// <inheritdoc/>
    public bool SetPassword(Guid userId, string passwordHash) =>
        inner.SetPassword(userId, passwordHash);

    /// <inheritdoc/>
    public UpdateProfileResult UpdateProfile(Guid userId, string fullName, string email) =>
        inner.UpdateProfile(userId, fullName, email);

    /// <inheritdoc/>
    public SetGroupResult SetGroup(Guid userId, Guid? groupId, Func<Guid, bool>? groupExists) =>
        inner.SetGroup(userId, groupId, groupExists);

    /// <inheritdoc/>
    public IReadOnlyList<User> ListStudents(string? search = null, string? groupIdFilter = null) =>
        inner.ListStudents(search, groupIdFilter);

    /// <inheritdoc/>
    public IReadOnlyList<User> ListByGroup(Guid groupId) => inner.ListByGroup(groupId);

    /// <inheritdoc/>
    public int CountByGroup(Guid groupId) => inner.CountByGroup(groupId);
}

/// <summary>
/// SEC-001, эндпойнт-уровень: конкурентная мутация записи студента в окне между
/// чтением (pre-check маршрута) и записью группы не теряется — новый хэш пароля
/// не откатывается устаревшим снимком, удалённая «в полёте» группа не оставляет
/// висячего GroupId и даёт 404 «Группа не найдена».
/// </summary>
public sealed class StudentsSetGroupAtomicityEndpointTests(StudentsSetGroupGateFixture fixture)
    : IClassFixture<StudentsSetGroupGateFixture>
{
    private readonly WebApplicationFactory<Program> _factory = fixture.Host;

    [Fact]
    public async Task SetGroup_PasswordChangedDuringRequest_NotRevertedByStaleSnapshot()
    {
        // Эксплойт SEC-001: пока PUT «в полёте», студент успевает сменить пароль
        // (полная замена записи через Update — тот же примитив, что и в
        // PUT /me/password). Узкая мутация группы обязана сохранить новый хэш
        // и назначить группу; снимок, снятый ДО смены пароля, не перезаписывается.
        var ik223 = StudentsEndpointHarness.GroupByName(_factory, "ИК-223");
        var student31 = StudentsEndpointHarness.StudentByLogin(_factory, "student31");
        var (arrived, release) = fixture.Gate.Arm(student31.Id);
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        var putTask = client.PutAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}/{student31.Id}/group",
            StudentsEndpointHarness.Json($$"""{"groupId":"{{ik223.Id}}"}"""));

        // Контроллер прочитал студента (pre-check) и удерживается швом: в этом
        // окне выполняется смена пароля.
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var users = StudentsEndpointHarness.Users(_factory);
        var current = users.GetById(student31.Id)
            ?? throw new InvalidOperationException("Студент student31 потерян из хранилища.");
        current.PasswordHash = "new-password-hash";
        users.Update(current);
        release.TrySetResult();

        using var response = await putTask;
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var stored = users.GetById(student31.Id)!;
        Assert.Equal("new-password-hash", stored.PasswordHash);
        Assert.Equal(ik223.Id, stored.GroupId);
    }

    [Fact]
    public async Task SetGroup_GroupDeletedDuringRequest_NoDanglingGroupId()
    {
        // Группа удаляется, пока PUT «в полёте»: составная проверка «группа
        // существует + смена» обязана увидеть отсутствие и ответить
        // 404 «Группа не найдена», не оставив студенту GroupId на несуществующую
        // группу (IF-011: groupId студента не меняется).
        var ik222 = StudentsEndpointHarness.GroupByName(_factory, "ИК-222");
        var student32 = StudentsEndpointHarness.StudentByLogin(_factory, "student32");
        var (arrived, release) = fixture.Gate.Arm(student32.Id);
        using var client = StudentsEndpointHarness.CreateTeacherClient(_factory);

        var putTask = client.PutAsync(
            $"{StudentsEndpointHarness.StudentsEndpoint}/{student32.Id}/group",
            StudentsEndpointHarness.Json($$"""{"groupId":"{{ik222.Id}}"}"""));

        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(30));
        StudentsEndpointHarness.Groups(_factory).Delete(ik222.Id);
        release.TrySetResult();

        using var response = await putTask;
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Группа не найдена", await StudentsEndpointHarness.MessageAsync(response));
        Assert.Null(StudentsEndpointHarness.StudentByLogin(_factory, "student32").GroupId);
    }
}
