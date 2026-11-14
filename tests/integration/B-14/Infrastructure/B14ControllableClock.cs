namespace LabsApp.IntegrationTests.B14.Infrastructure;

/// <summary>
/// Инжектируемые часы тестового хоста кейсов восстановления пароля (FR-003,
/// ADR-002: System.TimeProvider — единственный источник бизнес-времени):
/// стартуют от реального момента построения хоста и переводятся тестом
/// (Advance) за expiresAt кода/токена (TS-077, TS-081) либо точно на границу
/// TTL reset-токена 15 минут (TS-073). Кейсам нужен только линейный ход
/// времени — таймеры не создаются.
/// </summary>
public sealed class B14ControllableClock : TimeProvider
{
    private DateTimeOffset _utcNow = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    /// <summary>Переводит бизнес-время хоста вперёд на заданный интервал.</summary>
    public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
}
