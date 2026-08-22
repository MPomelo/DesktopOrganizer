param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$appPath = Join-Path $projectRoot "src\DesktopOrganizer.App\bin\$Configuration\net8.0-windows\DesktopOrganizer.exe"
$artifactDirectory = Join-Path $projectRoot "artifacts"
$confirmationScreenshot = Join-Path $artifactDirectory "stage7-release-confirmation.png"
$resultScreenshot = Join-Path $artifactDirectory "stage7-release-result.png"
$testDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("DesktopOrganizer.Stage7." + [Guid]::NewGuid().ToString("N"))
$appData = Join-Path $testDirectory "AppData"
$desktop = Join-Path $testDirectory "Desktop"
$publicDesktop = Join-Path $testDirectory "PublicDesktop"
$startMenu = Join-Path $testDirectory "StartMenu"
$managedDirectory = Join-Path $appData "ManagedShortcuts"
$configPath = Join-Path $appData "config.json"
$process = $null

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class Stage7Mouse
{
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
}
'@

function Start-IsolatedApp {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new($appPath)
    $startInfo.UseShellExecute = $false
    $startInfo.Environment["DESKTOPORGANIZER_APPDATA"] = $appData
    $startInfo.Environment["DESKTOPORGANIZER_DESKTOP"] = $desktop
    $startInfo.Environment["DESKTOPORGANIZER_PUBLIC_DESKTOP"] = $publicDesktop
    $startInfo.Environment["DESKTOPORGANIZER_START_MENU"] = $startMenu
    $startInfo.Environment["DESKTOPORGANIZER_SKIP_STARTUP_SETTINGS"] = "1"
    return [System.Diagnostics.Process]::Start($startInfo)
}

function Find-Element {
    param(
        [int]$ProcessId,
        [string]$Name,
        [int]$TimeoutSeconds = 12,
        [System.Windows.Automation.ControlType]$ControlType = $null
    )
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $conditions = @(
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
                $ProcessId),
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::NameProperty,
                $Name)
        )
        if ($null -ne $ControlType) {
            $conditions += [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                $ControlType)
        }
        $condition = [System.Windows.Automation.AndCondition]::new($conditions)
        $element = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            $condition)
        if ($null -ne $element) {
            return $element
        }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)
    return $null
}

function Invoke-Element {
    param([System.Windows.Automation.AutomationElement]$Element)
    $pattern = $null
    if (-not $Element.TryGetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern,
        [ref]$pattern)) {
        throw "'$($Element.Current.Name)' does not expose InvokePattern."
    }
    ([System.Windows.Automation.InvokePattern]$pattern).Invoke()
}

function Set-RangeValue {
    param(
        [System.Windows.Automation.AutomationElement]$Element,
        [double]$Value
    )
    $pattern = $null
    if (-not $Element.TryGetCurrentPattern(
        [System.Windows.Automation.RangeValuePattern]::Pattern,
        [ref]$pattern)) {
        throw "'$($Element.Current.Name)' does not expose RangeValuePattern."
    }
    ([System.Windows.Automation.RangeValuePattern]$pattern).SetValue($Value)
}

