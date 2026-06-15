using System.Text.Json;
using Vintagestory.API.Client;

namespace nutritionPlannerVintageStoryMod.Client;

/// <summary>
/// Client-side configuration for the NutritionHud.
/// Persists user preferences like visibility, thresholds, and chat cooldown.
/// </summary>
public class NutritionHudConfig
{
    public bool  HudVisible          { get; set; } = true;
    public float Threshold1          { get; set; } = 30f;
    public float Threshold2          { get; set; } = 15f;
    public int   ChatCooldownSeconds { get; set; } = 300;
    public int   ScanRadiusBlocks    { get; set; } = 64;

    private const string FileName = "nutritionplanner-client.json";

    /// <summary>Load client config from disk, or return defaults if missing/corrupt.</summary>
    public static NutritionHudConfig Load(ICoreClientAPI api)
    {
        try
        {
            return api.LoadModConfig<NutritionHudConfig>(FileName) ?? new NutritionHudConfig();
        }
        catch
        {
            return new NutritionHudConfig();
        }
    }

    /// <summary>Save client config to disk.</summary>
    public void Save(ICoreClientAPI api)
    {
        try
        {
            api.StoreModConfig(this, FileName);
        }
        catch { }
    }
}
