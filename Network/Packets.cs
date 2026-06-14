using ProtoBuf;

namespace nutritionPlannerVintageStoryMod.Network;

[ProtoContract]
public class HistoryPacket
{
    [ProtoMember(1)] public List<FoodEntryDto> Entries { get; set; } = [];
}

[ProtoContract]
public class FoodEntryDto
{
    [ProtoMember(1)] public string ItemCode    { get; set; } = "";
    [ProtoMember(2)] public long   GameTimestamp { get; set; }
    [ProtoMember(3)] public float  DeltaGrain   { get; set; }
    [ProtoMember(4)] public float  DeltaVeg     { get; set; }
    [ProtoMember(5)] public float  DeltaProtein { get; set; }
    [ProtoMember(6)] public float  DeltaDairy   { get; set; }
}

[ProtoContract]
public class SuggestRequestPacket
{
    // no payload — server uses the requesting player's state
}

[ProtoContract]
public class SuggestionPacket
{
    [ProtoMember(1)] public string Text   { get; set; } = "";
    [ProtoMember(2)] public string Source { get; set; } = "local"; // "local" | "chatai"
}

[ProtoContract]
public class ConfigSyncPacket
{
    [ProtoMember(1)] public float Threshold1 { get; set; } = 30f;
    [ProtoMember(2)] public float Threshold2 { get; set; } = 15f;
}
