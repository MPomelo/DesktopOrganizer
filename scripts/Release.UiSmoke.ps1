param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$setupPath = Join-Path $projectRoot "artifacts\release\DesktopOrganizer-Setup-$Version-x64.exe"
$artifactDirectory = Join-Path $projectRoot "artifacts"
$installerScreenshot = Join-Path $artifactDirectory "installer-custom-location.png"
$startupScreenshot = Join-Path $artifactDirectory "installed-startup-settings.png"
$testRoot = Join-Path $env:TEMP ("DesktopOrganizer.ReleaseTest." + [Guid]::NewGuid().ToString("N"))
$installPath = Join-Path $testRoot "Custom Install Location"
$programsPath = Join-Path $testRoot "Programs"
$desktopPath = Join-Path $testRoot "Desktop"
$appDataPath = Join-Path $testRoot "AppData"
$releaseDesktop = Join-Path $testRoot "ReleaseDesktop"
$publicDesktop = Join-Path $testRoot "PublicDesktop"
$releaseStartMenu = Join-Path $testRoot "ReleaseStartMenu"
$registryPath = "HKCU:\Software\DesktopOrganizer\ReleaseTests\" + [Guid]::NewGuid().ToString("N")
$setupProcess = $null
$installerProcess = $null
$appProcess = $null
$testSucceeded = $false

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing

function Find-Element {
    param(
        [string]$Name,
        [int]$TimeoutSeconds = 15,
        [Nullable[int]]$ProcessId = $null,
        [System.Windows.Automation.ControlType]$ControlType = $null
    )
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $conditions = @(
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::NameProperty,
                $Name)
        )
        if ($null -ne $ProcessId) {
            $conditions += [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
                [int]$ProcessId.Value)
        }
        if ($null -ne $ControlType) {
            $conditions += [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                $ControlType)
        }
        $condition = if ($conditions.Count -eq 1) { $conditions[0] } else {
            [System.Windows.Automation.AndCondition]::new($conditions)
        }
        $element = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            $condition)
        if ($null -ne $element) { return $element }
        Start-Sleep -Milliseconds 150
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    return $null
}

function Find-DescendantByType {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [System.Windows.Automation.ControlType]$ControlType
    )
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        $ControlType)
    return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Find-EditableDescendant {
    param([System.Windows.Automation.AutomationElement]$Root)
    $editCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Edit)
    $edit = $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $editCondition)
    if ($null -ne $edit) {
        return $edit
    }

    $all = $Root.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    return $all | Where-Object {
        $_.Current.IsEnabled -and $_.Current.IsKeyboardFocusable
    } | Select-Object -First 1
}

function Find-WindowContainingName {
    param(
        [int]$ProcessId,
        [string]$Text,
        [int]$TimeoutSeconds = 15
    )
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $processCondition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
            [int]$ProcessId)
        $windowCondition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Window)
        $condition = [System.Windows.Automation.AndCondition]::new($processCondition, $windowCondition)
        $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            $condition)
        $match = $windows | Where-Object { $_.Current.Name -like "*$Text*" } | Select-Object -First 1
        if ($null -ne $match) {
            return $match
        }
        Start-Sleep -Milliseconds 150
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    return $null
}

function Find-ChildByName {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name,
        [System.Windows.Automation.ControlType]$ControlType = $null
    )
    $nameCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)
    if ($null -eq $ControlType) {
        return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCondition)
    }
    $typeCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        $ControlType)
    return $Root.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.AndCondition]::new($nameCondition, $typeCondition))
}

function Get-Pattern {
    param(
        [System.Windows.Automation.AutomationElement]$Element,
        [System.Windows.Automation.AutomationPattern]$Pattern
    )
    $value = $null
    if ($null -eq $Element) {
        throw "UI Automation element argument was null while requesting $($Pattern.ProgrammaticName)."
    }
    if (-not $Element.TryGetCurrentPattern($Pattern, [ref]$value)) {
        throw "'$($Element.Current.Name)' does not expose the required automation pattern."
    }
    return $value
}

