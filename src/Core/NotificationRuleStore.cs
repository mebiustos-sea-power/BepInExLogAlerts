using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BepInExLogAlerts.Core;

internal sealed class NotificationRuleStore
{
    private const string Header = "BepInExLogAlerts muted logs / 1";
    private readonly string path;
    internal NotificationRuleStore(string path) { this.path = Path.GetFullPath(path); }
    internal IReadOnlyCollection<string> Load()
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > 1024 * 1024) throw new InvalidDataException("The notification suppression file exceeds 1 MiB: " + path);
            using var reader = new StreamReader(file, new UTF8Encoding(false, true), true);
            if (reader.ReadLine() != Header) throw new InvalidDataException("Unsupported notification suppression file header: " + path);
            var rules = new HashSet<string>(StringComparer.Ordinal);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!ValidId(line)) throw new InvalidDataException("Invalid notification suppression rule: " + path);
                rules.Add(line);
            }
            return rules.ToArray();
        }
        catch (FileNotFoundException) { return Array.Empty<string>(); }
        catch (DirectoryNotFoundException) { return Array.Empty<string>(); }
    }
    internal void Save(IReadOnlyCollection<string> rules)
    {
        if (rules.Any(r => !ValidId(r))) throw new InvalidDataException("Cannot save an invalid notification suppression rule.");
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
                    foreach (string rule in rules.OrderBy(x => x, StringComparer.Ordinal)) writer.WriteLine(rule);
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
    private static bool ValidId(string value) => value.Length == 64 && value.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');
}
