param(
    [string]$InstallPath = "",
    [switch]$Silent,
    [switch]$NoLaunch,
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$productName = "DesktopOrganizer"
$payloadPath = Join-Path $PSScriptRoot "DesktopOrganizer.exe"
$uninstallSource = Join-Path $PSScriptRoot "Uninstall.ps1"
$guideSource = Join-Path $PSScriptRoot "USER_GUIDE.md"
$defaultInstallPath = Join-Path $env:LOCALAPPDATA "Programs\DesktopOrganizer"
$programsDirectory = if ([string]::IsNullOrWhiteSpace($env:DESKTOPORGANIZER_INSTALLER_PROGRAMS)) {
    [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
} else { $env:DESKTOPORGANIZER_INSTALLER_PROGRAMS }
$desktopDirectory = if ([string]::IsNullOrWhiteSpace($env:DESKTOPORGANIZER_INSTALLER_DESKTOP)) {
    [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
} else { $env:DESKTOPORGANIZER_INSTALLER_DESKTOP }
$startMenuDirectory = Join-Path $programsDirectory $productName
$uninstallRegistryPath = if ([string]::IsNullOrWhiteSpace($env:DESKTOPORGANIZER_INSTALLER_REGISTRY)) {
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DesktopOrganizer"
} else { $env:DESKTOPORGANIZER_INSTALLER_REGISTRY }

function New-Shortcut {
    param(
        [string]$Path,
        [string]$TargetPath,
        [string]$Arguments = "",
        [string]$WorkingDirectory = "",
        [string]$Description = ""
    )
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($Path)
    $shortcut.TargetPath = $TargetPath
    $shortcut.Arguments = $Arguments
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.Description = $Description
    $shortcut.IconLocation = "$TargetPath,0"
    $shortcut.Save()
}

function Stop-RunningApplication {
    $running = @(Get-Process -Name "DesktopOrganizer" -ErrorAction SilentlyContinue)
    if ($running.Count -eq 0) {
        return
    }

    if (-not $Silent) {
        $answer = [System.Windows.Forms.MessageBox]::Show(
            "安装前需要关闭正在运行的 DesktopOrganizer。是否继续？",
            "DesktopOrganizer 安装",
            [System.Windows.Forms.MessageBoxButtons]::YesNo,
            [System.Windows.Forms.MessageBoxIcon]::Question)
        if ($answer -ne [System.Windows.Forms.DialogResult]::Yes) {
            throw [System.OperationCanceledException]::new("用户取消了安装。")
        }
    }

    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 300
}

function Install-Application {
    param(
        [string]$Destination,
        [bool]$CreateDesktopShortcut,
        [bool]$LaunchAfterInstall
    )
    if (-not (Test-Path -LiteralPath $payloadPath) -or -not (Test-Path -LiteralPath $uninstallSource)) {
        throw "安装包内容不完整，请重新下载安装程序。"
    }

    $Destination = [System.IO.Path]::GetFullPath($Destination.Trim())
    if ([string]::IsNullOrWhiteSpace($Destination)) {
        throw "请选择安装位置。"
    }

    Stop-RunningApplication
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    $installedExe = Join-Path $Destination "DesktopOrganizer.exe"
    $installedUninstaller = Join-Path $Destination "Uninstall.ps1"
    $installedGuide = Join-Path $Destination "USER_GUIDE.md"
    Copy-Item -LiteralPath $payloadPath -Destination $installedExe -Force
    Copy-Item -LiteralPath $uninstallSource -Destination $installedUninstaller -Force
    if (Test-Path -LiteralPath $guideSource) {
        Copy-Item -LiteralPath $guideSource -Destination $installedGuide -Force
    }

    New-Item -ItemType Directory -Path $startMenuDirectory -Force | Out-Null
    New-Shortcut `
        -Path (Join-Path $startMenuDirectory "DesktopOrganizer.lnk") `
        -TargetPath $installedExe `
        -WorkingDirectory $Destination `
        -Description "Windows 桌面图标收纳工具"
    New-Shortcut `
        -Path (Join-Path $startMenuDirectory "卸载 DesktopOrganizer.lnk") `
        -TargetPath "powershell.exe" `
        -Arguments "-NoLogo -NoProfile -ExecutionPolicy Bypass -STA -File `"$installedUninstaller`"" `
        -WorkingDirectory $Destination `
        -Description "卸载 DesktopOrganizer"
    if (Test-Path -LiteralPath $installedGuide) {
        New-Shortcut `
            -Path (Join-Path $startMenuDirectory "DesktopOrganizer 使用说明.lnk") `
            -TargetPath $installedGuide `
            -WorkingDirectory $Destination `
            -Description "打开 DesktopOrganizer 使用说明"
    }

    $desktopShortcut = Join-Path $desktopDirectory "DesktopOrganizer.lnk"
    if ($CreateDesktopShortcut) {
        New-Shortcut `
            -Path $desktopShortcut `
            -TargetPath $installedExe `
            -WorkingDirectory $Destination `
            -Description "Windows 桌面图标收纳工具"
    }
    elseif (Test-Path -LiteralPath $desktopShortcut) {
        Remove-Item -LiteralPath $desktopShortcut -Force
    }

    New-Item -Path $uninstallRegistryPath -Force | Out-Null
    Set-ItemProperty -Path $uninstallRegistryPath -Name "DisplayName" -Value $productName
    Set-ItemProperty -Path $uninstallRegistryPath -Name "DisplayVersion" -Value $Version
    Set-ItemProperty -Path $uninstallRegistryPath -Name "Publisher" -Value "Pomelo"
    Set-ItemProperty -Path $uninstallRegistryPath -Name "InstallLocation" -Value $Destination
    Set-ItemProperty -Path $uninstallRegistryPath -Name "DisplayIcon" -Value "$installedExe,0"
    Set-ItemProperty -Path $uninstallRegistryPath -Name "UninstallString" `
        -Value "powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -STA -File `"$installedUninstaller`""
    Set-ItemProperty -Path $uninstallRegistryPath -Name "NoModify" -Type DWord -Value 1
    Set-ItemProperty -Path $uninstallRegistryPath -Name "NoRepair" -Type DWord -Value 1

    if ($LaunchAfterInstall -and -not $NoLaunch) {
        Start-Process -FilePath $installedExe -WorkingDirectory $Destination
    }

    return $installedExe
}

if ($Silent) {
    $destination = if ([string]::IsNullOrWhiteSpace($InstallPath)) { $defaultInstallPath } else { $InstallPath }
    Install-Application $destination $false (-not $NoLaunch) | Out-Null
    exit 0
}

$form = New-Object System.Windows.Forms.Form
$form.Text = "安装 DesktopOrganizer"
$form.ClientSize = New-Object System.Drawing.Size(620, 350)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false
$form.MinimizeBox = $false
$form.Font = New-Object System.Drawing.Font("Microsoft YaHei UI", 9)
try {
    $form.Icon = [System.Drawing.Icon]::ExtractAssociatedIcon($payloadPath)
}
catch {
}

$titleLabel = New-Object System.Windows.Forms.Label
$titleLabel.Text = "DesktopOrganizer"
$titleLabel.Font = New-Object System.Drawing.Font("Microsoft YaHei UI", 18, [System.Drawing.FontStyle]::Bold)
$titleLabel.Location = New-Object System.Drawing.Point(28, 24)
$titleLabel.AutoSize = $true
$form.Controls.Add($titleLabel)

$descriptionLabel = New-Object System.Windows.Forms.Label
$descriptionLabel.Text = "Windows 10/11 桌面图标收纳工具  v$Version"
$descriptionLabel.ForeColor = [System.Drawing.Color]::FromArgb(75, 83, 92)
$descriptionLabel.Location = New-Object System.Drawing.Point(31, 66)
$descriptionLabel.AutoSize = $true
$form.Controls.Add($descriptionLabel)

$pathLabel = New-Object System.Windows.Forms.Label
$pathLabel.Text = "安装位置"
$pathLabel.Location = New-Object System.Drawing.Point(31, 112)
$pathLabel.AutoSize = $true
$form.Controls.Add($pathLabel)

$pathTextBox = New-Object System.Windows.Forms.TextBox
$pathTextBox.Text = if ([string]::IsNullOrWhiteSpace($InstallPath)) { $defaultInstallPath } else { $InstallPath }
$pathTextBox.Location = New-Object System.Drawing.Point(34, 138)
$pathTextBox.Size = New-Object System.Drawing.Size(455, 28)
$form.Controls.Add($pathTextBox)

$browseButton = New-Object System.Windows.Forms.Button
$browseButton.Text = "浏览..."
$browseButton.Location = New-Object System.Drawing.Point(500, 136)
$browseButton.Size = New-Object System.Drawing.Size(88, 31)
$browseButton.Add_Click({
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = "选择 DesktopOrganizer 安装位置"
    $dialog.SelectedPath = $pathTextBox.Text
    $dialog.ShowNewFolderButton = $true
    if ($dialog.ShowDialog($form) -eq [System.Windows.Forms.DialogResult]::OK) {
        $pathTextBox.Text = $dialog.SelectedPath
    }
    $dialog.Dispose()
})
$form.Controls.Add($browseButton)

$desktopCheckBox = New-Object System.Windows.Forms.CheckBox
$desktopCheckBox.Text = "创建桌面快捷方式"
$desktopCheckBox.Checked = $true
$desktopCheckBox.Location = New-Object System.Drawing.Point(34, 190)
$desktopCheckBox.AutoSize = $true
$form.Controls.Add($desktopCheckBox)

$launchCheckBox = New-Object System.Windows.Forms.CheckBox
$launchCheckBox.Text = "安装完成后启动 DesktopOrganizer"
$launchCheckBox.Checked = $true
$launchCheckBox.Location = New-Object System.Drawing.Point(34, 222)
$launchCheckBox.AutoSize = $true
$form.Controls.Add($launchCheckBox)

$statusLabel = New-Object System.Windows.Forms.Label
$statusLabel.Text = "安装程序不会移动或删除桌面上的原文件。"
$statusLabel.ForeColor = [System.Drawing.Color]::FromArgb(91, 99, 108)
$statusLabel.Location = New-Object System.Drawing.Point(31, 266)
$statusLabel.AutoSize = $true
$form.Controls.Add($statusLabel)

$cancelButton = New-Object System.Windows.Forms.Button
$cancelButton.Text = "取消"
$cancelButton.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
$cancelButton.Location = New-Object System.Drawing.Point(395, 301)
$cancelButton.Size = New-Object System.Drawing.Size(92, 34)
$form.CancelButton = $cancelButton
$form.Controls.Add($cancelButton)

$installButton = New-Object System.Windows.Forms.Button
$installButton.Text = "安装"
$installButton.Location = New-Object System.Drawing.Point(496, 301)
$installButton.Size = New-Object System.Drawing.Size(92, 34)
$form.AcceptButton = $installButton
$form.Controls.Add($installButton)

$installButton.Add_Click({
    $installButton.Enabled = $false
    $browseButton.Enabled = $false
    $statusLabel.Text = "正在安装..."
    $form.Refresh()
    try {
        Install-Application $pathTextBox.Text $desktopCheckBox.Checked $launchCheckBox.Checked | Out-Null
        [System.Windows.Forms.MessageBox]::Show(
            $form,
            "DesktopOrganizer 已安装完成。",
            "安装完成",
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
        $form.DialogResult = [System.Windows.Forms.DialogResult]::OK
        $form.Close()
    }
    catch [System.OperationCanceledException] {
        $statusLabel.Text = "安装已取消。"
        $installButton.Enabled = $true
        $browseButton.Enabled = $true
    }
    catch {
        $statusLabel.Text = "安装失败。"
        $installButton.Enabled = $true
        $browseButton.Enabled = $true
        [System.Windows.Forms.MessageBox]::Show(
            $form,
            "安装失败：`r`n`r`n$($_.Exception.Message)",
            "DesktopOrganizer 安装",
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    }
})

[void]$form.ShowDialog()
$form.Dispose()
