param(
    [string]$Version = "1.0.0",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$projectRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$solutionPath = Join-Path $projectRoot "DesktopOrganizer.sln"
$appProject = Join-Path $projectRoot "src\DesktopOrganizer.App\DesktopOrganizer.App.csproj"
$testProject = Join-Path $projectRoot "tests\DesktopOrganizer.Tests\DesktopOrganizer.Tests.csproj"
$installerDirectory = Join-Path $projectRoot "installer"
$guidePath = Join-Path $projectRoot "USER_GUIDE.md"
$workDirectory = Join-Path $projectRoot "artifacts\release-work"
$publishDirectory = Join-Path $workDirectory "publish"
$packageDirectory = Join-Path $workDirectory "package"
$releaseDirectory = Join-Path $projectRoot "artifacts\release"
$setupName = "DesktopOrganizer-Setup-$Version-x64.exe"
$setupPath = Join-Path $releaseDirectory $setupName
$iexpressPath = Join-Path $env:WINDIR "System32\iexpress.exe"

function Get-DotNetPath {
    $bundled = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet-sdk\dotnet.exe"
    if (Test-Path -LiteralPath $bundled) {
        return $bundled
    }

    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        $sdks = & $command.Source --list-sdks
        if ($LASTEXITCODE -eq 0 -and @($sdks).Count -gt 0) {
            return $command.Source
        }
    }

    throw "找不到 dotnet SDK。请安装 .NET 8 SDK 后重试。"
}

function Reset-ProjectDirectory {
    param([string]$Path)
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts")) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($artifactRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清理 artifacts 目录之外的路径：$fullPath"
    }
    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
    New-Item -ItemType Directory -Path $fullPath -Force | Out-Null
}

function Invoke-DotNet {
    param([string[]]$Arguments)
    & $dotnetPath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet 命令失败，退出代码：$LASTEXITCODE"
    }
}

if (-not (Test-Path -LiteralPath $iexpressPath)) {
    throw "当前系统缺少 IExpress：$iexpressPath"
}
if (-not (Test-Path -LiteralPath $guidePath)) {
    throw "缺少用户手册：$guidePath"
}

$dotnetPath = Get-DotNetPath
Reset-ProjectDirectory $workDirectory
Reset-ProjectDirectory $releaseDirectory
New-Item -ItemType Directory -Path $publishDirectory, $packageDirectory -Force | Out-Null

Write-Host "[1/6] 生成应用图标"
& (Join-Path $PSScriptRoot "Generate-AppIcon.ps1")
if (-not $?) {
    throw "应用图标生成失败。"
}

Write-Host "[2/6] 构建 Release"
Invoke-DotNet @(
    "clean", $solutionPath,
    "--configuration", "Release"
)
Invoke-DotNet @(
    "build", $solutionPath,
    "--configuration", "Release"
)

Write-Host "[3/6] 运行自动化测试"
Invoke-DotNet @(
    "run", "--project", $testProject,
    "--configuration", "Release",
    "--no-build"
)

Write-Host "[4/6] 发布自包含单文件应用"
Invoke-DotNet @(
    "publish", $appProject,
    "--configuration", "Release",
    "--runtime", $Runtime,
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "--output", $publishDirectory
)

$publishedExe = Join-Path $publishDirectory "DesktopOrganizer.exe"
if (-not (Test-Path -LiteralPath $publishedExe)) {
    throw "发布输出缺少 DesktopOrganizer.exe。"
}

Write-Host "[5/6] 生成单文件安装程序"
Copy-Item -LiteralPath $publishedExe -Destination (Join-Path $packageDirectory "DesktopOrganizer.exe")
Copy-Item -LiteralPath (Join-Path $installerDirectory "Install.cmd") -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $installerDirectory "Install.ps1") -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $installerDirectory "Uninstall.ps1") -Destination $packageDirectory
Copy-Item -LiteralPath $guidePath -Destination (Join-Path $packageDirectory "USER_GUIDE.md")

