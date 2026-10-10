using System.Text.RegularExpressions;
using LabsApp.Observability;
using Microsoft.Extensions.Logging;

namespace LabsApp.Tests.Observability;

/// <summary>
/// Unit-тесты обеих реализаций IEmailSender (IF-005, AC T-103 «Dev-заглушка»
/// и «No-op вне dev»): DevEmailSender кладёт РОВНО одну запись категории
/// «EmailDev» с маркером [DEV-EMAIL], адресатом и текстом письма (включая
/// 6-значный код); формат записи одинаков для существующего и несуществующего
/// email (NFR-006: журнал не раскрывает существование учётной записи); код
/// встречается только в категории «EmailDev». ProductionEmailSender — no-op
/// с ОДНИМ warning без адресата и содержимого; категории «EmailDev» вне
/// Development не существует.
/// </summary>
public sealed class EmailSendersTests : IDisposable
{
    private const string ExistingEmail = "student01@example.com";
    private const string NonExistingEmail = "nobody@example.com";
    private const string Subject = "Код восстановления пароля";
    private const string Code = "042713";

    private readonly TestLogSink _sink = new();
    private readonly ILoggerFactory _loggerFactory;

    public EmailSendersTests()
    {
        _loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(_sink));
    }

    public void Dispose()
    {
        _loggerFactory.Dispose();
        ((IDisposable)_sink).Dispose();
    }

    // ----------------------- DevEmailSender (Development) -----------------------

    [Fact]
    public async Task DevSendAsync_WritesSingleEmailDevRecordWithMarkerAddresseeAndBody()
    {
        await CreateDev().SendAsync("a@b.ru", Subject, $"Код восстановления: {Code}");

        // Ровно одна запись — и это запись категории «EmailDev» (Information).
        var record = Assert.Single(_sink.Snapshot());
        Assert.Equal(DevEmailSender.LogCategory, record.Category);
        Assert.Equal(LogLevel.Information, record.Level);

        var serialized = SerializeForMarkerCheck(record);
        // Маркер, адресат и текст письма с кодом — в записи целиком.
        Assert.Contains(DevEmailSender.DevEmailMarker, serialized, StringComparison.Ordinal);
        Assert.Contains("a@b.ru", serialized, StringComparison.Ordinal);
        Assert.Contains(Subject, serialized, StringComparison.Ordinal);
        Assert.Contains($"Код восстановления: {Code}", serialized, StringComparison.Ordinal);
        Assert.Equal("a@b.ru", record.State["To"]);
        Assert.Equal(Subject, record.State["Subject"]);
        Assert.Equal($"Код восстановления: {Code}", record.State["Body"]);
        // «Строка из 6 цифр» — код присутствует в записи целиком.
        Assert.True(Regex.IsMatch(serialized, @"\d{6}"), "в записи EmailDev должен быть код из 6 цифр");
    }

    [Fact]
    public async Task DevSendAsync_FormatIdenticalForExistingAndNonExistingEmail()
    {
        var sender = CreateDev();
        await sender.SendAsync(ExistingEmail, Subject, $"Код: {Code}");
        await sender.SendAsync(NonExistingEmail, Subject, $"Код: {Code}");

        var records = _sink.Snapshot();
        Assert.Equal(2, records.Count);
        Assert.All(records, record => Assert.Equal(DevEmailSender.LogCategory, record.Category));

        // Обе записи несут маркер [DEV-EMAIL] (AC «EmailDev-формат неизменен»).
        Assert.All(
            records,
            record => Assert.Contains(
                DevEmailSender.DevEmailMarker,
                SerializeForMarkerCheck(record),
                StringComparison.Ordinal));

        // Единый формат: один и тот же шаблон записи и один и тот же состав
        // полей состояния — независимо от существования адресата; отличаются
        // только значения полей (сам адресат).
        Assert.Equal(records[0].MessageTemplate, records[1].MessageTemplate);
        Assert.Equal(
            records[0].State.Keys.OrderBy(key => key, StringComparer.Ordinal),
            records[1].State.Keys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.Equal(ExistingEmail, records[0].State["To"]);
        Assert.Equal(NonExistingEmail, records[1].State["To"]);

        // Вне категории «EmailDev» записей нет вовсе (log-sink собирает все категории).
        Assert.DoesNotContain(
            records,
            record => record.Category != DevEmailSender.LogCategory);

        // Ни одна из записей не раскрывает факт существования учётной записи
        // текстом шаблона (в сообщении нет оценок существования).
        Assert.DoesNotContain(
            records,
            record => record.Message.Contains("найден", StringComparison.OrdinalIgnoreCase)
                || record.Message.Contains("существует", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DevSendAsync_CodeAppearsOnlyInEmailDevCategory()
    {
        // Единственность канала «EmailDev» (NFR-006/ISS-003): код встречается
        // только в записях этой категории — прочих категорий sender не создаёт.
        await CreateDev().SendAsync("a@b.ru", Subject, $"Код: {Code}");

        var records = _sink.Snapshot();
        Assert.Single(records, record => record.Category == DevEmailSender.LogCategory);
        Assert.Contains(
            records,
            record => record.Category == DevEmailSender.LogCategory
                && SerializeForMarkerCheck(record).Contains(Code, StringComparison.Ordinal));
        Assert.DoesNotContain(
            records,
            record => record.Category != DevEmailSender.LogCategory
                && SerializeForMarkerCheck(record).Contains(Code, StringComparison.Ordinal));
    }

    // --------------------- ProductionEmailSender (≠ Development) --------------------

    [Fact]
    public async Task ProductionSendAsync_WritesSingleWarningWithoutAddresseeOrContent()
    {
        await CreateProduction().SendAsync("a@b.ru", Subject, $"Код: {Code}");

        // Ровно один warning — письмо не пишется (no-op).
        var record = Assert.Single(_sink.Snapshot());
        Assert.Equal(ProductionEmailSender.LogCategory, record.Category);
        Assert.Equal(LogLevel.Warning, record.Level);

        // Ни адресата, ни темы, ни текста письма, ни кода — ни в сообщении,
        // ни в состоянии записи (NFR-006: вне «EmailDev» кода нет вовсе).
        var serialized = SerializeForMarkerCheck(record);
        Assert.DoesNotContain("a@b.ru", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(Subject, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain($"Код: {Code}", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(Code, serialized, StringComparison.Ordinal);
        Assert.False(Regex.IsMatch(serialized, @"\d{6}"), "warning не должен содержать 6-цифровых последовательностей");
        Assert.Empty(record.State);
        // Категория «EmailDev» вне Development не создаётся ни одной записью.
        Assert.DoesNotContain(
            _sink.Snapshot(),
            item => item.Category == DevEmailSender.LogCategory);
    }

    // ------------------------------- Границы входа -------------------------------

    [Fact]
    public async Task DevSendAsync_NullToEmail_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => CreateDev().SendAsync(null!, Subject, $"Код: {Code}"));

    [Fact]
    public async Task DevSendAsync_NullSubject_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => CreateDev().SendAsync("a@b.ru", null!, $"Код: {Code}"));

    [Fact]
    public async Task DevSendAsync_NullBody_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => CreateDev().SendAsync("a@b.ru", Subject, null!));

    [Fact]
    public async Task ProductionSendAsync_NullToEmail_ThrowsWithoutLogging()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => CreateProduction().SendAsync(null!, Subject, $"Код: {Code}"));
        Assert.Empty(_sink.Snapshot());
    }

    [Fact]
    public async Task ProductionSendAsync_NullSubject_ThrowsWithoutLogging()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => CreateProduction().SendAsync("a@b.ru", null!, $"Код: {Code}"));
        Assert.Empty(_sink.Snapshot());
    }

    [Fact]
    public async Task ProductionSendAsync_NullBody_ThrowsWithoutLogging()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => CreateProduction().SendAsync("a@b.ru", Subject, null!));
        Assert.Empty(_sink.Snapshot());
    }

    private DevEmailSender CreateDev() => new(_loggerFactory);

    private ProductionEmailSender CreateProduction() => new(_loggerFactory);

    private static string SerializeForMarkerCheck(TestLogRecord record) =>
        string.Join(
            "|",
            new[]
            {
                record.Category,
                record.Level.ToString(),
                record.Message,
                record.MessageTemplate ?? string.Empty,
            }.Concat(record.State.Select(pair => $"{pair.Key}={pair.Value}")));
}
