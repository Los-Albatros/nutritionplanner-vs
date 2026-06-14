using System.Text;
using System.Text.Json;
using chatAIVintageStoryMod;
using chatAIVintageStoryMod.MCP;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace nutritionPlannerVintageStoryMod.Server;

public class ChatAIBridge
{
    private readonly ICoreServerAPI     _api;
    private readonly FoodHistoryTracker _tracker;
    private ChatAIMod?                  _chatAI;

    public ChatAIBridge(ICoreServerAPI api, FoodHistoryTracker tracker)
    {
        _api     = api;
        _tracker = tracker;
    }

    public void Initialize()
    {
        try
        {
            _chatAI = _api.ModLoader.GetModSystem<ChatAIMod>();
            if (_chatAI != null)
            {
                RegisterMcpTool();
                _api.Logger.Notification("[NutritionPlanner] ChatAI integration active.");
            }
        }
        catch (Exception ex)
        {
            _api.Logger.Warning($"[NutritionPlanner] ChatAI init failed (using local fallback): {ex.Message}");
            _chatAI = null;
        }
    }

    public async Task<(string Text, string Source)> SuggestAsync(IServerPlayer player)
    {
        var beh = player.Entity.GetBehavior<EntityBehaviorHunger>();
        var values = beh == null
            ? new NutrientValues(0, 0, 0, 0, 1500f)
            : new NutrientValues(
                beh.GrainLevel,
                beh.VegetableLevel,
                beh.ProteinLevel,
                beh.DairyLevel,
                beh.MaxSaturation);

        var inventory = BuildInventoryFoods(player);
        var history   = _tracker.GetStore(player.PlayerUID);

        if (_chatAI != null)
        {
            try
            {
                var prompt  = BuildPrompt(values, inventory, history);
                var aiReply = await _chatAI.AskAsync(prompt);
                if (IsValidResponse(aiReply))
                    return (aiReply!, "chatai");
            }
            catch { /* fall through to local */ }
        }

        var local = NutritionFallback.Suggest(values, inventory)
                    ?? "All nutrition bars look balanced.";
        return (local, "local");
    }

    private static bool IsValidResponse(string? r) =>
        !string.IsNullOrWhiteSpace(r) && r.TrimEnd().Length >= 5;

    private void RegisterMcpTool()
    {
        _chatAI!.RegisterTool(new MCPTool
        {
            Name        = "nutrition_get_player_context",
            Description = "Returns the current nutrition values and recent food history for a player.",
            Parameters  =
            [
                new MCPParameter { Name = "player_name", Type = "string", Description = "Exact player name", Required = true }
            ],
            Handler = async args =>
            {
                var name   = args.TryGetValue("player_name", out var v) ? v.GetString() ?? "" : "";
                var player = _api.World.AllOnlinePlayers
                    .OfType<IServerPlayer>()
                    .FirstOrDefault(p => p.PlayerName.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (player == null) return $"Player '{name}' not online.";

                var beh = player.Entity.GetBehavior<EntityBehaviorHunger>();
                if (beh == null) return "Hunger behavior not available.";

                float max = beh.MaxSaturation > 0 ? beh.MaxSaturation : 1500f;
                float Pct(float val) => val / max * 100f;

                var history = _tracker.GetStore(player.PlayerUID).Entries
                    .TakeLast(5)
                    .Select(e => $"{e.ItemCode} (G:{e.DeltaGrain:F0} V:{e.DeltaVeg:F0} P:{e.DeltaProtein:F0} D:{e.DeltaDairy:F0})")
                    .ToList();

                return $"Nutrition: Grain={Pct(beh.GrainLevel):F0}% " +
                       $"Veg={Pct(beh.VegetableLevel):F0}% " +
                       $"Protein={Pct(beh.ProteinLevel):F0}% " +
                       $"Dairy={Pct(beh.DairyLevel):F0}%. " +
                       $"Recent meals: {(history.Count > 0 ? string.Join(", ", history) : "none")}.";
            }
        });
    }

    private static string BuildPrompt(NutrientValues v, List<InventoryFood> inv, FoodHistoryStore history)
    {
        var sb = new StringBuilder();
        sb.Append($"My nutrition: Grain={v.GrainPct:F0}%, Veg={v.VegPct:F0}%, ");
        sb.Append($"Protein={v.ProteinPct:F0}%, Dairy={v.DairyPct:F0}%. ");

        var recent = history.Entries.TakeLast(3).Select(e => e.ItemCode).ToList();
        if (recent.Count > 0) sb.Append($"Recently ate: {string.Join(", ", recent)}. ");

        var foods = inv.Where(i => !string.IsNullOrEmpty(i.NutrientCategory))
                       .Select(i => $"{i.ItemCode}({i.NutrientCategory})")
                       .Take(10)
                       .ToList();
        if (foods.Count > 0) sb.Append($"Available food: {string.Join(", ", foods)}. ");

        sb.Append("Suggest 1 food item to eat. One sentence only.");
        return sb.ToString();
    }

    private static List<InventoryFood> BuildInventoryFoods(IServerPlayer player)
    {
        var result = new List<InventoryFood>();
        foreach (var inv in player.InventoryManager.Inventories.Values)
        {
            foreach (var slot in inv)
            {
                var props = slot.Itemstack?.Collectible?.NutritionProps;
                if (props == null) continue;
                var cat = props.FoodCategory switch
                {
                    EnumFoodCategory.Grain     => "Grain",
                    EnumFoodCategory.Vegetable => "Veg",
                    EnumFoodCategory.Protein   => "Protein",
                    EnumFoodCategory.Dairy     => "Dairy",
                    _                          => ""
                };
                if (!string.IsNullOrEmpty(cat))
                    result.Add(new InventoryFood(
                        slot.Itemstack!.Collectible.Code.ToString(), cat));
            }
        }
        return result;
    }
}
