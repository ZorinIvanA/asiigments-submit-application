using LabsApp.Hosting.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace LabsApp.Tests.Configuration;

/// <summary>Юнит-проверки fail-fast Seed__TeacherPassword и разрешения Seed__DemoData (FR-006/FR-007).</summary>
public sealed class SeedOptionsTests
{
    private static SeedOptionsValidator Production() => new(Environments.Production);

    private static SeedOptionsValidator Development() => new(Environments.Development);

    [Fact]
    public void Defaults_AreDocumented()
    {
        var options = new SeedOptions();

        Assert.Equal("teacher", options.TeacherLogin);
        Assert.Equal("teacher123!", options.TeacherPassword);
        Assert.Null(options.DemoData);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TeacherPassword_ProductionMissing_FailsWithVariableName(string? password)
    {
        var result = Production().Validate(null, new SeedOptions { TeacherPassword = password! });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, m => m.Contains(SeedOptions.TeacherPasswordVariable, StringComparison.Ordinal));
    }

    [Fact]
    public void TeacherPassword_ProductionDefault_FailsWithVariableName()
    {
        var result = Production().Validate(null, new SeedOptions { TeacherPassword = "teacher123!" });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, m => m.Contains(SeedOptions.TeacherPasswordVariable, StringComparison.Ordinal));
    }

    [Fact]
    public void TeacherPassword_ProductionExplicitStrong_Passes()
    {
        Assert.True(Production().Validate(null, new SeedOptions { TeacherPassword = "Strong!Pass1" }).Succeeded);
    }

    // SEC-001 (регресс): §8 запрещает трим пароля — guard валидирует СЫРОЕ значение,
    // ровно его сида затем хэширует. Значение по умолчанию, обрамлённое пробелами,
    // дефолтом не является и само проходит правила §8 (пробел — спецзнак).
    [Fact]
    public void TeacherPassword_ProductionPaddedValue_ValidatedVerbatimWithoutTrim()
    {
        var result = Production().Validate(null, new SeedOptions { TeacherPassword = " teacher123! " });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void TeacherPassword_ProductionSpecExample_Passes()
    {
        // Дословный пример валидного Production из AC FR-006 «Корректный Production».
        Assert.True(Production().Validate(null, new SeedOptions { TeacherPassword = "Zx9$strong-pass" }).Succeeded);
    }

    // FR-006: сид-пароль в Production, нарушающий правила пароля §8, — отказ
    // старта с именем переменной (пример из FR-006 «Корректный Production» —
    // Zx9$strong-pass — напротив, валиден: см. следующий тест).
    [Theory]
    [InlineData("short1!")]
    [InlineData("nodigits!")]
    [InlineData("12345678!")]
    [InlineData("NoSpecial12345")]
    public void TeacherPassword_ProductionViolatingPasswordRules_FailsWithVariableName(string password)
    {
        var result = Production().Validate(null, new SeedOptions { TeacherPassword = password });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, m => m.Contains(SeedOptions.TeacherPasswordVariable, StringComparison.Ordinal));
    }

    [Fact]
    public void TeacherPassword_DevelopmentDefault_Passes()
    {
        Assert.True(Development().Validate(null, new SeedOptions()).Succeeded);
    }

    // FR-007 v2.3: вне Development флаг ВСЕГДА false — явное 'true' игнорируется;
    // в Development явное 'false' отключает набор, иное значение — умолчание true.
    [Theory]
    [InlineData("true", true, true)]
    [InlineData("true", false, false)]
    [InlineData("TRUE", false, false)]
    [InlineData(" true ", true, true)]
    [InlineData("false", true, false)]
    [InlineData("False", false, false)]
    [InlineData(" false ", true, false)]
    [InlineData(null, true, true)]
    [InlineData(null, false, false)]
    [InlineData("yes", true, true)]
    [InlineData("yes", false, false)]
    [InlineData("", false, false)]
    [InlineData("2", true, true)]
    [InlineData("2", false, false)]
    public void ResolveDemoData_ProductionAlwaysFalse_DevelopmentUsesExplicitFalseOrDefault(
        string? value, bool inDevelopment, bool expected)
    {
        var options = new SeedOptions { DemoData = value };

        Assert.Equal(expected, options.ResolveDemoData(inDevelopment));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("False")]
    [InlineData(" TRUE ")]
    public void IsKnownDemoDataValue_TrueForKnownValues(string value)
    {
        Assert.True(SeedOptions.IsKnownDemoDataValue(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("yes")]
    public void IsKnownDemoDataValue_FalseForUnknownValues(string? value)
    {
        Assert.False(SeedOptions.IsKnownDemoDataValue(value));
    }
}
