using Eco.Core.Controller;
using Eco.Core.Plugins.Interfaces;
using Eco.Core.Systems;
using Eco.Core.Utils;
using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Auth;
using Eco.Gameplay.Components.Store;
using Eco.Gameplay.Components.Store.Internal;
using Eco.Gameplay.Items;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Gameplay.UI;
using Eco.Gameplay.Utils;
using Eco.Mods.TechTree;
using Eco.Shared.Items;
using Eco.Shared.Localization;
using Eco.Shared.Networking;
using Eco.Shared.Time;
using Eco.Shared.Utils;
using Eco.World;
using System.Collections;
using System.ComponentModel;
using System.Runtime.Versioning;
using static Eco.Gameplay.Disasters.DisasterPlugin;

namespace CommonCents
{

    [SupportedOSPlatform("windows7.0")]
    public class CommonCentsPlugin : IModKitPlugin, IInitializablePlugin, IShutdownablePlugin
    {
        public const string VERSION = "v0.2.0";
        public const string NAME = "CommonCents";
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

        private int ComputeFingerprint(List<IArbitrageLoop> loops)
        {
            var hash = new HashCode();
            foreach (var loop in loops
                .OrderBy(x => x.SortKey)
                .ThenBy(x => x.GetStableHashCode()))
            {
                hash.Add(loop.GetStableHashCode());
            }            
            return hash.ToHashCode();
        }

