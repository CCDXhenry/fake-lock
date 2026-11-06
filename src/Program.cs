using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

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

            // 处理命令行控制指令（跨进程通信与配置）
            if (args != null && args.Length > 0)
            {
                Native.AttachConsole(Native.ATTACH_PARENT_PROCESS);
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
                    if (a == "-autostart" || a == "-auto")
                    {
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                        {
                            string val = args[++i].ToLowerInvariant();
                            bool enable = (val == "true" || val == "1" || val == "on" || val == "enable");
                            bool ok = AutoStartHelper.SetAutoStart(enable);
                            AutoStartHelper.UpdateConfigAutoStart(enable);
                            Console.WriteLine(ok ? ("[FakeLock] 开机自启动设置成功: " + (enable ? "已开启" : "已关闭"))
                                                 : "[FakeLock] 开机自启动设置失败！");
                        }
                        else
                        {
                            bool current = AutoStartHelper.IsAutoStartEnabled();
                            Console.WriteLine("[FakeLock] 当前开机自启动状态: " + (current ? "已开启" : "已关闭"));
                        }
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
}
