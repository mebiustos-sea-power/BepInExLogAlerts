using System.Collections.Generic;
using System.Linq;

namespace BepInExLogAlerts.Core;

internal sealed class NotificationCenter
{
    internal readonly AlertJournal Journal;
    internal readonly Toasts Toasts;
    internal readonly NotificationRules Rules;
    internal NotificationCenter(int historyLimit, double repeatSeconds, double warningSeconds, double errorSeconds, NotificationRules rules)
    {
        Rules = rules;
        Journal = new AlertJournal(historyLimit, repeatSeconds);
        Toasts = new Toasts(warningSeconds, errorSeconds, rules);
    }
    internal void Receive(LogRecord record, double now)
    {
        AlertItem item = Journal.Add(record, now);
        if (record.NotificationEligible) Toasts.Show(item, record.Count);
    }
    internal void PublishStartup(IEnumerable<LogRecord> records, double now)
    {
        var batch = records.ToList();
        foreach (LogRecord record in batch) Journal.Add(record, now);
        Toasts.ShowStartup(batch);
    }
    internal void Tick(double now) => Toasts.Tick(now);
}
