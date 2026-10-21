// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.CmdPal.AdaptiveCards.Polyfills;

/// <summary>Renders an Adaptive Cards <c>Badge</c> element.</summary>
internal sealed partial class BadgeControl : AdaptiveVisualControl
{
    private readonly Border _badge = new();
    private readonly TextBlock _text = new()
    {
        FontWeight = FontWeights.SemiBold,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly FontIcon _icon = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly StackPanel _content = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 4,
    };

    private BadgeModel _model;

    public BadgeControl(BadgeModel model)
    {
        _model = model;
        HorizontalAlignment = HorizontalAlignment.Left;
        _badge.Child = _text;
        Content = _badge;
        Render();
    }

    public override string IncrementalState => _model.IncrementalState;

    protected override bool WidthAffectsLayout => false;

    protected override void ApplyModel(AdaptiveVisualControl candidate) => _model = ((BadgeControl)candidate)._model;

    protected override void RenderCore()
    {
        var hasVisibleContent = _model.Text.Length > 0 || _model.IconGlyph is not null;
        Visibility = hasVisibleContent ? Visibility.Visible : Visibility.Collapsed;

        var isDarkTheme = IsDarkTheme;
        var (fontSize, padding) = _model.Size switch
        {
            BadgeSize.ExtraLarge => (16d, new Thickness(10, 3, 10, 4)),
            BadgeSize.Large => (14d, new Thickness(8, 2, 8, 3)),
            _ => (12d, new Thickness(6, 1, 6, 2)),
        };

        _badge.Padding = padding;
        _badge.CornerRadius = _model.Shape switch
        {
            BadgeShape.Square => new CornerRadius(2),
            BadgeShape.Circular => new CornerRadius(fontSize * 2),
            _ => new CornerRadius(4),
        };
        _badge.Background = null;
        _badge.BorderBrush = null;
        _badge.BorderThickness = new Thickness(0);

        // Badges are neutral unless they ask for a semantic style.
        Brush foreground;
        var color = ChartTheme.ResolveSemantic(_model.Style ?? "default", isDarkTheme);
        if (color is ChartColor semantic)
        {
            if (_model.Appearance == BadgeAppearance.Tint)
            {
                _badge.Background = ChartTheme.ToBrush(semantic.WithOpacity(isDarkTheme ? 0.22 : 0.14));
                _badge.BorderBrush = ChartTheme.ToBrush(semantic.WithOpacity(0.45));
                _badge.BorderThickness = new Thickness(1);
                foreground = ChartTheme.ToBrush(semantic);
            }
            else
            {
                _badge.Background = ChartTheme.ToBrush(semantic);
                foreground = ChartTheme.ToBrush(ChartTheme.GetContrastingText(semantic));
            }
        }
        else
        {
            _badge.Background = ChartTheme.ToBrush(ChartTheme.GetTrackColor(isDarkTheme));
            foreground = ChartTheme.ToBrush(ChartTheme.GetTextColor(isDarkTheme, secondary: false));
        }

        _text.Text = _model.Text;
        _text.FontSize = fontSize;
        _text.Foreground = foreground;
        _icon.Glyph = _model.IconGlyph ?? string.Empty;
        _icon.FontSize = fontSize;
        _icon.Foreground = foreground;
        if (_model.IconGlyph is null)
        {
            if (_badge.Child != _text)
            {
                _badge.Child = null;
                _content.Children.Clear();
                _badge.Child = _text;
            }
        }
        else if (_model.Text.Length == 0)
        {
            if (_badge.Child != _icon)
            {
                _badge.Child = null;
                _content.Children.Clear();
                _badge.Child = _icon;
            }
        }
        else
        {
            var contentIsAttached = _badge.Child == _content;
            if (!contentIsAttached)
            {
                _badge.Child = null;
            }

            var first = _model.IconPosition == BadgeIconPosition.After ? _text : _icon;
            var second = _model.IconPosition == BadgeIconPosition.After ? _icon : _text;
            if (_content.Children.Count != 2
                || _content.Children[0] != first
                || _content.Children[1] != second)
            {
                _content.Children.Clear();
                _content.Children.Add(first);
                _content.Children.Add(second);
            }

            if (!contentIsAttached)
            {
                _badge.Child = _content;
            }
        }

        ToolTipService.SetToolTip(_badge, _model.Tooltip);

        // An icon-only badge is described by its tooltip.
        var isDecorativeIconOnly = _model.Text.Length == 0
            && _model.IconGlyph is not null
            && string.IsNullOrWhiteSpace(_model.Tooltip);
        AutomationProperties.SetAccessibilityView(this, isDecorativeIconOnly ? AccessibilityView.Raw : AccessibilityView.Control);
        AutomationProperties.SetName(this, _model.Text.Length > 0 ? _model.Text : _model.Tooltip ?? string.Empty);
    }
}
