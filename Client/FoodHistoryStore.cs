using System.Text.Json;

namespace nutritionPlannerVintageStoryMod.Client;

public record FoodEntry(
    string ItemCode,
    long   GameTimestamp,
    float  DeltaGrain,
    float  DeltaVeg,
    float  DeltaProtein,
    float  DeltaDairy);

public class FoodHistoryStore
{
    private readonly int _maxEntries;
    private readonly List<FoodEntry> _entries = [];

    public FoodHistoryStore(int maxEntries = 20) => _maxEntries = maxEntries;

    public IReadOnlyList<FoodEntry> Entries => _entries;

    public void Add(FoodEntry entry)
    {
        _entries.Add(entry);
        if (_entries.Count > _maxEntries)
            _entries.RemoveAt(0);
    }

    public void Clear() => _entries.Clear();

    public byte[] Serialize() =>
        JsonSerializer.SerializeToUtf8Bytes(_entries);

    public static FoodHistoryStore Deserialize(byte[] data)
    {
        var store = new FoodHistoryStore();
        if (data is not { Length: > 0 }) return store;
        try
        {
            var entries = JsonSerializer.Deserialize<List<FoodEntry>>(data) ?? [];
            foreach (var e in entries) store.Add(e);
        }
        catch (JsonException) { }
        return store;
    }
}
