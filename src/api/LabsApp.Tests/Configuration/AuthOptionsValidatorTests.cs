using LabsApp.Hosting.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace LabsApp.Tests.Configuration;

/// <summary>
/// Юнит-проверки fail-fast веток Auth__* (FR-006): TTL в любом окружении;
/// Auth__JwtKey — 4 ветки (Production: отсутствует / короче границы / на границе;
/// Development: необязательна — эпизодический ключ подставляет композиция-корень).
/// </summary>
public sealed class AuthOptionsValidatorTests
{
    private static AuthOptionsValidator Production() => new(Environments.Production);

    private static AuthOptionsValidator Development() => new(Environments.Development);

    private static AuthOptions Valid() => new()
    {
        AccessTtlMinutes = AuthOptions.DefaultAccessTtlMinutes,
        RefreshTtlDays = AuthOptions.DefaultRefreshTtlDays,
        JwtKey = new string('k', AuthOptions.JwtKeyMinLength),
    };

    [Fact]
    public void Defaults_AreDocumented()
    {
        var options = new AuthOptions();

        Assert.Equal(15, options.AccessTtlMinutes);
        Assert.Equal(7, options.RefreshTtlDays);
        // FR-005/ASM-004: умолчание итераций PBKDF2 — 210 000 (реворк с 600 000).
        Assert.Equal(210_000, AuthOptions.DefaultPbkdf2Iterations);
        Assert.Equal(AuthOptions.DefaultPbkdf2Iterations, options.Pbkdf2Iterations);
        // Фиксированного документированного dev-ключа не существует: в Development
        // без Auth__JwtKey композиция-корень подставляет эпизодический случайный.
        Assert.True(options.JwtKey is null);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void AccessTtlMinutes_NonPositive_FailsWithVariableName(int value)
    {
        var options = Valid();
        options.AccessTtlMinutes = value;

        var result = Production().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, m => m.Contains(AuthOptions.AccessTtlMinutesVariable, StringComparison.Ordinal));
    }

    [Fact]
    public void AccessTtlMinutes_PositiveBoundary_Passes()
    {
        var options = Valid();
        options.AccessTtlMinutes = 1;

        Assert.True(Production().Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void RefreshTtlDays_NonPositive_FailsWithVariableName(int value)
    {
        var options = Valid();
        options.RefreshTtlDays = value;

        var result = Production().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, m => m.Contains(AuthOptions.RefreshTtlDaysVariable, StringComparison.Ordinal));
    }

    [Fact]
    public void RefreshTtlDays_PositiveBoundary_Passes()
    {
        var options = Valid();
        options.RefreshTtlDays = 1;

        Assert.True(Production().Validate(null, options).Succeeded);
    }

    // ------------------------------------------------------------------
    // Auth__Pbkdf2Iterations — целое от 1 до 10 000 000, любое окружение.
    // ------------------------------------------------------------------

    // Вне диапазона (ниже границы и выше) — отказ с именем переменной.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100_000)]
    [InlineData(10_000_001)]
    [InlineData(int.MaxValue)]
    public void Pbkdf2Iterations_OutOfRange_FailsWithVariableName(int value)
    {
        var options = Valid();
        options.Pbkdf2Iterations = value;

        var result = Production().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            m => m.Contains(AuthOptions.Pbkdf2IterationsVariable, StringComparison.Ordinal));
    }

    // Границы диапазона (1 и ровно 10 000 000) — проходят; валидация действует
    // и в Development (TtlChecked-аналог).
    [Theory]
    [InlineData(1)]
    [InlineData(10_000_000)]
    public void Pbkdf2Iterations_Boundary_Passes(int value)
    {
        var options = Valid();
        options.Pbkdf2Iterations = value;

        Assert.True(Production().Validate(null, options).Succeeded);
        Assert.True(Development().Validate(null, options).Succeeded);
    }

    // ------------------------------------------------------------------
    // Auth__JwtKey — 4 ветки.
    // ------------------------------------------------------------------

    // Ветка 1: Production без ключа (отсутствует/пустая/из пробелов) — отказ.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void JwtKey_ProductionMissing_FailsWithVariableName(string? jwtKey)
    {
        var options = Valid();
        options.JwtKey = jwtKey;

        var result = Production().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, m => m.Contains(AuthOptions.JwtKeyVariable, StringComparison.Ordinal));
    }

    // Ветка 2: Production с ключом короче границы (31 символ) — отказ.
    [Fact]
    public void JwtKey_ProductionBelowBoundary_Fails()
    {
        var options = Valid();
        options.JwtKey = new string('k', AuthOptions.JwtKeyMinLength - 1);

        var result = Production().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, m => m.Contains(AuthOptions.JwtKeyVariable, StringComparison.Ordinal));
    }

    // Ветка 3: Production ровно на границе (32 символа) — проходит.
    [Fact]
    public void JwtKey_ProductionAtBoundary_Passes()
    {
        var options = Valid();
        options.JwtKey = new string('k', AuthOptions.JwtKeyMinLength);

        Assert.True(Production().Validate(null, options).Succeeded);
    }

    // Ветка 4: Development — ключ необязателен (любое значение/отсутствие).
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void JwtKey_DevelopmentOptional_Passes(string? jwtKey)
    {
        var options = Valid();
        options.JwtKey = jwtKey;

        Assert.True(Development().Validate(null, options).Succeeded);
    }

    [Fact]
    public void TtlChecked_AlsoInDevelopment()
    {
        var options = Valid();
        options.AccessTtlMinutes = 0;
        options.JwtKey = null;

        var result = Development().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, m => m.Contains(AuthOptions.AccessTtlMinutesVariable, StringComparison.Ordinal));
        Assert.DoesNotContain(result.Failures, m => m.Contains(AuthOptions.JwtKeyVariable, StringComparison.Ordinal));
    }
}
