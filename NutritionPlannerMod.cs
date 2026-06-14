using nutritionPlannerVintageStoryMod.Config;
using nutritionPlannerVintageStoryMod.Network;
using nutritionPlannerVintageStoryMod.Server;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace nutritionPlannerVintageStoryMod;

public class NutritionPlannerMod : ModSystem
{
    private FoodHistoryTracker?    _tracker;
    private ChatAIBridge?          _bridge;
    private ConfigManager?         _cfg;
    private IServerNetworkChannel? _channel;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        _cfg = new ConfigManager(api);
        _cfg.Load();
        _cfg.Save();

        if (!_cfg.Data.Enabled)
        {
            Mod.Logger.Notification("[NutritionPlanner] Disabled via config.");
            return;
        }

        _channel = api.Network
            .RegisterChannel("nutritionplanner")
            .RegisterMessageType<HistoryPacket>()
            .RegisterMessageType<SuggestionPacket>()
            .RegisterMessageType<SuggestRequestPacket>()
            .RegisterMessageType<ConfigSyncPacket>()
            .SetMessageHandler<SuggestRequestPacket>(OnSuggestRequest);

        _tracker = new FoodHistoryTracker(api, _cfg.Data);
        _tracker.OnFoodConsumed += (player, entry, store) =>
        {
            var packet = new HistoryPacket
            {
                Entries = store.Entries.Select(e => new FoodEntryDto
                {
                    ItemCode      = e.ItemCode,
                    GameTimestamp = e.GameTimestamp,
                    DeltaGrain    = e.DeltaGrain,
                    DeltaVeg      = e.DeltaVeg,
                    DeltaProtein  = e.DeltaProtein,
                    DeltaDairy    = e.DeltaDairy,
                }).ToList()
            };
            _channel.SendPacket(packet, player);
        };
        _tracker.Start();

        _bridge = new ChatAIBridge(api, _tracker);
        _bridge.Initialize();

        var cmd = new NutritionCommand(api, _cfg.Data, _tracker, _bridge, _channel);
        cmd.Register();

        api.Event.PlayerNowPlaying += player =>
        {
            var sync = new ConfigSyncPacket
            {
                Threshold1 = _cfg.Data.Threshold1,
                Threshold2 = _cfg.Data.Threshold2,
            };
            api.Event.RegisterCallback(_ => _channel?.SendPacket(sync, player), 500);
        };

        Mod.Logger.Notification("[NutritionPlanner] Server side loaded.");
    }

    private void OnSuggestRequest(IServerPlayer player, SuggestRequestPacket _packet)
    {
        if (_bridge == null || _channel == null) return;
        var ch = _channel;
        var unused = Task.Run(async () =>
        {
            var (text, source) = await _bridge.SuggestAsync(player);
            ch.SendPacket(new SuggestionPacket { Text = text, Source = source }, player);
        });
    }

    public override void Dispose()
    {
        _tracker?.Stop();
    }
}
