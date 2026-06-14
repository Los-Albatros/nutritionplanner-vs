using nutritionPlannerVintageStoryMod.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace nutritionPlannerVintageStoryMod.Client;

public class NutritionPlannerClientSystem : ModSystem
{
    private ICoreClientAPI?        _capi;
    private NutritionHud?          _hud;
    private NutritionHudConfig?    _config;
    private IClientNetworkChannel? _channel;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        _capi   = api;
        _config = NutritionHudConfig.Load(api);

        _channel = api.Network
            .RegisterChannel("nutritionplanner")
            .RegisterMessageType<HistoryPacket>()
            .RegisterMessageType<SuggestionPacket>()
            .RegisterMessageType<SuggestRequestPacket>()
            .RegisterMessageType<ConfigSyncPacket>()
            .SetMessageHandler<HistoryPacket>(OnHistory)
            .SetMessageHandler<SuggestionPacket>(OnSuggestion)
            .SetMessageHandler<ConfigSyncPacket>(OnConfigSync);

        _hud = new NutritionHud(api, _config, req => _channel?.SendPacket(req));

        api.Input.RegisterHotKey("nutritionplanner_toggle", "Toggle Nutrition HUD", GlKeys.N,
            HotkeyType.GUIOrOtherControls);
        api.Input.SetHotKeyHandler("nutritionplanner_toggle", _ => { _hud?.Toggle(); return true; });

        api.Event.RegisterGameTickListener(dt => _hud?.Refresh(dt), 1000);

        if (api.ModLoader.IsModEnabled("configlib"))
        {
            try { ConfigLibIntegration.Register(api, _config, _channel!); }
            catch (Exception ex) { Mod.Logger.Warning("NutritionPlanner: ConfigLib integration failed: " + ex.Message); }
        }
    }

    private void OnHistory(HistoryPacket packet) =>
        _hud?.SetHistory(packet.Entries);

    private void OnSuggestion(SuggestionPacket packet)
    {
        if (packet.Text == "__toggle_hud__") { _hud?.Toggle(); return; }
        _hud?.SetSuggestion(packet.Text);
    }

    private void OnConfigSync(ConfigSyncPacket packet) =>
        _hud?.ApplyConfigSync(packet);

    public override void Dispose()
    {
        _hud?.TryClose();
        _hud?.Dispose();
    }
}
