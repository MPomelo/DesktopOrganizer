namespace DesktopOrganizer.App.Models;

public sealed record ShellLinkInfo(
    string TargetPath,
    string Arguments,
    string WorkingDirectory,
    string IconPath,
    int IconIndex,
    string Description);
