using System;
using System.Collections.Generic;

namespace BepInExLogAlerts.Core;

internal static class StartupMerge
{
    // The disk snapshot contains the startup events already received by our listener.
    // Reconcile occurrence counts, rather than removing every matching message: repeats
    // that happened before subscription must remain visible in the final count.
    internal static List<LogRecord> Merge(IEnumerable<LogRecord> disk, IEnumerable<LogRecord> captured)
    {
        var order = new List<LogKey>();
        var records = new Dictionary<LogKey, LogRecord>();
        var fileCounts = new Dictionary<LogKey, int>();
        var liveCounts = new Dictionary<LogKey, int>();
        void Add(IEnumerable<LogRecord> input, Dictionary<LogKey, int> counts)
        {
            foreach (LogRecord record in input)
            {
                var key = new LogKey(record);
                if (!records.ContainsKey(key)) { records.Add(key, record); order.Add(key); }
                counts.TryGetValue(key, out int count);
                counts[key] = count + record.Count;
            }
        }
        Add(disk, fileCounts); Add(captured, liveCounts);
        var result = new List<LogRecord>();
        foreach (LogKey key in order)
        {
            fileCounts.TryGetValue(key, out int a); liveCounts.TryGetValue(key, out int b);
            LogRecord record = records[key];
            result.Add(new LogRecord(record.Level, record.Source, record.Message, true, record.Time, Math.Max(a, b)));
        }
        return result;
    }
}
