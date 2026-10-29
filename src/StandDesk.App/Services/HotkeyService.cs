using System.Runtime.InteropServices;
using System.Windows.Interop;
using StandDesk.Core;

namespace StandDesk.App.Services;

public sealed class HotkeyService : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
    private readonly HwndSource _source;
    private readonly IntPtr _window;
    private readonly List<int> _ids = [];
    private Dictionary<int, string> _actions = [];
    private Dictionary<HotkeySpec, int> _bindings = [];
    private int _nextId = 100;
    public event Action<string>? Pressed;
    public HotkeyService(IntPtr window)
    {
        _window = window;
        _source = HwndSource.FromHwnd(window);
        _source.AddHook(HandleMessage);
    }
    public void Apply(DeskSettings settings)
    {
        settings.Validate();
        if (!settings.HotkeysEnabled) { Clear(); return; }
        var desired = new[] { ("stop", settings.StopKey), ("sit", settings.SitKey), ("stand", settings.StandKey) }
            .Select(x => (Action: x.Item1, Text: x.Item2, Spec: HotkeySpec.Parse(x.Item2))).ToArray();
        if (desired.Select(x => x.Spec).Distinct().Count() != 3) throw new ArgumentException("Choose three different shortcuts.");
        // Keep existing registrations if unchanged, otherwise reserve all replacements before releasing old keys.
        var reserved = new List<(int Id, string Action)>();
        var next = new Dictionary<int, string>();
        var bindings = new Dictionary<HotkeySpec, int>();
        try
        {
            foreach (var item in desired)
            {
                if (_bindings.TryGetValue(item.Spec, out var existing))
                {
                    next[existing] = item.Action;
                    bindings[item.Spec] = existing;
                    continue;
                }
                var id = ++_nextId;
                if (id > 0xbfff) throw new InvalidOperationException("Restart the app before changing more shortcuts.");
                if (!RegisterHotKey(_window, id, item.Spec.Modifiers | 0x4000, item.Spec.VirtualKey))
                    throw new InvalidOperationException($"Shortcut unavailable: {item.Text}. Choose another combination; existing keys were retained.");
                reserved.Add((id, item.Action));
                next[id] = item.Action;
                bindings[item.Spec] = id;
            }
        }
        catch { foreach (var item in reserved) UnregisterHotKey(_window, item.Id); throw; }
        foreach (var id in _ids.Where(id => !next.ContainsKey(id))) UnregisterHotKey(_window, id);
        _ids.Clear(); _ids.AddRange(next.Keys);
        _actions = next; _bindings = bindings;
    }
    private IntPtr HandleMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312 && _actions.TryGetValue(wParam.ToInt32(), out var action)) { handled = true; Pressed?.Invoke(action); }
        return IntPtr.Zero;
    }
    private void Clear() { foreach (var id in _ids) UnregisterHotKey(_window, id); _ids.Clear(); _actions.Clear(); _bindings.Clear(); }
    public void Dispose() { Clear(); _source.RemoveHook(HandleMessage); }
}