function Invoke-Element {
    param([System.Windows.Automation.AutomationElement]$Element)
    $pattern = Get-Pattern $Element ([System.Windows.Automation.InvokePattern]::Pattern)
    ([System.Windows.Automation.InvokePattern]$pattern).Invoke()
}

function Set-ElementValue {
    param(
        [System.Windows.Automation.AutomationElement]$Element,
        [string]$Value
    )
    $pattern = Get-Pattern $Element ([System.Windows.Automation.ValuePattern]::Pattern)
    ([System.Windows.Automation.ValuePattern]$pattern).SetValue($Value)
}

function Set-CheckBoxOff {
    param([System.Windows.Automation.AutomationElement]$Element)
    if ($null -eq $Element) {
        throw "The requested installer checkbox was not found."
    }
    $pattern = Get-Pattern $Element ([System.Windows.Automation.TogglePattern]::Pattern)
    if (([System.Windows.Automation.TogglePattern]$pattern).Current.ToggleState -eq
        [System.Windows.Automation.ToggleState]::On) {
        ([System.Windows.Automation.TogglePattern]$pattern).Toggle()
    }
}

function Save-Screenshot {
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

function Stop-ProcessIfRunning {
    param([System.Diagnostics.Process]$Process)
    if ($null -ne $Process -and -not $Process.HasExited) {
        Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
        $Process.WaitForExit(5000) | Out-Null
    }
}

try {
    if (-not (Test-Path -LiteralPath $setupPath)) {
        throw "Installer was not found: $setupPath"
    }
    foreach ($path in @($testRoot, $programsPath, $desktopPath, $appDataPath, $releaseDesktop, $publicDesktop)) {
        New-Item -ItemType Directory -Path $path -Force | Out-Null
    }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new($setupPath)
    $startInfo.UseShellExecute = $false
    $startInfo.Environment["DESKTOPORGANIZER_INSTALLER_PROGRAMS"] = $programsPath
    $startInfo.Environment["DESKTOPORGANIZER_INSTALLER_DESKTOP"] = $desktopPath
    $startInfo.Environment["DESKTOPORGANIZER_INSTALLER_REGISTRY"] = $registryPath
    $setupProcess = [System.Diagnostics.Process]::Start($startInfo)

    $installerWindow = Find-Element "安装 DesktopOrganizer" 20 $null ([System.Windows.Automation.ControlType]::Window)
    if ($null -eq $installerWindow) { throw "Installer window did not appear." }
    $installerProcess = Get-Process -Id $installerWindow.Current.ProcessId
    $pathField = Find-EditableDescendant $installerWindow
    if ($null -eq $pathField) {
        $controls = $installerWindow.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition) |
            ForEach-Object { "$($_.Current.ControlType.ProgrammaticName):$($_.Current.Name)" }
        throw "Installer path input was not found. Controls: $($controls -join '; ')"
    }
    Set-ElementValue $pathField $installPath
    Set-CheckBoxOff (Find-Element "创建桌面快捷方式" 5)
    Set-CheckBoxOff (Find-Element "安装完成后启动 DesktopOrganizer" 5)
    Start-Sleep -Milliseconds 300
    Save-Screenshot $installerWindow $installerScreenshot
    Invoke-Element (Find-Element "安装" 5 $null ([System.Windows.Automation.ControlType]::Button))

    $completeWindow = Find-Element "安装完成" 20 $null ([System.Windows.Automation.ControlType]::Window)
    if ($null -eq $completeWindow) { throw "Installation completion message did not appear." }
    Invoke-Element (Find-Element "确定" 5 $null ([System.Windows.Automation.ControlType]::Button))
    Start-Sleep -Milliseconds 500

    $installedExe = Join-Path $installPath "DesktopOrganizer.exe"
    $checks = @(
        (Test-Path -LiteralPath $installedExe)
        (Test-Path -LiteralPath (Join-Path $installPath "Uninstall.ps1"))
        (Test-Path -LiteralPath (Join-Path $installPath "USER_GUIDE.md"))
        (Test-Path -LiteralPath (Join-Path $programsPath "DesktopOrganizer\DesktopOrganizer.lnk"))
        (-not (Test-Path -LiteralPath (Join-Path $desktopPath "DesktopOrganizer.lnk")))
        (Test-Path -LiteralPath $registryPath)
        ((Get-ItemProperty -LiteralPath $registryPath).InstallLocation -eq $installPath)
    )
    if ($checks -contains $false) { throw "Installed files or registration did not match the custom location." }

    $appStartInfo = [System.Diagnostics.ProcessStartInfo]::new($installedExe)
    $appStartInfo.UseShellExecute = $false
    $appStartInfo.Environment["DESKTOPORGANIZER_APPDATA"] = $appDataPath
    $appStartInfo.Environment["DESKTOPORGANIZER_DESKTOP"] = $releaseDesktop
    $appStartInfo.Environment["DESKTOPORGANIZER_PUBLIC_DESKTOP"] = $publicDesktop
    $appStartInfo.Environment["DESKTOPORGANIZER_START_MENU"] = $releaseStartMenu
    $appProcess = [System.Diagnostics.Process]::Start($appStartInfo)

    $settingsWindow = Find-WindowContainingName $appProcess.Id "设置" 20
    if ($null -eq $settingsWindow) { throw "Installed app did not open settings on startup." }
    Start-Sleep -Milliseconds 500
    Save-Screenshot $settingsWindow $startupScreenshot
    $fullExitButton = Find-ChildByName $settingsWindow "彻底退出收纳区" ([System.Windows.Automation.ControlType]::Button)
    if ($null -eq $fullExitButton) { throw "Full-exit button was not found in the settings window." }
    Invoke-Element $fullExitButton
    $confirmation = Find-WindowContainingName $appProcess.Id "彻底退出" 10
    if ($null -eq $confirmation) { throw "Full-exit confirmation did not appear." }
    $yesButton = $confirmation.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)) |
        Where-Object { $_.Current.Name -like "是*" } | Select-Object -First 1
    if ($null -eq $yesButton) {
        $confirmationControls = $confirmation.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition) |
            ForEach-Object { "$($_.Current.ControlType.ProgrammaticName):$($_.Current.Name)" }
        throw "Full-exit confirmation yes button was not found. Controls: $($confirmationControls -join '; ')"
    }
    Invoke-Element $yesButton
    if (-not $appProcess.WaitForExit(10000)) { throw "Full exit did not terminate the installed application." }

    $config = Get-Content -LiteralPath (Join-Path $appDataPath "config.json") -Raw | ConvertFrom-Json
    if ($config.containers.Count -ne 0 -or -not $config.behavior.hasClosedAllContainers) {
        throw "Full exit did not commit the closed-container configuration."
    }

    $testSucceeded = $true

    [pscustomobject]@{
        InstallerOpened = $true
        CustomInstallLocation = $installPath
        ShortcutsAndRegistryCreated = $true
        InstalledAppOpenedSettings = $true
        FullExitConfirmedAndStopped = $true
        InstallerScreenshot = $installerScreenshot
        StartupSettingsScreenshot = $startupScreenshot
    } | Format-List
}
finally {
    Stop-ProcessIfRunning $appProcess
    Stop-ProcessIfRunning $installerProcess
    Stop-ProcessIfRunning $setupProcess
    if (Test-Path -LiteralPath $registryPath) {
        Remove-Item -LiteralPath $registryPath -Recurse -Force
    }
    if ($testSucceeded -and (Test-Path -LiteralPath $testRoot)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
    elseif (-not $testSucceeded) {
        Write-Host "Release UI test artifacts retained at: $testRoot"
        if (Test-Path -LiteralPath (Join-Path $appDataPath "Logs\app.log")) {
            Get-Content -LiteralPath (Join-Path $appDataPath "Logs\app.log") -Tail 80
        }
    }
}
