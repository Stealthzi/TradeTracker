using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using ItemSheet = Lumina.Excel.Sheets.Item;

namespace TradeTracker;

/// <summary>
/// Watches the game's trade state. While a trade window is open it keeps reading what both sides have put up.
/// When the window closes it waits for your inventory to change in a way that matches the offer, and only then
/// logs the trade. Cancelled trades never touch the inventory, so they are dropped.
/// </summary>
public sealed unsafe class TradeWatcher : IDisposable
{
    private const uint GilItemId = 1;
    private const int ItemSlots = 5; // the 6th trade slot holds gil

    // InventoryManager.TradeState values
    private const int SelectingTradeGoods = 3;
    private const int LockedIn = 4;
    private const int Confirmed = 6;

    // Inventory updates arrive just after the window closes; past this, assume the trade was cancelled.
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SettlePollInterval = TimeSpan.FromMilliseconds(250);

    private static readonly InventoryType[] CountedInventories =
    [
        InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4,
        InventoryType.Crystals, InventoryType.Currency,
        InventoryType.ArmoryMainHand, InventoryType.ArmoryOffHand, InventoryType.ArmoryHead, InventoryType.ArmoryBody,
        InventoryType.ArmoryHands, InventoryType.ArmoryLegs, InventoryType.ArmoryFeets, InventoryType.ArmoryEar,
        InventoryType.ArmoryNeck, InventoryType.ArmoryWrist, InventoryType.ArmoryRings,
    ];

    private readonly TradeStore store;
    private readonly Configuration config;

    private bool trading;
    private bool settling;
    private TradeRecord? offer;
    private Dictionary<(uint ItemId, bool Hq), long>? countsAtStart;
    private DateTime closedAt;
    private DateTime nextPoll;
    private bool loggedError;

    public TradeWatcher(TradeStore store, Configuration config)
    {
        this.store = store;
        this.config = config;
        Plugin.Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnUpdate;
    }

    private void OnUpdate(IFramework framework)
    {
        try
        {
            Tick();
        }
        catch (Exception ex)
        {
            if (!loggedError)
                Plugin.Log.Error(ex, "Trade watcher failed");
            loggedError = true;
            trading = settling = false;
            offer = null;
        }
    }

    private void Tick()
    {
        var im = InventoryManager.Instance();
        if (im == null || !Plugin.PlayerState.IsLoaded)
            return;

        if (IsTradeOpen(im))
        {
            if (!trading)
            {
                if (settling)
                    Settle(im, giveUp: true);

                trading = true;
                offer = null;
                countsAtStart = CountInventory(im);
            }

            // Once both sides lock in, the offer can't change any more. Stop re-reading at that point so a
            // buffer the game clears a frame before closing the window can't wipe out what we saw.
            var bothLocked = (int)im->TradeLocalState >= LockedIn && (int)im->TradeRemoteState >= LockedIn;
            if (offer == null || !bothLocked)
                offer = ReadOffer(im);
            return;
        }

        if (trading)
        {
            trading = false;
            settling = true;
            closedAt = nextPoll = DateTime.UtcNow;
        }

        if (settling && DateTime.UtcNow >= nextPoll)
        {
            nextPoll = DateTime.UtcNow + SettlePollInterval;
            Settle(im, giveUp: DateTime.UtcNow - closedAt > SettleTimeout);
        }
    }

    private static bool IsTradeOpen(InventoryManager* im) =>
        (int)im->TradeLocalState is >= SelectingTradeGoods and <= Confirmed;

    private void Settle(InventoryManager* im, bool giveUp)
    {
        if (offer == null || countsAtStart == null || offer.IsEmpty)
        {
            settling = false;
            return;
        }

        if (WentThrough(offer, countsAtStart, CountInventory(im)))
        {
            Record(offer);
            settling = false;
        }
        else if (giveUp)
        {
            Plugin.Log.Debug($"Trade with {offer.PartnerName} was cancelled, not logging it");
            settling = false;
        }
    }

    private static bool WentThrough(TradeRecord offer, Dictionary<(uint, bool), long> before, Dictionary<(uint, bool), long> after)
    {
        long Change(uint itemId, bool hq) => after.GetValueOrDefault((itemId, hq)) - before.GetValueOrDefault((itemId, hq));

        var expectedGil = offer.GilReceived - offer.GilGiven;
        return (expectedGil != 0 && Change(GilItemId, false) == expectedGil)
            || offer.ItemsGiven.Any(i => Change(i.ItemId, i.Hq) < 0)
            || offer.ItemsReceived.Any(i => Change(i.ItemId, i.Hq) > 0);
    }

