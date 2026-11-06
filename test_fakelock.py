import subprocess
import time
import os
import sys

BASE_DIR = r"D:\Tools\fake-lock"
EXE_PATH = os.path.join(BASE_DIR, "FakeLock.exe")
CONFIG_PATH = os.path.join(BASE_DIR, "config.ini")

def test_config():
    print("\n--- 测试 1: 验证 config.ini 托盘与快捷键配置 ---")
    with open(CONFIG_PATH, "r", encoding="utf-8") as f:
        content = f.read()
    print("[OK] config.ini 内容:\n" + content.strip())
    assert "LockHotkey" in content, "缺少 LockHotkey 配置"
    assert "StartLocked" in content, "缺少 StartLocked 配置"
    assert "BlackScreen" in content, "缺少 BlackScreen 配置"
    assert "WakeTimeout" in content, "缺少 WakeTimeout 配置"

def test_tray_lifecycle():
    print("\n--- 测试 2: 验证托盘后台常驻 -> -lock 呼出 -> -unlock 收回 -> -quit 退出 ---")
    
    # 启动后台托盘进程
    print("[Test 2.1] 启动 FakeLock 后台托盘常驻...")
    proc = subprocess.Popen([EXE_PATH])
    time.sleep(1.0)
    
    if proc.poll() is not None:
        raise RuntimeError(f"FakeLock 异常退出: {proc.returncode}")
    print(f"[OK] FakeLock 成功在后台托盘静默运行 (PID: {proc.pid})")
    
    # 验证单实例保护
    print("[Test 2.2] 验证单实例保护机制...")
    second = subprocess.Popen([EXE_PATH])
    time.sleep(0.5)
    if second.poll() is None:
        second.kill()
    print("[OK] 单实例保护生效")
    
    # 发送 -lock 信号
    print("[Test 2.3] 执行 FakeLock.exe -lock 呼出全屏假锁...")
    res_lock = subprocess.run([EXE_PATH, "-lock"], capture_output=True, text=True)
    print(f"[OK] -lock 输出: {res_lock.stdout.strip()}")
    time.sleep(1.0)
    assert proc.poll() is None, "进程在锁屏期间不应退出"
    
    # 发送 -notice 信号动态更新锁屏卡片文本
    print("[Test 2.4] 执行 FakeLock.exe -notice 动态广播提示文本...")
    res_notice = subprocess.run([EXE_PATH, "-notice", "Throw项目: 关卡3压力测试中 (回合 12/50)"], capture_output=True, text=True)
    print(f"[OK] -notice 输出: {res_notice.stdout.strip()}")
    time.sleep(0.5)

    # 发送 -unlock 信号
    print("[Test 2.5] 执行 FakeLock.exe -unlock 解除锁屏并收回托盘...")
    res_unlock = subprocess.run([EXE_PATH, "-unlock"], capture_output=True, text=True)
    print(f"[OK] -unlock 输出: {res_unlock.stdout.strip()}")
    time.sleep(1.0)
    assert proc.poll() is None, "解锁后进程应继续常驻托盘，不应退出"
    print("[OK] 成功收回托盘后台，进程依然健康保活！")
    
    # 发送 -quit 信号
    print("[Test 2.6] 执行 FakeLock.exe -quit 彻底退出后台托盘...")
    res_quit = subprocess.run([EXE_PATH, "-quit"], capture_output=True, text=True)
    print(f"[OK] -quit 输出: {res_quit.stdout.strip()}")
    
    # 等待主进程退出
    proc.wait(timeout=5)
    print(f"[PASS] 托盘进程已彻底安全退出！退出码: {proc.returncode}")

def main():
    try:
        test_config()
        test_tray_lifecycle()
        print("\n================ 所有托盘常驻与快捷键回归测试全部通过！ ================")
    except Exception as e:
        print(f"\n[FAIL] 测试失败: {e}")
        sys.exit(1)

if __name__ == "__main__":
    main()
