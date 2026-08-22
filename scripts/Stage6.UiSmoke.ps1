param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$appPath = Join-Path $projectRoot "src\DesktopOrganizer.App\bin\$Configuration\net8.0-windows\DesktopOrganizer.exe"
$testDirectory = Join-Path $projectRoot ("artifacts\stage6-ui-smoke-" + [Guid]::NewGuid().ToString("N"))
$configPath = Join-Path $testDirectory "config.json"
$screenshotPath = Join-Path $projectRoot "artifacts\stage6-settings-window.png"
$containerScreenshotPath = Join-Path $projectRoot "artifacts\stage6-custom-container.png"
$process = $null

if (-not (Test-Path -LiteralPath $appPath)) {
    throw "DesktopOrganizer.exe was not found at '$appPath'. Build the solution first."
}

function Get-AppWindows {
    param([int]$ProcessId)
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $ProcessId)
    return [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        $condition)
}

function Find-AppElementByName {
    param(
        [int]$ProcessId,
        [string]$Name,
        [int]$TimeoutSeconds = 15
    )

    $nameCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        foreach ($window in (Get-AppWindows -ProcessId $ProcessId)) {
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

function Find-AppControlByName {
    param(
        [int]$ProcessId,
        [string]$Name,
        [System.Windows.Automation.ControlType]$ControlType,
        [int]$TimeoutSeconds = 15
    )
    $nameCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)
    $typeCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        $ControlType)
    $condition = [System.Windows.Automation.AndCondition]::new($nameCondition, $typeCondition)
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        foreach ($window in (Get-AppWindows -ProcessId $ProcessId)) {
            $element = $window.FindFirst(
                [System.Windows.Automation.TreeScope]::Descendants,
                $condition)
            if ($null -ne $element) {
                return $element
            }
        }
        $settingsHandle = [Stage6User32]::FindWindowHandle([uint32]$ProcessId, "DesktopOrganizer 设置")
        if ($settingsHandle -ne [IntPtr]::Zero) {
            $settingsRoot = [System.Windows.Automation.AutomationElement]::FromHandle($settingsHandle)
            $element = $settingsRoot.FindFirst(
                [System.Windows.Automation.TreeScope]::Descendants,
                $condition)
            if ($null -ne $element) {
                return $element
            }
        }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)
    return $null
}

function Find-AppWindowByName {
    param(
        [int]$ProcessId,
        [string]$Name,
        [int]$TimeoutSeconds = 15
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        foreach ($window in (Get-AppWindows -ProcessId $ProcessId)) {
            if ($window.Current.Name -eq $Name) {
                return $window
            }
        }
        $nativeHandle = [Stage6User32]::FindWindowHandle([uint32]$ProcessId, $Name)
        if ($nativeHandle -ne [IntPtr]::Zero) {
            return [System.Windows.Automation.AutomationElement]::FromHandle($nativeHandle)
        }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)
    return $null
}

function Get-ElementPattern {
    param(
        [System.Windows.Automation.AutomationElement]$Element,
        [System.Windows.Automation.AutomationPattern]$Pattern
    )
    $patternObject = $null
    if (-not $Element.TryGetCurrentPattern($Pattern, [ref]$patternObject)) {
        throw "The '$($Element.Current.Name)' element does not expose the requested UI Automation pattern."
    }
    return $patternObject
}

function Invoke-Element {
    param([System.Windows.Automation.AutomationElement]$Element)
    $pattern = Get-ElementPattern -Element $Element -Pattern ([System.Windows.Automation.InvokePattern]::Pattern)
    ([System.Windows.Automation.InvokePattern]$pattern).Invoke()
}

function Set-ElementValue {
    param(
        [System.Windows.Automation.AutomationElement]$Element,
        [string]$Value
    )
    $pattern = Get-ElementPattern -Element $Element -Pattern ([System.Windows.Automation.ValuePattern]::Pattern)
    ([System.Windows.Automation.ValuePattern]$pattern).SetValue($Value)
}

