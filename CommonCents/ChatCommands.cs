using Eco.Gameplay.Systems.Chat;
using Eco.Gameplay.Systems.Messaging.Chat.Commands;
using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Text;

namespace CommonCents
{
    [ChatCommandHandler]
    [SupportedOSPlatform("windows7.0")]
    public static class CommonCentsCommands
    {
        [ChatCommand("commoncents", ChatAuthorizationLevel.Moderator)]
        public static void commoncents(IChatClient chat)
        {
            chat.MsgLoc($"Version: {CommonCentsPlugin.VERSION}");
        }
    }
}
}
}