$utf8Bom = [System.Text.UTF8Encoding]::new($true)
foreach ($scriptName in @("Install.ps1", "Uninstall.ps1")) {
    $scriptPath = Join-Path $packageDirectory $scriptName
    $content = [System.IO.File]::ReadAllText($scriptPath).Replace(
        '[string]$Version = "1.0.0"',
        "[string]`$Version = `"$Version`"")
    [System.IO.File]::WriteAllText($scriptPath, $content, $utf8Bom)
}

$sourcePath = $packageDirectory.TrimEnd('\') + '\'
$sedPath = Join-Path $workDirectory "DesktopOrganizer.sed"
$sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3

[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=0
HideExtractAnimation=0
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=
DisplayLicense=
FinishMessage=
TargetName=$setupPath
FriendlyName=DesktopOrganizer $Version 安装程序
AppLaunched=Install.cmd
PostInstallCmd=<None>
AdminQuietInstCmd=Install.cmd
UserQuietInstCmd=Install.cmd
SourceFiles=SourceFiles

[Strings]
FILE0="DesktopOrganizer.exe"
FILE1="Install.cmd"
FILE2="Install.ps1"
FILE3="Uninstall.ps1"
FILE4="USER_GUIDE.md"

[SourceFiles]
SourceFiles0=$sourcePath

[SourceFiles0]
%FILE0%=
%FILE1%=
%FILE2%=
%FILE3%=
%FILE4%=
"@
[System.IO.File]::WriteAllText($sedPath, $sed, [System.Text.Encoding]::ASCII)
& $iexpressPath /N /Q $sedPath
if ($LASTEXITCODE -ne 0) {
    throw "IExpress 安装包生成失败，退出代码：$LASTEXITCODE"
}
$deadline = [DateTimeOffset]::UtcNow.AddMinutes(3)
while (-not (Test-Path -LiteralPath $setupPath) -and [DateTimeOffset]::UtcNow -lt $deadline) {
    Start-Sleep -Milliseconds 500
}
if (-not (Test-Path -LiteralPath $setupPath)) {
    throw "IExpress 在等待时间内没有生成安装包。"
}
$previousLength = -1L
$stableReads = 0
while ([DateTimeOffset]::UtcNow -lt $deadline) {
    try {
        $stream = [System.IO.File]::Open($setupPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::None)
        $stream.Dispose()
        $currentLength = (Get-Item -LiteralPath $setupPath).Length
        if ($currentLength -gt 0 -and $currentLength -eq $previousLength) {
            $stableReads++
        }
        else {
            $stableReads = 0
        }
        $previousLength = $currentLength
        if ($stableReads -ge 6) {
            break
        }
        Start-Sleep -Milliseconds 500
    }
    catch [System.IO.IOException] {
        $stableReads = 0
        Start-Sleep -Milliseconds 500
    }
}
if ($stableReads -lt 6) {
    throw "IExpress 安装包在等待时间内没有稳定下来。"
}

Write-Host "[6/6] 生成校验值"
$hash = Get-FileHash -LiteralPath $setupPath -Algorithm SHA256
$checksumPath = Join-Path $releaseDirectory "SHA256SUMS.txt"
[System.IO.File]::WriteAllText(
    $checksumPath,
    "$($hash.Hash.ToLowerInvariant())  $setupName`r`n",
    [System.Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath $guidePath -Destination (Join-Path $releaseDirectory "USER_GUIDE.md")

$portablePath = Join-Path $releaseDirectory "DesktopOrganizer-$Version-$Runtime-portable.exe"
Copy-Item -LiteralPath $publishedExe -Destination $portablePath

[pscustomobject]@{
    Setup = $setupPath
    SetupSizeMB = [Math]::Round((Get-Item -LiteralPath $setupPath).Length / 1MB, 2)
    Portable = $portablePath
    Sha256 = $hash.Hash.ToLowerInvariant()
    Guide = (Join-Path $releaseDirectory "USER_GUIDE.md")
} | Format-List
