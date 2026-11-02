using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BepInExLogAlerts.Core;

internal sealed class NotificationRuleStore
{
    private const string Header = "BepInExLogAlerts muted logs / 2", LegacyHeader = "BepInExLogAlerts muted logs / 1";
    private readonly string path;
    internal NotificationRuleStore(string path) { this.path = Path.GetFullPath(path); }
    internal NotificationRuleSet Load()
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > 1024 * 1024) throw new InvalidDataException("The notification suppression file exceeds 1 MiB: " + path);
            using var reader = new StreamReader(file, new UTF8Encoding(false, true), true);
            string? header = reader.ReadLine();
            bool warningEnabled = true, errorEnabled = true, fatalEnabled = true;
            if (header == Header)
            {
                warningEnabled = ReadEnabled(reader, "Warning");
                errorEnabled = ReadEnabled(reader, "Error");
                fatalEnabled = ReadEnabled(reader, "Fatal");
            }
            else if (header != LegacyHeader)
                throw new InvalidDataException("Unsupported notification suppression file header: " + path);
            var rules = new HashSet<string>(StringComparer.Ordinal);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!ValidId(line)) throw new InvalidDataException("Invalid notification suppression rule: " + path);
                rules.Add(line);
            }
            return new NotificationRuleSet(rules, warningEnabled, errorEnabled, fatalEnabled);
        }
        catch (FileNotFoundException) { return new NotificationRuleSet(Array.Empty<string>()); }
        catch (DirectoryNotFoundException) { return new NotificationRuleSet(Array.Empty<string>()); }
    }
    internal void Save(NotificationRuleSet rules)
    {
        if (rules.MessageIds.Any(r => !ValidId(r))) throw new InvalidDataException("Cannot save an invalid notification suppression rule.");
        string directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("The notification suppression directory is unavailable.");
        Directory.CreateDirectory(directory);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var writer = new StreamWriter(file, new UTF8Encoding(false), 1024, true))
                {
                    writer.WriteLine(Header);
                    writer.WriteLine("Warning=" + (rules.WarningEnabled ? "true" : "false"));
                    writer.WriteLine("Error=" + (rules.ErrorEnabled ? "true" : "false"));
                    writer.WriteLine("Fatal=" + (rules.FatalEnabled ? "true" : "false"));
                    foreach (string rule in rules.MessageIds) writer.WriteLine(rule);
                    writer.Flush();
                }
                if (file.Length > 1024 * 1024) throw new InvalidDataException("The notification suppression file would exceed 1 MiB: " + path);
                file.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        catch (Exception error)
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception cleanup) { throw new AggregateException("Suppression save failed, and the temporary file could not be removed.", error, cleanup); }
            throw;
        }
    }
    private bool ReadEnabled(TextReader reader, string level)
    {
        string? line = reader.ReadLine();
        if (line == level + "=true") return true;
        if (line == level + "=false") return false;
        throw new InvalidDataException("Invalid " + level + " notification setting: " + path);
    }
    private static bool ValidId(string value) => value.Length == 64 && value.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');
}
