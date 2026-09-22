// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ScreenTranslator;

internal sealed partial class ResizeHandle : Grid
{
    public ResizeHandle(ResizeHandleDirection direction)
    {
        Direction = direction;
        Width = 8;
        Height = 8;
        Children.Add(new Border
        {
            Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
        });
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        IsHitTestVisible = true;
        ProtectedCursor = InputSystemCursor.Create(GetCursorShape(direction));
    }

    public ResizeHandleDirection Direction { get; }

    private static InputSystemCursorShape GetCursorShape(ResizeHandleDirection direction)
    {
        return direction switch
        {
            ResizeHandleDirection.North or ResizeHandleDirection.South => InputSystemCursorShape.SizeNorthSouth,
            ResizeHandleDirection.East or ResizeHandleDirection.West => InputSystemCursorShape.SizeWestEast,
            ResizeHandleDirection.NorthEast or ResizeHandleDirection.SouthWest => InputSystemCursorShape.SizeNortheastSouthwest,
            _ => InputSystemCursorShape.SizeNorthwestSoutheast,
        };
    }
}

internal enum ResizeHandleDirection
{
    North,
    NorthEast,
    East,
    SouthEast,
    South,
    SouthWest,
    West,
    NorthWest,
}
