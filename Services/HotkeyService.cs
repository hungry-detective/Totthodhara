using System;
using System.Windows.Input;
using NHotkey;
using NHotkey.Wpf;

namespace ClipDropPro.Services
{
    public class HotkeyService : IHotkeyService
    {
        private const string HotkeyId = "ToggleShelfHotkey";

        public void RegisterHotkey(string keyString, ModifierKeys modifiers, Action action)
        {
            if (!Enum.TryParse(keyString, out Key key))
                return;

            try
            {
                HotkeyManager.Current.AddOrReplace(HotkeyId, key, modifiers, (s, e) => action?.Invoke());
            }
            catch (Exception)
            {
                // Key might be already registered
            }
        }

        public void UnregisterHotkey()
        {
            try
            {
                HotkeyManager.Current.Remove(HotkeyId);
            }
            catch { }
        }
    }
}
