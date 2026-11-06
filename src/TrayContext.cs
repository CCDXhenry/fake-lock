using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FakeLock
{
    public class TrayContext : ApplicationContext
    {
        private NotifyIcon _notifyIcon;
        private LockForm _lockForm;
        private MessageWindow _msgWindow;
        private MenuItem _itemAutoStart;
        private EventWaitHandle _lockEvt;
        private EventWaitHandle _unlockEvt;
        private EventWaitHandle _quitEvt;
        private EventWaitHandle _noticeEvt;
        private Thread _ipcThread;
        private bool _running = true;

        public TrayContext(string[] args)
        {
            _lockForm = new LockForm(this);
            _lockForm.LoadConfigAndArgs(args);

            // 同步开机自启动配置
            if (_lockForm.AutoStartConfig.HasValue)
            {
                AutoStartHelper.SyncWithConfig(_lockForm.AutoStartConfig.Value);
            }

            // 创建原生消息监听窗口（用于捕获全局热键 WM_HOTKEY）
            _msgWindow = new MessageWindow(OnGlobalHotkeyTriggered);

            // 注册全局快捷键
            RegisterHotkey();

            // 初始化系统托盘图标
            InitTrayIcon();

            // 初始化跨进程事件监听
            InitIpcEvents();

            // 监听 Windows 系统会话事件（当用户 Win+L 或锁屏时自动解除假锁）
            SystemEvents.SessionSwitch += OnSessionSwitch;

            // 检查启动时是否立即锁屏
            if (_lockForm.StartLocked)
            {
                _lockForm.ShowLock();
            }
            else
            {
                _notifyIcon.ShowBalloonTip(3000, "FakeLock 已就绪",
                    string.Format("已常驻系统托盘，按快捷键【{0}】随时开启半透明锁屏。", _lockForm.LockHotkeyStr),
                    ToolTipIcon.Info);
            }
        }

        private void InitTrayIcon()
        {
            _notifyIcon = new NotifyIcon();
            _notifyIcon.Text = string.Format("FakeLock - 后台就绪 ({0} 锁定)", _lockForm.LockHotkeyStr);
            _notifyIcon.Icon = CreateTrayIcon();
            _notifyIcon.Visible = true;

            // 右键菜单
            ContextMenu menu = new ContextMenu();
            MenuItem itemLock = new MenuItem("🔒 立即锁定 (" + _lockForm.LockHotkeyStr + ")", (s, e) => _lockForm.ShowLock());
            _itemAutoStart = new MenuItem("🚀 开机自启动", OnToggleAutoStart);
            _itemAutoStart.Checked = AutoStartHelper.IsAutoStartEnabled();
            MenuItem itemReload = new MenuItem("⚙️ 重新加载配置", (s, e) => ReloadConfig());
            MenuItem itemSep = new MenuItem("-");
            MenuItem itemExit = new MenuItem("❌ 退出程序", (s, e) => Shutdown());

            menu.MenuItems.Add(itemLock);
            menu.MenuItems.Add(_itemAutoStart);
            menu.MenuItems.Add(itemReload);
            menu.MenuItems.Add(itemSep);
            menu.MenuItems.Add(itemExit);
            _notifyIcon.ContextMenu = menu;

            // 双击托盘图标立即锁定
            _notifyIcon.DoubleClick += (s, e) => _lockForm.ShowLock();
        }

        public void UpdateTrayTooltip(string status)
        {
            if (_notifyIcon != null)
            {
                string text = "FakeLock - " + status;
                if (text.Length >= 64) text = text.Substring(0, 63);
                _notifyIcon.Text = text;
            }
        }

        private void RegisterHotkey()
        {
            uint mods;
            uint vk;
            if (HotkeyHelper.ParseHotkey(_lockForm.LockHotkeyStr, out mods, out vk))
            {
                Native.RegisterHotKey(_msgWindow.Handle, 1001, mods | Native.MOD_NOREPEAT, vk);
            }
        }

        private void UnregisterHotkey()
        {
            if (_msgWindow != null && _msgWindow.Handle != IntPtr.Zero)
            {
                Native.UnregisterHotKey(_msgWindow.Handle, 1001);
            }
        }

        public void ReloadConfig()
        {
            UnregisterHotkey();
            _lockForm.LoadConfigAndArgs(null);
            RegisterHotkey();
            if (_lockForm.AutoStartConfig.HasValue)
            {
                AutoStartHelper.SyncWithConfig(_lockForm.AutoStartConfig.Value);
            }
            if (_itemAutoStart != null)
            {
                _itemAutoStart.Checked = AutoStartHelper.IsAutoStartEnabled();
            }
            UpdateTrayTooltip("已更新配置 (" + _lockForm.LockHotkeyStr + " 锁定)");
            _notifyIcon.ShowBalloonTip(2000, "配置已重新加载",
                string.Format("当前快捷键: {0} | 自启动: {1}", _lockForm.LockHotkeyStr, AutoStartHelper.IsAutoStartEnabled() ? "已开启" : "未开启"), ToolTipIcon.Info);
        }

        private void OnToggleAutoStart(object sender, EventArgs e)
        {
            bool current = AutoStartHelper.IsAutoStartEnabled();
            bool next = !current;
            bool ok = AutoStartHelper.SetAutoStart(next);
            if (ok)
            {
                if (_itemAutoStart != null) _itemAutoStart.Checked = next;
                AutoStartHelper.UpdateConfigAutoStart(next);
                string tip = next
                    ? "已开启开机自启动：登录 Windows 时将自动在后台托盘就绪。"
                    : "已关闭开机自启动。";
                _notifyIcon.ShowBalloonTip(2000, "自启动设置", tip, ToolTipIcon.Info);
            }
            else
            {
                _notifyIcon.ShowBalloonTip(2000, "设置失败", "修改注册表失败，请检查安全权限。", ToolTipIcon.Warning);
            }
        }

        private void OnGlobalHotkeyTriggered()
        {
            if (!_lockForm.IsLocked)
            {
                _lockForm.ShowLock();
            }
        }

        public void UpdateNotice(string newNotice)
        {
            if (_lockForm != null && !_lockForm.IsDisposed)
            {
                SafeInvoke(_lockForm, new Action(() => _lockForm.UpdateNotice(newNotice)));
            }
        }

        private void InitIpcEvents()
        {
            _lockEvt = new EventWaitHandle(false, EventResetMode.AutoReset, Program.LockEventName);
            _unlockEvt = new EventWaitHandle(false, EventResetMode.AutoReset, Program.UnlockEventName);
            _quitEvt = new EventWaitHandle(false, EventResetMode.AutoReset, Program.QuitEventName);
            _noticeEvt = new EventWaitHandle(false, EventResetMode.AutoReset, Program.NoticeEventName);

            WaitHandle[] handles = new WaitHandle[] { _lockEvt, _unlockEvt, _quitEvt, _noticeEvt };

            _ipcThread = new Thread(() =>
            {
                while (_running)
                {
                    try
                    {
                        int index = WaitHandle.WaitAny(handles, 500);
                        if (index == 0) // Lock
                        {
                            if (!_lockForm.IsDisposed)
                            {
                                SafeInvoke(_lockForm, _lockForm.ShowLock);
                            }
                        }
                        else if (index == 1) // Unlock
                        {
                            if (!_lockForm.IsDisposed)
                            {
                                SafeInvoke(_lockForm, _lockForm.HideLock);
                            }
                        }
                        else if (index == 2) // Quit
                        {
                            if (!_lockForm.IsDisposed)
                            {
                                SafeInvoke(_lockForm, Shutdown);
                            }
                            break;
                        }
                        else if (index == 3) // Notice
                        {
                            try
                            {
                                string noticeFile = Path.Combine(Path.GetTempPath(), "fakelock_notice.txt");
                                if (File.Exists(noticeFile))
                                {
                                    string text = File.ReadAllText(noticeFile, Encoding.UTF8);
                                    UpdateNotice(text);
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            });
            _ipcThread.IsBackground = true;
            _ipcThread.Start();
        }

        private void SafeInvoke(Control ctrl, Action act)
        {
            try
            {
                if (ctrl != null && !ctrl.IsDisposed)
                {
                    if (ctrl.IsHandleCreated)
                    {
                        ctrl.BeginInvoke(act);
                    }
                    else
                    {
                        IntPtr h = ctrl.Handle;
                        ctrl.BeginInvoke(act);
                    }
                }
            }
            catch { }
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock)
            {
                if (_lockForm != null && _lockForm.IsLocked)
                {
                    SafeInvoke(_lockForm, _lockForm.HideLock);
                }
            }
        }

        public void Shutdown()
        {
            _running = false;
            try
            {
                SystemEvents.SessionSwitch -= OnSessionSwitch;
            }
            catch { }
            UnregisterHotkey();

            if (_lockForm != null)
            {
                _lockForm.HideLock();
                _lockForm.Dispose();
            }

            if (_msgWindow != null)
            {
                _msgWindow.DestroyHandle();
                _msgWindow = null;
            }

            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            ExitThread();
            Application.Exit();
        }

        private static Icon CreateTrayIcon()
        {
            try
            {
                using (Bitmap bmp = new Bitmap(32, 32))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(18, 24, 38)))
                    {
                        g.FillEllipse(bg, 1, 1, 30, 30);
                    }
                    using (Pen border = new Pen(Color.FromArgb(0, 229, 255), 2.5f))
                    {
                        g.DrawEllipse(border, 1, 1, 30, 30);
                    }
                    using (SolidBrush lockBody = new SolidBrush(Color.FromArgb(0, 229, 255)))
                    {
                        g.FillRectangle(lockBody, 9, 14, 14, 11);
                    }
                    using (Pen lockShackle = new Pen(Color.FromArgb(240, 245, 255), 2.2f))
                    {
                        g.DrawArc(lockShackle, 11, 7, 10, 10, 180, 180);
                    }
                    using (SolidBrush keyhole = new SolidBrush(Color.FromArgb(18, 24, 38)))
                    {
                        g.FillEllipse(keyhole, 14, 17, 4, 4);
                    }

                    IntPtr hIcon = bmp.GetHicon();
                    return Icon.FromHandle(hIcon);
                }
            }
            catch
            {
                return SystemIcons.Shield;
            }
        }
    }

    public class MessageWindow : NativeWindow
    {
        private Action _onHotkey;

        public MessageWindow(Action onHotkey)
        {
            _onHotkey = onHotkey;
            CreateParams cp = new CreateParams();
            CreateHandle(cp);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                if (_onHotkey != null) _onHotkey();
                return;
            }
            base.WndProc(ref m);
        }
    }
}
