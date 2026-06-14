using Vintagestory.API.Server;

namespace nutritionPlannerVintageStoryMod.Config;

public class ConfigManager
{
    private const string FileName = "nutritionplanner.json";
    private readonly ICoreServerAPI _api;
    private ModConfigData _data = new();

    public ConfigManager(ICoreServerAPI api) => _api = api;

    public ModConfigData Data => _data;

    public void Load()
    {
        try   { _data = _api.LoadModConfig<ModConfigData>(FileName) ?? new ModConfigData(); }
        catch (Exception ex) { _api.Logger.Warning($"[NutritionPlanner] Config load failed: {ex.Message}"); _data = new ModConfigData(); }
    }

    public void Save()
    {
        try   { _api.StoreModConfig(_data, FileName); }
        catch (Exception ex) { _api.Logger.Warning($"[NutritionPlanner] Config save failed: {ex.Message}"); }
    }
}
