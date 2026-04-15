using Eco.Core.Plugins.Interfaces;
using Eco.Core.Utils;
using Eco.Gameplay.Components.Store;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Shared.Localization;
using Eco.Shared.Networking;
using Eco.Shared.Utils;
using System.Runtime.Versioning;

namespace CommonCents
{

    [SupportedOSPlatform("windows7.0")]
    public class CommonCentsPlugin: IModKitPlugin, IInitializablePlugin, IShutdownablePlugin
    {
        public const string VERSION = "v0.0.1";
        private HashSet<StoreComponent> _trackedStores = new();
        
        #region IModKitPlugin
        private int _warningsServed = 0;
        private string _status = "Starting...";

        public string GetCategory() => "Mods";

        public string GetStatus() => Localizer.DoStr($"Status: {_status} -- {_warningsServed} mistakes caught.");
        #endregion

        private IEventSubscription? AddedEventSubscription = null;
        private IEventSubscription? RemovedEventSubscription = null;

        public void Initialize(TimedTask timer)
        {
            AddedEventSubscription = WorldObjectManager.WorldObjectAddedEvent.SubscribeUnique(HandleWorldObjectAdded);
            RemovedEventSubscription = WorldObjectManager.WorldObjectRemovedEvent.SubscribeUnique(HandleWorldObjectRemoved);
            var stores = WorldObjectManager.GetWorldObjectsFromComponent(typeof(StoreComponent)) as IEnumerable<StoreComponent>;
            InitStores(stores);
            _status = "Running";
        }

        public Task ShutdownAsync()
        {
            AddedEventSubscription?.Dispose();
            RemovedEventSubscription?.Dispose();
            _status = "Shutdown";
            // TODO: Remove hooks from _trackedStores
            return Task.CompletedTask;
        }

        private void HandleWorldObjectAdded(WorldObject obj, User usr)
        {
            // TODO: Filter Obj to StoreComponent
            // TODO: Add obj to _trackedStores
            // TODO: Add hook into the sell orders to check for exploits
            throw new NotImplementedException();
        }

        private void HandleWorldObjectRemoved(WorldObject obj)
        {
            // TODO: Filter Obj to StoreComponent
            // TODO: Remove hooks from this StoreComponent
            throw new NotImplementedException();
        }

        private void InitStores(IEnumerable<StoreComponent>? stores)
        {
            if (stores == null)
            {
                Logger.Info("During initialization the list of stores was null! Expected a list of 0 or more items.");
                return;
            }
            _trackedStores.AddRange(stores);
            // TODO: Add hook into the sell orders to check if this store is buying at a higher price
            throw new NotImplementedException();
        }
    }
}