    private void Record(TradeRecord trade)
    {
        trade.Time = DateTimeOffset.Now;
        trade.MyCharacter = MyCharacter();
        store.Add(trade);

        if (config.PrintToChat)
            Plugin.ChatGui.Print($"Traded with {trade.PartnerName}: gave {trade.DescribeGiven()}, got {trade.DescribeReceived()}.", "Trades");
    }

    private static TradeRecord ReadOffer(InventoryManager* im)
    {
        var offer = new TradeRecord { PartnerName = im->TradePartnerNameString };

        if (Plugin.ObjectTable.SearchByEntityId(im->TradePartnerEntityId) is IPlayerCharacter partner)
        {
            if (string.IsNullOrEmpty(offer.PartnerName))
                offer.PartnerName = partner.Name.TextValue;
            offer.PartnerWorld = partner.HomeWorld.ValueNullable?.Name.ExtractText() ?? string.Empty;
        }

        if (string.IsNullOrEmpty(offer.PartnerName))
            offer.PartnerName = "Unknown";

        var local = im->TradeItemsLocal;
        var remote = im->TradeItemsRemote;
        for (var i = 0; i < ItemSlots; i++)
        {
            AddItem(offer.ItemsGiven, (InventoryItem*)Unsafe.AsPointer(ref local[i]), followLink: true);
            AddItem(offer.ItemsReceived, (InventoryItem*)Unsafe.AsPointer(ref remote[i]), followLink: false);
        }

        offer.GilGiven = Math.Max(0, local[ItemSlots].Quantity);
        offer.GilReceived = Math.Max(0, remote[ItemSlots].Quantity);
        return offer;
    }

    private static void AddItem(List<TradedItem> list, InventoryItem* slot, bool followLink)
    {
        // Your own offered items can point at the bag slot they came from instead of holding a copy.
        var source = slot;
        for (var hops = 0; followLink && source != null && source->IsSymbolic && hops < 4; hops++)
            source = source->GetLinkedItem();

        if (source == null || source->IsSymbolic || source->ItemId == 0)
            return;

        var quantity = slot->Quantity > 0 ? slot->Quantity : source->Quantity;
        if (quantity <= 0)
            return;

        var (itemId, hq) = Key(source);
        list.Add(new TradedItem
        {
            ItemId = itemId,
            Hq = hq,
            Quantity = quantity,
            Name = ItemName(itemId),
        });
    }

    private static Dictionary<(uint ItemId, bool Hq), long> CountInventory(InventoryManager* im)
    {
        var counts = new Dictionary<(uint ItemId, bool Hq), long>();
        foreach (var type in CountedInventories)
        {
            var container = im->GetInventoryContainer(type);
            if (container == null || !container->IsLoaded)
                continue;

            for (var i = 0; i < container->Size; i++)
            {
                var item = container->Items + i;
                if (item->IsSymbolic || item->ItemId == 0)
                    continue;

                var key = Key(item);
                counts[key] = counts.GetValueOrDefault(key) + item->Quantity;
            }
        }

        return counts;
    }

    /// <summary>Base item id plus HQ, whether HQ is stored in the flags or as the +1,000,000 id offset.</summary>
    private static (uint ItemId, bool Hq) Key(InventoryItem* item)
    {
        var id = item->ItemId;
        var hq = item->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality);
        if (id is > 1_000_000 and < 2_000_000)
            return (id - 1_000_000, true);
        if (id is > 500_000 and < 1_000_000)
            id -= 500_000; // collectable
        return (id, hq);
    }

    private static string ItemName(uint itemId)
    {
        if (itemId == GilItemId)
            return "Gil";
        var name = Plugin.DataManager.GetExcelSheet<ItemSheet>().GetRowOrDefault(itemId)?.Name.ExtractText();
        return string.IsNullOrEmpty(name) ? $"Item #{itemId}" : name;
    }

    private static string MyCharacter()
    {
        var name = Plugin.PlayerState.CharacterName;
        var world = Plugin.PlayerState.HomeWorld.ValueNullable?.Name.ExtractText();
        return string.IsNullOrEmpty(world) ? name : $"{name}@{world}";
    }
}