function Save-ElementScreenshot {
    param(
        [System.Windows.Automation.AutomationElement]$Element,
        [string]$Path
    )
    $bounds = $Element.Current.BoundingRectangle
    $bitmap = [System.Drawing.Bitmap]::new([int]$bounds.Width, [int]$bounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen([int]$bounds.Left, [int]$bounds.Top, 0, 0, $bitmap.Size)
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function RightClick-Element {
    param([System.Windows.Automation.AutomationElement]$Element)
    $bounds = $Element.Current.BoundingRectangle
    [Stage7Mouse]::SetForegroundWindow([IntPtr]$Element.Current.NativeWindowHandle) | Out-Null
    [Stage7Mouse]::SetCursorPos(
        [int]($bounds.Left + $bounds.Width / 2),
        [int]($bounds.Top + $bounds.Height / 2)) | Out-Null
    [Stage7Mouse]::mouse_event(0x0008, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 80
    [Stage7Mouse]::mouse_event(0x0010, 0, 0, 0, [UIntPtr]::Zero)
}

function Stop-TestProcess {
    if ($null -ne $script:process -and -not $script:process.HasExited) {
        Stop-Process -Id $script:process.Id -Force
        $script:process.WaitForExit(5000) | Out-Null
    }
    $script:process = $null
}

try {
    foreach ($path in @($artifactDirectory, $appData, $desktop, $publicDesktop, $managedDirectory)) {
        New-Item -ItemType Directory -Path $path -Force | Out-Null
    }
    $managedPath = Join-Path $managedDirectory "managed.lnk"
    $sourceFile = Join-Path $testDirectory "Normal document.txt"
    $sourceUrl = Join-Path $testDirectory "Reference.url"
    Set-Content -LiteralPath $managedPath -Value "managed shortcut payload" -Encoding UTF8
    Set-Content -LiteralPath $sourceFile -Value "source must remain" -Encoding UTF8
    Set-Content -LiteralPath $sourceUrl -Value "[InternetShortcut]`r`nURL=https://example.com/stage7" -Encoding ASCII

    $containerId = [Guid]::NewGuid().ToString("N")
    $items = @(
        [ordered]@{
            id = "stage7-managed"; displayName = "托管图标"; kind = "Shortcut"
            sourcePath = $managedPath; launchTarget = ""; arguments = ""; workingDirectory = ""; iconSource = ""
            storageMode = "MovedToManagedStorage"; originalPath = (Join-Path $desktop "托管图标.lnk")
            managedPath = $managedPath; addedAt = [DateTimeOffset]::Now.ToString("O")
        },
        [ordered]@{
            id = "stage7-file"; displayName = "普通文档"; kind = "File"
            sourcePath = $sourceFile; launchTarget = $sourceFile; arguments = ""; workingDirectory = $testDirectory; iconSource = ""
            storageMode = "ReferenceOnly"; originalPath = $sourceFile; managedPath = ""; addedAt = [DateTimeOffset]::Now.ToString("O")
        },
        [ordered]@{
            id = "stage7-url"; displayName = "参考网址"; kind = "Url"
            sourcePath = $sourceUrl; launchTarget = $sourceUrl; arguments = ""; workingDirectory = ""; iconSource = ""
            storageMode = "ReferenceOnly"; originalPath = $sourceUrl; managedPath = ""; addedAt = [DateTimeOffset]::Now.ToString("O")
        }
    )
    $config = [ordered]@{
        schemaVersion = 1
        containers = @([ordered]@{
            id = $containerId; name = "阶段7自动化测试"; sizeMode = "TwoByTwo"; orientation = "Horizontal"
            left = 140; top = 140; hasSavedPosition = $true; monitorDeviceName = ""
            items = $items; pinnedIconIds = @("stage7-managed", "stage7-file", "stage7-url")
            style = [ordered]@{
                borderThickness = 4; backgroundOpacity = 0.45
                borderColor = [ordered]@{ mode = "SystemAccent" }
                backgroundColor = [ordered]@{ mode = "SystemAccent" }
                moreButtonColor = [ordered]@{ mode = "Auto" }
                hoverBackgroundColor = [ordered]@{ mode = "Auto" }
            }
        })
        theme = [ordered]@{}
        behavior = [ordered]@{
            hasChosenDesktopShortcutImportBehavior = $false
            moveDesktopShortcutsToManagedStorage = $true
            hasClosedAllContainers = $false
        }
    }
    $config | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $configPath -Encoding UTF8

    $process = Start-IsolatedApp
    $closeButton = Find-Element $process.Id "关闭收纳区"
    if ($null -eq $closeButton) { throw "Close button was not found." }
    Invoke-Element $closeButton
    $confirmation = Find-Element $process.Id "关闭并释放图标确认"
    if ($null -eq $confirmation) { throw "Release confirmation did not open." }
    Start-Sleep -Milliseconds 600
    Save-ElementScreenshot $confirmation $confirmationScreenshot
    Invoke-Element (Find-Element $process.Id "取消关闭")
    Start-Sleep -Milliseconds 400
    if (-not (Test-Path -LiteralPath $managedPath) -or (Test-Path -LiteralPath $startMenu)) {
        throw "Cancelling release modified icon files."
    }
    if ((Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json).containers.Count -ne 1) {
        throw "Cancelling release modified configuration."
    }

    $containerWindow = Find-Element $process.Id "阶段7自动化测试" 8 ([System.Windows.Automation.ControlType]::Window)
    RightClick-Element $containerWindow
    $hideItem = Find-Element $process.Id "隐藏收纳区" 5 ([System.Windows.Automation.ControlType]::MenuItem)
    if ($null -eq $hideItem) { throw "Hide context-menu item was not found." }
    Invoke-Element $hideItem
    Start-Sleep -Milliseconds 500
    if ($process.HasExited -or -not (Test-Path -LiteralPath $managedPath)) {
        throw "Hiding the container exited the app or released files."
    }
    Stop-TestProcess

    $process = Start-IsolatedApp
    $closeButton = Find-Element $process.Id "关闭收纳区"
    if ($null -eq $closeButton) { throw "Hidden container did not restore after restart." }
    Invoke-Element $closeButton
    $confirmation = Find-Element $process.Id "关闭并释放图标确认"
    Set-RangeValue (Find-Element $process.Id "释放到桌面的数量") 1
    Invoke-Element (Find-Element $process.Id "确认关闭")

    $resultWindow = Find-Element $process.Id "释放完成" 12 ([System.Windows.Automation.ControlType]::Window)
    if ($null -eq $resultWindow) { throw "Release result message did not appear." }
    Start-Sleep -Milliseconds 300
    Save-ElementScreenshot $resultWindow $resultScreenshot
    $okButton = Find-Element $process.Id "确定" 5 ([System.Windows.Automation.ControlType]::Button)
    if ($null -ne $okButton) { Invoke-Element $okButton }
    Start-Sleep -Milliseconds 700

    $savedConfig = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    $checks = @(
        $savedConfig.containers.Count -eq 0
        $savedConfig.behavior.hasClosedAllContainers -eq $true
        (Test-Path -LiteralPath (Join-Path $desktop "托管图标.lnk"))
        (Test-Path -LiteralPath (Join-Path $startMenu "普通文档.lnk"))
        (Test-Path -LiteralPath (Join-Path $startMenu "参考网址.url"))
        (Test-Path -LiteralPath $sourceFile)
        (Test-Path -LiteralPath $sourceUrl)
        (-not (Test-Path -LiteralPath $managedPath))
        (-not (Test-Path -LiteralPath (Join-Path $appData "release-journal.json")))
        (-not $process.HasExited)
    )
    if ($checks -contains $false) { throw "Stage 7 release output did not match the confirmed plan." }

    Stop-TestProcess
    $process = Start-IsolatedApp
    Start-Sleep -Milliseconds 800
    if ($process.HasExited) { throw "Tray-only startup exited after the last container was closed." }
    if ($null -ne (Find-Element $process.Id "阶段7自动化测试" 1 ([System.Windows.Automation.ControlType]::Window))) {
        throw "Closed container was recreated on startup."
    }

    [pscustomobject]@{
        PreviewCancelledWithoutChanges = $true
        HideRetainedConfiguration = $true
        RestartRestoredHiddenContainer = $true
        DesktopReleased = 1
        StartMenuReleased = 2
        TrayRemainedRunning = $true
        ClosedContainerStayedClosed = $true
        ConfirmationScreenshot = $confirmationScreenshot
        ResultScreenshot = $resultScreenshot
    } | Format-List
}
finally {
    Stop-TestProcess
    if (Test-Path -LiteralPath $testDirectory) {
        Remove-Item -LiteralPath $testDirectory -Recurse -Force
    }
}
