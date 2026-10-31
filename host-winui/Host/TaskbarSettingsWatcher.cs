// Adapted from Awqat Salaat (https://github.com/Khiro95/Awqat-Salaat)
// Copyright (c) 2023 Khiro
// Licensed under the MIT License.
using Microsoft.Win32;
using Serilog;
using System;
using System.Collections.Generic;
using System.Management;
using System.Security.Principal;

namespace Mawaqit.WinUI
{
    internal static class TaskbarSettingsWatcher
    {
        private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        private static readonly Dictionary<string, TaskbarSetting> WatchedSettings = new()
        {
            ["MMTaskbarEnabled"] = TaskbarSetting.ShowOnAllDisplays,
            ["TaskbarAl"] = TaskbarSetting.Alignment,
            ["TaskbarDa"] = TaskbarSetting.WidgetsButton,
        };
        private static readonly Dictionary<string, object> CurrentValues = new();
        private static readonly ManagementEventWatcher watcher;

        public static event EventHandler<TaskbarSettingChangedEventArgs> SettingChanged;

        static TaskbarSettingsWatcher()
        {
            Log.Information("Creating registry watcher in TaskbarSettingsWatcher");
            var currentUser = WindowsIdentity.GetCurrent();

            WqlEventQuery query = new WqlEventQuery(
                "SELECT * FROM RegistryKeyChangeEvent WHERE " +
                 "Hive = 'HKEY_USERS' " +
                 @"AND KeyPath = '" + currentUser.User.Value + @"\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced'");

            query.WithinInterval = new TimeSpan(0, 0, 0, 1);

            watcher = new ManagementEventWatcher(query);
            watcher.EventArrived += new EventArrivedEventHandler(RegistryKeyChanged);
            watcher.Start();

            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath))
            {
                foreach (var regValue in WatchedSettings.Keys)
                {
                    var value = key.GetValue(regValue, 1);
                    CurrentValues[regValue] = value;
                }
            }

            App.Quitting += () => watcher?.Dispose();
        }

        private static void RegistryKeyChanged(object sender, EventArrivedEventArgs e)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath))
            {
                foreach (var regValue in WatchedSettings.Keys)
                {
                    var value = key.GetValue(regValue, 1);

                    if (!Equals(CurrentValues[regValue], value))
                    {
                        Log.Debug($"Detected a change in registry value: {regValue}");
                        CurrentValues[regValue] = value;
                        SettingChanged?.Invoke(null, new TaskbarSettingChangedEventArgs(WatchedSettings[regValue]));
                    }
                }
            }
        }
    }

    internal class TaskbarSettingChangedEventArgs : EventArgs
    {
        public TaskbarSetting Setting { get; }

        public TaskbarSettingChangedEventArgs(TaskbarSetting setting)
        {
            Setting = setting;
        }
    }

    internal enum TaskbarSetting
    {
        Other = 0,
        ShowOnAllDisplays = 1,
        Alignment = 2,
        WidgetsButton = 3,
    }
}