function Set-RangeValue {
    param(
        [System.Windows.Automation.AutomationElement]$Element,
        [double]$Value
    )
    $pattern = Get-ElementPattern -Element $Element -Pattern ([System.Windows.Automation.RangeValuePattern]::Pattern)
    ([System.Windows.Automation.RangeValuePattern]$pattern).SetValue($Value)
}

function Select-ComboOption {
    param(
        [System.Windows.Automation.AutomationElement]$ComboBox,
        [string]$OptionName
    )
    $expand = Get-ElementPattern -Element $ComboBox -Pattern ([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    ([System.Windows.Automation.ExpandCollapsePattern]$expand).Expand()
    Start-Sleep -Milliseconds 150
    $nameCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $OptionName)
    $option = $ComboBox.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCondition)
    if ($null -eq $option) {
        $option = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            $nameCondition)
    }
    if ($null -eq $option) {
        throw "Combo-box option '$OptionName' was not found."
    }
    while ($null -ne $option) {
        if ($option.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) {
            break
        }
        $option = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($option)
    }
    if ($null -eq $option) {
        throw "Combo-box option container '$OptionName' was not found."
    }
    $selection = $null
    if ($option.TryGetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern,
        [ref]$selection)) {
        ([System.Windows.Automation.SelectionItemPattern]$selection).Select()
    }
    else {
        Invoke-Element $option
    }
}

