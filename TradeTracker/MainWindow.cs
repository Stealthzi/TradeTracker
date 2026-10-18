using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace TradeTracker;

public sealed class MainWindow : Window
{
    private static readonly string[] SortNames = ["Last traded", "Most trades", "Most gil given", "Most gil received", "Name"];
    private static readonly Vector4 Green = new(0.45f, 0.85f, 0.45f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.45f, 0.45f, 1f);

    private readonly TradeStore store;
    private readonly Configuration config;

    private string search = string.Empty;
    private int sortMode;
    private bool jumpToHistory;

    public MainWindow(TradeStore store, Configuration config)
        : base("Trade Tracker###TradeTracker")
    {
        this.store = store;
        this.config = config;
        Size = new Vector2(780, 460);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(480, 260),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    private sealed record Person(
        string Key, string Name, string World, int Trades, long GilGiven, long GilReceived, int ItemsGiven, int ItemsReceived, DateTimeOffset Last)
    {
        public long Net => GilReceived - GilGiven;
    }

    public override void Draw()
    {
        var trades = Filtered();
        DrawToolbar(trades);
        ImGui.Separator();

        using var tabs = ImRaii.TabBar("##tabs");
        if (!tabs)
            return;

        using (var tab = ImRaii.TabItem("People"))
        {
            if (tab)
                DrawPeople(trades);
        }

        var historyFlags = jumpToHistory ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        jumpToHistory = false;
        if (ImGui.BeginTabItem("History", historyFlags))
        {
            DrawHistory(trades);
            ImGui.EndTabItem();
        }
    }

    private List<TradeRecord> Filtered()
    {
        if (string.IsNullOrWhiteSpace(search))
            return store.Trades;

        var q = search.Trim();
        return store.Trades.Where(t =>
                t.PartnerName.Contains(q, StringComparison.OrdinalIgnoreCase)
                || t.PartnerWorld.Contains(q, StringComparison.OrdinalIgnoreCase)
                || t.MyCharacter.Contains(q, StringComparison.OrdinalIgnoreCase)
                || t.ItemsGiven.Concat(t.ItemsReceived).Any(i => i.Name.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private void DrawToolbar(List<TradeRecord> trades)
    {
        ImGui.SetNextItemWidth(220 * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("##search", "Search player, world or item", ref search, 64);
        if (search.Length > 0)
        {
            ImGui.SameLine();
            if (ImGui.Button("Clear"))
                search = string.Empty;
        }

        ImGui.SameLine();
        if (ImGui.Button("Export CSV"))
        {
            var path = store.ExportCsv();
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }

        ImGui.SameLine();
        var print = config.PrintToChat;
        if (ImGui.Checkbox("Announce trades in chat", ref print))
        {
            config.PrintToChat = print;
            config.Save();
        }

        var people = trades.Select(t => t.PartnerKey).Distinct().Count();
        ImGui.TextUnformatted(
            $"{trades.Count:N0} trades with {people:N0} people   |   gave {trades.Sum(t => t.GilGiven):N0} gil   |   received {trades.Sum(t => t.GilReceived):N0} gil");
    }

    private void DrawPeople(List<TradeRecord> trades)
    {
        ImGui.SetNextItemWidth(160 * ImGuiHelpers.GlobalScale);
        using (var combo = ImRaii.Combo("Sort by", SortNames[sortMode]))
        {
            if (combo)
            {
                for (var i = 0; i < SortNames.Length; i++)
                {
                    if (ImGui.Selectable(SortNames[i], i == sortMode))
                        sortMode = i;
                }
            }
        }

        var people = trades
            .GroupBy(t => t.PartnerKey)
            .Select(g => new Person(
                g.Key,
                g.First().PartnerName,
                g.First().PartnerWorld,
                g.Count(),
                g.Sum(t => t.GilGiven),
                g.Sum(t => t.GilReceived),
                g.Sum(t => t.ItemsGiven.Sum(i => i.Quantity)),
                g.Sum(t => t.ItemsReceived.Sum(i => i.Quantity)),
                g.Max(t => t.Time)));

        people = sortMode switch
        {
            1 => people.OrderByDescending(p => p.Trades),
            2 => people.OrderByDescending(p => p.GilGiven),
            3 => people.OrderByDescending(p => p.GilReceived),
            4 => people.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
            _ => people.OrderByDescending(p => p.Last),
        };

        using var table = ImRaii.Table("##people", 8,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable);
        if (!table)
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Player");
        ImGui.TableSetupColumn("World");
        ImGui.TableSetupColumn("Trades");
        ImGui.TableSetupColumn("Gil given");
        ImGui.TableSetupColumn("Gil received");
        ImGui.TableSetupColumn("Net gil");
        ImGui.TableSetupColumn("Items given / got");
        ImGui.TableSetupColumn("Last trade");
        ImGui.TableHeadersRow();

        foreach (var p in people)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (ImGui.Selectable($"{p.Name}##{p.Key}", false, ImGuiSelectableFlags.SpanAllColumns))
            {
                search = p.Name;
                jumpToHistory = true;
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show every trade with this player");

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(p.World);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{p.Trades:N0}");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{p.GilGiven:N0}");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{p.GilReceived:N0}");
            ImGui.TableNextColumn();
            using (ImRaii.PushColor(ImGuiCol.Text, p.Net >= 0 ? Green : Red, p.Net != 0))
                ImGui.TextUnformatted($"{p.Net:+#,0;-#,0;0}");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{p.ItemsGiven:N0} / {p.ItemsReceived:N0}");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(p.Last.LocalDateTime.ToString("yyyy-MM-dd HH:mm"));
        }
    }

    private void DrawHistory(List<TradeRecord> trades)
    {
        var showMine = trades.Select(t => t.MyCharacter).Distinct().Count() > 1;
        using var table = ImRaii.Table("##history", showMine ? 6 : 5,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable);
        if (!table)
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("When", ImGuiTableColumnFlags.WidthFixed);
        if (showMine)
            ImGui.TableSetupColumn("Your character", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Player", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("You gave", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("You got", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(string.Empty, ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableHeadersRow();

        TradeRecord? toDelete = null;
        foreach (var t in trades.OrderByDescending(t => t.Time))
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(t.Time.LocalDateTime.ToString("yyyy-MM-dd HH:mm"));
            if (showMine)
            {
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(t.MyCharacter);
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(t.PartnerKey);
            ImGui.TableNextColumn();
            Wrapped(t.DescribeGiven());
            ImGui.TableNextColumn();
            Wrapped(t.DescribeReceived());
            ImGui.TableNextColumn();
            if (ImGui.SmallButton($"Delete##{t.Id}") && ImGui.GetIO().KeyCtrl)
                toDelete = t;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Hold Ctrl and click to delete this entry");
        }

        if (toDelete != null)
            store.Remove(toDelete);
    }

    // Names come from the game, so never pass them as an ImGui format string; wrap unformatted text instead.
    private static void Wrapped(string text)
    {
        ImGui.PushTextWrapPos(0);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }
}
