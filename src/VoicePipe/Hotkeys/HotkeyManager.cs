using System.ComponentModel;

namespace VoicePipe.Hotkeys;

public sealed class HotkeyManager : IDisposable
{
    private readonly Dictionary<int, SoundClip> _clipsById = new();
    private IntPtr _windowHandle;
    private int _nextId = 1000;

    public void Initialize(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
    }

    public SoundClip? GetClip(int hotkeyId)
    {
        return _clipsById.GetValueOrDefault(hotkeyId);
    }

    public IReadOnlyList<string> RegisterClips(IEnumerable<SoundClip> clips)
    {
        UnregisterAll();
        var failures = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (_windowHandle == IntPtr.Zero)
        {
            return failures;
        }

        foreach (var clip in clips)
        {
            clip.IsHotkeyRegistered = false;
            if (!HotkeyDefinition.TryParse(clip.HotkeyText, out var hotkey) || hotkey is null)
            {
                continue;
            }

            var normalized = hotkey.DisplayText;
            if (!seen.Add(normalized))
            {
                failures.Add($"{clip.Name}: {normalized} は他のクリップと重複しています");
                continue;
            }

            var id = _nextId++;
            if (!HotkeyNative.RegisterHotKey(_windowHandle, id, hotkey.Modifiers, (uint)hotkey.VirtualKey))
            {
                var error = new Win32Exception();
                failures.Add($"{clip.Name}: {normalized} を登録できません ({error.Message})");
                continue;
            }

            clip.IsHotkeyRegistered = true;
            _clipsById[id] = clip;
        }

        return failures;
    }

    public void UnregisterAll()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            _clipsById.Clear();
            return;
        }

        foreach (var id in _clipsById.Keys.ToList())
        {
            HotkeyNative.UnregisterHotKey(_windowHandle, id);
        }

        _clipsById.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
    }
}
