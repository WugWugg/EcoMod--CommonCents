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
    static class Logger
    {
        const string NAME = "CommonCents";

        [Conditional("DEBUG")]
        public static void Debug(string message)
        {
            NLogManager.GetEcoLogWriter().Write($"[{NAME}] {message}\n");
        }

        [Conditional("DEBUG")]
        [SupportedOSPlatform("windows7.0")]

        public static void NewsFeed(string message)
        {
            NotificationManager.ServerMessageToAll(Localizer.DoStr(message));
        }

        public static void Info(string message)
        {
            NLogManager.GetEcoLogWriter().Write($"[{NAME}] {message}\n");
        }
    }
}
