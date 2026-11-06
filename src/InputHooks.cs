using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FakeLock
{
    public partial class LockForm
    {
        private IntPtr _kbHook = IntPtr.Zero;
        private IntPtr _mouseHook = IntPtr.Zero;
        private static Native.LowLevelKeyboardProc _kbProc;
        private static Native.LowLevelMouseProc _mouseProc;

        private bool _ctrlDown = false;
        private bool _shiftDown = false;
        private bool _altDown = false;
        private bool _winDown = false;

        private static void ReleaseModifierKeys()
        {
            try
            {
                byte[] keys = new byte[] {
                    0x12, // VK_MENU (Alt)
                    0xA4, // VK_LMENU
                    0xA5, // VK_RMENU
                    0x11, // VK_CONTROL (Ctrl)
                    0xA2, // VK_LCONTROL
                    0xA3, // VK_RCONTROL
                    0x10, // VK_SHIFT (Shift)
                    0xA0, // VK_LSHIFT
                    0xA1, // VK_RSHIFT
                    0x5B, // VK_LWIN
                    0x5C  // VK_RWIN
                };

                foreach (byte vk in keys)
                {
                    Native.keybd_event(vk, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
                }
            }
            catch { }
        }

        private void InitHooks()
        {
            UninstallHooks();

            _kbProc = LowLevelKeyboardHandler;
            _mouseProc = LowLevelMouseHandler;

            _kbHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _kbProc, IntPtr.Zero, 0);
            _mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseProc, IntPtr.Zero, 0);
            Log("InitHooks kb=" + _kbHook + " mouse=" + _mouseHook);
        }

        private void UninstallHooks()
        {
            if (_kbHook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(_kbHook);
                _kbHook = IntPtr.Zero;
            }
            if (_mouseHook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
        }

        private IntPtr LowLevelKeyboardHandler(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = (int)wParam;
                Native.KBDLLHOOKSTRUCT k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                bool injected = (k.flags & Native.LLKHF_INJECTED) != 0;
                bool isDown = (msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN);

                if (injected && !_testMode)
                {
                    return Native.CallNextHookEx(_kbHook, nCode, wParam, lParam);
                }

                if (k.vkCode == 0x11 || k.vkCode == 0xA2 || k.vkCode == 0xA3) _ctrlDown = isDown;
                else if (k.vkCode == 0x10 || k.vkCode == 0xA0 || k.vkCode == 0xA1) _shiftDown = isDown;
                else if (k.vkCode == 0x12 || k.vkCode == 0xA4 || k.vkCode == 0xA5) _altDown = isDown;
                else if (k.vkCode == 0x5B || k.vkCode == 0x5C) _winDown = isDown;

                if (isDown && k.vkCode == 0x7B && _ctrlDown && _shiftDown && _altDown)
                {
                    Log("Emergency hotkey triggered");
                    this.BeginInvoke(new Action(HideLock));
                    return (IntPtr)1;
                }

                // 检测 Win+L：自动解除假锁并锁定 Windows 工作站
                if (isDown && _winDown && (k.vkCode == 0x4C || k.vkCode == 'L'))
                {
                    Log("Win+L detected, dismissing FakeLock and locking Windows workstation");
                    this.BeginInvoke(new Action(() =>
                    {
                        HideLock();
                        Native.LockWorkStation();
                    }));
                    return (IntPtr)1;
                }

                if (isDown)
                {
                    // 如果当前处于黑屏休眠状态，按任意键唤醒点亮屏幕
                    if (_isAsleep)
                    {
                        this.BeginInvoke(new Action(WakeUp));

                        // 常用唤醒功能键/修饰键仅用于点亮，不作为密码字符输入
                        if (k.vkCode == 0x0D || k.vkCode == 0x1B || k.vkCode == 0x20 || k.vkCode == 0x09 ||
                            k.vkCode == 0x10 || k.vkCode == 0xA0 || k.vkCode == 0xA1 ||
                            k.vkCode == 0x11 || k.vkCode == 0xA2 || k.vkCode == 0xA3 ||
                            k.vkCode == 0x12 || k.vkCode == 0xA4 || k.vkCode == 0xA5 ||
                            k.vkCode == 0x5B || k.vkCode == 0x5C)
                        {
                            return (IntPtr)1;
                        }
                    }
                    else
                    {
                        _sleepCountdown = _wakeTimeout;
                    }

                    if (k.vkCode == 0x5B || k.vkCode == 0x5C) return (IntPtr)1;
                    if (k.vkCode == 0x09 && _altDown) return (IntPtr)1;
                    if (k.vkCode == 0x1B && _altDown) return (IntPtr)1;

                    if (k.vkCode == 0x0D)
                    {
                        this.BeginInvoke(new Action(TryUnlock));
                        return (IntPtr)1;
                    }

                    if (k.vkCode == 0x1B)
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            _txtPassword.Clear();
                            _lblError.Visible = false;
                        }));
                        return (IntPtr)1;
                    }

                    if (k.vkCode == 0x08)
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            if (_txtPassword.Text.Length > 0)
                            {
                                _txtPassword.Text = _txtPassword.Text.Substring(0, _txtPassword.Text.Length - 1);
                                _txtPassword.SelectionStart = _txtPassword.Text.Length;
                            }
                        }));
                        return (IntPtr)1;
                    }

                    char? c = VkCodeToChar(k.vkCode, _shiftDown);
                    if (c.HasValue)
                    {
                        char ch = c.Value;
                        this.BeginInvoke(new Action(() =>
                        {
                            _lblError.Visible = false;
                            _txtPassword.AppendText(ch.ToString());
                        }));
                        return (IntPtr)1;
                    }

                    return (IntPtr)1;
                }
                else
                {
                    // 修饰键（Alt, Ctrl, Shift, Win）的抬起事件绝不吞噬，放行给 OS 以免系统卡键
                    if (k.vkCode == 0x12 || k.vkCode == 0xA4 || k.vkCode == 0xA5 ||
                        k.vkCode == 0x11 || k.vkCode == 0xA2 || k.vkCode == 0xA3 ||
                        k.vkCode == 0x10 || k.vkCode == 0xA0 || k.vkCode == 0xA1 ||
                        k.vkCode == 0x5B || k.vkCode == 0x5C)
                    {
                        return Native.CallNextHookEx(_kbHook, nCode, wParam, lParam);
                    }
                    return (IntPtr)1;
                }
            }

            return Native.CallNextHookEx(_kbHook, nCode, wParam, lParam);
        }

        private static char? VkCodeToChar(uint vk, bool shift)
        {
            if (vk >= 0x30 && vk <= 0x39)
            {
                if (!shift) return (char)('0' + (vk - 0x30));
                switch (vk)
                {
                    case 0x31: return '!';
                    case 0x32: return '@';
                    case 0x33: return '#';
                    case 0x34: return '$';
                    case 0x35: return '%';
                    case 0x36: return '^';
                    case 0x37: return '&';
                    case 0x38: return '*';
                    case 0x39: return '(';
                    case 0x30: return ')';
                }
            }
            if (vk >= 0x60 && vk <= 0x69)
            {
                return (char)('0' + (vk - 0x60));
            }
            if (vk >= 0x41 && vk <= 0x5A)
            {
                char baseChar = shift ? (char)vk : (char)('a' + (vk - 0x41));
                return baseChar;
            }

            // 常用标点符号支持（防止配置包含符号的密码时无法输入）
            if (vk == 0xBD) return shift ? '_' : '-'; // VK_OEM_MINUS
            if (vk == 0xBB) return shift ? '+' : '='; // VK_OEM_PLUS
            if (vk == 0xBE) return shift ? '>' : '.'; // VK_OEM_PERIOD
            if (vk == 0xBC) return shift ? '<' : ','; // VK_OEM_COMMA
            if (vk == 0xBA) return shift ? ':' : ';'; // VK_OEM_1
            if (vk == 0xBF) return shift ? '?' : '/'; // VK_OEM_2
            if (vk == 0xC0) return shift ? '~' : '`'; // VK_OEM_3
            if (vk == 0xDB) return shift ? '{' : '['; // VK_OEM_4
            if (vk == 0xDC) return shift ? '|' : '\\'; // VK_OEM_5
            if (vk == 0xDD) return shift ? '}' : ']'; // VK_OEM_6
            if (vk == 0xDE) return shift ? '"' : '\''; // VK_OEM_7
            if (vk == 0x20) return ' '; // VK_SPACE

            return null;
        }

        private IntPtr LowLevelMouseHandler(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = (int)wParam;
                Native.MSLLHOOKSTRUCT m = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                bool injected = (m.flags & Native.LLMHF_INJECTED) != 0;

                if (injected)
                {
                    return Native.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                }

                Point pt = new Point(m.pt.x, m.pt.y);

                // 如果处于黑屏休眠状态：检测鼠标晃动（超过10像素）或点击唤醒点亮
                if (_isAsleep)
                {
                    int dx = Math.Abs(pt.X - _lastMousePos.X);
                    int dy = Math.Abs(pt.Y - _lastMousePos.Y);
                    if (dx > 10 || dy > 10 ||
                        msg == Native.WM_LBUTTONDOWN || msg == Native.WM_RBUTTONDOWN || msg == Native.WM_MBUTTONDOWN)
                    {
                        _lastMousePos = pt;
                        this.BeginInvoke(new Action(WakeUp));
                    }
                    return (IntPtr)1;
                }

                _lastMousePos = pt;

                if (_cardScreenBounds.Contains(pt))
                {
                    if (msg == Native.WM_LBUTTONDOWN || msg == Native.WM_RBUTTONDOWN)
                    {
                        _sleepCountdown = _wakeTimeout;
                    }
                    return Native.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                }

                if (msg == Native.WM_LBUTTONDOWN || msg == Native.WM_LBUTTONUP ||
                    msg == Native.WM_RBUTTONDOWN || msg == Native.WM_RBUTTONUP ||
                    msg == Native.WM_MBUTTONDOWN || msg == Native.WM_MBUTTONUP ||
                    msg == Native.WM_MOUSEWHEEL)
                {
                    return (IntPtr)1;
                }
            }

            return Native.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }
    }
}
