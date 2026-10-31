// Adapted from Awqat Salaat (https://github.com/Khiro95/Awqat-Salaat)
// Copyright (c) 2023 Khiro
// Licensed under the MIT License.
using System;

namespace Mawaqit.WinUI
{
    internal class WidgetNotInjectedException : Exception
    {
        public WidgetNotInjectedException(string message) : base(message) { }
    }
}
