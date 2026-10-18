using LabsApp.Hosting.Configuration;
using Microsoft.Extensions.Options;

namespace LabsApp.Tests.Configuration;

/// <summary>Юнит-проверки границы Labs__MaxSemester 1..100 (FR-030).</summary>
public sealed class LabsOptionsValidatorTests
{
    private readonly LabsOptionsValidator _validator = new();

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(101)]
    public void MaxSemester_OutOfRange_FailsWithVariableName(int value)
    {
        var result = _validator.Validate(null, new LabsOptions { MaxSemester = value });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, m => m.Contains(LabsOptions.MaxSemesterVariable, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    public void MaxSemester_InRange_Passes(int value)
    {
        Assert.True(_validator.Validate(null, new LabsOptions { MaxSemester = value }).Succeeded);
    }

    [Fact]
    public void Default_IsTen()
    {
        Assert.Equal(10, new LabsOptions().MaxSemester);
    }
}
