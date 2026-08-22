param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$appPath = Join-Path $projectRoot "src\DesktopOrganizer.App\bin\$Configuration\net8.0-windows\DesktopOrganizer.exe"
$testDirectory = Join-Path $projectRoot ("artifacts\stage4-ui-smoke-" + [Guid]::NewGuid().ToString("N"))
$configPath = Join-Path $testDirectory "config.json"
$markerPath = Join-Path $testDirectory "launch-marker.txt"
$vbsPath = Join-Path $testDirectory "record-launch.vbs"
$process = $null

if (-not (Test-Path -LiteralPath $appPath)) {
    throw "DesktopOrganizer.exe was not found at '$appPath'. Build the solution first."
}

try {
    New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null

    @'
Set fileSystem = CreateObject("Scripting.FileSystemObject")
Set marker = fileSystem.OpenTextFile(WScript.Arguments(0), 8, True)
marker.WriteLine Now
marker.Close
'@ | Set-Content -LiteralPath $vbsPath -Encoding ASCII

    $itemId = [Guid]::NewGuid().ToString("N")
    $wscriptPath = Join-Path $env:WINDIR "System32\wscript.exe"
    $config = [ordered]@{
        schemaVersion = 1
        containers = @(
            [ordered]@{
                id = [Guid]::NewGuid().ToString("N")
                name = "阶段4自动化测试"
                sizeMode = "TwoByTwo"
                orientation = "Horizontal"
                left = 120
                top = 120
                hasSavedPosition = $true
                monitorDeviceName = ""
                items = @(
                    [ordered]@{
                        id = $itemId
                        displayName = "阶段4测试"
                        kind = "Shortcut"
                        sourcePath = $wscriptPath
                        launchTarget = $wscriptPath
                        arguments = ('"{0}" "{1}"' -f $vbsPath, $markerPath)
                        workingDirectory = $testDirectory
                        iconSource = ""
                        storageMode = "ReferenceOnly"
                        originalPath = ""
                        managedPath = ""
                        addedAt = [DateTimeOffset]::Now.ToString("O")
                    }
                )
                pinnedIconIds = @($itemId)
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

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new($appPath)
    $startInfo.UseShellExecute = $false
    $startInfo.Environment["DESKTOPORGANIZER_APPDATA"] = $testDirectory
    $startInfo.Environment["DESKTOPORGANIZER_SKIP_STARTUP_SETTINGS"] = "1"
    $process = [System.Diagnostics.Process]::Start($startInfo)

    $button = $null
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    $processCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $process.Id)
    $nameCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        "阶段4测试")

    while ($null -eq $button -and [DateTime]::UtcNow -lt $deadline) {
        $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            $processCondition)
        foreach ($window in $windows) {
            $button = $window.FindFirst(
                [System.Windows.Automation.TreeScope]::Descendants,
                $nameCondition)
            if ($null -ne $button) {
                break
            }
        }

        if ($null -eq $button) {
            Start-Sleep -Milliseconds 150
        }
    }

    if ($null -eq $button) {
        throw "The launch tile was not found through Windows UI Automation."
    }

    $patternObject = $null
    if (-not $button.TryGetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern,
        [ref]$patternObject)) {
        throw "The launch tile does not expose the UI Automation Invoke pattern."
    }

    $invokePattern = [System.Windows.Automation.InvokePattern]$patternObject
    $invokePattern.Invoke()
    Start-Sleep -Milliseconds 250
    $invokePattern.Invoke()

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
        throw "Expected one process launch after two rapid clicks, but found $($markerLines.Count)."
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
        throw "The successful UI launch did not persist lastLaunchedAt."
    }

    [pscustomobject]@{
        UiElement = $button.Current.Name
        LaunchCount = $markerLines.Count
        LastLaunchedAt = $lastLaunchedAt
        DebounceIntervalMilliseconds = 250
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
