namespace nutritionPlannerVintageStoryMod.Config;

public class ModConfigData
{
    public bool  Enabled             { get; set; } = true;
    public float Threshold1          { get; set; } = 30f; // % — bar pulses red
    public float Threshold2          { get; set; } = 15f; // % — chat message fires
    public int   ChatCooldownSeconds { get; set; } = 300; // min gap between chat messages per nutrient
    public int   PollIntervalMs      { get; set; } = 2000;// server-side nutrition poll interval
}
