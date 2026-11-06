using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FakeLock
{
    internal static class AutoStartHelper
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppValueName = "FakeLock";

        public static string GetCurrentExecutablePath()
        {
            try
            {
                string path = Application.ExecutablePath;
                if (!string.IsNullOrEmpty(path)) return path;
            }
            catch { }

            try
            {
                using (Process proc = Process.GetCurrentProcess())
                {
                    if (proc.MainModule != null)
                    {
                        return proc.MainModule.FileName;
                    }
                }
            }
            catch { }

            return AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\') + "\\FakeLock.exe";
        }

        public static bool IsAutoStartEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null) return false;
                    object val = key.GetValue(AppValueName);
                    if (val == null) return false;

                    string regPath = val.ToString().Trim().Trim('\"');
                    string exePath = GetCurrentExecutablePath();
                    return string.Equals(regPath, exePath, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool SetAutoStart(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null) return false;
                    if (enable)
                    {
                        string exePath = GetCurrentExecutablePath();
                        string quoted = "\"" + exePath + "\"";
                        key.SetValue(AppValueName, quoted, RegistryValueKind.String);
                    }
                    else
                    {
                        if (key.GetValue(AppValueName) != null)
                        {
                            key.DeleteValue(AppValueName, false);
                        }
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FakeLock] 修改注册表自启动失败: " + ex.Message);
                return false;
            }
        }

        public static void SyncWithConfig(bool desiredState)
        {
            try
            {
                bool current = IsAutoStartEnabled();
                if (current != desiredState)
                {
                    SetAutoStart(desiredState);
                }
                else if (desiredState)
                {
                    // 即使已开启，也确认路径是否匹配当前 exe 位置（防止目录移动后路径失效）
                    SetAutoStart(true);
                }
            }
            catch { }
        }

        public static void UpdateConfigAutoStart(bool enable)
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
                if (!File.Exists(configPath)) return;

                string[] lines = File.ReadAllLines(configPath, Encoding.UTF8);
                bool found = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.StartsWith("AutoStart=", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = "AutoStart=" + (enable ? "true" : "false");
                        found = true;
                        break;
                    }
                }

                if (found)
                {
                    File.WriteAllLines(configPath, lines, Encoding.UTF8);
                }
                else
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (string l in lines) sb.AppendLine(l);
                    sb.AppendLine("; 是否开机自启动（true: 登录 Windows 时自动在后台托盘常驻; false: 手动启动）");
                    sb.AppendLine("AutoStart=" + (enable ? "true" : "false"));
                    File.WriteAllText(configPath, sb.ToString(), Encoding.UTF8);
                }
            }
            catch { }
        }
    }
}
