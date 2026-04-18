using Eco.Core.Controller;
using Eco.Core.Plugins.Interfaces;
using Eco.Core.Systems;
using Eco.Core.Utils;
using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Auth;
using Eco.Gameplay.Components.Store;
using Eco.Gameplay.Components.Store.Internal;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Gameplay.UI;
using Eco.Mods.TechTree;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Networking;
using Eco.Shared.Time;
using Eco.Shared.Utils;
using Eco.World;
using System.ComponentModel;
using System.Runtime.Versioning;
using static Eco.Gameplay.Disasters.DisasterPlugin;

namespace CommonCents
{

    [SupportedOSPlatform("windows7.0")]
    public class CommonCentsPlugin : IModKitPlugin, IInitializablePlugin, IShutdownablePlugin
    {
        public const string VERSION = "v0.1.0";
        /// <summary>
        /// Maps Store IDs to last warning state. See: ComputeFingerprint. Used to trigger warnings ONLY on state change.
        /// </summary>
        private Dictionary<int, int> lastFingerprintByStore = new();
        private readonly Dictionary<int, int> debounceVersionByStore = new();
        private const double DebounceDelay = 0.5;

        #region IModKitPlugin
        private int _warningsServed = 0;
        private string _status = "Starting...";

        public string GetCategory() => "Mods";

        public string GetStatus() => Localizer.DoStr($"Status: {_status} -- {_warningsServed} mistakes caught.");
        #endregion

        public void Initialize(TimedTask timer)
        {
            // Listen for Store offer changes
            StoreItemData.SellOffersChangedEvent.AddUnique(HandleOffersChanged);
            StoreItemData.BuyOffersChangedEvent.AddUnique(HandleOffersChanged);
            _status = "Running";
        }

        public Task ShutdownAsync()
        {
            StoreItemData.SellOffersChangedEvent.Remove(HandleOffersChanged);
            StoreItemData.BuyOffersChangedEvent.Remove(HandleOffersChanged);
            _status = "Shutdown";
            return Task.CompletedTask;
        }

        private void HandleOffersChanged(StoreItemData data)
        {
            // Debounce events because Eco will call this multiple times if things are drag-n-dropped in the store interface
            var storeId = data.ControllerID;
            if (!debounceVersionByStore.TryGetValue(storeId, out var version))
                version = 0;

            version++;
            debounceVersionByStore[storeId] = version;
            var capturedVersion = version;
            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(DebounceDelay));

                if (!debounceVersionByStore.TryGetValue(storeId, out var latestVersion) ||
                    latestVersion != capturedVersion)
                    return;

