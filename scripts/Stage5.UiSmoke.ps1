param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$appPath = Join-Path $projectRoot "src\DesktopOrganizer.App\bin\$Configuration\net8.0-windows\DesktopOrganizer.exe"
$testDirectory = Join-Path $projectRoot ("artifacts\stage5-ui-smoke-" + [Guid]::NewGuid().ToString("N"))
$configPath = Join-Path $testDirectory "config.json"
$markerPath = Join-Path $testDirectory "launch-marker.txt"
$vbsPath = Join-Path $testDirectory "record-launch.vbs"
$screenshotPath = Join-Path $projectRoot "artifacts\stage5-expanded-window.png"
$process = $null

if (-not (Test-Path -LiteralPath $appPath)) {
    throw "DesktopOrganizer.exe was not found at '$appPath'. Build the solution first."
}

function Find-AppElementByName {
    param(
        [int]$ProcessId,
        [string]$Name,
        [int]$TimeoutSeconds = 15
    )

    $processCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $ProcessId)
    $nameCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)

    do {
        $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            $processCondition)
        foreach ($window in $windows) {
            if ($window.Current.Name -eq $Name) {
                return $window
            }

            $element = $window.FindFirst(
                [System.Windows.Automation.TreeScope]::Descendants,
                $nameCondition)
            if ($null -ne $element) {
                return $element
            }
        }

        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)

    return $null
}

function Get-InvokePattern {
    param([System.Windows.Automation.AutomationElement]$Element)

    $patternObject = $null
    if (-not $Element.TryGetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern,
        [ref]$patternObject)) {
        throw "The '$($Element.Current.Name)' element does not expose the Invoke pattern."
    }

    return [System.Windows.Automation.InvokePattern]$patternObject
}

