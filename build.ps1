$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}

Write-Host "[FakeLock] 正在编译 FakeLock.cs..." -ForegroundColor Cyan
& $csc /target:winexe /optimize+ /platform:anycpu /r:System.dll,System.Windows.Forms.dll,System.Drawing.dll /out:"$PSScriptRoot\FakeLock.exe" "$PSScriptRoot\FakeLock.cs"

if ($LASTEXITCODE -eq 0) {
    Write-Host "[FakeLock] 编译成功: FakeLock.exe" -ForegroundColor Green
} else {
    Write-Host "[FakeLock] 编译失败，退出码: $LASTEXITCODE" -ForegroundColor Red
}
