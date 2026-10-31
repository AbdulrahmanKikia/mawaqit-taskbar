// Adapted from Awqat Salaat (https://github.com/Khiro95/Awqat-Salaat)
// Copyright (c) 2023 Khiro
// Licensed under the MIT License.
using Mawaqit.WinUI.Helpers;
using Mawaqit.WinUI.Interop;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.Win32;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using WindowsDisplayAPI;

namespace Mawaqit.WinUI.Helpers
{
    internal static class DisplayHelper
    {
        private const string TaskbarClassName = "Shell_TrayWnd";
        private const string SecondaryTaskbarClassName = "Shell_SecondaryTrayWnd";
        private const string ReBarWindow32ClassName = "ReBarWindow32";
        private const string WorkerWClassName = "WorkerW";
        private const string NotificationAreaClassName = "TrayNotifyWnd";

        private static readonly Dictionary<string, DisplayEntity> data;

        public static event EventHandler<DisplayChangedEventArgs> DisplayChanged;

        public static IEnumerable<DisplayEntity> AvailableDisplays => data.Values;
        public static DisplayEntity PrimaryDisplay => data.Values.FirstOrDefault(d => d.IsPrimary);

        static DisplayHelper()
        {
            SystemEvents.InvokeOnEventsThread(() => SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged);
            Log.Debug("Getting displays in DisplayHelper ctor");
            data = GetDisplayss();
        }

