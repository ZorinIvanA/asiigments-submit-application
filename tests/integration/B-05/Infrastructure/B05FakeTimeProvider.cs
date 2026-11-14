namespace LabsApp.IntegrationTests.B05.Infrastructure;

/// <summary>
/// Управляемый источник бизнес-времени сценариев безопасности батча B-05
/// (глоссарий TimeProvider, ADR-010). Локальная копия механики FakeTimeProvider
/// (Microsoft.Extensions.Time.Testing): зона самодостаточна и не расширяет
/// список пакетов (образец зон B-07/B-08, BL-001 BUG-001). Стартует от реального
/// UtcNow в момент создания фикстуры и сдвигается ТОЛЬКО явными вызовами
/// Advance/SetUtcNow из теста — окно лимитера входа (TS-164) детерминировано.
/// Потокобезопасность не требуется: сценарии одного тестового класса исполняются
/// xUnit последовательно.
/// </summary>
public sealed class B05FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow = DateTimeOffset.UtcNow;

    /// <summary>Текущее бизнес-время теста (UTC).</summary>
    public override DateTimeOffset GetUtcNow() => _utcNow;

    /// <summary>Сдвигает бизнес-время вперёд на delta (окна лимитеров, TTL).</summary>
    public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);

    /// <summary>Устанавливает бизнес-время абсолютно (перевод часов кейса).</summary>
    public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;
}
