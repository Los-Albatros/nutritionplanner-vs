using nutritionPlannerVintageStoryMod.Config;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace nutritionPlannerVintageStoryMod.Server;

public class FoodHistoryTracker
{
    private const string PersistKey = "nutritionplanner_history";

    private readonly ICoreServerAPI _api;
    private readonly ModConfigData  _cfg;

    private readonly Dictionary<string, NutrientValues> _prev   = new();
    private readonly Dictionary<string, FoodHistoryStore> _stores = new();
    private long _tickListenerId = -1;

    public FoodHistoryTracker(ICoreServerAPI api, ModConfigData cfg)
    {
        _api = api;
        _cfg = cfg;
    }

    public void Start()
    {
        _tickListenerId = _api.Event.RegisterGameTickListener(OnTick, _cfg.PollIntervalMs);
        _api.Event.PlayerNowPlaying += OnPlayerJoin;
        _api.Event.PlayerDisconnect += OnPlayerLeave;
    }

    public void Stop()
    {
        if (_tickListenerId >= 0) _api.Event.UnregisterGameTickListener(_tickListenerId);
        _api.Event.PlayerNowPlaying -= OnPlayerJoin;
        _api.Event.PlayerDisconnect -= OnPlayerLeave;
    }

    public FoodHistoryStore GetStore(string playerUid) =>
        _stores.TryGetValue(playerUid, out var s) ? s : new FoodHistoryStore();

    private void OnPlayerJoin(IServerPlayer player)
    {
        var raw = player.WorldData.GetModdata(PersistKey);
        _stores[player.PlayerUID] = FoodHistoryStore.Deserialize(raw ?? []);
        _prev[player.PlayerUID]   = ReadNutrients(player);
    }

    private void OnPlayerLeave(IServerPlayer player)
    {
        Persist(player);
        _stores.Remove(player.PlayerUID);
        _prev.Remove(player.PlayerUID);
    }

    private void OnTick(float _)
    {
        foreach (var player in _api.World.AllOnlinePlayers.OfType<IServerPlayer>())
        {
            var uid     = player.PlayerUID;
            var current = ReadNutrients(player);

            if (_prev.TryGetValue(uid, out var prev) && HasPositiveDelta(prev, current))
            {
                var itemCode = player.Entity.ActiveHandItemSlot?.Itemstack?.Collectible?.Code?.ToString()
                               ?? "unknown";
                var entry = new FoodEntry(
                    itemCode,
                    (long)(_api.World.Calendar.TotalDays * 24000L),
                    Math.Max(0f, current.Grain   - prev.Grain),
                    Math.Max(0f, current.Veg     - prev.Veg),
                    Math.Max(0f, current.Protein - prev.Protein),
                    Math.Max(0f, current.Dairy   - prev.Dairy));

                if (!_stores.ContainsKey(uid)) _stores[uid] = new FoodHistoryStore();
                _stores[uid].Add(entry);
                Persist(player);

                OnFoodConsumed?.Invoke(player, entry, _stores[uid]);
            }

            _prev[uid] = current;
        }
    }

    public event Action<IServerPlayer, FoodEntry, FoodHistoryStore>? OnFoodConsumed;

    private static NutrientValues ReadNutrients(IServerPlayer player)
    {
        var beh = player.Entity.GetBehavior<EntityBehaviorHunger>();
        if (beh == null) return new NutrientValues(0, 0, 0, 0, 1500f);
        return new NutrientValues(
            beh.GrainLevel,
            beh.VegetableLevel,
            beh.ProteinLevel,
            beh.DairyLevel,
            beh.MaxSaturation);
    }

    private static bool HasPositiveDelta(NutrientValues prev, NutrientValues curr) =>
        curr.Grain   > prev.Grain   + 0.01f ||
        curr.Veg     > prev.Veg     + 0.01f ||
        curr.Protein > prev.Protein + 0.01f ||
        curr.Dairy   > prev.Dairy   + 0.01f;

    private void Persist(IServerPlayer player)
    {
        if (_stores.TryGetValue(player.PlayerUID, out var store))
            player.WorldData.SetModdata(PersistKey, store.Serialize());
    }
}
