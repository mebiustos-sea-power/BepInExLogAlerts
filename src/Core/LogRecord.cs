using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BepInExLogAlerts.Core;

internal enum Severity { Warning, Error, Fatal }

internal sealed class LogRecord
{
    internal readonly Severity Level;
    internal readonly string Source, Message;
    internal readonly DateTimeOffset? Time;
    internal readonly bool Startup;
    internal readonly int Count;
    internal bool NotificationEligible { get; private set; } = true;
    internal void SuppressNotification() => NotificationEligible = false;
    private string? notificationId;
    internal string NotificationId
    {
        get
        {
            if (notificationId != null) return notificationId;
            // Length-prefix the source so different source/message boundaries cannot
            // produce the same key. Time, repeat count and startup/live phase are excluded.
            string value = ((int)Level).ToString(CultureInfo.InvariantCulture) + ":" + Source.Length.ToString(CultureInfo.InvariantCulture) + ":" + Source + Message;
            using var hash = SHA256.Create();
            return notificationId = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
    }

    internal LogRecord(Severity level, string source, string message, bool startup, DateTimeOffset? time = null, int count = 1)
    {
        Level = level; Source = source.Trim();
        message = message.Replace("\r\n", "\n").TrimEnd('\r', '\n');
        Message = message.Length <= 16384 ? message : message.Substring(0, 16384) + "\n[message truncated at 16384 characters]";
        Startup = startup; Time = time; Count = count;
    }

    internal string Summary(int length = 180)
    {
        foreach (string line in Message.Split('\n'))
        {
            string value = line.Trim();
            if (value.Length != 0) return value.Length <= length ? value : value.Substring(0, length) + "…";
        }
        return "—";
    }
}

internal readonly struct LogKey : IEquatable<LogKey>
{
    private readonly Severity level;
    private readonly string source, message;
    internal LogKey(LogRecord value) { level = value.Level; source = value.Source; message = value.Message; }
    public bool Equals(LogKey other) => level == other.level && source == other.source && message == other.message;
    public override bool Equals(object? other) => other is LogKey value && Equals(value);
    public override int GetHashCode() => ((int)level * 397 ^ source.GetHashCode()) * 397 ^ message.GetHashCode();
}
