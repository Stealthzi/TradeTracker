using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace TradeTracker;

public sealed class TradedItem
{
    public uint ItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Hq { get; set; }
    public int Quantity { get; set; }

    public override string ToString() => $"{Quantity:N0}x {Name}{(Hq ? " (HQ)" : string.Empty)}";
}

public sealed class TradeRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Time { get; set; }
    public string MyCharacter { get; set; } = string.Empty;
    public string PartnerName { get; set; } = string.Empty;
    public string PartnerWorld { get; set; } = string.Empty;
    public long GilGiven { get; set; }
    public long GilReceived { get; set; }
    public List<TradedItem> ItemsGiven { get; set; } = [];
    public List<TradedItem> ItemsReceived { get; set; } = [];

    [JsonIgnore]
    public string PartnerKey => string.IsNullOrEmpty(PartnerWorld) ? PartnerName : $"{PartnerName}@{PartnerWorld}";

    [JsonIgnore]
    public bool IsEmpty => GilGiven == 0 && GilReceived == 0 && ItemsGiven.Count == 0 && ItemsReceived.Count == 0;

    public static string Describe(long gil, List<TradedItem> items)
    {
        var parts = new List<string>();
        if (gil > 0)
            parts.Add($"{gil:N0} gil");
        parts.AddRange(items.Select(i => i.ToString()));
        return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
    }

    public string DescribeGiven() => Describe(GilGiven, ItemsGiven);
    public string DescribeReceived() => Describe(GilReceived, ItemsReceived);
}
