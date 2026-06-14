using nutritionPlannerVintageStoryMod.Config;
using nutritionPlannerVintageStoryMod.Network;
using Vintagestory.API.Common;
using Vintagestory.API.Common.CommandAbbr;
using Vintagestory.API.Server;

namespace nutritionPlannerVintageStoryMod.Server;

public class NutritionCommand
{
    private readonly ICoreServerAPI       _api;
    private readonly ModConfigData        _cfg;
    private readonly FoodHistoryTracker   _tracker;
    private readonly ChatAIBridge         _bridge;
    private readonly IServerNetworkChannel _channel;

    public NutritionCommand(ICoreServerAPI api, ModConfigData cfg,
        FoodHistoryTracker tracker, ChatAIBridge bridge, IServerNetworkChannel channel)
    {
        _api     = api;
        _cfg     = cfg;
        _tracker = tracker;
        _bridge  = bridge;
        _channel = channel;
    }

    public void Register()
    {
        _api.ChatCommands
            .Create("nutrition")
            .WithAlias("nutr")
            .WithDescription("NutritionPlanner commands.")
            .RequiresPrivilege(Privilege.chat)
            .BeginSubCommand("suggest")
                .WithDescription("Request an AI or fallback meal suggestion.")
                .HandleWith(OnSuggest)
            .EndSubCommand()
            .BeginSubCommand("history")
                .WithDescription("Print your recent food history to chat.")
                .HandleWith(OnHistory)
            .EndSubCommand()
            .BeginSubCommand("reset")
                .WithDescription("Clear your food history.")
                .HandleWith(OnReset)
            .EndSubCommand()
            .BeginSubCommand("hud")
                .WithDescription("Toggle the nutrition HUD on/off.")
                .HandleWith(OnHudToggle)
            .EndSubCommand();
    }

    private TextCommandResult OnSuggest(TextCommandCallingArgs args)
    {
        if (args.Caller.Player is not IServerPlayer player)
            return TextCommandResult.Error("Must be called by a player.");

        _ = Task.Run(async () =>
        {
            var (text, source) = await _bridge.SuggestAsync(player);
            var packet = new SuggestionPacket { Text = text, Source = source };
            _channel.SendPacket(packet, player);
        });

        return TextCommandResult.Success("Fetching suggestion…");
    }

    private TextCommandResult OnHistory(TextCommandCallingArgs args)
    {
        if (args.Caller.Player is not IServerPlayer player)
            return TextCommandResult.Error("Must be called by a player.");

        var entries = _tracker.GetStore(player.PlayerUID).Entries;
        if (entries.Count == 0)
            return TextCommandResult.Success("No food history recorded yet.");

        var lines = entries
            .TakeLast(10)
            .Select(e => $"  {e.ItemCode}: G+{e.DeltaGrain:F0} V+{e.DeltaVeg:F0} P+{e.DeltaProtein:F0} D+{e.DeltaDairy:F0}");
        return TextCommandResult.Success("Recent meals:\n" + string.Join("\n", lines));
    }

    private TextCommandResult OnReset(TextCommandCallingArgs args)
    {
        if (args.Caller.Player is not IServerPlayer player)
            return TextCommandResult.Error("Must be called by a player.");

        _tracker.GetStore(player.PlayerUID).Clear();
        return TextCommandResult.Success("Food history cleared.");
    }

    private TextCommandResult OnHudToggle(TextCommandCallingArgs args)
    {
        if (args.Caller.Player is not IServerPlayer player)
            return TextCommandResult.Error("Must be called by a player.");

        _channel.SendPacket(new SuggestionPacket { Text = "__toggle_hud__", Source = "system" }, player);
        return TextCommandResult.Success("HUD toggled.");
    }
}
