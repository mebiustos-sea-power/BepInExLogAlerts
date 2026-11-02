using System;
using System.Collections.Generic;
using System.Threading;

namespace BepInExLogAlerts.Core;

internal sealed class NotificationRules
{
    private NotificationRuleSet current;
    private readonly Action<NotificationRuleSet> persist;
    internal int Count => Volatile.Read(ref current).Count;
    internal bool IsLevelEnabled(Severity level) => Volatile.Read(ref current).IsLevelEnabled(level);
    internal int Revision { get; private set; }
    internal NotificationRules(NotificationRuleSet initial, Action<NotificationRuleSet> persist)
    { current = initial; this.persist = persist; }
    internal bool IsSuppressed(LogRecord record)
    {
        // Readers can run on logging threads. Published snapshots are immutable.
        NotificationRuleSet snapshot = Volatile.Read(ref current);
        return !snapshot.IsLevelEnabled(record.Level) || snapshot.Contains(record);
    }
    internal bool IsIndividuallySuppressed(LogRecord record) => Volatile.Read(ref current).Contains(record);
    internal void SetLevelEnabled(Severity level, bool enabled)
    {
        NotificationRuleSet snapshot = Volatile.Read(ref current);
        if (snapshot.IsLevelEnabled(level) == enabled) return;
        Commit(new NotificationRuleSet(snapshot.MessageIds,
            level == Severity.Warning ? enabled : snapshot.WarningEnabled,
            level == Severity.Error ? enabled : snapshot.ErrorEnabled,
            level == Severity.Fatal ? enabled : snapshot.FatalEnabled));
    }
    internal void SetSuppressed(LogRecord record, bool value)
    {
        NotificationRuleSet snapshot = Volatile.Read(ref current);
        if (snapshot.Contains(record) == value) return;
        var next = new HashSet<string>(snapshot.MessageIds, StringComparer.Ordinal);
        if (value) next.Add(record.NotificationId); else next.Remove(record.NotificationId);
        Commit(new NotificationRuleSet(next, snapshot.WarningEnabled, snapshot.ErrorEnabled, snapshot.FatalEnabled));
    }
    internal void Clear()
    {
        if (Count != 0) Commit(new NotificationRuleSet(Array.Empty<string>()));
    }
    private void Commit(NotificationRuleSet next)
    {
        // Apply only after durable storage succeeds. Failed saves must not leave
        // the checkbox disagreeing with the rules used on the next launch.
        persist(next);
        Volatile.Write(ref current, next);
        Revision++;
    }
}
