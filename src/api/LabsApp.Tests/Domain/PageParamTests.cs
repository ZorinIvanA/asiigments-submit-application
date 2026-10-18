using LabsApp.Domain;
using LabsApp.Domain.Dtos;

namespace LabsApp.Tests.Domain;

/// <summary>
/// Юнит-проверки PageParam.Normalize (глоссарий «Нормализация страницы», T-002):
/// 0, отрицательное, нецелое, отсутствующее, переполнение → 1; корректные значения ≥ 1
/// проходят как есть; эхо некорректного значения исключено.
/// </summary>
public sealed class PageParamTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("-100")]
    [InlineData("+0")]
    [InlineData("2.5")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("2147483648")] // переполнение int
    [InlineData("١٢٣")] // не-ASCII цифры
    public void Normalize_InvalidRawPage_FallsBackToOne(string? rawPage)
    {
        Assert.Equal(1, PageParam.Normalize(rawPage));
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("2", 2)]
    [InlineData("10", 10)]
    [InlineData(" 3 ", 3)]
    [InlineData("007", 7)]
    [InlineData("+5", 5)]
    [InlineData("2147483647", int.MaxValue)]
    public void Normalize_ValidRawPage_KeepsValueAsIs(string rawPage, int expected)
    {
        Assert.Equal(expected, PageParam.Normalize(rawPage));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-7, 1)]
    [InlineData(int.MinValue, 1)]
    [InlineData(1, 1)]
    [InlineData(9, 9)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void Normalize_IntPage_ClampsBelowOne(int page, int expected)
    {
        Assert.Equal(expected, PageParam.Normalize(page));
    }

    [Fact]
    public void PagedResult_Normalize_DelegatesToPageParam()
    {
        // Единая точка нормализации: PagedResult.Normalize не расходится с PageParam.
        Assert.Equal(PageParam.Normalize("0"), PagedResult<string>.Normalize("0"));
        Assert.Equal(PageParam.Normalize("4"), PagedResult<string>.Normalize("4"));
        Assert.Equal(PageParam.Normalize(-2), PagedResult<string>.Normalize(-2));
        Assert.Equal(PageParam.DefaultPage, PagedResult<string>.DefaultPage);
    }
}
