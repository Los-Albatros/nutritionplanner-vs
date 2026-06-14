namespace nutritionPlannerVintageStoryMod.Client;

public record NutrientValues(float Grain, float Veg, float Protein, float Dairy, float Max)
{
    private float Pct(float v) => Max > 0 ? v / Max * 100f : 0f;
    public float GrainPct   => Pct(Grain);
    public float VegPct     => Pct(Veg);
    public float ProteinPct => Pct(Protein);
    public float DairyPct   => Pct(Dairy);

    public IEnumerable<string> OrderedByNeed() =>
        new[] { ("Grain", GrainPct), ("Veg", VegPct), ("Protein", ProteinPct), ("Dairy", DairyPct) }
        .OrderBy(x => x.Item2)
        .Select(x => x.Item1);
}

public record InventoryFood(string ItemCode, string NutrientCategory);

public static class NutritionFallback
{
    public static string? Suggest(NutrientValues values, IEnumerable<InventoryFood> inventory)
    {
        var list = inventory.ToList();
        if (list.Count == 0) return null;

        foreach (var need in values.OrderedByNeed())
        {
            var match = list.FirstOrDefault(
                i => string.Equals(i.NutrientCategory, need, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return $"{match.ItemCode} (+{need})";
        }
        return null;
    }
}
