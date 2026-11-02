using System;
using System.Collections.Generic;

namespace BepInExLogAlerts.Core;

internal sealed class AlertItem
{
    internal readonly LogRecord Record;
    internal int Count;
    internal double LastSeen;
    internal AlertItem(LogRecord record, double now) { Record = record; Count = record.Count; LastSeen = now; }
}

internal sealed class AlertJournal
{
    internal readonly List<AlertItem> Items = new List<AlertItem>();
    internal int Evicted { get; private set; }
    private readonly int capacity;
    private readonly double repeatWindow;
    internal AlertJournal(int capacity, double repeatWindow) { this.capacity = capacity; this.repeatWindow = repeatWindow; }
    internal AlertItem Add(LogRecord record, double now)
    {
        var key = new LogKey(record);
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            AlertItem item = Items[i];
            if (item.Record.Startup == record.Startup && now - item.LastSeen <= repeatWindow && new LogKey(item.Record).Equals(key))
            { item.Count += record.Count; item.LastSeen = now; return item; }
        }
        var added = new AlertItem(record, now);
        if (Items.Count == capacity) { Items.RemoveAt(0); Evicted++; }
        Items.Add(added);
        return added;
    }
}

internal sealed class Toast
{
    internal readonly AlertItem? Item;
    private readonly List<LogRecord>? startup;
    internal bool StartupSummary => startup != null;
    internal int Warnings { get; private set; }
    internal int Errors { get; private set; }
    internal int Count { get; set; }
    internal double? RenderedAt;
    internal Severity Level => StartupSummary ? (Errors != 0 ? Severity.Error : Severity.Warning) : Item!.Record.Level;
    internal Toast(AlertItem item, int count) { Item = item; Count = count; }
    internal Toast(IEnumerable<LogRecord> records) { startup = new List<LogRecord>(records); }
    internal bool ApplyRules(NotificationRules rules)
    {
        if (startup == null) return !rules.IsSuppressed(Item!.Record);
        // Keep only contributions that are still eligible. Re-enabling a rule must
        // not add old occurrences back to a summary already on screen.
        startup.RemoveAll(rules.IsSuppressed);
        Warnings = Errors = 0;
        foreach (LogRecord record in startup)
            if (record.Level == Severity.Warning) Warnings += record.Count; else Errors += record.Count;
        return Warnings + Errors != 0;
    }
}

internal sealed class Toasts
{
    internal readonly List<Toast> Visible = new List<Toast>();
    private readonly double warningSeconds, errorSeconds;
    private readonly NotificationRules rules;
    private int ruleRevision = -1;
    internal Toasts(double warningSeconds, double errorSeconds, NotificationRules rules)
    { this.warningSeconds = warningSeconds; this.errorSeconds = errorSeconds; this.rules = rules; }
    internal void Show(AlertItem item, int occurrences = 1)
    {
        if (rules.IsSuppressed(item.Record)) return;
        foreach (Toast toast in Visible)
            if (ReferenceEquals(toast.Item, item)) { toast.Count += occurrences; return; }
        Add(new Toast(item, occurrences));
    }
    internal void ShowStartup(IEnumerable<LogRecord> records)
    {
        var toast = new Toast(records);
        if (toast.ApplyRules(rules)) Add(toast);
    }
    private void Add(Toast toast)
    {
        if (Visible.Count == 2) Visible.RemoveAt(0);
        Visible.Add(toast);
    }
    internal void Rendered(Toast toast, double now) { if (!toast.RenderedAt.HasValue) toast.RenderedAt = now; }
    internal void Tick(double now)
    {
        if (ruleRevision != rules.Revision)
        {
            Visible.RemoveAll(t => !t.ApplyRules(rules));
            ruleRevision = rules.Revision;
        }
        Visible.RemoveAll(t => t.RenderedAt.HasValue && now - t.RenderedAt.Value >= (t.Level == Severity.Warning ? warningSeconds : errorSeconds));
    }
}
