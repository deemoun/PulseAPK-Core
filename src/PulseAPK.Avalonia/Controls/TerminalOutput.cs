using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace PulseAPK.Avalonia.Controls;

public sealed class TerminalOutput : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);
    private ScrollViewer? _scroll;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_scroll != null) _scroll.ScrollChanged -= OnScrollChanged;
        base.OnApplyTemplate(e);
        _scroll = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");
        if (_scroll != null) _scroll.ScrollChanged += OnScrollChanged;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scroll == null || e.ExtentDelta.Y == 0 || SelectionStart != SelectionEnd) return;
        var previousEnd = Math.Max(0, _scroll.Extent.Height - e.ExtentDelta.Y - _scroll.Viewport.Height + e.ViewportDelta.Y);
        var previousOffset = _scroll.Offset.Y - e.OffsetDelta.Y;
        if (previousOffset >= previousEnd - 1)
            _scroll.Offset = new Vector(_scroll.Offset.X, _scroll.ScrollBarMaximum.Y);
    }
}
