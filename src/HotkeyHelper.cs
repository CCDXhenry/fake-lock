using System;
using System.Windows.Forms;

namespace FakeLock
{
    internal static class HotkeyHelper
    {
        public static bool ParseHotkey(string text, out uint modifiers, out uint vk)
        {
            modifiers = 0;
            vk = 0;
            if (string.IsNullOrEmpty(text)) return false;

            string[] parts = text.Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim().ToLowerInvariant();
                if (p == "ctrl" || p == "control") modifiers |= Native.MOD_CONTROL;
                else if (p == "alt") modifiers |= Native.MOD_ALT;
                else if (p == "shift") modifiers |= Native.MOD_SHIFT;
                else if (p == "win" || p == "windows") modifiers |= Native.MOD_WIN;
                else
                {
                    if (p.StartsWith("f") && p.Length > 1)
                    {
                        int fNum;
                        if (int.TryParse(p.Substring(1), out fNum) && fNum >= 1 && fNum <= 24)
                        {
                            vk = (uint)((int)Keys.F1 + (fNum - 1));
                            continue;
                        }
                    }

                    if (p.Length == 1)
                    {
                        char c = p[0];
                        if (c >= 'a' && c <= 'z') vk = (uint)(0x41 + (c - 'a'));
                        else if (c >= '0' && c <= '9') vk = (uint)(0x30 + (c - '0'));
                    }
                }
            }

            return vk != 0;
        }
    }
}
