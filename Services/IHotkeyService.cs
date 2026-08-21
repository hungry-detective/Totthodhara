using System;

namespace ClipDropPro.Services
{
    public interface IHotkeyService
    {
        void RegisterHotkey(string key, System.Windows.Input.ModifierKeys modifiers, Action action);
        void UnregisterHotkey();
    }
}
