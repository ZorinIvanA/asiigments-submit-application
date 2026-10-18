using System.Net;
using System.Text;
using LabsApp.Auth;
using LabsApp.Domain.Entities;
using LabsApp.Hosting;
using LabsApp.Hosting.Configuration;
using LabsApp.Storage;
using LabsApp.Storage.InMemory;
using LabsApp.Tests.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LabsApp.Tests.Submissions;

// ============================================================================
// Юнит-тесты CR-003 (контрактная аномалия репозитория): ISubmissionRepository
// обязуется возвращать из Upsert «всегда не null» (IF-015). Контроллер обязан
// НЕ подавлять нарушение контракта null-forgiving'ом, а отвечать предсказуемым
// 500-конвертом хоста {'message':'Внутренняя ошибка сервера'} (IF-001), а не
// NullReferenceException. Хост — TestWebAppFactory с DI-заменой
// ISubmissionRepository на декоратор с нарушенным Upsert через
// WithWebHostBuilder (последняя регистрация выигрывает — образец зон
// Recovery/Hosting: RecoveryRequestFailingEmailSenderTests). Демо-сид выключен
// (сид сам пишет сдачи через Upsert): данные сеются через DI ДО запроса.
// ============================================================================

/// <summary>
/// Фикстура хоста с декоратором ISubmissionRepository, чей Upsert возвращает
/// null (симуляция дефекта хранилища). Демо-сид выключен; teacher сеется
/// базовым сидом хоста, студент/работа — DI-сидом через <see cref="SeedData"/>.
/// </summary>
public sealed class SubmissionsNullUpsertFixture : IDisposable
{
    private readonly TestWebAppFactory _root = new(
        null,
        new Dictionary<string, string?>
        {
            [SeedOptions.DemoDataVariable] = "false",
        });

    /// <summary>Хост с подменённым ISubmissionRepository (запросы и DI-сид — только он).</summary>
    public WebApplicationFactory<Program> Host { get; }

    public SubmissionsNullUpsertFixture()
    {
        Host = _root.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISubmissionRepository>();
                services.AddSingleton<ISubmissionRepository>(static sp =>
                    new NullUpsertSubmissionRepository(
                        sp.GetRequiredService<InMemorySubmissionRepository>()));
            });
        });
    }

    /// <summary>
    /// DI-сид данных сценария: студент в группе и работа (1:1). Декоратор
    /// прозрачен для чтения — PUT упирается в нарушенный Upsert.
    /// </summary>
    public (User Student, Lab Lab) SeedData()
    {
        var student = TestSession.SeedStudent(Host, new TestUserSeed
        {
            Login = "submissions-nullupsert-student",
            GroupName = "ИК-НУЛЛ",
        });

        var time = Host.Services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var lab = new Lab
        {
            Id = Guid.NewGuid(),
            Semester = 1,
            Number = 1,
            Content = "Контрактная аномалия Upsert (CR-003).",
            DefenseRequired = false,
            CreatedAt = time,
            UpdatedAt = time,
        };
        Host.Services.GetRequiredService<ILabRepository>().Add(lab);
        return (student, lab);
    }

    public void Dispose()
    {
        Host.Dispose();
        _root.Dispose();
    }
}

/// <summary>Декоратор ISubmissionRepository с нарушенным контрактом Upsert:
/// возвращает null вместо сохранённой записи; остальные операции прозрачны.</summary>
public sealed class NullUpsertSubmissionRepository(ISubmissionRepository inner) : ISubmissionRepository
{
    // Нарушение контракта IF-015 («всегда не null») — предмет теста.
    public Submission? Upsert(
        Guid studentId,
        Guid labId,
        DateOnly? submitDate,
        DateOnly? defenseDate,
        Guid? updatedBy,
        DateTime updatedAt) => null;

    public Submission? GetByStudentAndLab(Guid studentId, Guid labId) =>
        inner.GetByStudentAndLab(studentId, labId);

    public IReadOnlyList<Submission> ListByLabIds(IReadOnlyCollection<Guid> labIds) =>
        inner.ListByLabIds(labIds);

    public IReadOnlyList<Submission> ListByStudent(Guid studentId) =>
        inner.ListByStudent(studentId);

    public IReadOnlyList<Submission> GetByPairs(
        IReadOnlyCollection<Guid> studentIds, IReadOnlyCollection<Guid> labIds) =>
        inner.GetByPairs(studentIds, labIds);

    public IReadOnlyList<Submission> ListForStudentAndSemester(
        Guid studentId, IReadOnlyCollection<Guid> semesterLabIds) =>
        inner.ListForStudentAndSemester(studentId, semesterLabIds);

    public void DeleteByLabId(Guid labId) => inner.DeleteByLabId(labId);
}

/// <summary>
/// CR-003: PUT /submissions при null-результате Upsert — 500-конверт хоста
/// (IF-001), без повреждения конверта деталями исключения.
/// </summary>
public sealed class SubmissionsUpsertNullRepositoryTests(SubmissionsNullUpsertFixture fixture)
    : IClassFixture<SubmissionsNullUpsertFixture>
{
    private readonly WebApplicationFactory<Program> _factory = fixture.Host;

    [Fact]
    public async Task Put_NullUpsertResult_InternalServerErrorEnvelope()
    {
        // given: teacher (сид хоста) и валидное по контракту тело PUT;
        // репозиторий-декоратор возвращает null из Upsert (нарушение IF-015).
        var (student, lab) = fixture.SeedData();
        using var client = CreateTeacherClient(_factory);

        // when: upsert валидной пары.
        using var response = await client.PutAsync(
            SubmissionsEndpointHarness.GridEndpoint,
            new StringContent(
                $$"""{"studentId":"{{student.Id}}","labId":"{{lab.Id}}","submitDate":"2026-09-20","defenseDate":null}""",
                Encoding.UTF8,
                "application/json"));

        // then: 500 {'message':'Внутренняя ошибка сервера'} — fail-fast аномалия
        // контракта уходит в общий обработчик хоста (IF-001), без stack trace.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var body = await SubmissionsEndpointHarness.ReadJsonAsync(response);
        Assert.Equal(
            ErrorTexts.InternalServerError,
            body.RootElement.GetProperty("message").GetString());
        Assert.False(
            body.RootElement.TryGetProperty("errors", out _),
            "500-конверт не содержит errors-карты (IF-001).");
    }

    private static HttpClient CreateTeacherClient(WebApplicationFactory<Program> factory)
    {
        var user = factory.Services.GetRequiredService<IUserRepository>().GetByLogin("teacher")
            ?? throw new InvalidOperationException("Учителя нет в хранилище тестового хоста.");
        var token = factory.Services
            .GetRequiredService<ITokenService>()
            .IssueAccessToken(user.Id, user.Role);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{AuthCoreDefaults.AccessTokenCookieName}={token}");
        return client;
    }
}
