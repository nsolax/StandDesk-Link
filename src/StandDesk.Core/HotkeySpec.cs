namespace StandDesk.Core;

public sealed record HotkeySpec(uint Modifiers, uint VirtualKey)
{
    public static HotkeySpec Parse(string text)
    {
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts.Distinct(StringComparer.OrdinalIgnoreCase).Count() != parts.Length) throw new ArgumentException("Use a modifier and a key, such as Ctrl+Alt+Shift+Up.");
        uint modifiers = 0;
        foreach (var part in parts[..^1]) modifiers |= part.ToUpperInvariant() switch
        { "CTRL" or "CONTROL" => 2u, "ALT" => 1u, "SHIFT" => 4u, _ => throw new ArgumentException("Use Ctrl, Alt, or Shift modifiers.") };
        var key = parts[^1].ToUpperInvariant();
        uint code = key switch
        {
            "UP" => 0x26, "DOWN" => 0x28, "LEFT" => 0x25, "RIGHT" => 0x27,
            "SPACE" => 0x20, "HOME" => 0x24, "END" => 0x23,
            _ when key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]) => key[0],
            _ when key.StartsWith('F') && int.TryParse(key[1..], out var f) && f is >= 1 and <= 24 && f != 12 => (uint)(0x70 + f - 1),
            _ => throw new ArgumentException("Choose a letter, digit, arrow, Space, Home, End or F-key (except reserved F12).")
        };
        return new(modifiers, code);
    }
}
