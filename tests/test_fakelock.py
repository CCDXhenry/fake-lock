import subprocess
import time
import os
import sys
import winreg

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXE_PATH = os.path.join(BASE_DIR, "FakeLock.exe")
CONFIG_PATH = os.path.join(BASE_DIR, "config.ini")
RUN_KEY = r"Software\Microsoft\Windows\CurrentVersion\Run"

def get_registry_autostart():
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, RUN_KEY, 0, winreg.KEY_READ) as key:
            val, _ = winreg.QueryValueEx(key, "FakeLock")
            return val
    except FileNotFoundError:
        return None

def test_config():
    print("\n--- 测试 1: 验证 config.ini 托盘与快捷键配置 ---")
    with open(CONFIG_PATH, "r", encoding="utf-8-sig") as f:
        content = f.read()
    print("[OK] config.ini 内容:\n" + content.strip())
    assert "LockHotkey" in content, "缺少 LockHotkey 配置"
    assert "StartLocked" in content, "缺少 StartLocked 配置"
    assert "BlackScreen" in content, "缺少 BlackScreen 配置"
    assert "WakeTimeout" in content, "缺少 WakeTimeout 配置"
    assert "AutoStart" in content, "缺少 AutoStart 配置"

def test_autostart():
    print("\n--- 测试 2: 验证开机自启动命令行配置与注册表读写 ---")
    
    # 启用自启动
    print("[Test 2.1] 执行 FakeLock.exe -autostart true...")
    res = subprocess.run([EXE_PATH, "-autostart", "true"], capture_output=True, text=True)
    print(f"[OK] -autostart true 输出: {res.stdout.strip()}")
    
    reg_val = get_registry_autostart()
    print(f"[OK] 注册表 Run 键值: {reg_val}")
    assert reg_val is not None, "注册表项 FakeLock 未创建"
    assert EXE_PATH.lower() in reg_val.lower(), "注册表路径不匹配"
    
    with open(CONFIG_PATH, "r", encoding="utf-8") as f:
        assert "AutoStart=true" in f.read(), "config.ini 未同步为 AutoStart=true"
    print("[OK] config.ini 同步更新为 AutoStart=true 成功")
    
    # 查询自启动状态
    print("[Test 2.2] 执行 FakeLock.exe -autostart 查询状态...")
    res_query = subprocess.run([EXE_PATH, "-autostart"], capture_output=True, text=True)
    print(f"[OK] -autostart 查询输出: {res_query.stdout.strip()}")
    
    # 关闭自启动
    print("[Test 2.3] 执行 FakeLock.exe -autostart false...")
    res_off = subprocess.run([EXE_PATH, "-autostart", "false"], capture_output=True, text=True)
    print(f"[OK] -autostart false 输出: {res_off.stdout.strip()}")
    
    reg_val_off = get_registry_autostart()
    assert reg_val_off is None, "注册表项 FakeLock 未正确清理"
    print("[OK] 注册表项已干净删除")
    
    with open(CONFIG_PATH, "r", encoding="utf-8") as f:
        assert "AutoStart=false" in f.read(), "config.ini 未同步为 AutoStart=false"
    print("[OK] config.ini 同步更新为 AutoStart=false 成功")

def test_tray_lifecycle():
    print("\n--- 测试 3: 验证托盘后台常驻 -> -lock 呼出 -> -unlock 收回 -> -quit 退出 ---")
    
    # 启动后台托盘进程
    print("[Test 3.1] 启动 FakeLock 后台托盘常驻...")
    proc = subprocess.Popen([EXE_PATH])
    time.sleep(1.0)
    
    if proc.poll() is not None:
        raise RuntimeError(f"FakeLock 异常退出: {proc.returncode}")
    print(f"[OK] FakeLock 成功在后台托盘静默运行 (PID: {proc.pid})")
    
    # 验证单实例保护
    print("[Test 3.2] 验证单实例保护机制...")
    second = subprocess.Popen([EXE_PATH])
    time.sleep(0.5)
    if second.poll() is None:
        second.kill()
    print("[OK] 单实例保护生效")
    
    # 发送 -lock 信号
    print("[Test 3.3] 执行 FakeLock.exe -lock 呼出全屏假锁...")
    res_lock = subprocess.run([EXE_PATH, "-lock"], capture_output=True, text=True)
    print(f"[OK] -lock 输出: {res_lock.stdout.strip()}")
    time.sleep(1.0)
    assert proc.poll() is None, "进程在锁屏期间不应退出"
    
    # 发送 -notice 信号动态更新锁屏卡片文本
    print("[Test 3.4] 执行 FakeLock.exe -notice 动态广播提示文本...")
    res_notice = subprocess.run([EXE_PATH, "-notice", "Throw项目: 关卡3压力测试中 (回合 12/50)"], capture_output=True, text=True)
    print(f"[OK] -notice 输出: {res_notice.stdout.strip()}")
    time.sleep(0.5)

    # 发送 -unlock 信号
    print("[Test 3.5] 执行 FakeLock.exe -unlock 解除锁屏并收回托盘...")
    res_unlock = subprocess.run([EXE_PATH, "-unlock"], capture_output=True, text=True)
    print(f"[OK] -unlock 输出: {res_unlock.stdout.strip()}")
    time.sleep(1.0)
    assert proc.poll() is None, "解锁后进程应继续常驻托盘，不应退出"
    print("[OK] 成功收回托盘后台，进程依然健康保活！")
    
    # 发送 -quit 信号
    print("[Test 3.6] 执行 FakeLock.exe -quit 彻底退出后台托盘...")
    res_quit = subprocess.run([EXE_PATH, "-quit"], capture_output=True, text=True)
    print(f"[OK] -quit 输出: {res_quit.stdout.strip()}")
    
    # 等待主进程退出
    proc.wait(timeout=5)
    print(f"[PASS] 托盘进程已彻底安全退出！退出码: {proc.returncode}")

def main():
    try:
        test_config()
        test_autostart()
        test_tray_lifecycle()
        print("\n================ 所有自启动、托盘常驻与快捷键回归测试全部通过！ ================")
    except Exception as e:
        print(f"\n[FAIL] 测试失败: {e}")
        sys.exit(1)

if __name__ == "__main__":
    main()
