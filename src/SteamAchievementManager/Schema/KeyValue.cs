using System.Text;

namespace SteamAchievementManager.Schema;

internal enum KeyValueType : byte
{
    None = 0, // a parent node; its children follow
    String = 1,
    Int32 = 2,
    Float32 = 3,
    Pointer = 4,
    WideString = 5,
    Color = 6,
    UInt64 = 7,
    End = 8
}

// a node of Valve's binary KeyValues format
internal sealed class KeyValue
{
    // returned for missing keys, so lookups can be chained
    private static readonly KeyValue Missing = new(null);

    private readonly List<KeyValue> _children = [];

    private KeyValue(object value)
    {
        Value = value;
    }

    public string Name { get; private init; }

    // string, int, float, uint or ulong; null for a parent node
    public object Value { get; }

    public IReadOnlyList<KeyValue> Children => _children;
    public bool Exists => this != Missing;

    public KeyValue this[string key] =>
        _children.FirstOrDefault(c => string.Equals(c.Name, key, StringComparison.OrdinalIgnoreCase)) ?? Missing;

    public string AsString(string defaultValue)
    {
        return Value?.ToString() ?? defaultValue;
    }

    public int AsInteger(int defaultValue)
    {
        return Value switch
        {
            string s => int.TryParse(s, out var v) ? v : defaultValue,
            int i => i,
            float f => (int)f,
            ulong u => (int)(u & 0xFFFFFFFF),
            _ => defaultValue
        };
    }

    public float AsFloat(float defaultValue)
    {
        return Value switch
        {
            string s => float.TryParse(s, out var v) ? v : defaultValue,
            int i => i,
            float f => f,
            ulong u => u & 0xFFFFFFFF,
            _ => defaultValue
        };
    }

    public bool AsBoolean(bool defaultValue)
    {
        return Value switch
        {
            string s => int.TryParse(s, out var v) ? v != 0 : defaultValue,
            int i => i != 0,
            float f => (int)f != 0,
            ulong u => u != 0,
            _ => defaultValue
        };
    }

    // null if the file is missing, unreadable or malformed
    public static KeyValue Load(string path)
    {
        try
        {
            using BinaryReader reader = new(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            KeyValue root = new(null) { Name = "<root>" };
            root.ReadChildren(reader);
            return reader.BaseStream.Position == reader.BaseStream.Length ? root : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void ReadChildren(BinaryReader reader)
    {
        while (true)
        {
            var type = (KeyValueType)reader.ReadByte();
            if (type == KeyValueType.End) return;

            var name = ReadString(reader);
            KeyValue child = new(type switch
            {
                KeyValueType.None => null,
                KeyValueType.String => ReadString(reader),
                KeyValueType.Int32 => reader.ReadInt32(),
                KeyValueType.Float32 => reader.ReadSingle(),
                KeyValueType.UInt64 => reader.ReadUInt64(),
                KeyValueType.Pointer or KeyValueType.Color => reader.ReadUInt32(),
                _ => throw new FormatException($"unsupported node type {type}")
            }) { Name = name };

            if (type == KeyValueType.None) child.ReadChildren(reader);
            _children.Add(child);
        }
    }

    // null-terminated UTF-8
    private static string ReadString(BinaryReader reader)
    {
        List<byte> bytes = [];
        for (var b = reader.ReadByte(); b != 0; b = reader.ReadByte()) bytes.Add(b);
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}