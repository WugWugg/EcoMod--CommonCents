using Eco.Core.Utils.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

            public static void Info(string message)
            {
                NLogManager.GetEcoLogWriter().Write($"[{NAME}] {message}\n");
            }
        }
    }
}