try {
    New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null

    @'
Set fileSystem = CreateObject("Scripting.FileSystemObject")
Set marker = fileSystem.OpenTextFile(WScript.Arguments(0), 8, True)
marker.WriteLine Now
marker.Close
'@ | Set-Content -LiteralPath $vbsPath -Encoding ASCII

    $wscriptPath = Join-Path $env:WINDIR "System32\wscript.exe"
    $items = @()
    $pinnedIds = @()
    for ($index = 1; $index -le 30; $index++) {
        $itemId = "stage5-item-{0:D2}" -f $index
        $pinnedIds += $itemId
        $isLaunchProbe = $index -eq 1
        $items += [ordered]@{
            id = $itemId
            displayName = "阶段5图标 {0:D2}" -f $index
            kind = if ($isLaunchProbe) { "Shortcut" } else { "File" }
            sourcePath = if ($isLaunchProbe) { $wscriptPath } else { $vbsPath }
            launchTarget = if ($isLaunchProbe) { $wscriptPath } else { "" }
            arguments = if ($isLaunchProbe) { ('"{0}" "{1}"' -f $vbsPath, $markerPath) } else { "" }
            workingDirectory = $testDirectory
            iconSource = ""
            storageMode = "ReferenceOnly"
            originalPath = ""
            managedPath = ""
            addedAt = [DateTimeOffset]::Now.AddMinutes($index).ToString("O")
        }
    }

    $config = [ordered]@{
        schemaVersion = 1
        containers = @(
            [ordered]@{
                id = [Guid]::NewGuid().ToString("N")
                name = "阶段5自动化测试"
                sizeMode = "TwoByTwo"
                orientation = "Horizontal"
                left = 120
                top = 120
                hasSavedPosition = $true
                monitorDeviceName = ""
                items = $items
                pinnedIconIds = $pinnedIds
                style = [ordered]@{
                    borderThickness = 4
                    backgroundOpacity = 0.45
                    borderColor = [ordered]@{ mode = "SystemAccent" }
                    backgroundColor = [ordered]@{ mode = "SystemAccent" }
                    moreButtonColor = [ordered]@{ mode = "Auto" }
                    hoverBackgroundColor = [ordered]@{ mode = "Auto" }
                }
            }
        )
        theme = [ordered]@{}
        behavior = [ordered]@{
            hasChosenDesktopShortcutImportBehavior = $false
            moveDesktopShortcutsToManagedStorage = $true
        }
    }
    $config | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $configPath -Encoding UTF8

    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    Add-Type -AssemblyName System.Drawing
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class Stage5User32
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
'@

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new($appPath)
    $startInfo.UseShellExecute = $false
    $startInfo.Environment["DESKTOPORGANIZER_APPDATA"] = $testDirectory
    $startInfo.Environment["DESKTOPORGANIZER_SKIP_STARTUP_SETTINGS"] = "1"
    $process = [System.Diagnostics.Process]::Start($startInfo)

    $moreButton = Find-AppElementByName -ProcessId $process.Id -Name "显示全部图标"
    if ($null -eq $moreButton) {
        throw "The compact container's three-dot button was not found."
    }
    (Get-InvokePattern -Element $moreButton).Invoke()

    $expandedWindow = Find-AppElementByName -ProcessId $process.Id -Name "全部图标"
    if ($null -eq $expandedWindow -or $expandedWindow.Current.ControlType -ne [System.Windows.Automation.ControlType]::Window) {
        throw "The expanded icon window did not open."
    }

    $missingNames = @()
    for ($index = 1; $index -le 30; $index++) {
        $name = "阶段5图标 {0:D2}" -f $index
        $nameCondition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $name)
        $itemButton = $expandedWindow.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            $nameCondition)
        if ($null -eq $itemButton) {
            $missingNames += $name
        }
    }
    if ($missingNames.Count -gt 0) {
        throw "Expanded window omitted icons: $($missingNames -join ', ')"
    }

    $scrollRegion = Find-AppElementByName -ProcessId $process.Id -Name "全部图标滚动区"
    $scrollPatternObject = $null
    if ($null -eq $scrollRegion -or -not $scrollRegion.TryGetCurrentPattern(
        [System.Windows.Automation.ScrollPattern]::Pattern,
        [ref]$scrollPatternObject)) {
        throw "The expanded icon collection does not expose scrolling."
    }
    $scrollPattern = [System.Windows.Automation.ScrollPattern]$scrollPatternObject
    if (-not $scrollPattern.Current.VerticallyScrollable) {
        throw "The 30-item expanded collection is not vertically scrollable."
    }
    $isVerticallyScrollable = $scrollPattern.Current.VerticallyScrollable

    $bounds = $expandedWindow.Current.BoundingRectangle
    $bitmap = [System.Drawing.Bitmap]::new([int]$bounds.Width, [int]$bounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen(
            [int]$bounds.Left,
            [int]$bounds.Top,
            0,
            0,
            $bitmap.Size)
        $bitmap.Save($screenshotPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }

    $launchButton = Find-AppElementByName -ProcessId $process.Id -Name "阶段5图标 01"
    (Get-InvokePattern -Element $launchButton).Invoke()

    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 100
        $markerLines = if (Test-Path -LiteralPath $markerPath) {
            @(Get-Content -LiteralPath $markerPath)
        }
        else {
            @()
        }
    } while ($markerLines.Count -lt 1 -and [DateTime]::UtcNow -lt $deadline)
    if ($markerLines.Count -ne 1) {
        throw "The expanded icon click launched the target $($markerLines.Count) times instead of once."
    }

    $lastLaunchedAt = $null
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 100
        try {
            $savedConfig = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
            $lastLaunchedAt = $savedConfig.containers[0].items[0].lastLaunchedAt
        }
        catch {
            $lastLaunchedAt = $null
        }
    } while ([string]::IsNullOrWhiteSpace($lastLaunchedAt) -and [DateTime]::UtcNow -lt $deadline)
    if ([string]::IsNullOrWhiteSpace($lastLaunchedAt)) {
        throw "The expanded icon launch did not persist lastLaunchedAt."
    }

    $windowHandle = [IntPtr]$expandedWindow.Current.NativeWindowHandle
    [Stage5User32]::PostMessage($windowHandle, 0x0100, [IntPtr]0x1B, [IntPtr]::Zero) | Out-Null
    [Stage5User32]::PostMessage($windowHandle, 0x0101, [IntPtr]0x1B, [IntPtr]::Zero) | Out-Null

    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 100
        $remainingExpandedWindow = Find-AppElementByName -ProcessId $process.Id -Name "全部图标" -TimeoutSeconds 0
    } while ($null -ne $remainingExpandedWindow -and [DateTime]::UtcNow -lt $deadline)
    if ($null -ne $remainingExpandedWindow) {
        throw "Escape did not close the expanded window."
    }

    (Get-InvokePattern -Element $moreButton).Invoke()
    $outsideCloseWindow = Find-AppElementByName -ProcessId $process.Id -Name "全部图标"
    if ($null -eq $outsideCloseWindow) {
        throw "The expanded window did not reopen for the outside-close check."
    }
    $moreButton.SetFocus()

    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 100
        $remainingExpandedWindow = Find-AppElementByName -ProcessId $process.Id -Name "全部图标" -TimeoutSeconds 0
    } while ($null -ne $remainingExpandedWindow -and [DateTime]::UtcNow -lt $deadline)
    if ($null -ne $remainingExpandedWindow) {
        throw "Moving focus outside did not close the expanded window."
    }

    [pscustomobject]@{
        ExpandedWindow = "opened"
        VisibleIconCount = 30
        VerticallyScrollable = $isVerticallyScrollable
        ExpandedLaunchCount = $markerLines.Count
        LastLaunchedAt = $lastLaunchedAt
        EscapeClosed = $true
        OutsideFocusClosed = $true
        Screenshot = $screenshotPath
    } | Format-List
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit(5000) | Out-Null
    }

    if (Test-Path -LiteralPath $testDirectory) {
        Remove-Item -LiteralPath $testDirectory -Recurse -Force
    }
}
