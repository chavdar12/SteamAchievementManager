using System.Globalization;
using SteamAchievementManager.Schema;

namespace SteamAchievementManager.Manager;

[Flags]
internal enum StatFlags
{
    None = 0,
    IncrementOnly = 1 << 0,
    Protected = 1 << 1,
    UnknownPermission = 1 << 2
}

internal sealed class StatIsProtectedException : Exception;

// a stat row in the statistics grid; Value is a boxed int or float
internal sealed class StatInfo(StatDefinition definition, object value)
{
    private readonly object _originalValue = value;
    private object _value = value;

    public StatDefinition Definition { get; } = definition;
    public string DisplayName => Definition.DisplayName;
    public bool IsModified => _value.Equals(_originalValue) == false;

    // set by the grid with the edited text
    public object Value
    {
        get => _value;
        set
        {
            var text = (string)value;
            // each branch boxed separately: a plain ?: would turn the int into a float
            var parsed = Definition.IsFloat
                ? (object)float.Parse(text, CultureInfo.CurrentCulture)
                : int.Parse(text, CultureInfo.CurrentCulture);
            if (Definition.IsProtected && parsed.Equals(_value) == false) throw new StatIsProtectedException();
            _value = parsed;
        }
    }

    public string Extra
    {
        get
        {
            var flags = StatFlags.None;
            if (Definition.IncrementOnly) flags |= StatFlags.IncrementOnly;
            if (Definition.IsProtected) flags |= StatFlags.Protected;
            if ((Definition.Permission & ~2) != 0) flags |= StatFlags.UnknownPermission;
            return flags.ToString();
        }
    }
}