function Open-SettingsWithRightClick {
    param([int]$ProcessId)
    $moreButton = Find-AppElementByName -ProcessId $ProcessId -Name "显示全部图标"
    if ($null -eq $moreButton) {
        throw "The three-dot button was not found."
    }
    $containerWindow = $moreButton
    while ($null -ne $containerWindow) {
        if ($containerWindow.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window) {
            break
        }
        $containerWindow = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($containerWindow)
    }
    for ($attempt = 0; $attempt -lt 3; $attempt++) {
        if ($null -ne $containerWindow) {
            [Stage6User32]::SetForegroundWindow([IntPtr]$containerWindow.Current.NativeWindowHandle) | Out-Null
        }
        $moreButton.SetFocus()
        $bounds = $moreButton.Current.BoundingRectangle
        [Stage6User32]::SetCursorPos(
            [int]($bounds.Left + $bounds.Width / 2),
            [int]($bounds.Top + $bounds.Height / 2)) | Out-Null
        Start-Sleep -Milliseconds 100
        [Stage6User32]::mouse_event(0x0008, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 80
        [Stage6User32]::mouse_event(0x0010, 0, 0, 0, [UIntPtr]::Zero)
        $settings = Find-AppWindowByName -ProcessId $ProcessId -Name "DesktopOrganizer 设置" -TimeoutSeconds 3
        if ($null -ne $settings) {
            return $settings
        }
    }
    return $null
}

function Wait-SettingsClosed {
    param([int]$ProcessId)
    $deadline = [DateTime]::UtcNow.AddSeconds(8)
    do {
        Start-Sleep -Milliseconds 100
        $window = Find-AppWindowByName -ProcessId $ProcessId -Name "DesktopOrganizer 设置" -TimeoutSeconds 0
    } while ($null -ne $window -and [DateTime]::UtcNow -lt $deadline)
    if ($null -ne $window) {
        throw "The settings window did not close."
    }
}

function Start-IsolatedApp {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new($appPath)
    $startInfo.UseShellExecute = $false
    $startInfo.Environment["DESKTOPORGANIZER_APPDATA"] = $testDirectory
    $startInfo.Environment["DESKTOPORGANIZER_SKIP_STARTUP_SETTINGS"] = "1"
    return [System.Diagnostics.Process]::Start($startInfo)
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

try {
    New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
    $items = @()
    $pinnedIds = @()
    for ($index = 1; $index -le 6; $index++) {
        $itemId = "stage6-item-{0:D2}" -f $index
        $pinnedIds += $itemId
        $items += [ordered]@{
            id = $itemId
            displayName = "阶段6图标 {0:D2}" -f $index
            kind = "File"
            sourcePath = $configPath
            launchTarget = ""
            arguments = ""
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
        containers = @([ordered]@{
            id = [Guid]::NewGuid().ToString("N")
            name = "阶段6自动化测试"
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
        })
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
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class Stage6User32
{
    public delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    public static string[] GetWindowTitles(uint expectedProcessId)
    {
        var titles = new List<string>();
        EnumWindows((window, parameter) =>
        {
            GetWindowThreadProcessId(window, out var processId);
            if (processId == expectedProcessId && IsWindowVisible(window))
            {
                var text = new StringBuilder(512);
                GetWindowText(window, text, text.Capacity);
                titles.Add($"'{text}' (0x{window.ToInt64():X})");
            }
            return true;
        }, IntPtr.Zero);
        return titles.ToArray();
    }

    public static IntPtr FindWindowHandle(uint expectedProcessId, string expectedTitle)
    {
        var result = IntPtr.Zero;
        EnumWindows((window, parameter) =>
        {
            GetWindowThreadProcessId(window, out var processId);
            if (processId == expectedProcessId && IsWindowVisible(window))
            {
                var text = new StringBuilder(512);
                GetWindowText(window, text, text.Capacity);
                if (string.Equals(text.ToString(), expectedTitle, StringComparison.Ordinal))
                {
                    result = window;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
'@

    $process = Start-IsolatedApp
    $settingsWindow = Open-SettingsWithRightClick -ProcessId $process.Id
    if ($null -eq $settingsWindow) {
        $windowNames = @(
            Get-AppWindows -ProcessId $process.Id |
                ForEach-Object { "'$($_.Current.Name)' ($($_.Current.ControlType.ProgrammaticName))" }
        )
        $nativeWindows = [Stage6User32]::GetWindowTitles([uint32]$process.Id)
        throw "Right-clicking the three-dot button did not expose settings. UIA: $($windowNames -join ', '); Win32: $($nativeWindows -join ', ')"
    }

    Start-Sleep -Milliseconds 500
    Save-ElementScreenshot -Element $settingsWindow -Path $screenshotPath

    Set-ElementValue (Find-AppElementByName $process.Id "收纳区名称") "阶段6已保存"
    Select-ComboOption (Find-AppElementByName $process.Id "收纳区尺寸") "2 x 4"
    Select-ComboOption (Find-AppElementByName $process.Id "收纳区方向") "纵向"
    Set-RangeValue (Find-AppControlByName $process.Id "边框粗细" ([System.Windows.Automation.ControlType]::Slider)) 8
    Set-RangeValue (Find-AppControlByName $process.Id "背景透明度" ([System.Windows.Automation.ControlType]::Slider)) 0.7
    Select-ComboOption (Find-AppElementByName $process.Id "边框颜色模式") "自定义"
    Set-ElementValue (Find-AppElementByName $process.Id "边框颜色值") "#D13B55"
    Select-ComboOption (Find-AppElementByName $process.Id "背景颜色模式") "自定义"
    Set-ElementValue (Find-AppElementByName $process.Id "背景颜色值") "#287A68"

    $nameCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        "图标名称")
    $nameFields = $settingsWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, $nameCondition)
    Set-ElementValue $nameFields[0] "已重命名图标"

    $pinCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        "外露图标")
    $pinBoxes = $settingsWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, $pinCondition)
    $toggle = Get-ElementPattern -Element $pinBoxes[0] -Pattern ([System.Windows.Automation.TogglePattern]::Pattern)
    ([System.Windows.Automation.TogglePattern]$toggle).Toggle()

    $upCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        "上移图标")
    $upButtons = $settingsWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, $upCondition)
    Invoke-Element $upButtons[1]

    $deleteCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        "删除图标")
    $deleteButtons = $settingsWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, $deleteCondition)
    Invoke-Element $deleteButtons[$deleteButtons.Count - 1]
    Invoke-Element (Find-AppElementByName $process.Id "保存设置")
    Wait-SettingsClosed -ProcessId $process.Id

    $deadline = [DateTime]::UtcNow.AddSeconds(8)
    do {
        Start-Sleep -Milliseconds 100
        $savedConfig = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    } while ($savedConfig.containers[0].name -ne "阶段6已保存" -and [DateTime]::UtcNow -lt $deadline)
    $savedContainer = $savedConfig.containers[0]
    $savedChecks = @(
        $savedContainer.name -eq "阶段6已保存"
        $savedContainer.sizeMode -eq "TwoByFour"
        $savedContainer.orientation -eq "Vertical"
        $savedContainer.style.borderThickness -eq 8
        [Math]::Abs($savedContainer.style.backgroundOpacity - 0.7) -le 0.001
        $savedContainer.style.borderColor.hexColor -eq "#D13B55"
        $savedContainer.style.backgroundColor.hexColor -eq "#287A68"
        $savedContainer.items.Count -eq 5
        $savedContainer.items[0].id -eq "stage6-item-02"
        $savedContainer.items[1].displayName -eq "已重命名图标"
        $savedContainer.pinnedIconIds -notcontains "stage6-item-01"
    )
    if ($savedChecks -contains $false) {
        throw "The saved stage 6 configuration did not match the UI edits."
    }

    Start-Sleep -Milliseconds 300
    $customContainer = Find-AppWindowByName -ProcessId $process.Id -Name "阶段6已保存"
    if ($null -eq $customContainer) {
        throw "The customized compact container was not found for visual verification."
    }
    Save-ElementScreenshot -Element $customContainer -Path $containerScreenshotPath

    Stop-Process -Id $process.Id -Force
    $process.WaitForExit(5000) | Out-Null
    $process = Start-IsolatedApp
    if ($null -eq (Find-AppElementByName $process.Id "阶段6图标 02")) {
        throw "The reordered external icon was not restored after restart."
    }

    $settingsWindow = Open-SettingsWithRightClick -ProcessId $process.Id
    if ($null -eq $settingsWindow) {
        throw "Settings did not reopen after restart."
    }
    $nameValue = Get-ElementPattern (Find-AppElementByName $process.Id "收纳区名称") ([System.Windows.Automation.ValuePattern]::Pattern)
    $borderRange = Get-ElementPattern (Find-AppControlByName $process.Id "边框粗细" ([System.Windows.Automation.ControlType]::Slider)) ([System.Windows.Automation.RangeValuePattern]::Pattern)
    $restartChecks = @(
        ([System.Windows.Automation.ValuePattern]$nameValue).Current.Value -eq "阶段6已保存"
        ([System.Windows.Automation.RangeValuePattern]$borderRange).Current.Value -eq 8
    )
    if ($restartChecks -contains $false) {
        throw "Saved settings were not restored into the settings window after restart."
    }

    Invoke-Element (Find-AppElementByName $process.Id "恢复系统主题")
    Invoke-Element (Find-AppElementByName $process.Id "保存设置")
    Wait-SettingsClosed -ProcessId $process.Id
    Start-Sleep -Milliseconds 300
    $restoredConfig = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    $restoredStyle = $restoredConfig.containers[0].style
    $restoreChecks = @(
        $restoredStyle.borderColor.mode -eq "SystemAccent"
        $restoredStyle.backgroundColor.mode -eq "SystemAccent"
        $restoredStyle.moreButtonColor.mode -eq "Auto"
        $restoredStyle.hoverBackgroundColor.mode -eq "Auto"
        $restoredStyle.borderThickness -eq 4
    )
    if ($restoreChecks -contains $false) {
        throw "Restore system theme did not persist the expected modes and defaults."
    }

    $settingsWindow = Open-SettingsWithRightClick -ProcessId $process.Id
    Set-RangeValue (Find-AppControlByName $process.Id "边框粗细" ([System.Windows.Automation.ControlType]::Slider)) 11
    Invoke-Element (Find-AppElementByName $process.Id "取消设置")
    Wait-SettingsClosed -ProcessId $process.Id
    $cancelledConfig = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    if ($cancelledConfig.containers[0].style.borderThickness -ne 4) {
        throw "Cancel persisted a preview value."
    }

    [pscustomobject]@{
        RightClickOpenedSettings = $true
        SavedAndRestarted = $true
        RenamedReorderedDeleted = $true
        LayoutAndMaterialSaved = $true
        CustomColorsSaved = $true
        SystemThemeRestored = $true
        CancelRolledBack = $true
        Screenshot = $screenshotPath
        ContainerScreenshot = $containerScreenshotPath
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
