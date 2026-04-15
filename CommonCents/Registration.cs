using Eco.Core.Plugins.Interfaces;
using Eco.Shared.Localization;
using System;
using System.Collections.Generic;
using System.Text;

namespace CommonCents
{
    public class CommonCentsRegistration : IModInit
    {
        public static ModRegistration Register() => new()
        {
            ModName = "CommonCents",
            ModDescription = Localizer.DoStr($"CommonCents alerts you when your store allows profitable buy-and-sell loops within itself."),
            ModDisplayName = "Common Cents"
        };
    }
}
