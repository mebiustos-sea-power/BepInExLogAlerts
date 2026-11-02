using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace BepInExLogAlerts.Core;

internal sealed class NotificationRules
{
    private HashSet<string> suppressed;
    private readonly Action<IReadOnlyCollection<string>> persist;
    internal int Count => Volatile.Read(ref suppressed).Count;
    internal int Revision { get; private set; }
    internal NotificationRules(IEnumerable<string> initial, Action<IReadOnlyCollection<string>> persist)
    { suppressed = new HashSet<string>(initial, StringComparer.Ordinal); this.persist = persist; }
    internal bool IsSuppressed(LogRecord record)
    {
        // Readers can run on logging threads. Published sets are never mutated.
        HashSet<string> current = Volatile.Read(ref suppressed);
        return current.Count != 0 && current.Contains(record.NotificationId);
    }
    internal void SetSuppressed(LogRecord record, bool value)
    {
        string key = record.NotificationId;
        if (suppressed.Contains(key) == value) return;
        var next = new HashSet<string>(suppressed, StringComparer.Ordinal);
        if (value) next.Add(key); else next.Remove(key);
        Commit(next);
    }
    internal void Clear()
    {
        if (suppressed.Count != 0) Commit(new HashSet<string>(StringComparer.Ordinal));
    }
    private void Commit(HashSet<string> next)
    {
        // Apply only after durable storage succeeds. Failed saves must not leave
        // the checkbox disagreeing with the rules used on the next launch.
        persist(next.OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Volatile.Write(ref suppressed, next);
        Revision++;
    }
}
