using System;
using System.Collections.Generic;

namespace BepInExLogAlerts.Core;

internal sealed class LogMailbox
{
    private readonly object gate = new object();
    private readonly Queue<LogRecord> pending = new Queue<LogRecord>();
    private readonly List<LogRecord> startup = new List<LogRecord>();
    private readonly int capacity;
    private readonly NotificationRules rules;
    private bool sealedStartup;
    private long dropped;
    internal LogMailbox(int capacity, NotificationRules rules) { if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity)); this.capacity = capacity; this.rules = rules; }
    internal void Post(Severity level, string source, string message, DateTimeOffset time)
    {
        lock (gate)
        {
            if (pending.Count + startup.Count >= capacity) { dropped++; return; }
            var record = new LogRecord(level, source, message, !sealedStartup, time);
            // Keep collecting muted events, but remember eligibility at receipt so
            // a later unmute cannot replay an old event still waiting in the queue.
            if (rules.IsSuppressed(record)) record.SuppressNotification();
            if (sealedStartup) pending.Enqueue(record); else startup.Add(record);
        }
    }

    internal T SealStartup<T>(Func<T> snapshot, out List<LogRecord> captured)
    {
        lock (gate)
        {
            if (sealedStartup) throw new InvalidOperationException("Startup was already sealed.");
            // Freeze the file prefix before changing phase. An event delivered before
            // this gate has already passed BepInEx's disk listener and is in the prefix.
            T value = snapshot();
            captured = new List<LogRecord>(startup);
            startup.Clear(); sealedStartup = true;
            return value;
        }
    }

    internal List<LogRecord> Drain(int maximum)
    {
        lock (gate)
        {
            var result = new List<LogRecord>();
            while (result.Count < maximum && pending.Count != 0) result.Add(pending.Dequeue());
            return result;
        }
    }
    internal long TakeDropped() { lock (gate) { long value = dropped; dropped = 0; return value; } }
}
