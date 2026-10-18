using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace TradeTracker;

/// <summary>Trade history, saved as trades.json in the plugin's config folder.</summary>
public sealed class TradeStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string path;

    public TradeStore(string directory)
    {
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "trades.json");
        Load();
    }

    public string Directory { get; }

    public List<TradeRecord> Trades { get; private set; } = [];

    public void Add(TradeRecord record)
    {
        Trades.Add(record);
        Save();
    }

    public void Remove(TradeRecord record)
    {
        Trades.Remove(record);
        Save();
    }

    public string ExportCsv()
    {
        var csvPath = Path.Combine(Directory, "trades.csv");
        var sb = new StringBuilder();
        sb.AppendLine("Time,My Character,Partner,World,Gil Given,Gil Received,Items Given,Items Received");
        foreach (var t in Trades.OrderBy(t => t.Time))
        {
            sb.AppendJoin(',',
                Csv(t.Time.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss")),
                Csv(t.MyCharacter),
                Csv(t.PartnerName),
                Csv(t.PartnerWorld),
                t.GilGiven,
                t.GilReceived,
                Csv(string.Join("; ", t.ItemsGiven)),
                Csv(string.Join("; ", t.ItemsReceived)));
            sb.AppendLine();
        }

        File.WriteAllText(csvPath, sb.ToString(), new UTF8Encoding(true));
        return csvPath;
    }

    private static string Csv(string value)
    {
        // A cell starting with = + - @ would run as a formula when opened in Excel or Sheets.
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0]))
            value = "'" + value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private void Load()
    {
        if (!File.Exists(path))
            return;

        try
        {
            Trades = JsonSerializer.Deserialize<List<TradeRecord>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception ex)
        {
            // Keep the unreadable file around rather than overwriting someone's history on the next save.
            var backup = $"{path}.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}";
            try
            {
                File.Copy(path, backup, true);
                Plugin.Log.Error(ex, $"Could not read trades.json, copied it to {backup} and started a new log");
            }
            catch (Exception copyEx)
            {
                Plugin.Log.Error(copyEx, "Could not read trades.json or back it up");
            }
        }
    }

    private void Save()
    {
        try
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Trades, JsonOptions));
            File.Move(tmp, path, true);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Could not save trades.json");
        }
    }
}
