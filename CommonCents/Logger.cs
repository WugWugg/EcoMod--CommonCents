using Eco.Core.Utils.Logging;
using Eco.Gameplay.Systems.Messaging.Notifications;
using Eco.Shared.Localization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace CommonCents
{
    [SupportedOSPlatform("windows7.0")]
    static class Logger
    {

        [Conditional("DEBUG")]
        public static void Debug(string message)
        {
            NLogManager.GetEcoLogWriter().Write($"[{CommonCentsPlugin.NAME}] {message}\n");
        }

        [Conditional("DEBUG")]
        public static void NewsFeed(string message)
        {
            NotificationManager.ServerMessageToAll(Localizer.DoStr(message));
        }

        public static void Info(string message)
        {
            NLogManager.GetEcoLogWriter().Write($"[{CommonCentsPlugin.NAME}] {message}\n");
        }
    }
}
