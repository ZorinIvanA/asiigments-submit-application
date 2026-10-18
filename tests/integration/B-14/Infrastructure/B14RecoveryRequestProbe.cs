using System.Globalization;
using System.Reflection;
using LabsApp.Storage;
using LabsApp.Storage.InMemory;
using Microsoft.Extensions.DependencyInjection;

namespace LabsApp.IntegrationTests.B14.Infrastructure;

/// <summary>
/// Поддержка кейсов recovery/request (TS-068..TS-072): проверки, у которых нет
/// готового помощника в зоне.
///
///  - <see cref="CountAllRecords"/> — «в хранилище не появилось кодов» (TS-069) и
///    «создан ровно один живой код» (TS-068): прямой подсчёт ВСЕХ записей кодов
///    в хранилище тестового хоста. Шва в IF-015 нет (GetLiveForUser отдаёт только
///    живой код конкретного владельца и требует userId), поэтому реализация —
///    рефлексия в единственное in-memory хранилище зоны (LabsApp.Storage,
///    singleton InMemoryRecoveryCodeRepository, FR-002/FR-024). Любое отклонение
///    структуры — ЯВНАЯ ошибка теста с диагнозом, а не молчаливый пропуск.
///  - <see cref="AssertEmptyOkBodyAsync"/> — «200; тело 0 байт и Content-Length: 0,
///    НЕ JSON-объект» (TS-068/TS-069/TS-070/TS-071; ISS-014, ADR-012): тело
///    прочитывается ровно в 0 байт; заголовок Content-Length, если хост его
///    выставил, равен 0 (TestHost для ответов без тела может не материализовать
///    заголовок — связующая проверка «0 байт» остаётся строгой); content-type
///    JSON у пустого ответа отсутствует.
///  - <see cref="ToUtcOffset"/> — безопасное приведение меток хранилища
///    (DateTime с Kind Unspecified/Local/Utc) к DateTimeOffset для сравнения с
///    инжектируемыми часами (FR-003).
/// </summary>
public static class B14RecoveryRequestProbe
{
    /// <summary>
    /// Общее число записей кодов восстановления в хранилище тестового хоста
    /// (использованные и просроченные включительно).
    /// </summary>
    public static int CountAllRecords(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var repository = services.GetRequiredService<ISecurityTokenRepository>();
        if (repository is not InMemorySecurityTokenRepository inMemory)
        {
            throw new InvalidOperationException(
                "ISecurityTokenRepository тестового хоста не InMemorySecurityTokenRepository — " +
                "прямой подсчёт записей хранилища недоступен (IF-015).");
        }

        var recordsField = typeof(InMemorySecurityTokenRepository)
            .GetField("_recoveryCodes", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "У InMemorySecurityTokenRepository нет ожидаемого поля _recoveryCodes — " +
                "структура хранилища изменилась, подсчёт записей недоступен (IF-015).");

        if (recordsField.GetValue(inMemory) is not System.Collections.IDictionary records)
        {
            throw new InvalidOperationException(
                "Поле _recoveryCodes InMemorySecurityTokenRepository не является словарём записей — " +
                "подсчёт записей хранилища недоступен (IF-015).");
        }

        return records.Count;
    }

    /// <summary>
    /// then кейсов recovery/request: 200 с ПУСТЫМ телом — 0 байт, Content-Length: 0,
    /// НЕ JSON-объект (ISS-014, ADR-012, FR-012 п.5).
    /// </summary>
    public static async Task AssertEmptyOkBodyAsync(HttpResponseMessage response, string scenarioId)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            body.Length == 0,
            $"{scenarioId}: тело ответа обязано быть пустым (0 байт, ISS-014), фактически " +
            $"{body.Length} симв.: «{body}».");
        Assert.True(
            response.Content.Headers.ContentLength is null or 0,
            $"{scenarioId}: Content-Length обязана быть 0 (ISS-014), фактически " +
            $"{response.Content.Headers.ContentLength?.ToString(CultureInfo.InvariantCulture) ?? "<отсутствует>"}.");
        var contentType = response.Content.Headers.ContentType;
        Assert.True(
            contentType is null
                || !contentType.MediaType!.Contains("json", StringComparison.OrdinalIgnoreCase),
            $"{scenarioId}: у пустого ответа не должно быть JSON content-type (ISS-014: НЕ JSON-объект), " +
            $"фактически {contentType}.");
    }

    /// <summary>
    /// Приведение метки времени хранилища к DateTimeOffset (UTC): Kind Unspecified
    /// трактуется как UTC (хранилище пишет TimeProvider.UtcNow), Local —
    /// конвертируется явно; исключает зависимость от локальной таймзоны машины.
    /// </summary>
    public static DateTimeOffset ToUtcOffset(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => new DateTimeOffset(value, TimeSpan.Zero),
        DateTimeKind.Local => new DateTimeOffset(value.ToUniversalTime(), TimeSpan.Zero),
        _ => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero),
    };
}
