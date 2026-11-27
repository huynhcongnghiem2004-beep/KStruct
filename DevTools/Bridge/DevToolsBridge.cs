using System;
using System.Reflection;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;

namespace SDK_HD.KStruct.Diagnostics
{
    // Read-only API. Invoke on the Revit UI thread.
    public static class DevToolsBridge
    {
        public static string ApiVersion() { return "2.0"; }
        public static string AssemblyVersion() { return typeof(DevToolsBridge).Assembly.GetName().Version.ToString(); }
        public static string[] WindowSnapshot()
        {
            var result = new List<string>();
            var app = Application.Current;
            if (app == null || !app.Dispatcher.CheckAccess()) return result.ToArray();
            foreach (Window window in app.Windows)
            {
                if (window.GetType().Assembly != typeof(DevToolsBridge).Assembly) continue;
                result.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0} | {1} | {2:F0}x{3:F0} DIP | visible={4}",
                    window.GetType().FullName, window.Title, window.ActualWidth, window.ActualHeight, window.IsVisible));
            }
            return result.ToArray();
        }
    }
}
