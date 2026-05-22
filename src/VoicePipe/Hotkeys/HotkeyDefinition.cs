using System.Windows.Input;

namespace VoicePipe.Hotkeys;

public sealed record HotkeyDefinition(bool Control, bool Alt, bool Shift, bool Win, Key Key)
{
    public string DisplayText
    {
        get
        {
            var parts = new List<string>();
            if (Control)
            {
                parts.Add("Ctrl");
            }

            if (Alt)
            {
                parts.Add("Alt");
            }

            if (Shift)
            {
                parts.Add("Shift");
            }

            if (Win)
            {
                parts.Add("Win");
            }

            parts.Add(FormatKey(Key));
            return string.Join("+", parts);
        }
    }

    public bool IsRegisterable => (Control || Alt || Shift || Win || IsFunctionKey(Key)) && !IsModifierKey(Key);

    public uint Modifiers
    {
        get
        {
            var modifiers = HotkeyNative.ModNoRepeat;
            if (Alt)
            {
                modifiers |= HotkeyNative.ModAlt;
            }

            if (Control)
            {
                modifiers |= HotkeyNative.ModControl;
            }

            if (Shift)
            {
                modifiers |= HotkeyNative.ModShift;
            }

            if (Win)
            {
                modifiers |= HotkeyNative.ModWin;
            }

            return modifiers;
        }
    }

    public int VirtualKey => KeyInterop.VirtualKeyFromKey(Key);

    public static bool TryParse(string? text, out HotkeyDefinition? hotkey)
    {
        hotkey = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var control = false;
        var alt = false;
        var shift = false;
        var win = false;
        Key? key = null;

        foreach (var rawPart in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (rawPart.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    control = true;
                    break;
                case "ALT":
                    alt = true;
                    break;
                case "SHIFT":
                    shift = true;
                    break;
                case "WIN":
                case "WINDOWS":
                    win = true;
                    break;
                default:
                    if (!TryParseKey(rawPart, out var parsedKey))
                    {
                        return false;
                    }

                    key = parsedKey;
                    break;
            }
        }

        if (key is null)
        {
            return false;
        }

        hotkey = new HotkeyDefinition(control, alt, shift, win, key.Value);
        return hotkey.IsRegisterable;
    }

    public static HotkeyDefinition? FromKeyEvent(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.ImeProcessed)
        {
            key = e.ImeProcessedKey;
        }

        if (IsModifierKey(key) || key == Key.None)
        {
            return null;
        }

        var definition = new HotkeyDefinition(
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control),
            Keyboard.Modifiers.HasFlag(ModifierKeys.Alt),
            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift),
            Keyboard.Modifiers.HasFlag(ModifierKeys.Windows),
            key);
        return definition.IsRegisterable ? definition : null;
    }

    private static bool TryParseKey(string text, out Key key)
    {
        if (text.Length == 1)
        {
            var character = text[0];
            if (character is >= '0' and <= '9')
            {
                return Enum.TryParse($"D{character}", out key);
            }

            if (char.IsLetter(character))
            {
                return Enum.TryParse(char.ToUpperInvariant(character).ToString(), out key);
            }
        }

        if (text.StartsWith("Num", StringComparison.OrdinalIgnoreCase) &&
            text.Length == 4 &&
            text[3] is >= '0' and <= '9')
        {
            return Enum.TryParse($"NumPad{text[3]}", ignoreCase: true, out key);
        }

        return Enum.TryParse(text, ignoreCase: true, out key) ||
               Enum.TryParse($"D{text}", ignoreCase: true, out key);
    }

    private static string FormatKey(Key key)
    {
        return key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => $"Num{(int)(key - Key.NumPad0)}",
            _ => key.ToString()
        };
    }

    private static bool IsFunctionKey(Key key) => key is >= Key.F1 and <= Key.F24;

    private static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl or
            Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or
            Key.LWin or Key.RWin;
    }
}
