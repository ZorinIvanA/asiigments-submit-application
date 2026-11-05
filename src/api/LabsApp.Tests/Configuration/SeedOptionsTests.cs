using LabsApp.Hosting.Configuration;

namespace LabsApp.Tests.Configuration;

/// <summary>Юнит-проверки разрешения Seed__DemoData (FR-007).</summary>
public sealed class SeedOptionsTests
{
    [Fact]
    public void Defaults_AreDocumented()
    {
        var options = new SeedOptions();

        Assert.Equal("teacher", options.TeacherLogin);
        Assert.Null(options.DemoData);
    }

    // SEC-001 (регресс): §8 запрещает трим пароля — guard валидирует СЫРОЕ значение,
    // ровно его сида затем хэширует. Значение по умолчанию, обрамлённое пробелами,
    // дефолтом не является и само проходит правила §8 (пробел — спецзнак).
    // FR-006: сид-пароль в Production, нарушающий правила пароля §8, — отказ
    // старта с именем переменной (пример из FR-006 «Корректный Production» —
    // Zx9$strong-pass — напротив, валиден: см. следующий тест).
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