                ProcessSettledOffersChanged(data);
            });
        }

        private void ProcessSettledOffersChanged(StoreItemData data)
        {
            var storeId = data.ControllerID;
            var arbitrageLoops = FindArbitrageLoops(data).ToList();
            if (arbitrageLoops.Count == 0)
            {
                lastFingerprintByStore.Remove(storeId);
                return;
            }
            // See if we are in the same state as last time
            var fingerprint = ComputeFingerprint(arbitrageLoops);
            if (lastFingerprintByStore.TryGetValue(storeId, out var oldFingerprint) &&
                oldFingerprint == fingerprint)
            {
                return;
            }
            lastFingerprintByStore[storeId] = fingerprint;
            // Warn owners and/or users with full access
            var storeName = data.Categories
                .FirstOrDefault()?
                .StoreComponent?
                .Parent?
                .MarkedUpName
                ?? Localizer.DoStr("This store");
            var message = FormatMessage(storeName, arbitrageLoops);
            GetUsersToNotify(data).ForEach(x => NotifyUser(x, message));
        }

        private int ComputeFingerprint(List<ArbitrageLoop> loops)
        {
            var hash = new HashCode();
            var test = loops.OrderBy(x => x.ItemTypeID)
                .ThenBy(x => x.SellTradeOffer.Price)
                .ThenBy(x => x.BuyTradeOffer.Price);
            test.ForEach(x => hash.Add(x.ToHashCode()));
            return hash.ToHashCode();
        }

        private IEnumerable<ArbitrageLoop> FindArbitrageLoops(StoreItemData data)
        {
            // TODO: Review how durability and integrity changes this. For example, a Sell offer of 50%-100% Rice will not match a 49%-100% Rice even if the sell price is < the buy!
            // For now I'm choosing to KISS
            // Collapse duplicates in the buy and sell
            var maxBuyByItem = data.BuyOffers.GroupBy(x => x.Stack.Item.TypeID)
                .ToDictionary(g => g.Key, g => g.MaxBy(x => x.Price));
            var minSellByItem = data.SellOffers.GroupBy(x => x.Stack.Item.TypeID)
                .ToDictionary(g => g.Key, g => g.MinBy(x => x.Price));
            foreach (var (item, sellOffer) in minSellByItem)
            {
                if (!maxBuyByItem.TryGetValue(item, out var buyOffer)) continue;
                if (sellOffer != null && buyOffer != null && sellOffer.Price < buyOffer.Price)
                {
                    yield return new ArbitrageLoop(sellOffer, buyOffer);
                }
            }
        }

        /// <summary>
        /// This will return a list of Users that have Full Access to the store. Why not just the User that made the change?
        /// Well, the even listener I'm using in this code doesn't have that level of detail. I do know that one of the people
        /// who has Full Access is resposible and that everyone that can fix it will be notified of it.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        private IEnumerable<User> GetUsersToNotify(StoreItemData data)
        {
            if (!data.Categories.Any()) return Array.Empty<User>();
            // I know StoreComponent has a AuthComponent because in StoreComponent.cs it has
            // `[RequireComponent(typeof(AuthDataTrackerComponent))]` which adds an AuthComponent
            // to this object.
            var authComponent =
                data.Categories.First().StoreComponent.Parent.GetComponent<AuthComponent>();
            var users = new HashSet<User>();
            var owners = authComponent.Owners;
            users.AddRange(owners.UserSet);
            var fullAccessList = authComponent.UsersWithFullAccess;
            foreach (var fullAccessAlias in fullAccessList)
            {
                users.AddRange(fullAccessAlias.UserSet);
            }
            return users;
        }

        private void NotifyUser(User user, string msg)
        {
            PlayerPopups.OkBox(user.Player, Localizer.DoStr(msg));
        }

        private string FormatMessage(LocString storeName, IEnumerable<ArbitrageLoop> arbitrages)
        {
            if (!arbitrages.Any())
            {
                Logger.Info("FormatMessage was called with 0 arbitrages!");
                return $"{storeName} has no pricing issues.";
            }
            var title = $"{storeName} might have a pricing issue...";
            var desc = $"Players can repeatedly trade the following items for profit:";
            var list = String.Join("<br>", arbitrages.Select(x => x.ToString()));
            var tip = "To fix this have the store either <u>buy for less</u> or <u>sell for more</u>.";
            return $"<size=150%>{title}</size><br>{desc}<br><br><align=left>{list}<br><br><i>{tip}</i></align>";
        }
    }

    [SupportedOSPlatform("windows7.0")]
    public class ArbitrageLoop
    {
        public readonly TradeOffer SellTradeOffer;
        public readonly TradeOffer BuyTradeOffer;
        public readonly float delta = 0;
        public readonly int ItemTypeID;

        public ArbitrageLoop(TradeOffer sell,  TradeOffer buy)
        {
            this.SellTradeOffer = sell;
            this.BuyTradeOffer = buy;
            delta = sell.Price - buy.Price;
            ItemTypeID = sell.Stack.Item.TypeID;
        }

        public override string ToString() 
        {
            // Iron Ore: sell 5 → buy 7 (−2 per trade)
            return $"{SellTradeOffer.Stack.Item.MarkedUpName}: sell {SellTradeOffer.Price} → buy {BuyTradeOffer.Price} (earn {delta:0.00} per trade)";
        }

        public int ToHashCode()
        {
            var hash = new HashCode();
            hash.Add(SellTradeOffer.Stack.Item.TypeID);
            hash.Add(SellTradeOffer.Price);
            hash.Add(BuyTradeOffer.Price);
            return hash.ToHashCode();
        }
    }
}
