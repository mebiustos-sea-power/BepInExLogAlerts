using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace BepInExLogAlerts;

internal sealed class FileSnapshot
{
    internal readonly byte[] Bytes;
    internal readonly bool Truncated;
    internal readonly string Path;
    internal FileSnapshot(byte[] bytes, bool truncated, string path) { Bytes = bytes; Truncated = truncated; Path = path; }
}

internal sealed class DiskLogAccess
{
    private readonly TextWriter writer;
    private readonly FileStream file;
    internal DiskLogAccess(TextWriter writer)
    { this.writer = writer; file = UnderlyingFile(writer); }

    internal static DiskLogAccess FindCurrent()
    {
        // Appended logs have no reliable per-session timestamps. Do not mistake a
        // previous session's errors for this launch's errors.
        var config = new ConfigFile(Paths.BepInExConfigPath, false) { SaveOnConfigSet = false };
        if (config.Bind("Logging.Disk", "AppendLog", false).Value)
            throw new InvalidOperationException("BepInEx Logging.Disk.AppendLog is enabled; startup recovery requires AppendLog=false to identify this launch reliably.");
        DiskLogListener[] disks = Logger.Listeners.OfType<DiskLogListener>().Where(d => d.LogWriter != null).ToArray();
        if (disks.Length != 1) throw new InvalidOperationException("Expected one active BepInEx disk log writer; found " + disks.Length + ".");
        return new DiskLogAccess(disks[0].LogWriter);
    }

    internal FileSnapshot Snapshot(int maximumBytes)
    {
        // BepInEx exposes the synchronized TextWriter. Synchronize with WriteLine
        // and flush its buffer before freezing the prefix; never request a new log file.
        lock (writer)
        {
            writer.Flush();
            long length = file.Length;
            long start = Math.Max(0, length - maximumBytes);
            using var read = new FileStream(file.Name, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            read.Position = start;
            if (start != 0)
                while (read.Position < length && read.ReadByte() != '\n') { }
            byte[] bytes = new byte[checked((int)(length - read.Position))];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int count = read.Read(bytes, offset, bytes.Length - offset);
                if (count == 0) throw new EndOfStreamException("The startup log changed while its snapshot was being read.");
                offset += count;
            }
            return new FileSnapshot(bytes, start != 0, file.Name);
        }
    }

    private static FileStream UnderlyingFile(TextWriter writer)
    {
        for (int depth = 0; depth < 4; depth++)
        {
            if (writer is StreamWriter stream)
                return stream.BaseStream as FileStream ?? throw new InvalidOperationException("The BepInEx disk writer is not backed by a file.");
            // TextWriter.Synchronized wraps StreamWriter in Mono and .NET. Discover
            // the contained writer by type, without depending on private field names.
            FieldInfo[] fields = writer.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(f => typeof(TextWriter).IsAssignableFrom(f.FieldType)).ToArray();
            if (fields.Length != 1 || !(fields[0].GetValue(writer) is TextWriter inner))
                throw new InvalidOperationException("Unsupported BepInEx disk writer: " + writer.GetType().FullName);
            writer = inner;
        }
        throw new InvalidOperationException("The BepInEx disk writer has too many wrappers.");
    }
}
