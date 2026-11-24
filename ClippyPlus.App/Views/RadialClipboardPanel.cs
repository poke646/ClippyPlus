using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ClippyPlus.App.Views;

internal sealed class RadialClipboardPanel : Decorator
{
    private const double PreferredGroupWidth = 420;
    private const double PreferredGroupHeight = 385;
    private const double HorizontalGap = 17;
    private const double SelectorWidth = 178;
    private const double SelectorHeight = 44;
    private const double SelectorClearance = 7;

    public RadialClipboardPanel(IBrush accentBrush, Control tabSelector)
    {
        var clipboardIcon = RadialClipboardVisuals.CreateIcon(RadialClipboardIcon.Clipboard, 38, accentBrush);
        clipboardIcon.HorizontalAlignment = HorizontalAlignment.Center;
        clipboardIcon.VerticalAlignment = VerticalAlignment.Center;
        clipboardIcon.IsVisible = false;
        var groups = new GroupPanel();
        groups.Children.CollectionChanged += (_, _) => clipboardIcon.IsVisible = groups.Children.Count > 0;
        Children = groups.Children;
        tabSelector.Width = SelectorWidth;
        tabSelector.Height = SelectorHeight;
        tabSelector.HorizontalAlignment = HorizontalAlignment.Center;
        tabSelector.VerticalAlignment = VerticalAlignment.Center;
        Child = new Grid { Children = { groups, clipboardIcon, tabSelector } };
    }

    public Controls Children { get; }

    private static Size GetGroupSize(Size size)
    {
        var innerWidth = size.Width - 2 * GetHorizontalInset(size);
        var availableGroupHeight = size.Height / 2 - GetVerticalInset(size) - GetCenterClearance(size);
        return new Size(
            Math.Clamp((innerWidth - HorizontalGap) / 2, 0, PreferredGroupWidth),
            Math.Clamp(availableGroupHeight, 0, PreferredGroupHeight));
    }

    private static Point GetCenter(Size size) => new(size.Width / 2, size.Height / 2);

    private static double GetCenterClearance(Size size) =>
        Math.Clamp(size.Height * 0.061, SelectorHeight / 2, 39) + SelectorClearance;

    private static double GetHorizontalInset(Size size) => Math.Clamp(size.Width * 0.03, 8, 27);

    private static double GetVerticalInset(Size size) => Math.Clamp(size.Height * 0.033, 6, 20);

    private sealed class GroupPanel : Panel
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            var size = new Size(
                double.IsFinite(availableSize.Width) ? availableSize.Width : 1_456,
                double.IsFinite(availableSize.Height) ? availableSize.Height : 902);
            var groupSize = GetGroupSize(size);
            foreach (var group in Children)
            {
                group.Measure(groupSize);
            }

            return size;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var groupSize = GetGroupSize(finalSize);
            var center = GetCenter(finalSize);
            var top = GetVerticalInset(finalSize);
            var sideShift = Math.Min(
                center.X - groupSize.Width / 2 - GetHorizontalInset(finalSize),
                groupSize.Width + 36);
            var lowerTop = center.Y - 18;
            if (sideShift < groupSize.Width + HorizontalGap)
            {
                lowerTop = Math.Max(lowerTop, top + groupSize.Height + SelectorClearance);
            }

            if (sideShift - groupSize.Width / 2 < SelectorWidth / 2 + SelectorClearance)
            {
                lowerTop = Math.Max(lowerTop, center.Y + GetCenterClearance(finalSize));
            }

            lowerTop = Math.Min(lowerTop, finalSize.Height - top - groupSize.Height);
            var centerLeft = center.X - groupSize.Width / 2;
            for (var index = 0; index < Children.Count; index++)
            {
                var bounds = index switch
                {
                    0 => new Rect(new Point(centerLeft + sideShift, lowerTop), groupSize),
                    1 => new Rect(new Point(centerLeft - sideShift, lowerTop), groupSize),
                    2 => new Rect(new Point(centerLeft, top), groupSize),
                    _ => new Rect(finalSize.Width, finalSize.Height, 0, 0),
                };
                Children[index].Arrange(bounds);
            }

            return finalSize;
        }
    }
}
