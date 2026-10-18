# Trade Tracker

A Dalamud plugin for FFXIV that keeps a log of every player-to-player trade: who it was with, their world, gil given and received, and the items that changed hands.

- **People** tab: one row per player with trade count, gil given, gil received, net gil and last trade. Click a name to see every trade with them.
- **History** tab: every trade, newest first, searchable by player, world or item.
- **Export CSV** to open the log in Excel or Google Sheets.
- Optional one-line chat message after each trade.
- Cancelled trades are not logged. A trade only counts once your gil or inventory actually changes to match it.

## Install

1. In game, type `/xlsettings` and open the **Experimental** tab.
2. Under **Custom Plugin Repositories**, paste this into the empty row, click **+**, then **Save**:

   ```
   https://raw.githubusercontent.com/Stealthzi/TradeTracker/main/repo.json
   ```

3. Type `/xlplugins`, search for **Trade Tracker** and click **Install**.
4. Type `/trades` to open the log.

## Your data

The log stays on your PC at `%AppData%\XIVLauncher\pluginConfigs\TradeTracker\trades.json`. The plugin only reads the game's memory and never sends anything anywhere.

Third-party tools are against the FFXIV terms of service, like every Dalamud plugin. Use at your own risk.