        public static (IntPtr, IntPtr, IntPtr) GetTaskbarFromDisplay(DisplayEntity target)
        {
            Log.Information($"Getting taskbar for display: {target?.Summary}");

            if (target is null)
            {
                return (IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            }

            if (target.IsPrimary)
            {
                var hwndTaskbar = User32.FindWindow(TaskbarClassName, null);
                var hwndReBar = User32.FindWindowEx(hwndTaskbar, IntPtr.Zero, ReBarWindow32ClassName, null);
                var hwndTrayNotify = User32.FindWindowEx(hwndTaskbar, IntPtr.Zero, NotificationAreaClassName, null);

                return (hwndTaskbar, hwndReBar, hwndTrayNotify);
            }
            else
            {
                List<IntPtr> secondaryTaskbars = new List<IntPtr>();

                GCHandle gcSecondaryTaskbarsList = GCHandle.Alloc(secondaryTaskbars);
                IntPtr pointerSecondaryTaskbarsList = GCHandle.ToIntPtr(gcSecondaryTaskbarsList);

                try
                {
                    Log.Debug("Looking for secondary taskbars...");
                    EnumWindowProc enumProc = new EnumWindowProc(EnumWindow);
                    User32.EnumChildWindows(IntPtr.Zero, enumProc, pointerSecondaryTaskbarsList);
                }
                finally
                {
                    gcSecondaryTaskbarsList.Free();
                }

                Log.Debug($"Found {secondaryTaskbars.Count} secondary taskbar(s)");

                foreach (var taskbarHwnd in secondaryTaskbars)
                {
                    var id = Win32Interop.GetWindowIdFromWindow(taskbarHwnd);
                    var displayArea = DisplayArea.GetFromWindowId(id, DisplayAreaFallback.Primary);
                    var position = target.CachedSettings.Position;
                    var resolution = target.CachedSettings.Resolution;

                    bool sameDisplay =
                        position.X == displayArea.OuterBounds.X &&
                        position.Y == displayArea.OuterBounds.Y &&
                        resolution.Width == displayArea.OuterBounds.Width &&
                        resolution.Height == displayArea.OuterBounds.Height;

                    if (sameDisplay)
                    {
                        var hwndWorker = User32.FindWindowEx(taskbarHwnd, IntPtr.Zero, WorkerWClassName, null);
                        Log.Information("Found matching secondary taskbar");
                        return (taskbarHwnd, hwndWorker, IntPtr.Zero);
                    }
                }

                Log.Information("Could not find matching secondary taskbar");
                return (IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            }
        }

        private static bool EnumWindow(IntPtr hWnd, IntPtr lParam)
        {
            StringBuilder builder = new StringBuilder(256);
            User32.GetClassName(hWnd, builder, builder.MaxCapacity);
            string className = builder.ToString();

            if (className == SecondaryTaskbarClassName)
            {
                GCHandle gcSecondaryTaskbarsList = GCHandle.FromIntPtr(lParam);
                List<IntPtr> secondaryTaskbars = gcSecondaryTaskbarsList.Target as List<IntPtr>;
                secondaryTaskbars.Add(hWnd);
            }

            return true;
        }

        public static int GetMonitorDpi(Display display)
        {
            var position = display.CurrentSetting.Position;
            var pt = new POINT
            {
                x = position.X,
                y = position.Y
            };
            var hmonitor = User32.MonitorFromPoint(pt, MonitorFrom_Flags.MONITOR_DEFAULTTONEAREST);

            Shcore.GetDpiForMonitor(hmonitor, MonitorDpiType.MDT_DEFAULT, out uint dpiX, out uint dpiY);

            return (int)dpiX;
        }

        public static DisplayEntity FindProperDisplay(string requested)
        {
            Log.Information($"Looking for the proper display given the requested one is: {requested}");

            if (requested == "PRIMARY")
            {
                return PrimaryDisplay;
            }
            else
            {
                var showTaskbarOnAllDisplays = SystemInfos.ShowTaskbarOnAllDisplays();

                if (showTaskbarOnAllDisplays && data.TryGetValue(requested, out var display))
                {
                    return display;
                }

                Log.Information("Could not find the requested display. The primary is returned");
                return PrimaryDisplay;
            }
        }

        private static void SystemEvents_DisplaySettingsChanged(object sender, EventArgs e)
        {
            try
            {
                Log.Information("Display Settings Changed");
                var current = GetDisplayss();
                DisplayChangedEventArgs args = null;

                // A display has been connected
                if (current.Count > data.Count)
                {
                    DisplayEntity added = null;

                    foreach (var monKV in current)
                    {
                        if (!data.ContainsKey(monKV.Key))
                        {
                            data.Add(monKV.Key, monKV.Value);
                            added = monKV.Value;
                        }
                    }

                    Log.Information($"Display Added: {added.Summary}");
                    args = new DisplayChangedEventArgs(DisplayChangedReason.Connected, added);
                }
                // A display has been disconnected
                else if (current.Count < data.Count)
                {
                    DisplayEntity removed = null;

                    foreach (var monKey in data.Keys)
                    {
                        if (!current.ContainsKey(monKey))
                        {
                            data.Remove(monKey, out removed);
                        }
                    }

                    Log.Information($"Display Removed: {removed.Summary}");
                    args = new DisplayChangedEventArgs(DisplayChangedReason.Disconnected, removed);
                }
                // Some display settings were changed
                else
                {
                    var oldPrimaries = data.Values.Where(d => d.IsPrimary).ToArray();
                    var currentPrimaries = current.Values.Where(d => d.IsPrimary).ToArray();

                    // Primary display duplicated
                    if (currentPrimaries.Length > oldPrimaries.Length)
                    {
                        var newPrimary = currentPrimaries
                            .ExceptBy(oldPrimaries.Select(d => d.Display.DevicePath), d => d.Display.DevicePath)
                            .First();
                        data[newPrimary.Display.DevicePath] = newPrimary;
                        Log.Information($"Primary display is duplicated: {string.Join('\n', currentPrimaries.Select(d => d.Display.DevicePath))}");
                        args = new DisplayChangedEventArgs(DisplayChangedReason.PrimaryDuplicated, newPrimary);
                    }
                    // Primary display deduplicated
                    else if (currentPrimaries.Length < oldPrimaries.Length)
                    {
                        var deduplicated = oldPrimaries
                            .ExceptBy(currentPrimaries.Select(d => d.Display.DevicePath), d => d.Display.DevicePath)
                            .First();
                        deduplicated = current[deduplicated.Display.DevicePath];
                        data[deduplicated.Display.DevicePath] = deduplicated;
                        Log.Information($"Primary display is deduplicated: {string.Join('\n', currentPrimaries.Select(d => d.Display.DevicePath))}");
                        args = new DisplayChangedEventArgs(DisplayChangedReason.PrimaryDeduplicated, deduplicated);
                    }
                    // Another display became primary
                    else if (!oldPrimaries[0].Display.DevicePath.Equals(currentPrimaries[0].Display.DevicePath))
                    {
                        var oldPrimary = oldPrimaries[0];
                        var newPrimary = currentPrimaries[0];

                        data[oldPrimary.Display.DevicePath] = current[oldPrimary.Display.DevicePath];
                        data[newPrimary.Display.DevicePath] = current[newPrimary.Display.DevicePath];
                        Log.Information($"New primary display: {newPrimary.Summary}");
                        args = new DisplayChangedEventArgs(DisplayChangedReason.PrimaryDisplay, newPrimary);
                    }
                    else
                    {
                        foreach (var monKey in data.Keys)
                        {
                            var old = data[monKey];
                            var curr = current[monKey];

                            // Resolution changed for a display
                            if (old.CachedSettings.Resolution != curr.CachedSettings.Resolution)
                            {
                                data[monKey] = curr;
                                Log.Information($"Display {old.Name} changed resolution: {curr.CachedSettings.Resolution}");
                                args = new DisplayChangedEventArgs(DisplayChangedReason.Resolution, curr);
                                break;
                            }
                            // DPI changed for a display
                            else if (old.Dpi != curr.Dpi)
                            {
                                data[monKey] = curr;
                                Log.Information($"Display {old.Name} changed DPI: {curr.Dpi}");
                                args = new DisplayChangedEventArgs(DisplayChangedReason.Dpi, curr);
                                break;
                            }
                        }
                    }
                }

                args ??= new DisplayChangedEventArgs(DisplayChangedReason.Unknown, null);
                OnDisplayChanged(args);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Error occured while handling DisplaySettingsChanged event: {ex.Message}");
            }
        }

        private static void OnDisplayChanged(DisplayChangedEventArgs args)
        {
            Log.Debug($"Dispatching DisplayChanged event for the reason: {args.Reason}");
            DisplayChanged?.Invoke(null, args);
        }

        private static Dictionary<string, DisplayEntity> GetDisplayss()
        {
            return Display.GetDisplays().ToDictionary(d => d.DevicePath, d => new DisplayEntity(d));
        }
    }

    internal class DisplayEntity
    {
        public readonly Display Display;
        public readonly string Name;
        public readonly string Summary;
        public readonly int Dpi;
        public readonly bool IsPrimary;
        public readonly DisplaySetting CachedSettings;

        public DisplayEntity(Display display)
        {
            Display = display;
            IsPrimary = display.IsGDIPrimary;
            Name = display.ToPathDisplayTarget().FriendlyName;
            Dpi = DisplayHelper.GetMonitorDpi(display);
            CachedSettings = display.CurrentSetting;
            Summary = $"{Name}: " +
                $"{CachedSettings.Resolution.Width}x{CachedSettings.Resolution.Height} " +
                $"@ {CachedSettings.Position.X},{CachedSettings.Position.Y}" +
                (IsPrimary ? " (Primary)" : "");
        }
    }

    internal class DisplayChangedEventArgs : EventArgs
    {
        public DisplayChangedReason Reason { get; }
        public DisplayEntity DisplayEntity { get; }

        public DisplayChangedEventArgs(DisplayChangedReason reason, DisplayEntity displayEntity)
        {
            Reason = reason;
            DisplayEntity = displayEntity;
        }
    }

    internal enum DisplayChangedReason
    {
        Unknown = 0,
        Connected = 1,
        Disconnected = 2,
        PrimaryDisplay = 3,
        PrimaryDuplicated = 4,
        PrimaryDeduplicated = 5,
        Resolution = 6,
        Dpi = 7,
    }
}
