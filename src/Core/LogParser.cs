using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BepInExLogAlerts.Core;

internal static class LogParser
{
    internal static List<LogRecord> Parse(TextReader reader, bool includeUnity)
    {
        var result = new List<LogRecord>();
        Severity? level = null;
        string source = "";
        var body = new StringBuilder();
        void Finish()
        {
            if (level.HasValue && (includeUnity || source != "Unity Log") && source != "BepInEx Log Alerts")
                result.Add(new LogRecord(level.Value, source, body.ToString().TrimEnd('\r', '\n'), true));
            body.Clear();
        }
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (Header(line, out string tag, out string nextSource, out string text))
            {
                Finish();
                level = Level(tag);
                source = nextSource;
                if (level.HasValue) body.Append(text);
            }
            else if (level.HasValue)
            {
                if (body.Length != 0) body.Append('\n');
                body.Append(line);
            }
        }
        Finish();
        return result;
    }

    private static Severity? Level(string value)
    {
        Severity? result = null;
        foreach (string item in value.Split(','))
        {
            string tag = item.Trim();
            if (tag == "Fatal" || tag == "All") return Severity.Fatal;
            if (tag == "Error") result = Severity.Error;
            else if (tag == "Warning" && result == null) result = Severity.Warning;
        }
        return result;
    }

    private static bool Header(string line, out string level, out string source, out string text)
    {
        level = source = text = "";
        if (!line.StartsWith("[", StringComparison.Ordinal)) return false;
        int colon = line.IndexOf(':'), end = line.IndexOf(']');
        if (colon < 2 || end < colon) return false;
        level = line.Substring(1, colon - 1).Trim();
        foreach (string item in level.Split(','))
        {
            string tag = item.Trim();
            if (tag != "Fatal" && tag != "Error" && tag != "Warning" && tag != "Info" && tag != "Message" && tag != "Debug" && tag != "None" && tag != "All") return false;
        }
        source = line.Substring(colon + 1, end - colon - 1).Trim();
        text = line.Substring(end + 1);
        if (text.StartsWith(" ", StringComparison.Ordinal)) text = text.Substring(1);
        return true;
    }
}
