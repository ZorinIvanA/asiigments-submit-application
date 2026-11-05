using System.Globalization;
using LabsApp.Auth;
using LabsApp.IntegrationTests.B09.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LabsApp.IntegrationTests.B09.Scenarios;

/// <summary>
/// TS-180 «NFR-004: счётчик KDF доступен тестам и логируется раз в 60 с»
/// (nfr, NFR-004 + FR-005, P1).
///
/// given: счётчик обнулён (baseline снимка); инжектируемые часы; тестовый
///        log-sink (фикстура <see cref="B09TimedLogWebAppFactory"/>).
/// when:  несколько KDF-операций; перевод часов на +60 с; ещё операции;
///        перевод ещё на +60 с.
/// then:  после каждого 60-с интервала в логе появляется запись суммарного
///        значения auth_kdf_operations_total (не реже раза в 60 с); счётчик
///        читается тестовым швом и растёт с метками вызывателя (NFR-004
///        constraint + verification: «проверка записи лога при закреплённом
///        тестовом времени»).
///
/// Механика: KdfCounter логирует раз в 60 с через TimeProvider.CreateTimer
/// (ADR-002) — FakeTimeProvider зажигает тик детерминированно при Advance,
/// реального ожидания нет. Файл текущей волны батча B-09.
/// </summary>
public sealed class Ts180_Nfr004KdfCounterLoggedEveryMinuteTests : IClassFixture<B09TimedLogWebAppFactory>
{
    private readonly B09TimedLogWebAppFactory _factory;

    public Ts180_Nfr004KdfCounterLoggedEveryMinuteTests(B09TimedLogWebAppFactory factory) => _factory = factory;

    [Fact]
    public void KdfCounterReadableByTestSeam_AndTotalLoggedAfterEachSixtySecondInterval()
    {
        // given: счётчик обнулён (baseline снимка тестового шва IKdfCounter);
        // log-sink очищен — тест видит только записи своего сценария.
        var hasher = _factory.Services.GetRequiredService<IPasswordHasher>();
        var baseline = B09KdfSeams.KdfSnapshot(_factory);
        _factory.LogSink.Clear();

        // when: несколько KDF-операций; перевод часов на +60 с.
        _ = hasher.Hash("Ts180-pass1!", KdfCallers.Login);
        _ = hasher.Hash("Ts180-pass2!", KdfCallers.Login);
        var afterFirstOps = B09KdfSeams.KdfSnapshot(_factory);
        _factory.Time.Advance(TimeSpan.FromSeconds(60));

        // then: в логе появилась запись суммарного значения
        // auth_kdf_operations_total со значением на момент тика.
        var firstLoggedTotal = AssertSingleLoggedTotal();
        Assert.Equal(B09KdfSeams.Total(afterFirstOps), firstLoggedTotal);

        // when: ещё операции; перевод ещё на +60 с.
        var hash = hasher.Hash("Ts180-pass3!", KdfCallers.Login);
        _ = hasher.Verify("Wrong0rd!", hash, KdfCallers.Login);
        var afterMoreOps = B09KdfSeams.KdfSnapshot(_factory);
        _factory.Time.Advance(TimeSpan.FromSeconds(60));

        // then: после второго 60-с интервала — запись с выросшим суммарным
        // значением (не реже раза в 60 с).
        var secondLoggedTotal = AssertSingleLoggedTotal();
        Assert.Equal(B09KdfSeams.Total(afterMoreOps), secondLoggedTotal);
        Assert.True(
            secondLoggedTotal > firstLoggedTotal,
            $"Суммарное значение второй записи ({secondLoggedTotal}) обязано расти: {firstLoggedTotal}.");

        // then: счётчик читается тестовым швом и растёт с метками вызывателя:
        // 4 деривации (2+1 Hash + Verify) приращены метке 'login' (сумма по
        // меткам = 4).
        Assert.Equal(4, B09KdfSeams.DeltaTotal(baseline, afterMoreOps));
        Assert.True(
            B09KdfSeams.Delta(baseline, afterMoreOps, KdfCallers.Login) == 4,
            "Ожидался прирост auth_kdf_operations_total{login} = 3, фактически: " +
            $"{B09KdfSeams.DeltaBreakdown(baseline, afterMoreOps)}.");
    }

    /// <summary>
    /// Суммарное значение последней записи лога тика (запись опознаётся по
    /// структурированному состоянию auth_kdf_operations_total — IF-016).
    /// </summary>
    private long AssertSingleLoggedTotal()
    {
        var ticks = _factory.LogSink.Snapshot()
            .Where(record => record.State.ContainsKey(KdfCounter.MetricName))
            .ToList();
        Assert.True(
            ticks.Count > 0,
            "За 60-с интервал в логе не появилась запись суммарного значения " +
            "auth_kdf_operations_total (NFR-004: не реже раза в 60 с).");
        var last = ticks[^1];
        Assert.Equal(LogLevel.Information, last.Level);
        return Convert.ToInt64(last.State[KdfCounter.MetricName], CultureInfo.InvariantCulture);
    }
}
