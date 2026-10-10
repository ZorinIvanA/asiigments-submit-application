using LabsApp.Auth;
using LabsApp.IntegrationTests.B21.Infrastructure;

namespace LabsApp.IntegrationTests.B21.Scenarios;

/// <summary>
/// TS-031 (NFR-004, P2): сумма счётчика KDF логируется не реже раза в 60 с.
///
/// given: закреплённое тестовое время T0 (FakeTimeProvider в DI через
///        ConfigureTestServices — B21WebAppFactory.KdfClock; ADR-002: 60-секундный
///        лог KDF-счётчика построен на TimeProvider.CreateTimer и следует за
///        фейковым временем, перевод часов — без реального ожидания); тестовый
///        log-sink (B21LogSink в конвейере журналирования тестового хоста);
///        выполнено несколько дериваций (реальный компонент IPasswordHasher:
///        2 хэширования + 1 проверка — 3 операции KDF).
/// when:  перевод инжектируемых часов на T0+60с и далее на T0+120с (Advance
///        зажигает таймер счётчика детерминированно); подсчёт записей лога
///        счётчика в каждом интервале (разность количеств на границах).
/// then:  за каждые 60 с инжектируемого времени появляется НЕ МЕНЕЕ одной
///        итоговой записи лога счётчика (нижняя граница; частота выше
///        допустима) (NFR-004: «суммарное значение логируется не реже раза
///        в 60 с»; методика verification — «проверка записи лога при
///        закреплённом тестовом времени»).
///
/// Опознание итоговых записей и чтение суммарного значения — общий помощник
/// зоны <see cref="B21KdfCounterLog"/> (REWORK CR-002: один экземпляр
/// помощника на зону вместо приватных копий).
/// </summary>
public sealed class Ts031_KdfCounterLogPeriodTests : IClassFixture<Ts031_KdfCounterLogPeriodTests.Fixture>
{
    private readonly Fixture _fixture;

    public Ts031_KdfCounterLogPeriodTests(Fixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Фикстура: хост с инжектируемыми часами (FakeTimeProvider в DI) и log-sink.</summary>
    public sealed class Fixture : IDisposable
    {
        public B21WebAppFactory.KdfClock Factory { get; } = new();

        public void Dispose() => Factory.Dispose();
    }

    [Fact]
    public void ClockAdvanced_ToT0Plus60s_ThenToT0Plus120s_AtLeastOneCounterLogRecord_PerInterval()
    {
        var factory = _fixture.Factory;

        // given: часы на T0 (свежий FakeTimeProvider — ни одного Advance до шага
        // when); выполнено несколько дериваций — через реальный компонент
        // IPasswordHasher (каждая деривация инкрементирует счётчик — FR-005).
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        const string secretOne = "B031-Kdf-Secret-Один-1!";
        const string secretTwo = "B031-Kdf-Secret-Два-2!";
        var storedHash = hasher.Hash(secretOne, KdfCallers.Seed);  // деривация 1
        hasher.Hash(secretTwo, KdfCallers.Seed);                   // деривация 2
        hasher.Verify(secretTwo, storedHash, KdfCallers.Login);    // деривация 3

        var totalBefore = B21KdfCounterLog.Total(factory);
        Assert.True(
            totalBefore > 0,
            "Предусловие given «выполнено несколько дериваций»: суммарное значение "
            + $"счётчика должно быть положительным, фактически {totalBefore}.");

        var countAtT0 = B21KdfCounterLog.TotalRecords(factory.LogSink.Snapshot()).Count;

        // when: перевод инжектируемых часов T0 → T0+60с; подсчёт записей
        // лога счётчика в первом интервале.
        factory.Clock.Advance(TimeSpan.FromSeconds(60));
        var countAfterFirstInterval = B21KdfCounterLog.TotalRecords(factory.LogSink.Snapshot()).Count;

        // when: T0+60с → T0+120с; подсчёт во втором интервале.
        factory.Clock.Advance(TimeSpan.FromSeconds(60));
        var countAfterSecondInterval = B21KdfCounterLog.TotalRecords(factory.LogSink.Snapshot()).Count;

        // then: за каждые 60 с инжектируемого времени — НЕ МЕНЕЕ одной итоговой
        // записи лога счётчика (нижняя граница; частота выше допустима).
        Assert.True(
            countAfterFirstInterval - countAtT0 >= 1,
            "NFR-004: за первый 60-секундный интервал (T0 → T0+60с) в логе нет итоговой "
            + $"записи счётчика KDF (новых записей: {countAfterFirstInterval - countAtT0}). "
            + $"Записи sink: {B21KdfCounterLog.Describe(factory.LogSink.Snapshot())}.");
        Assert.True(
            countAfterSecondInterval - countAfterFirstInterval >= 1,
            "NFR-004: за второй 60-секундный интервал (T0+60с → T0+120с) в логе нет "
            + $"новой итоговой записи счётчика KDF (новых записей: "
            + $"{countAfterSecondInterval - countAfterFirstInterval}). "
            + $"Записи sink: {B21KdfCounterLog.Describe(factory.LogSink.Snapshot())}.");
    }
}
