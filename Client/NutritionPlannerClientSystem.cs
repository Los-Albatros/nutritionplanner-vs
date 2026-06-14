using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace nutritionPlannerVintageStoryMod.Client;

public class NutritionPlannerClientSystem : ModSystem
{
    private ICoreClientAPI?     _capi;
    private NutritionHud?       _hud;
    private NutritionHudConfig? _config;
    private FoodHistoryStore    _history = new();
    private NutrientValues      _prev    = new(0, 0, 0, 0, 1500f);

    private const string HistoryFile = "nutritionplanner_history.json";

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        _capi   = api;
        _config = NutritionHudConfig.Load(api);
        LoadHistory(api);

        _hud = new NutritionHud(api, _config, OnSuggestRequested);

        api.Input.RegisterHotKey("nutritionplanner_toggle", "Toggle Nutrition HUD",
            GlKeys.N, HotkeyType.GUIOrOtherControls);
        api.Input.SetHotKeyHandler("nutritionplanner_toggle", _ => { _hud?.Toggle(); return true; });

        api.Event.RegisterGameTickListener(OnTick, 1000);

        api.ChatCommands.Create("nutrition")
            .WithDescription("NutritionPlanner commands")
            .BeginSubCommand("suggest")
                .WithDescription("Request a nutrition suggestion now")
                .HandleWith(_ => { OnSuggestRequested(); return TextCommandResult.Success(); })
            .EndSubCommand();

        if (api.ModLoader.IsModEnabled("configlib"))
        {
            try { ConfigLibIntegration.Register(api, _config); }
            catch (Exception ex)
            {
                Mod.Logger.Warning("[NutritionPlanner] ConfigLib integration failed: " + ex.Message);
            }
        }
    }

    private void OnTick(float dt)
    {
        var entity = _capi?.World.Player?.Entity;
        if (entity == null) return;

        var hunger = entity.WatchedAttributes.GetTreeAttribute("hunger");
        if (hunger == null) return;

        float max = hunger.GetFloat("maxsaturation", 1500f);
        if (max <= 0) max = 1500f;
        var current = new NutrientValues(
            hunger.GetFloat("grainLevel"),
            hunger.GetFloat("vegetableLevel"),
            hunger.GetFloat("proteinLevel"),
            hunger.GetFloat("dairyLevel"),
            max);

        if (HasPositiveDelta(_prev, current))
        {
            var itemCode = _capi!.World.Player.Entity.ActiveHandItemSlot
                               ?.Itemstack?.Collectible?.Code?.ToString() ?? "unknown";
            var entry = new FoodEntry(
                itemCode,
                (long)(_capi.World.Calendar.TotalDays * 24000L),
                Math.Max(0f, current.Grain   - _prev.Grain),
                Math.Max(0f, current.Veg     - _prev.Veg),
                Math.Max(0f, current.Protein - _prev.Protein),
                Math.Max(0f, current.Dairy   - _prev.Dairy));
            _history.Add(entry);
            SaveHistory();
            _hud?.SetHistory(_history.Entries);
        }

        _prev = current;
        _hud?.Refresh(dt);
    }

    private static bool HasPositiveDelta(NutrientValues prev, NutrientValues curr) =>
        curr.Grain   > prev.Grain   + 0.01f ||
        curr.Veg     > prev.Veg     + 0.01f ||
        curr.Protein > prev.Protein + 0.01f ||
        curr.Dairy   > prev.Dairy   + 0.01f;

    private void OnSuggestRequested()
    {
        var inv  = BuildInventoryFoods();
        var text = NutritionFallback.Suggest(_prev, inv) ?? "All nutrition bars look balanced.";
        _hud?.SetSuggestion(text);
    }

    private List<InventoryFood> BuildInventoryFoods()
    {
        var result = new List<InventoryFood>();
        if (_capi?.World.Player == null) return result;

        foreach (var inv in _capi.World.Player.InventoryManager.Inventories.Values)
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

    private void LoadHistory(ICoreClientAPI api)
    {
        try
        {
            var entries = api.LoadModConfig<List<FoodEntry>>(HistoryFile);
            if (entries != null)
                foreach (var e in entries) _history.Add(e);
        }
        catch { }
    }

    private void SaveHistory()
    {
        try { _capi?.StoreModConfig(_history.Entries.ToList(), HistoryFile); }
        catch { }
    }

    public override void Dispose()
    {
        _hud?.TryClose();
        _hud?.Dispose();
    }
}
