using ConfigLib;
using ImGuiNET;
using Vintagestory.API.Client;

namespace nutritionPlannerVintageStoryMod.Client;

public static class ConfigLibIntegration
{
    private static float _threshold1 = 30f;
    private static float _threshold2 = 15f;
    private static int   _cooldown   = 300;
    private static string _lastHash  = "";

    public static void InvalidateCache() => _lastHash = "";

    public static void Register(ICoreClientAPI api, NutritionHudConfig config)
    {
        var modSys = api.ModLoader.GetModSystem<ConfigLibModSystem>();
        if (modSys == null) return;

        modSys.RegisterCustomConfig("nutritionplanner", (id, buttons) =>
        {
            try
            {
                Sync(config);
                Draw(id, buttons.Save, config, api);
            }
            catch (Exception ex)
            {
                api.Logger.Error($"[NutritionPlanner] ConfigLib draw error: {ex.Message}");
            }
        });
    }

    private static void Sync(NutritionHudConfig config)
    {
        string hash = $"{config.Threshold1}|{config.Threshold2}|{config.ChatCooldownSeconds}";
        if (hash == _lastHash) return;
        _lastHash = hash;

        _threshold1 = config.Threshold1;
        _threshold2 = config.Threshold2;
        _cooldown   = config.ChatCooldownSeconds;
    }

    private static void Draw(string id, bool save, NutritionHudConfig config, ICoreClientAPI api)
    {
        ImGui.TextDisabled("NutritionPlanner settings");
        ImGui.SetNextItemWidth(200f);
        ImGui.SliderFloat($"Warning threshold % (pulse)##{id}t1", ref _threshold1, 5f, 80f);
        ImGui.SetNextItemWidth(200f);
        ImGui.SliderFloat($"Critical threshold % (chat alert)##{id}t2", ref _threshold2, 1f, 50f);
        ImGui.SetNextItemWidth(200f);
        ImGui.SliderInt($"Alert cooldown (seconds)##{id}cd", ref _cooldown, 30, 600);

        if (save)
        {
            config.Threshold1          = _threshold1;
            config.Threshold2          = _threshold2;
            config.ChatCooldownSeconds = _cooldown;
            config.Save(api);
            _lastHash = "";
        }
    }
}
