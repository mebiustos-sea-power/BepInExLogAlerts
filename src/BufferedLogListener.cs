using System;
using System.Threading;
using BepInEx.Logging;
using BepInExLogAlerts.Core;

namespace BepInExLogAlerts;

internal sealed class BufferedLogListener : ILogListener
{
    private readonly LogMailbox mailbox;
    private readonly bool includeUnity;
    private int stopped;
    private Exception? failure;
    internal BufferedLogListener(LogMailbox mailbox, bool includeUnity) { this.mailbox = mailbox; this.includeUnity = includeUnity; }
    public void LogEvent(object sender, LogEventArgs args)
    {
        if (Volatile.Read(ref stopped) != 0) return;
        try
        {
            if ((args.Level & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) == 0) return;
            string source = args.Source?.SourceName ?? "Unknown";
            if (source == ModInfo.Name || (!includeUnity && source == "Unity Log")) return;
            Severity level = (args.Level & LogLevel.Fatal) != 0 ? Severity.Fatal : (args.Level & LogLevel.Error) != 0 ? Severity.Error : Severity.Warning;
            mailbox.Post(level, source, args.Data?.ToString() ?? "", DateTimeOffset.Now);
        }
        catch (Exception error)
        {
            // BepInEx calls listeners synchronously and does not isolate exceptions.
            // Report from Update, outside this callback, to prevent recursion.
            Interlocked.CompareExchange(ref failure, error, null);
            Interlocked.Exchange(ref stopped, 1);
        }
    }
    internal Exception? TakeFailure() => Interlocked.Exchange(ref failure, null);
    public void Dispose() => Interlocked.Exchange(ref stopped, 1);
}
