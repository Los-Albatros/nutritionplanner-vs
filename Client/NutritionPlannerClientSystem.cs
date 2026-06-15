using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
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
        var mostNeeded = _prev.OrderedByNeed().FirstOrDefault() ?? "Grain";
        var inv        = PrioritizeByHistory(BuildInventoryFoods(), mostNeeded);

        var match = inv.FirstOrDefault(f => string.Equals(f.NutrientCategory, mostNeeded, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            int qty = CountInInventory(match.ItemCode);
            _hud?.SetSuggestion(FormatSuggestion(match.ItemCode, match.NutrientCategory, qty: qty, source: "in bag"), match.ItemCode);
            return;
        }

        if ((_config?.ScanRadiusBlocks ?? 0) > 0)
        {
            var nearby  = ScanNearbyContainers();
            var scores  = _history.Entries.Count > 0
                ? _history.Entries
                    .GroupBy(e => e.ItemCode)
                    .ToDictionary(g => g.Key, g => g.Sum(e => mostNeeded switch
                    {
                        "Grain"   => e.DeltaGrain,
                        "Veg"     => e.DeltaVeg,
                        "Protein" => e.DeltaProtein,
                        "Dairy"   => e.DeltaDairy,
                        "Fruit"   => e.DeltaFruit,
                        _         => 0f
                    }))
                : new Dictionary<string, float>();

            var best = nearby
                .Where(t => string.Equals(t.Food.NutrientCategory, mostNeeded, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(t => scores.TryGetValue(t.Food.ItemCode, out var s) ? s : 0f)
                .FirstOrDefault();

            if (best != default)
            {
                var dir = DirectionHint(_capi!.World.Player.Entity.Pos.AsBlockPos, best.Pos);
                _hud?.SetSuggestion(FormatSuggestion(best.Food.ItemCode, best.Food.NutrientCategory, source: dir), best.Food.ItemCode);
                return;
            }
        }

        var fallback = inv.FirstOrDefault();
        if (fallback != null)
        {
            int qty = CountInInventory(fallback.ItemCode);
            _hud?.SetSuggestion(FormatSuggestion(fallback.ItemCode, fallback.NutrientCategory, qty: qty, missingCat: mostNeeded), fallback.ItemCode);
            return;
        }

        _hud?.SetSuggestion($"{mostNeeded} needed — no food found nearby.", null);
    }

    private List<(InventoryFood Food, BlockPos Pos)> ScanNearbyContainers()
    {
        var result = new List<(InventoryFood, BlockPos)>();
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
                        result.Add((new InventoryFood(slot.Itemstack!.Collectible.Code.ToString(), cat), bpos));
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

    private static string DirectionHint(BlockPos from, BlockPos to)
    {
        int    dx   = to.X - from.X;
        int    dz   = to.Z - from.Z;
        double dist = Math.Sqrt(dx * dx + dz * dz);
        string dir;
        if      (Math.Abs(dx) > Math.Abs(dz) * 2) dir = dx > 0 ? "E" : "W";
        else if (Math.Abs(dz) > Math.Abs(dx) * 2) dir = dz > 0 ? "S" : "N";
        else                                        dir = (dz > 0 ? "S" : "N") + (dx > 0 ? "E" : "W");
        return $"{dist:F0}m {dir}";
    }

    private float GetSatiety(string itemCode)
    {
        var loc   = new AssetLocation(itemCode);
        var item  = _capi!.World.GetItem(loc);
        if (item  != null) return item.NutritionProps?.Satiety ?? 0f;
        var block = _capi!.World.GetBlock(loc);
        return block?.NutritionProps?.Satiety ?? 0f;
    }

    private int CountInInventory(string itemCode)
    {
        if (_capi?.World.Player == null) return 0;
        int count = 0;
        foreach (var inv in _capi.World.Player.InventoryManager.Inventories.Values)
            foreach (var slot in inv)
                if (slot.Itemstack?.Collectible?.Code?.ToString() == itemCode)
                    count += slot.Itemstack.StackSize;
        return count;
    }

    private List<InventoryFood> PrioritizeByHistory(List<InventoryFood> foods, string targetCategory)
    {
        if (_history.Entries.Count == 0) return foods;
        var scores = _history.Entries
            .GroupBy(e => e.ItemCode)
            .ToDictionary(g => g.Key, g => g.Sum(e => targetCategory switch
            {
                "Grain"   => e.DeltaGrain,
                "Veg"     => e.DeltaVeg,
                "Protein" => e.DeltaProtein,
                "Dairy"   => e.DeltaDairy,
                "Fruit"   => e.DeltaFruit,
                _         => 0f
            }));
        return foods.OrderByDescending(f => scores.TryGetValue(f.ItemCode, out var s) ? s : 0f).ToList();
    }

    private string FormatSuggestion(string itemCode, string category, int qty = 0, string? source = null, string? missingCat = null)
    {
        var   name    = TranslateName(itemCode);
        float sat     = GetSatiety(itemCode);
        var   gain    = sat > 0 ? $" +{sat:F0}" : "";
        var   count   = qty > 1 ? $"×{qty} " : "";
        var   src     = source     != null ? $" — {source}"           : "";
        var   missing = missingCat != null ? $" — {missingCat} missing!" : "";
        return $"{count}{name} ({category}{gain}){src}{missing}";
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
