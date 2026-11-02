using System;
using System.Collections.Generic;
using System.Linq;

namespace BepInExLogAlerts.Core;

// One immutable snapshot keeps level and individual settings consistent for logging threads.
internal sealed class NotificationRuleSet
{
    private readonly HashSet<string> ids;
    internal bool WarningEnabled { get; }
    internal bool ErrorEnabled { get; }
    internal bool FatalEnabled { get; }
    internal IReadOnlyCollection<string> MessageIds { get; }
    internal int Count => ids.Count + (WarningEnabled ? 0 : 1) + (ErrorEnabled ? 0 : 1) + (FatalEnabled ? 0 : 1);
    internal NotificationRuleSet(IEnumerable<string> messageIds, bool warningEnabled = true, bool errorEnabled = true, bool fatalEnabled = true)
    {
        ids = new HashSet<string>(messageIds, StringComparer.Ordinal);
        MessageIds = Array.AsReadOnly(ids.OrderBy(x => x, StringComparer.Ordinal).ToArray());
        WarningEnabled = warningEnabled; ErrorEnabled = errorEnabled; FatalEnabled = fatalEnabled;
    }
    internal bool IsLevelEnabled(Severity level) => level switch
    {
        Severity.Warning => WarningEnabled,
        Severity.Error => ErrorEnabled,
        Severity.Fatal => FatalEnabled,
        _ => throw new ArgumentOutOfRangeException(nameof(level))
    };
    internal bool Contains(LogRecord record) => ids.Count != 0 && ids.Contains(record.NotificationId);
}
