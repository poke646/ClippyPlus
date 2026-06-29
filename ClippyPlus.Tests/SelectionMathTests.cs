using ClippyPlus.App.Models;

namespace ClippyPlus.Tests;

public sealed class SelectionMathTests
{
    [Fact]
    public void Normalize_HandlesForwardDrag()
    {
        var result = SelectionMath.Normalize(new UiPoint(10, 20), new UiPoint(40, 70));

        Assert.Equal(new UiRect(10, 20, 30, 50), result);
    }

    [Fact]
    public void Normalize_HandlesReverseDrag()
    {
        var result = SelectionMath.Normalize(new UiPoint(40, 70), new UiPoint(10, 20));

        Assert.Equal(new UiRect(10, 20, 30, 50), result);
    }

    [Fact]
    public void ToPixelRect_ScalesAndClampsSelection()
    {
        var result = SelectionMath.ToPixelRect(
            new UiRect(50, 25, 500, 250),
            surfaceWidth: 1000,
            surfaceHeight: 500,
            pixelWidth: 2000,
            pixelHeight: 1000);

        Assert.Equal(new PixelRect(100, 50, 1000, 500), result);
    }

    [Fact]
    public void ToPixelRect_ReturnsEmptyForTinySelection()
    {
        var result = SelectionMath.ToPixelRect(
            new UiRect(10, 10, 0.5, 10),
            surfaceWidth: 1000,
            surfaceHeight: 500,
            pixelWidth: 2000,
            pixelHeight: 1000);

        Assert.True(result.IsEmpty);
    }
}
