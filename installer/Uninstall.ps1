$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms

$productName = "DesktopOrganizer"
$installDirectory = $PSScriptRoot
$programsDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$desktopDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
$startMenuDirectory = Join-Path $programsDirectory $productName
$desktopShortcut = Join-Path $desktopDirectory "DesktopOrganizer.lnk"
$appDataDirectory = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)) "DesktopOrganizer"
$uninstallRegistryPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DesktopOrganizer"

$answer = [System.Windows.Forms.MessageBox]::Show(
    "确定要卸载 DesktopOrganizer 吗？`r`n`r`n收纳区中的图标不会由卸载程序自动释放。建议先在设置中使用“彻底退出收纳区”。",
    "卸载 DesktopOrganizer",
    [System.Windows.Forms.MessageBoxButtons]::YesNo,
    [System.Windows.Forms.MessageBoxIcon]::Warning,
    [System.Windows.Forms.MessageBoxDefaultButton]::Button2)
if ($answer -ne [System.Windows.Forms.DialogResult]::Yes) {
    exit 0
}

@(Get-Process -Name "DesktopOrganizer" -ErrorAction SilentlyContinue) | Stop-Process -Force
Start-Sleep -Milliseconds 300

if (Test-Path -LiteralPath $desktopShortcut) {
    Remove-Item -LiteralPath $desktopShortcut -Force
}
if (Test-Path -LiteralPath $startMenuDirectory) {
    Remove-Item -LiteralPath $startMenuDirectory -Recurse -Force
}
if (Test-Path -LiteralPath $uninstallRegistryPath) {
    Remove-Item -LiteralPath $uninstallRegistryPath -Recurse -Force
}

$removeData = [System.Windows.Forms.MessageBox]::Show(
    "是否同时删除保存的收纳区配置、图标缓存和日志？`r`n`r`n选择“否”可在以后重新安装时继续使用原配置。",
    "保留用户数据",
    [System.Windows.Forms.MessageBoxButtons]::YesNo,
    [System.Windows.Forms.MessageBoxIcon]::Question,
    [System.Windows.Forms.MessageBoxDefaultButton]::Button2)
if ($removeData -eq [System.Windows.Forms.DialogResult]::Yes -and (Test-Path -LiteralPath $appDataDirectory)) {
    Remove-Item -LiteralPath $appDataDirectory -Recurse -Force
}

$escapedInstallDirectory = $installDirectory.Replace("'", "''")
$cleanupPath = Join-Path $env:TEMP ("DesktopOrganizer-Uninstall-" + [Guid]::NewGuid().ToString("N") + ".ps1")
$cleanupScript = @"
Start-Sleep -Seconds 2
Remove-Item -LiteralPath '$escapedInstallDirectory' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath `$MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue
"@
[System.IO.File]::WriteAllText($cleanupPath, $cleanupScript, [System.Text.UTF8Encoding]::new($true))
Start-Process -FilePath "powershell.exe" `
    -ArgumentList @("-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$cleanupPath`"") `
    -WindowStyle Hidden

[System.Windows.Forms.MessageBox]::Show(
    "DesktopOrganizer 已卸载。",
    "卸载完成",
    [System.Windows.Forms.MessageBoxButtons]::OK,
    [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
