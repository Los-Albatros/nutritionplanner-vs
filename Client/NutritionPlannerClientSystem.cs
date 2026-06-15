using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace nutritionPlannerVintageStoryMod.Client;

public class NutritionPlannerClientSystem : ModSystem
{
    private ICoreClientAPI?     _capi;
    private NutritionHud?       _hud;
    private NutritionHudConfig? _config;
    private FoodHistoryStore    _history = new();
    private NutrientValues      _prev    = new(0, 0, 0, 0, 0, 1500f);

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
            hunger.GetFloat("fruitLevel"),
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
                Math.Max(0f, current.Dairy   - _prev.Dairy),
                Math.Max(0f, current.Fruit   - _prev.Fruit));
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
        curr.Dairy   > prev.Dairy   + 0.01f ||
        curr.Fruit   > prev.Fruit   + 0.01f;

    private void OnSuggestRequested()
    {
        // Priority 1: player inventory
        var inv = BuildInventoryFoods();
        var raw = NutritionFallback.Suggest(_prev, inv);

        // Priority 2: nearby containers within configured radius
        if (raw == null && (_config?.ScanRadiusBlocks ?? 0) > 0)
        {
            var nearby = ScanNearbyContainers();
            raw = NutritionFallback.Suggest(_prev, nearby);
        }

        if (raw == null) { _hud?.SetSuggestion("No food found in inventory or nearby.", null); return; }

        var sep  = raw.IndexOf(" (+", StringComparison.Ordinal);
        var code = sep >= 0 ? raw[..sep] : raw;
        var rest = sep >= 0 ? raw[sep..] : "";
        _hud?.SetSuggestion(TranslateName(code) + rest, code);
    }

    private List<InventoryFood> ScanNearbyContainers()
    {
        var result = new List<InventoryFood>();
        if (_capi?.World.Player?.Entity == null || _config == null) return result;

        var center = _capi.World.Player.Entity.Pos.AsBlockPos;
        int radius = _config.ScanRadiusBlocks;
        int cr     = (radius >> 5) + 1;
        int cx0    = center.X >> 5, cy0 = center.Y >> 5, cz0 = center.Z >> 5;

        for (int cx = cx0 - cr; cx <= cx0 + cr; cx++)
        for (int cy = cy0 - cr; cy <= cy0 + cr; cy++)
        for (int cz = cz0 - cr; cz <= cz0 + cr; cz++)
        {
            var chunk = _capi.World.BlockAccessor.GetChunk(cx, cy, cz);
            if (chunk?.BlockEntities == null) continue;

            foreach (var (bpos, be) in chunk.BlockEntities)
            {
                if (Math.Abs(bpos.X - center.X) > radius ||
                    Math.Abs(bpos.Y - center.Y) > radius ||
                    Math.Abs(bpos.Z - center.Z) > radius) continue;

                if (be is not BlockEntityContainer container) continue;

                foreach (var slot in container.Inventory)
                {
                    var props = slot.Itemstack?.Collectible?.NutritionProps;
                    if (props == null) continue;
                    var cat = props.FoodCategory switch
                    {
                        EnumFoodCategory.Grain     => "Grain",
                        EnumFoodCategory.Vegetable => "Veg",
                        EnumFoodCategory.Protein   => "Protein",
                        EnumFoodCategory.Dairy     => "Dairy",
                        EnumFoodCategory.Fruit     => "Fruit",
                        _                          => ""
                    };
                    if (!string.IsNullOrEmpty(cat))
                        result.Add(new InventoryFood(
                            slot.Itemstack!.Collectible.Code.ToString(), cat));
                }
            }
        }
        return result;
    }

    private string TranslateName(string itemCode)
    {
        var loc   = new AssetLocation(itemCode);
        var item  = _capi!.World.GetItem(loc);
        if (item  != null) return item.GetHeldItemName(new ItemStack(item));
        var block = _capi!.World.GetBlock(loc);
        if (block != null) return block.GetHeldItemName(new ItemStack(block, 1));
        return loc.Path.Replace("-", " ");
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
                    EnumFoodCategory.Fruit     => "Fruit",
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
