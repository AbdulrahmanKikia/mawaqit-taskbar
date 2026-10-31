// Adapted from Awqat Salaat (https://github.com/Khiro95/Awqat-Salaat)
// Copyright (c) 2023 Khiro
// Licensed under the MIT License.
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace Mawaqit.WinUI.Controls
{
    internal class GridEx : Grid
    {
        public void SetCursor(InputSystemCursorShape cursorShape) => ProtectedCursor = InputSystemCursor.Create(cursorShape);

        public void ResetCursor() => ProtectedCursor = null;
    }
}
