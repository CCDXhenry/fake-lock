using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FakeLock
{
    static class Program
    {
        public const string MutexName = "Global\\FakeLock_Roblox_SingleInstanceMutex";
        public const string LockEventName = "Global\\FakeLock_LockEvent";
        public const string UnlockEventName = "Global\\FakeLock_UnlockEvent";
        public const string QuitEventName = "Global\\FakeLock_QuitEvent";
        public const string NoticeEventName = "Global\\FakeLock_NoticeEvent";

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 处理命令行控制指令（跨进程通信）
            if (args != null && args.Length > 0)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i].ToLowerInvariant();
                    if (a == "-unlock")
                    {
                        SignalEvent(UnlockEventName, "[FakeLock] 已发送解锁信号（收起至托盘）。");
                        return;
                    }
                    if (a == "-lock")
                    {
                        SignalEvent(LockEventName, "[FakeLock] 已发送锁定信号（呼出假锁）。");
                        return;
                    }
                    if (a == "-quit" || a == "-exit")
                    {
                        SignalEvent(QuitEventName, "[FakeLock] 已发送退出信号。");
                        return;
                    }
                    if (a == "-notice" || a == "-n" || a == "-status")
                    {
                        string noticeMsg = (i + 1 < args.Length) ? args[++i] : "";
                        if (!string.IsNullOrEmpty(noticeMsg))
                        {
                            try
                            {
                                string noticeFile = Path.Combine(Path.GetTempPath(), "fakelock_notice.txt");
                                File.WriteAllText(noticeFile, noticeMsg, Encoding.UTF8);
                                SignalEvent(NoticeEventName, "[FakeLock] 已发送更新提示信号: " + noticeMsg);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("[FakeLock] 写入提示文件失败: " + ex.Message);
                            }
                        }
                        return;
                    }
                }
            }

            // 单实例检查
            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    bool lockNow = false;
                    if (args != null)
                    {
                        foreach (string arg in args)
                        {
                            if (arg.ToLowerInvariant() == "-now" || arg.ToLowerInvariant() == "-lock") lockNow = true;
                        }
                    }

                    if (lockNow)
                    {
                        SignalEvent(LockEventName, "[FakeLock] 已唤醒正在运行的实例并锁定。");
                    }
                    else
                    {
                        MessageBox.Show("FakeLock 已经在后台托盘运行中！\n\n- 快捷键锁定：按下配置的全局热键（默认 Alt+L）\n- 双击右下角系统托盘图标也可立即锁定\n- 命令行控制：FakeLock.exe -lock / -unlock / -quit",
                            "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    return;
                }

                // 启动托盘宿主上下文
                Application.Run(new TrayContext(args));
            }
        }

        private static void SignalEvent(string eventName, string successMsg)
        {
            try
            {
                using (EventWaitHandle evt = EventWaitHandle.OpenExisting(eventName))
                {
                    evt.Set();
                    Console.WriteLine(successMsg);
                }
            }
            catch (Exception)
            {
                Console.WriteLine("[FakeLock] 未检测到后台运行的 FakeLock 实例。");
            }
        }
    }

    public class TrayContext : ApplicationContext
    {
        private NotifyIcon _notifyIcon;
        private LockForm _lockForm;
        private MessageWindow _msgWindow;
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
            MenuItem itemReload = new MenuItem("⚙️ 重新加载配置", (s, e) => ReloadConfig());
            MenuItem itemSep = new MenuItem("-");
            MenuItem itemExit = new MenuItem("❌ 退出程序", (s, e) => Shutdown());

            menu.MenuItems.Add(itemLock);
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
            UpdateTrayTooltip("已更新配置 (" + _lockForm.LockHotkeyStr + " 锁定)");
            _notifyIcon.ShowBalloonTip(2000, "配置已重新加载",
                string.Format("当前快捷键: {0} | 密码: 已生效", _lockForm.LockHotkeyStr), ToolTipIcon.Info);
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

    public class LockForm : Form
    {
        private TrayContext _trayContext;

        private string _password = "1234";
        private double _opacity = 0.55;
        private string _title = "Roblox Studio 实机挂机测试中";
        private string _notice = "AI 正在前台渲染并操作测试，物理键鼠已锁定";
        private bool _excludeFromCapture = true;
        private string _lockHotkeyStr = "Alt+L";
        private bool _startLocked = false;
        private bool _testMode = false;

        public bool StartLocked { get { return _startLocked; } }
        public string LockHotkeyStr { get { return _lockHotkeyStr; } }
        public bool IsLocked { get; private set; }

        private Panel _cardPanel;
        private Label _lblIcon;
        private Label _lblTitle;
        private Label _lblNotice;
        private TextBox _txtPassword;
        private Button _btnUnlock;
        private Label _lblError;
        private DateTime _startTime;
        private Rectangle _cardScreenBounds;

        private IntPtr _kbHook = IntPtr.Zero;
        private IntPtr _mouseHook = IntPtr.Zero;
        private static Native.LowLevelKeyboardProc _kbProc;
        private static Native.LowLevelMouseProc _mouseProc;

        private bool _ctrlDown = false;
        private bool _shiftDown = false;
        private bool _altDown = false;
        private bool _winDown = false;

        private bool _blackScreen = true;
        private int _wakeTimeout = 15;
        private bool _isAsleep = false;
        private bool _cursorHidden = false;
        private int _sleepCountdown = 15;
        private System.Windows.Forms.Timer _sleepTimer;
        private Point _lastMousePos = Point.Empty;

        private void Log(string msg)
        {
            try
            {
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fakelock.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + "\r\n");
            }
            catch { }
        }

        public LockForm(TrayContext trayContext)
        {
            _trayContext = trayContext;
            InitWindow();
            InitControls();

            // 强制立即创建 Win32 窗口句柄，确保随时可接收 BeginInvoke
            IntPtr force = this.Handle;

            _sleepTimer = new System.Windows.Forms.Timer();
            _sleepTimer.Interval = 1000;
            _sleepTimer.Tick += SleepTimer_Tick;
        }

        private void SleepTimer_Tick(object sender, EventArgs e)
        {
            if (!this.IsLocked || _isAsleep) return;
            if (_blackScreen && _wakeTimeout > 0)
            {
                _sleepCountdown--;
                if (_sleepCountdown <= 0)
                {
                    EnterSleepMode();
                }
            }
        }

        public void LoadConfigAndArgs(string[] args)
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
                if (!File.Exists(configPath))
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("[Settings]");
                    sb.AppendLine("Password=1234");
                    sb.AppendLine("Opacity=0.55");
                    sb.AppendLine("Title=Roblox Studio 实机挂机测试中");
                    sb.AppendLine("Notice=AI 正在前台渲染并操作测试，物理键鼠已锁定");
                    sb.AppendLine("ExcludeFromCapture=true");
                    sb.AppendLine("; 呼出锁屏的全局快捷键（支持 Ctrl, Alt, Shift, Win 与按键组合）");
                    sb.AppendLine("LockHotkey=Alt+L");
                    sb.AppendLine("; 启动时是否立即锁屏（true: 立即锁屏; false: 常驻托盘就绪）");
                    sb.AppendLine("StartLocked=false");
                    sb.AppendLine("; 是否启用黑屏息屏模式（true: 锁屏后全黑模拟息屏，输入时点亮；false: 直接显示半透明悬浮卡片）");
                    sb.AppendLine("BlackScreen=true");
                    sb.AppendLine("; 唤醒点亮后，无操作再次自动黑屏的秒数（0 表示点亮后不自动重黑，默认 15 秒）");
                    sb.AppendLine("WakeTimeout=15");
                    File.WriteAllText(configPath, sb.ToString(), Encoding.UTF8);
                }
                else
                {
                    string[] lines = File.ReadAllLines(configPath, Encoding.UTF8);
                    foreach (string line in lines)
                    {
                        string trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#") || trimmed.StartsWith(";")) continue;
                        int eq = trimmed.IndexOf('=');
                        if (eq <= 0) continue;
                        string key = trimmed.Substring(0, eq).Trim().ToLowerInvariant();
                        string val = trimmed.Substring(eq + 1).Trim();

                        if (key == "password") _password = val;
                        else if (key == "opacity")
                        {
                            double op;
                            if (double.TryParse(val, out op) && op >= 0.1 && op <= 0.95) _opacity = op;
                        }
                        else if (key == "title") _title = val;
                        else if (key == "notice") _notice = val;
                        else if (key == "excludefromcapture")
                        {
                            bool b;
                            if (bool.TryParse(val, out b)) _excludeFromCapture = b;
                        }
                        else if (key == "lockhotkey")
                        {
                            if (!string.IsNullOrEmpty(val)) _lockHotkeyStr = val;
                        }
                        else if (key == "startlocked")
                        {
                            bool b;
                            if (bool.TryParse(val, out b)) _startLocked = b;
                        }
                        else if (key == "blackscreen")
                        {
                            bool b;
                            if (bool.TryParse(val, out b)) _blackScreen = b;
                        }
                        else if (key == "waketimeout")
                        {
                            int to;
                            if (int.TryParse(val, out to) && to >= 0) _wakeTimeout = to;
                        }
                    }
                }
            }
            catch { }

            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i].ToLowerInvariant();
                    if ((a == "-p" || a == "-password") && i + 1 < args.Length)
                    {
                        _password = args[++i];
                    }
                    else if ((a == "-opacity" || a == "-o") && i + 1 < args.Length)
                    {
                        double op;
                        if (double.TryParse(args[++i], out op) && op >= 0.1 && op <= 0.95) _opacity = op;
                    }
                    else if (a == "-now")
                    {
                        _startLocked = true;
                    }
                    else if (a == "-noexclude")
                    {
                        _excludeFromCapture = false;
                    }
                    else if (a == "-black" || a == "-blackscreen")
                    {
                        _blackScreen = true;
                    }
                    else if (a == "-noblack")
                    {
                        _blackScreen = false;
                    }
                    else if (a == "-waketimeout" && i + 1 < args.Length)
                    {
                        int to;
                        if (int.TryParse(args[++i], out to) && to >= 0) _wakeTimeout = to;
                    }
                    else if (a == "-test")
                    {
                        _testMode = true;
                    }
                }
            }

            this.Opacity = _opacity;
            if (_lblTitle != null) _lblTitle.Text = _title;
            if (_lblNotice != null) _lblNotice.Text = _notice;
        }

        public void UpdateNotice(string newNotice)
        {
            _notice = newNotice;
            if (_lblNotice != null && !_lblNotice.IsDisposed)
            {
                _lblNotice.Text = newNotice;
            }
            Log("Notice updated: " + newNotice);
        }

        private void InitWindow()
        {
            Rectangle bounds = SystemInformation.VirtualScreen;
            this.Bounds = bounds;
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.BackColor = Color.FromArgb(12, 16, 24);
            this.Opacity = _opacity;
            this.DoubleBuffered = true;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        private void InitControls()
        {
            Rectangle primary = Screen.PrimaryScreen.Bounds;
            int cardW = 500;
            int cardH = 280;
            int cardX = primary.Left + (primary.Width - cardW) / 2 - this.Bounds.Left;
            int cardY = primary.Top + (primary.Height - cardH) / 2 - this.Bounds.Top;

            _cardScreenBounds = new Rectangle(primary.Left + (primary.Width - cardW) / 2,
                                              primary.Top + (primary.Height - cardH) / 2,
                                              cardW, cardH);

            _cardPanel = new CardPanel();
            _cardPanel.Size = new Size(cardW, cardH);
            _cardPanel.Location = new Point(cardX, cardY);
            _cardPanel.BackColor = Color.FromArgb(24, 30, 42);
            this.Controls.Add(_cardPanel);

            _lblIcon = new Label();
            _lblIcon.Text = "🛡️";
            _lblIcon.Font = new Font("Segoe UI Emoji", 28f, FontStyle.Regular);
            _lblIcon.ForeColor = Color.FromArgb(0, 229, 255);
            _lblIcon.TextAlign = ContentAlignment.MiddleCenter;
            _lblIcon.BackColor = Color.Transparent;
            _lblIcon.Size = new Size(cardW, 44);
            _lblIcon.Location = new Point(0, 16);
            _cardPanel.Controls.Add(_lblIcon);

            _lblTitle = new Label();
            _lblTitle.Text = _title;
            _lblTitle.Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold);
            _lblTitle.ForeColor = Color.FromArgb(240, 245, 255);
            _lblTitle.TextAlign = ContentAlignment.MiddleCenter;
            _lblTitle.BackColor = Color.Transparent;
            _lblTitle.Size = new Size(cardW - 40, 30);
            _lblTitle.Location = new Point(20, 64);
            _cardPanel.Controls.Add(_lblTitle);

            _lblNotice = new Label();
            _lblNotice.Text = _notice;
            _lblNotice.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular);
            _lblNotice.ForeColor = Color.FromArgb(160, 175, 200);
            _lblNotice.TextAlign = ContentAlignment.MiddleCenter;
            _lblNotice.BackColor = Color.Transparent;
            _lblNotice.Size = new Size(cardW - 40, 22);
            _lblNotice.Location = new Point(20, 98);
            _cardPanel.Controls.Add(_lblNotice);

            _txtPassword = new TextBox();
            _txtPassword.Font = new Font("Consolas", 15f, FontStyle.Bold);
            _txtPassword.PasswordChar = '●';
            _txtPassword.TextAlign = HorizontalAlignment.Center;
            _txtPassword.BackColor = Color.FromArgb(16, 20, 28);
            _txtPassword.ForeColor = Color.FromArgb(240, 245, 255);
            _txtPassword.BorderStyle = BorderStyle.FixedSingle;
            _txtPassword.Size = new Size(280, 36);
            _txtPassword.Location = new Point((cardW - 280) / 2, 134);
            _cardPanel.Controls.Add(_txtPassword);

            _btnUnlock = new Button();
            _btnUnlock.Text = "🔓 解 除 锁 定 (Enter)";
            _btnUnlock.Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
            _btnUnlock.BackColor = Color.FromArgb(0, 160, 210);
            _btnUnlock.ForeColor = Color.White;
            _btnUnlock.FlatStyle = FlatStyle.Flat;
            _btnUnlock.FlatAppearance.BorderSize = 0;
            _btnUnlock.Cursor = Cursors.Hand;
            _btnUnlock.Size = new Size(280, 40);
            _btnUnlock.Location = new Point((cardW - 280) / 2, 180);
            _btnUnlock.Click += (s, e) => TryUnlock();
            _cardPanel.Controls.Add(_btnUnlock);

            _lblError = new Label();
            _lblError.Text = "密码错误，请重新输入！";
            _lblError.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            _lblError.ForeColor = Color.FromArgb(255, 82, 82);
            _lblError.TextAlign = ContentAlignment.MiddleCenter;
            _lblError.BackColor = Color.Transparent;
            _lblError.Size = new Size(cardW - 40, 20);
            _lblError.Location = new Point(20, 230);
            _lblError.Visible = false;
            _cardPanel.Controls.Add(_lblError);
        }

        public void EnterSleepMode()
        {
            if (!_blackScreen || _isAsleep) return;
            _isAsleep = true;
            _cardPanel.Visible = false;
            this.BackColor = Color.Black;
            this.Opacity = 1.0;

            if (!_cursorHidden)
            {
                Cursor.Hide();
                _cursorHidden = true;
            }
            if (_sleepTimer != null) _sleepTimer.Stop();
            _trayContext.UpdateTrayTooltip("黑屏息屏挂机中 (键鼠输入唤醒)");
            Log("Entered black screen sleep mode");
        }

        public void WakeUp()
        {
            if (!_isAsleep)
            {
                _sleepCountdown = _wakeTimeout;
                return;
            }

            _isAsleep = false;
            this.BackColor = Color.FromArgb(12, 16, 24);
            this.Opacity = _opacity;
            _cardPanel.Visible = true;

            if (_cursorHidden)
            {
                Cursor.Show();
                _cursorHidden = false;
            }

            _txtPassword.Focus();
            _sleepCountdown = _wakeTimeout;
            if (_wakeTimeout > 0 && _sleepTimer != null)
            {
                _sleepTimer.Start();
            }
            _trayContext.UpdateTrayTooltip("挂机锁定中 (输入密码解锁)");
            Log("Woke up from black screen sleep mode");
        }

        public void ShowLock()
        {
            if (this.IsLocked)
            {
                if (_isAsleep)
                {
                    WakeUp();
                }
                else
                {
                    this.Activate();
                    _txtPassword.Focus();
                }
                return;
            }

            this.IsLocked = true;
            Log("ShowLock activated");

            _startTime = DateTime.Now;
            _txtPassword.Clear();
            _lblError.Visible = false;
            ReleaseModifierKeys();
            _ctrlDown = false;
            _shiftDown = false;
            _altDown = false;
            _winDown = false;
            _lastMousePos = Cursor.Position;

            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.TopMost = true;

            if (_excludeFromCapture)
            {
                Native.SetWindowDisplayAffinity(this.Handle, Native.WDA_EXCLUDEFROMCAPTURE);
            }

            Native.SetForegroundWindow(this.Handle);
            this.Activate();

            if (_blackScreen)
            {
                EnterSleepMode();
            }
            else
            {
                this.BackColor = Color.FromArgb(12, 16, 24);
                this.Opacity = _opacity;
                _cardPanel.Visible = true;
                _txtPassword.Focus();
            }

            InitHooks();

            if (!_blackScreen)
            {
                _trayContext.UpdateTrayTooltip("挂机锁定中 (输入密码解锁)");
            }
        }

        public void HideLock()
        {
            if (!this.IsLocked) return;

            this.IsLocked = false;
            Log("HideLock hidden to tray");

            if (_sleepTimer != null) _sleepTimer.Stop();

            if (_cursorHidden)
            {
                Cursor.Show();
                _cursorHidden = false;
            }
            _isAsleep = false;

            _ctrlDown = false;
            _shiftDown = false;
            _altDown = false;
            _winDown = false;

            UninstallHooks();
            ReleaseModifierKeys();

            this.Hide();

            _trayContext.UpdateTrayTooltip("后台就绪 (" + _lockHotkeyStr + " 锁定)");
        }

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

        public void TryUnlock()
        {
            string input = _txtPassword.Text;
            Log("TryUnlock input='" + input + "' expected='" + _password + "'");
            if (input == _password)
            {
                HideLock();
            }
            else
            {
                _lblError.Visible = true;
                _txtPassword.SelectAll();
                _txtPassword.Focus();
                ShakeCard();
            }
        }

        private void ShakeCard()
        {
            int origX = _cardPanel.Location.X;
            int origY = _cardPanel.Location.Y;
            int[] offsets = new int[] { -12, 12, -8, 8, -4, 4, 0 };

            System.Windows.Forms.Timer shakeTimer = new System.Windows.Forms.Timer();
            shakeTimer.Interval = 25;
            int step = 0;
            shakeTimer.Tick += (s, e) =>
            {
                if (step < offsets.Length)
                {
                    _cardPanel.Location = new Point(origX + offsets[step], origY);
                    step++;
                }
                else
                {
                    shakeTimer.Stop();
                    shakeTimer.Dispose();
                    _cardPanel.Location = new Point(origX, origY);
                }
            };
            shakeTimer.Start();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_NCHITTEST)
            {
                if (_isAsleep)
                {
                    m.Result = (IntPtr)Native.HTTRANSPARENT;
                    return;
                }

                short x = (short)(m.LParam.ToInt32() & 0xFFFF);
                short y = (short)((m.LParam.ToInt32() >> 16) & 0xFFFF);
                Point pt = new Point(x, y);

                if (_cardScreenBounds.Contains(pt))
                {
                    m.Result = (IntPtr)Native.HTCLIENT;
                    return;
                }
                else
                {
                    m.Result = (IntPtr)Native.HTTRANSPARENT;
                    return;
                }
            }

            base.WndProc(ref m);
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

    public class CardPanel : Panel
    {
        public CardPanel()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(1, 1, this.Width - 3, this.Height - 3);
            int radius = 16;
            using (GraphicsPath path = GetRoundedPath(rect, radius))
            {
                using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(24, 30, 42)))
                {
                    g.FillPath(bgBrush, path);
                }

                using (Pen borderPen = new Pen(Color.FromArgb(0, 180, 220), 2f))
                {
                    g.DrawPath(borderPen, path);
                }
            }
        }

        private GraphicsPath GetRoundedPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

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

    internal static class Native
    {
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_TRANSPARENT = 0x00000020;

        public const int WM_NCHITTEST = 0x0084;
        public const int HTTRANSPARENT = -1;
        public const int HTCLIENT = 1;

        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        public const int WH_KEYBOARD_LL = 13;
        public const int WH_MOUSE_LL = 14;

        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;

        public const int WM_MOUSEMOVE = 0x0200;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_LBUTTONUP = 0x0202;
        public const int WM_RBUTTONDOWN = 0x0204;
        public const int WM_RBUTTONUP = 0x0205;
        public const int WM_MBUTTONDOWN = 0x0207;
        public const int WM_MBUTTONUP = 0x0208;
        public const int WM_MOUSEWHEEL = 0x020A;

        public const uint LLKHF_INJECTED = 0x00000010;
        public const uint LLMHF_INJECTED = 0x00000001;

        public const uint WDA_NONE = 0x00000000;
        public const uint WDA_MONITOR = 0x00000001;
        public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        public const uint KEYEVENTF_KEYUP = 0x0002;

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool LockWorkStation();

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }
    }
}