        private IEnumerable<IArbitrageLoop> FindArbitrageLoops(StoreItemData data)
        {
            // TODO: Review how durability and integrity should interact with this.
            // For now I'm choosing to KISS and ignoring those fields
            if (data.BuyOffers.Count() <= 0 || data.SellOffers.Count() <= 0) yield break;
            Dictionary<string, TradeOffer> maxBuyByType =
                data.BuyOffers
                .GroupBy(x => x.IsTagOffer ? x.Tag.Name : x.Stack.Item.Name)
                .ToDictionary(
                    g => g.Key,
                    g => g.MaxBy(x => x.Price)! //data.BuyOffers is not empty -- if it waas there'd be no loops!
                );
            Dictionary<string, TradeOffer> minSellByType =
                data.SellOffers
                .GroupBy(x => x.IsTagOffer ? x.Tag.Name : x.Stack.Item.Name)
                .ToDictionary(
                    g => g.Key,
                    g => g.MinBy(x => x.Price)! //data.SellOffers is not empty -- if it was there'd be no loops!
                );
            // naive matching (sell == buy)
            foreach (var (item, sellOffer) in minSellByType)
            {
                if (maxBuyByType.TryGetValue(item, out var buyOffer))
                {
                    if (sellOffer != null && buyOffer != null && sellOffer.Price < buyOffer.Price)
                    {
                        yield return new ArbitrageLoop(sellOffer, buyOffer);
                    }
                }
            }
            // tag matching (sell -> buy)
            IEnumerable<TradeOffer> minSellTagOffers = minSellByType.Values.Where(x => x.IsTagOffer); // read: returns all buy offers that are tags. Thanks to minBuyByType, this is deduped and already the smallest priced offer.
            foreach (TradeOffer sellOffer in minSellTagOffers)
            {
                var matches = new List<TradeOffer>();
                foreach (var item in sellOffer.Tag.TaggedItems()) 
                {
                    if (maxBuyByType.TryGetValue(item.Name, out var buyOffer) &&
                        sellOffer != null &&
                        buyOffer != null &&
                        sellOffer.Price < buyOffer.Price)
                    {
                        matches.Add(buyOffer);
                    }
                }
                yield return new ArbitrageLoop_Tag(sellOffer!, matches);
            }
            // tag matching (buy -> sell)
            IEnumerable<TradeOffer> maxBuyTagOffers = maxBuyByType.Values.Where(x => x.IsTagOffer); // read: returns all buy offers that are tags. Thanks to maxBuyByType, this is deduped and already the largest priced offer.
            foreach (TradeOffer buyOffer in maxBuyTagOffers)
            {
                var matches = new List<TradeOffer>();
                foreach (var item in buyOffer.Tag.TaggedItems())
                {
                    if (minSellByType.TryGetValue(item.Name, out var sellOffer) &&
                        sellOffer != null &&
                        buyOffer != null &&
                        sellOffer.Price < buyOffer.Price)
                    {
                        matches.Add(sellOffer);
                    }
                }
                yield return new ArbitrageLoop_Tag(buyOffer!, matches);
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

        private void NotifyUser(User user, LocString msg)
        {
            _warningsServed++;
            PlayerPopups.OkBox(user.Player, msg);
        }

        private LocString FormatMessage(LocString storeName, IEnumerable<IArbitrageLoop> arbitrages)
        {
            if (!arbitrages.Any())
            {
                Logger.Info("FormatMessage was called with 0 arbitrages!");
                return Localizer.DoStr($"{storeName} has no pricing issues.");
            }
            var sb = new LocStringBuilder();
            sb.AppendLine(TextLoc.SizeLoc(30, $"{storeName} has {(arbitrages.Count() > 1 ? "pricing issues..." : "a pricing issue...")}"));
            sb.AppendLine(TextLoc.WarningLocStr("The following items currently allow players to profit at the store's expense:"));
            sb.AppendLine(Localizer.NotLocalizedStr("<align=left>"));
            foreach (var a in arbitrages)
            {
                sb.AppendDashLineLocStr(a.ToString());
            }
            sb.AppendLine(Localizer.NotLocalizedStr("</align>"));
            sb.AppendLine(TextLoc.InfoLightLocStr("You can fix this by either selling for more or buying for less."));
            return sb.ToLocString();
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// Invariant: SellTradeOffer and BuyTradeOffer must have the same underlying Item or Tag.
    [SupportedOSPlatform("windows7.0")]
    public class ArbitrageLoop: IArbitrageLoop
    {
        public string SortKey => SellTradeOffer.IsTagOffer ? SellTradeOffer.Tag.Name : SellTradeOffer.Stack.Item.Name;

        public readonly TradeOffer SellTradeOffer;
        public readonly TradeOffer BuyTradeOffer;
        public readonly float delta = 0;
        public readonly int ItemTypeID;

        public ArbitrageLoop(TradeOffer sell, TradeOffer buy)
        {
            this.SellTradeOffer = sell;
            this.BuyTradeOffer = buy;
            delta = Math.Abs(sell.Price - buy.Price);
            ItemTypeID = sell.IsTagOffer ? sell.Tag.TypeID() : sell.Stack.Item.TypeID;
        }

        public override string ToString() { return ToString(null, null); }

        public string ToString(string? str, IFormatProvider? fmt)
        {
            // Iron Ore: sell 5 → buy 7 (−2 per trade)
            var sellName = SellTradeOffer.IsTagOffer ? SellTradeOffer.Tag.MarkedUpName : SellTradeOffer.Stack.Item.MarkedUpName;
            var buyName = BuyTradeOffer.IsTagOffer ? BuyTradeOffer.Tag.MarkedUpName : BuyTradeOffer.Stack.Item.MarkedUpName;
            var sb = new LocStringBuilder();
            sb.AppendLoc($"Selling and buying {buyName} loses ");
            sb.Append(TextLoc.ErrorLoc($"{delta:0.00}"));
            sb.AppendLocStr(" per trade.");
            return sb.ToString();
        }

        public int GetStableHashCode()
        {
            var hash = new HashCode();
            hash.Add(nameof(ArbitrageLoop));
            var name = SellTradeOffer.IsTagOffer ? SellTradeOffer.Tag.Name : SellTradeOffer.Stack.Item.Name;
            hash.Add(name);
            hash.Add(SellTradeOffer.Price);
            hash.Add(BuyTradeOffer.Price);
            return hash.ToHashCode();
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// Invariant: SellTradeOffer and BuyTradeOffer must have the same underlying Item or Tag.
    [SupportedOSPlatform("windows7.0")]
    public class ArbitrageLoop_Tag: IArbitrageLoop
    {
        public string SortKey => TaggedOffer.Tag.Name;

        public readonly TradeOffer TaggedOffer;
        public bool IsBuying => TaggedOffer.Buying;
        public readonly IReadOnlyList<TradeOffer> RelatedOffers;

        public ArbitrageLoop_Tag(TradeOffer taggedOffer, IEnumerable<TradeOffer> relatedOffers)
        {
            if (!relatedOffers.Any()) throw new ArgumentException("Related offers was empty! Expected there to be at least one element.");
            if (relatedOffers.Any(x => x.IsTagOffer)) throw new ArgumentException("A tagged offer cannot have tag offers in it's related list. They must all be Items.");
            this.TaggedOffer = taggedOffer;
            this.RelatedOffers = relatedOffers.ToList();
        }

        public override string ToString() { return ToString(null, null); }

        public string ToString(string? str, IFormatProvider? fmt)
        {
            if (RelatedOffers.Count > 1)
            {
                int count = 0;
                float maxDelta = 0;
                var itemStringBuilder = new LocStringBuilder();
                foreach (var offer in RelatedOffers)
                {
                    var delta = Math.Abs(TaggedOffer.Price - offer.Price);
                    var name = offer.IsTagOffer ? offer.Tag.MarkedUpName : offer.Stack.Item.MarkedUpName;
                    itemStringBuilder.AppendLoc($"{name} (");
                    itemStringBuilder.Append(TextLoc.ErrorLoc($"{delta:0.00}"));
                    itemStringBuilder.Append(")");
                    itemStringBuilder.AppendLine();
                    if (delta > maxDelta) maxDelta = delta;
                    count++;
                }                
                var foldout = TextLoc.FoldoutLoc(
                    $"{count} items",
                    $"Items (Loss)",
                    itemStringBuilder.ToLocString()
                );
                var sb = new LocStringBuilder();
                sb.AppendLocStr("Selling ");
                if (IsBuying) sb.Append(foldout);
                else sb.Append(TaggedOffer.Tag.MarkedUpName);
                sb.AppendLocStr(" while buying ");
                if (IsBuying) sb.Append(TaggedOffer.Tag.MarkedUpName);
                else sb.Append(foldout);
                sb.AppendLocStr(" loses up to ");
                sb.Append(TextLoc.ErrorLoc($"{maxDelta:0.00}"));
                sb.Append(" per trade.");
                return sb.ToString();
            } 
            else
            {
                var offer = RelatedOffers.First();
                if (offer == null) return "<Error: A problem happened when trying to print this variable>";
                var offerName = offer.IsTagOffer ? offer.Tag.MarkedUpName : offer.Stack.Item.Name;
                var delta = Math.Abs(offer.Price - TaggedOffer.Price);
                var sb = new LocStringBuilder();
                sb.AppendLoc($"Selling {TaggedOffer.Tag.MarkedUpName} and buying {offerName} loses ");
                sb.Append(TextLoc.ErrorLoc($"{delta:0.00}"));
                sb.AppendLocStr(" per trade.");
                return sb.ToString();
            }
        }

        public int GetStableHashCode()
        {
            var hash = new HashCode();
            hash.Add(nameof(ArbitrageLoop_Tag));
            hash.Add(TaggedOffer.Tag.Name);
            hash.Add(TaggedOffer.Price);
            hash.Add(IsBuying);
            var sorted = RelatedOffers
                .OrderBy(x => x.Stack.Item.Name)
                .ThenBy(x => x.Price)
                .ThenBy(x => x.Buying);
            foreach (var offer in sorted)
            {
                hash.Add(offer.Stack.Item.Name);
                hash.Add(offer.Price);
                hash.Add(offer.Buying);
            }
            return hash.ToHashCode();
        }
    }

    public interface IArbitrageLoop: IFormattable
    {
        string SortKey { get; }
        int GetStableHashCode();
    }

}
