using System.Text;

namespace SteamAchievementManager.Tests;

// writes Valve's binary KeyValues format, to build schema fixtures
internal sealed class KeyValueWriter
{
    private readonly MemoryStream _stream = new();
    private readonly BinaryWriter _writer;

    public KeyValueWriter() => _writer = new BinaryWriter(_stream);

    public KeyValueWriter Begin(string name) => Header(0, name);
    public KeyValueWriter String(string name, string value) { Header(1, name); WriteString(value); return this; }
    public KeyValueWriter Int(string name, int value) { Header(2, name); _writer.Write(value); return this; }
    public KeyValueWriter Float(string name, float value) { Header(3, name); _writer.Write(value); return this; }
    public KeyValueWriter UInt64(string name, ulong value) { Header(7, name); _writer.Write(value); return this; }
    public KeyValueWriter End() { _writer.Write((byte)8); return this; }

    public byte[] ToArray() => _stream.ToArray();

    // writes the bytes to a temp file and returns its path
    public string Save()
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, ToArray());
        return path;
    }

    private KeyValueWriter Header(byte type, string name)
    {
        _writer.Write(type);
        WriteString(name);
        return this;
    }

    private void WriteString(string value)
    {
        _writer.Write(Encoding.UTF8.GetBytes(value));
        _writer.Write((byte)0);
    }
}
