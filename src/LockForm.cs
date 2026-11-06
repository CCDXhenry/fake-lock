using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace FakeLock
{
    public partial class LockForm : Form
    {
        private TrayContext _trayContext;

        private string _password = "1234";
        private double _opacity = 0.55;
        private string _title = "Roblox Studio 实机挂机测试中";
        private string _notice = "AI 正在前台渲染并操作测试，物理键鼠已锁定";
        private bool _excludeFromCapture = true;
        private string _lockHotkeyStr = "Alt+L";
        private bool _startLocked = false;
        private bool? _autoStartConfig = null;
        private bool _testMode = false;

        public bool StartLocked { get { return _startLocked; } }
        public bool? AutoStartConfig { get { return _autoStartConfig; } }
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
                    sb.AppendLine("; 是否开机自启动（true: 登录 Windows 时自动在后台托盘常驻; false: 手动启动）");
                    sb.AppendLine("AutoStart=false");
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
                        else if (key == "autostart")
                        {
                            bool b;
                            if (bool.TryParse(val, out b)) _autoStartConfig = b;
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
                    else if ((a == "-autostart" || a == "-auto") && i + 1 < args.Length)
                    {
                        string val = args[++i].ToLowerInvariant();
                        if (val == "true" || val == "1" || val == "on" || val == "enable") _autoStartConfig = true;
                        else if (val == "false" || val == "0" || val == "off" || val == "disable") _autoStartConfig = false;
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
    }
}
