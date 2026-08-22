using DesktopOrganizer.App.Models;
using DesktopOrganizer.App.Services;
using DesktopOrganizer.App.ViewModels;
using System.Text.Json;
using System.Text.Json.Serialization;

var tests = new (string Name, Func<Task> Run)[]
{
    ("creates a default configuration", CreatesDefaultConfigurationAsync),
    ("round-trips container and icon data", RoundTripsConfigurationAsync),
    ("normalizes an incomplete current schema", NormalizesIncompleteConfigurationAsync),
    ("backs up and recovers corrupt JSON", RecoversCorruptConfigurationAsync),
    ("does not overwrite a future schema", PreservesFutureSchemaAsync),
    ("imports files and folders with cached icons", ImportsFilesAndFoldersAsync),
    ("parses and rolls back a managed shortcut", ManagesAndRollsBackShortcutAsync),
    ("persists imported icons and restores visible tiles", PersistsAndRestoresImportedIconsAsync),
    ("restores a shortcut when configuration save fails", RollsBackWhenPersistenceFailsAsync),
    ("builds Shell launch settings for supported item types", BuildsShellLaunchSettingsAsync),
    ("launches a target and persists recent launch time", LaunchesAndPersistsRecentTimeAsync),
    ("exposes every stored icon to the expanded window", ExposesAllStoredIconsAsync),
    ("resolves system, automatic, and custom theme colors", ResolvesThemeColorsAsync),
    ("previews, cancels, and persists container settings", ManagesContainerSettingsAsync),
    ("rolls back settings when persistence fails", RollsBackSettingsWhenPersistenceFailsAsync),
    ("plans release capacity, conflicts, and existing desktop references", PlansContainerReleaseAsync),
    ("releases managed, file, and URL items transactionally", ReleasesContainerItemsAsync),
    ("rolls back release operations when execution fails", RollsBackContainerReleaseAsync),
    ("rolls back release when configuration save fails", RollsBackReleaseWhenConfigurationSaveFailsAsync),
    ("recovers an interrupted release from its journal", RecoversInterruptedContainerReleaseAsync)
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS: {test.Name}");
    }
    catch (Exception exception)
    {
        failed++;
        Console.Error.WriteLine($"FAIL: {test.Name}");
        Console.Error.WriteLine(exception);
    }
}

Console.WriteLine($"Completed {tests.Length} tests; {failed} failed.");
return failed == 0 ? 0 : 1;

static async Task CreatesDefaultConfigurationAsync()
{
    await WithTestDirectoryAsync(async directory =>
    {
        var service = new ConfigService(new TestLogger(), directory);
        var config = await service.LoadAsync();

        Assert(File.Exists(service.ConfigPath), "config.json was not created.");
        Assert(config.SchemaVersion == AppConfig.CurrentSchemaVersion, "Unexpected schema version.");
        Assert(config.Containers.Count == 1, "A single default container was expected.");

        var container = config.Containers[0];
        Assert(container.SizeMode == ContainerSizeMode.TwoByTwo, "Default size must be 2 x 2.");
        Assert(
            container.Orientation == ContainerOrientation.Horizontal,
            "Default orientation must be horizontal.");
        Assert(!container.HasSavedPosition, "Default configuration must not claim a saved position.");
        AssertNoSaveArtifacts(directory);
    });
}

static async Task RoundTripsConfigurationAsync()
{
    await WithTestDirectoryAsync(async directory =>
    {
        var service = new ConfigService(new TestLogger(), directory);
        var config = AppConfig.CreateDefault();
        var container = config.Containers[0];
        container.SizeMode = ContainerSizeMode.TwoByFour;
        container.Orientation = ContainerOrientation.Vertical;
        container.Left = 321.5;
        container.Top = 88.25;
        container.HasSavedPosition = true;
        container.MonitorDeviceName = "DISPLAY_TEST";
        container.Items.Add(new StoredIconItem
        {
            Id = "item-1",
            DisplayName = "Test item",
            Kind = IconItemKind.Shortcut,
            SourcePath = @"C:\Source\Test.lnk",
            LaunchTarget = @"C:\Apps\Test.exe",
            Arguments = "--test",
            WorkingDirectory = @"C:\Apps",
            IconSource = @"C:\Apps\Test.exe,0",
            StorageMode = IconStorageMode.MovedToManagedStorage,
            OriginalPath = @"C:\Desktop\Test.lnk",
            ManagedPath = @"C:\Managed\Test.lnk",
            AddedAt = DateTimeOffset.Parse("2026-08-18T08:00:00+08:00"),
            LastLaunchedAt = DateTimeOffset.Parse("2026-08-18T09:00:00+08:00")
        });
        container.PinnedIconIds.Add("item-1");

        await service.SaveAsync(config);
        var reloaded = await new ConfigService(new TestLogger(), directory).LoadAsync();
        var actual = reloaded.Containers.Single();

        Assert(actual.SizeMode == ContainerSizeMode.TwoByFour, "Size mode did not round-trip.");
        Assert(actual.Orientation == ContainerOrientation.Vertical, "Orientation did not round-trip.");
        Assert(actual.Left == 321.5 && actual.Top == 88.25, "Window position did not round-trip.");
        Assert(actual.HasSavedPosition, "Saved-position marker did not round-trip.");
        Assert(actual.MonitorDeviceName == "DISPLAY_TEST", "Monitor name did not round-trip.");
        Assert(actual.Items.Count == 1, "Stored icon item did not round-trip.");
        Assert(actual.Items[0].ManagedPath == @"C:\Managed\Test.lnk", "Managed path changed.");
        Assert(actual.PinnedIconIds.SequenceEqual(["item-1"]), "Pinned icon IDs changed.");
        AssertNoSaveArtifacts(directory);
    });
}

static async Task RecoversCorruptConfigurationAsync()
{
    await WithTestDirectoryAsync(async directory =>
    {
        Directory.CreateDirectory(directory);
        var configPath = Path.Combine(directory, "config.json");
        await File.WriteAllTextAsync(configPath, "{ this is not valid JSON");

        var logger = new TestLogger();
        var config = await new ConfigService(logger, directory).LoadAsync();

        Assert(config.Containers.Count == 1, "Recovery did not create a default container.");
        Assert(File.Exists(configPath), "Recovery did not recreate config.json.");
        Assert(
            Directory.GetFiles(directory, "config.bad*.json").Length == 1,
            "Corrupt configuration was not backed up exactly once.");
        Assert(logger.Errors.Count == 1, "Corrupt configuration was not logged.");
        AssertNoSaveArtifacts(directory);
    });
}

static async Task NormalizesIncompleteConfigurationAsync()
{
    await WithTestDirectoryAsync(async directory =>
    {
        Directory.CreateDirectory(directory);
        var configPath = Path.Combine(directory, "config.json");
        const string incompleteConfig = """
            {
              "schemaVersion": 1,
              "containers": []
            }
            """;
        await File.WriteAllTextAsync(configPath, incompleteConfig);

        var service = new ConfigService(new TestLogger(), directory);
        var config = await service.LoadAsync();

        Assert(config.Containers.Count == 1, "Missing default container was not normalized.");
        AssertNoSaveArtifacts(directory);

        var savedJson = await File.ReadAllTextAsync(configPath);
        Assert(savedJson.Contains("TwoByTwo"), "Normalized configuration was not saved.");
    });
}

static async Task PreservesFutureSchemaAsync()
{
    await WithTestDirectoryAsync(async directory =>
    {
        Directory.CreateDirectory(directory);
        var configPath = Path.Combine(directory, "config.json");
        const string futureConfig = """
            {
              "schemaVersion": 999,
              "containers": []
            }
            """;
        await File.WriteAllTextAsync(configPath, futureConfig);

        var service = new ConfigService(new TestLogger(), directory);
        await AssertThrowsAsync<NotSupportedException>(() => service.LoadAsync());

        Assert(await File.ReadAllTextAsync(configPath) == futureConfig, "Future config was overwritten.");
        Assert(
            Directory.GetFiles(directory, "config.bad*.json").Length == 0,
            "Future config was incorrectly marked as corrupt.");
    });
}

static async Task ImportsFilesAndFoldersAsync()
{
    await WithTestDirectoryAsync(async directory =>
    {
        var sourceDirectory = Path.Combine(directory, "Source");
        var folderPath = Path.Combine(sourceDirectory, "Example Folder");
        var filePath = Path.Combine(sourceDirectory, "Example.txt");
        Directory.CreateDirectory(folderPath);
        await File.WriteAllTextAsync(filePath, "DesktopOrganizer stage 3");

        var (service, _) = CreateImportService(directory, [Path.Combine(directory, "Desktop")]);
        var result = await service.ImportAsync([filePath, folderPath], [], false);

        Assert(result.Failures.Count == 0, "Valid file and folder imports should not fail.");
        Assert(result.Imported.Count == 2, "Both dropped items should be imported.");

        var fileItem = result.Imported.Single(item => item.Item.Kind == IconItemKind.File);
        var folderItem = result.Imported.Single(item => item.Item.Kind == IconItemKind.Folder);
        Assert(fileItem.Item.DisplayName == "Example", "File display name is incorrect.");
        Assert(folderItem.Item.DisplayName == "Example Folder", "Folder display name is incorrect.");
        Assert(fileItem.Item.StorageMode == IconStorageMode.ReferenceOnly, "File must remain a reference.");
        Assert(folderItem.Item.StorageMode == IconStorageMode.ReferenceOnly, "Folder must remain a reference.");
        Assert(File.Exists(filePath), "Import moved or deleted the original file.");
        Assert(Directory.Exists(folderPath), "Import moved or deleted the original folder.");

        foreach (var artifact in result.Imported)
        {
            Assert(!string.IsNullOrWhiteSpace(artifact.Item.IconSource), "An icon was not cached.");
            AssertPngFile(artifact.Item.IconSource);
        }

        var duplicate = await service.ImportAsync([filePath], result.Imported.Select(x => x.Item).ToArray(), false);
        Assert(duplicate.Imported.Count == 0, "A duplicate item was imported twice.");
        Assert(duplicate.Failures.Count == 1, "Duplicate import should return one failure.");
    });
}

static async Task ManagesAndRollsBackShortcutAsync()
{
    await WithTestDirectoryAsync(async directory =>
    {
        var desktopDirectory = Path.Combine(directory, "Desktop");
        Directory.CreateDirectory(desktopDirectory);
        var shortcutPath = Path.Combine(desktopDirectory, "Stage 3 Test.lnk");
        var targetPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "notepad.exe");
        CreateShortcut(shortcutPath, targetPath, "test-file.txt", Path.GetDirectoryName(targetPath)!);

        var (service, _) = CreateImportService(directory, [desktopDirectory]);
        Assert(service.ContainsDesktopShortcut([shortcutPath]), "Desktop shortcut was not detected.");

        var result = await service.ImportAsync([shortcutPath], [], true);
        Assert(result.Failures.Count == 0, "Shortcut import failed.");
        Assert(result.Imported.Count == 1, "Shortcut was not imported.");

        var artifact = result.Imported[0];
        Assert(artifact.Item.Kind == IconItemKind.Shortcut, "Shortcut kind is incorrect.");
        Assert(
            string.Equals(artifact.Item.LaunchTarget, targetPath, StringComparison.OrdinalIgnoreCase),
            "Shortcut target was not parsed.");
        Assert(artifact.Item.Arguments == "test-file.txt", "Shortcut arguments were not parsed.");
        Assert(
            artifact.Item.StorageMode == IconStorageMode.MovedToManagedStorage,
            "Desktop shortcut was not marked as managed.");
        Assert(!File.Exists(shortcutPath), "Original desktop shortcut still exists after managed import.");
        Assert(File.Exists(artifact.Item.ManagedPath), "Managed shortcut does not exist.");
        AssertPngFile(artifact.Item.IconSource);

        await service.RollbackAsync(result);
        Assert(File.Exists(shortcutPath), "Rollback did not restore the desktop shortcut.");
        Assert(!File.Exists(artifact.Item.ManagedPath), "Rollback left the managed shortcut behind.");
        Assert(!File.Exists(artifact.Item.IconSource), "Rollback left the icon cache behind.");
    });
}

static async Task PersistsAndRestoresImportedIconsAsync()
{
    await WithTestDirectoryAsync(async directory =>
    {
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "Persistent Item.txt");
        await File.WriteAllTextAsync(sourcePath, "persistent");

        var appDirectory = Path.Combine(directory, "AppData");
        var logger = new TestLogger();
        var configService = new ConfigService(logger, appDirectory);
        var config = await configService.LoadAsync();
        var (importService, _) = CreateImportService(
            directory,
            [Path.Combine(directory, "Desktop")],
            appDirectory,
            logger);

        var viewModel = new ContainerViewModel(
            new DesktopGridService(),
            config.Containers[0],
            config.Behavior,
            importService,
            new IconLaunchService(logger),
            logger,
            () => configService.SaveAsync(config));
        var result = await viewModel.ImportFilesAsync([sourcePath]);

        Assert(result.ImportedCount == 1, "View model did not import the item.");
        Assert(config.Containers[0].Items.Count == 1, "Imported item was not added to config.");
        Assert(config.Containers[0].PinnedIconIds.Count == 1, "Imported item was not pinned.");
        Assert(viewModel.Tiles[0].HasIcon, "Imported item is not displayed with its cached icon.");
        Assert(viewModel.Tiles[0].Label == "Persistent Item", "Visible tile name is incorrect.");

        var reloaded = await new ConfigService(logger, appDirectory).LoadAsync();
        var restoredViewModel = new ContainerViewModel(
            new DesktopGridService(),
            reloaded.Containers[0],
            reloaded.Behavior,
            importService,
            new IconLaunchService(logger),
            logger,
            () => Task.CompletedTask);

        Assert(restoredViewModel.StoredItems.Count == 1, "Imported item was not restored from JSON.");
        Assert(restoredViewModel.Tiles[0].HasIcon, "Restored tile did not reload its cached icon.");
        Assert(restoredViewModel.Tiles[0].Label == "Persistent Item", "Restored tile name changed.");
    });
}

static async Task RollsBackWhenPersistenceFailsAsync()
{
    await WithTestDirectoryAsync(async directory =>
    {
        var desktopDirectory = Path.Combine(directory, "Desktop");
        Directory.CreateDirectory(desktopDirectory);
        var shortcutPath = Path.Combine(desktopDirectory, "Rollback Test.lnk");
        var targetPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "notepad.exe");
        CreateShortcut(shortcutPath, targetPath, "", Path.GetDirectoryName(targetPath)!);

        var appDirectory = Path.Combine(directory, "AppData");
        var logger = new TestLogger();
        var (importService, _) = CreateImportService(
            directory,
            [desktopDirectory],
            appDirectory,
            logger);
        var config = AppConfig.CreateDefault();
        config.Behavior.HasChosenDesktopShortcutImportBehavior = true;
        config.Behavior.MoveDesktopShortcutsToManagedStorage = true;

        var viewModel = new ContainerViewModel(
            new DesktopGridService(),
            config.Containers[0],
            config.Behavior,
            importService,
            new IconLaunchService(logger),
            logger,
            () => Task.FromException(new IOException("Simulated configuration failure.")));

        await AssertThrowsAsync<IOException>(() => viewModel.ImportFilesAsync([shortcutPath]));

        Assert(File.Exists(shortcutPath), "Save failure did not restore the desktop shortcut.");
        Assert(config.Containers[0].Items.Count == 0, "Save failure left an item in configuration.");
        Assert(config.Containers[0].PinnedIconIds.Count == 0, "Save failure left a pinned ID.");
        Assert(
            !Directory.Exists(Path.Combine(appDirectory, "IconCache"))
                || Directory.GetFiles(Path.Combine(appDirectory, "IconCache")).Length == 0,
            "Save failure left an icon cache artifact.");
    });
}

static Task BuildsShellLaunchSettingsAsync()
{
    var fileItem = new StoredIconItem
    {
        Kind = IconItemKind.File,
        SourcePath = @"C:\Documents\Example.txt",
        LaunchTarget = @"C:\Wrong\Target.exe"
    };
    var fileStartInfo = IconLaunchService.CreateStartInfo(fileItem);
    Assert(fileStartInfo.UseShellExecute, "Files must use Windows Shell execution.");
    Assert(
        fileStartInfo.FileName == fileItem.SourcePath,
        "A file must launch its source path with the default application.");

    var folderItem = new StoredIconItem
    {
        Kind = IconItemKind.Folder,
        SourcePath = @"C:\Documents\Example Folder"
    };
    var folderStartInfo = IconLaunchService.CreateStartInfo(folderItem);
    Assert(folderStartInfo.UseShellExecute, "Folders must use Windows Shell execution.");
    Assert(folderStartInfo.FileName == folderItem.SourcePath, "Folder source path changed.");

    var shortcutItem = new StoredIconItem
    {
        Kind = IconItemKind.Shortcut,
        SourcePath = @"C:\Managed\Example.lnk",
        LaunchTarget = @"C:\Apps\Example.exe",
        Arguments = "--stage-4",
        WorkingDirectory = Environment.SystemDirectory
    };
    var shortcutStartInfo = IconLaunchService.CreateStartInfo(shortcutItem);
    Assert(shortcutStartInfo.UseShellExecute, "Shortcuts must use Windows Shell execution.");
    Assert(
        shortcutStartInfo.FileName == shortcutItem.LaunchTarget,
        "Shortcut target was not selected for launch.");
    Assert(shortcutStartInfo.Arguments == "--stage-4", "Shortcut arguments changed.");
    Assert(
        shortcutStartInfo.WorkingDirectory == Environment.SystemDirectory,
        "Shortcut working directory changed.");

    return Task.CompletedTask;
}

static async Task LaunchesAndPersistsRecentTimeAsync()
{
    await WithTestDirectoryAsync(async directory =>
    {
        Directory.CreateDirectory(directory);
        var scriptPath = Path.Combine(directory, "launch-probe.vbs");
        var markerPath = Path.Combine(directory, "launched.txt");
        const string script = """
            Set fso = CreateObject("Scripting.FileSystemObject")
            Set marker = fso.CreateTextFile(WScript.Arguments(0), True)
            marker.WriteLine "launched"
            marker.Close
            """;
        await File.WriteAllTextAsync(scriptPath, script);

        var logger = new TestLogger();
        var appDirectory = Path.Combine(directory, "AppData");
        var configService = new ConfigService(logger, appDirectory);
        var config = AppConfig.CreateDefault();
        var item = new StoredIconItem
        {
            Id = "launch-probe",
            DisplayName = "Launch probe",
            Kind = IconItemKind.Shortcut,
            SourcePath = scriptPath,
            LaunchTarget = Path.Combine(Environment.SystemDirectory, "wscript.exe"),
            Arguments = $"\"{scriptPath}\" \"{markerPath}\"",
            WorkingDirectory = directory,
            StorageMode = IconStorageMode.ReferenceOnly,
            OriginalPath = scriptPath,
            AddedAt = DateTimeOffset.Now
        };
        config.Containers[0].Items.Add(item);
        config.Containers[0].PinnedIconIds.Add(item.Id);

        var (importService, _) = CreateImportService(
            directory,
            [Path.Combine(directory, "Desktop")],
            appDirectory,
            logger);
        var viewModel = new ContainerViewModel(
            new DesktopGridService(),
            config.Containers[0],
            config.Behavior,
            importService,
            new IconLaunchService(logger),
            logger,
            () => configService.SaveAsync(config));

        var beforeLaunch = DateTimeOffset.Now;
        await viewModel.LaunchItemAsync(item.Id);
        await WaitForFileAsync(markerPath, TimeSpan.FromSeconds(5));
        await WaitForFileUnlockedAsync(markerPath, TimeSpan.FromSeconds(5));

        Assert(item.LastLaunchedAt >= beforeLaunch, "Recent launch time was not recorded.");
        Assert(await File.ReadAllTextAsync(markerPath) == "launched\r\n", "Target process did not run.");

        var reloaded = await new ConfigService(logger, appDirectory).LoadAsync();
        Assert(
            reloaded.Containers[0].Items.Single().LastLaunchedAt is not null,
            "Recent launch time was not persisted to JSON.");
    });
}

static async Task ExposesAllStoredIconsAsync()
{
    await WithTestDirectoryAsync(directory =>
    {
        var config = AppConfig.CreateDefault();
        var container = config.Containers[0];
        for (var index = 0; index < 12; index++)
        {
            var item = new StoredIconItem
            {
                Id = $"expanded-item-{index}",
                DisplayName = $"Expanded item {index}",
                Kind = IconItemKind.File,
                SourcePath = Path.Combine(directory, $"item-{index}.txt"),
                AddedAt = DateTimeOffset.Now.AddMinutes(index)
            };
            container.Items.Add(item);
            container.PinnedIconIds.Add(item.Id);
        }

        var logger = new TestLogger();
        var (importService, _) = CreateImportService(
            directory,
            [Path.Combine(directory, "Desktop")],
            logger: logger);
        var viewModel = new ContainerViewModel(
            new DesktopGridService(),
            container,
            config.Behavior,
            importService,
            new IconLaunchService(logger),
            logger,
            () => Task.CompletedTask);

        Assert(viewModel.Tiles.Count == 4, "The compact 2 x 2 container must keep four slots.");
        Assert(
            viewModel.Tiles.Count(tile => tile.ItemId is not null) == 3,
            "The compact container must reserve one slot for the more button.");
        Assert(viewModel.AllItemTiles.Count == 12, "The expanded collection omitted stored icons.");
        Assert(
            viewModel.AllItemTiles.Select(tile => tile.ItemId)
                .SequenceEqual(container.Items.Select(item => item.Id)),
            "The expanded collection changed the configured icon order.");
        Assert(
            viewModel.AllItemTiles.All(tile => !tile.IsMoreButton && !tile.IsPlaceholder),
            "The expanded collection must contain only real icon items.");

        viewModel.SetTwoByFourCommand.Execute(null);
        Assert(viewModel.Tiles.Count == 8, "The compact 2 x 4 layout did not refresh.");
        Assert(
            viewModel.AllItemTiles.Count == 12,
            "Changing compact capacity truncated the expanded collection.");

        return Task.CompletedTask;
    });
}

static Task ResolvesThemeColorsAsync()
{
    var accent = System.Windows.Media.Color.FromRgb(0x22, 0x66, 0xAA);
    var service = new ThemeService(() => accent, () => false);
    var style = new ContainerStyleConfig
    {
        BorderThickness = 7,
        BackgroundOpacity = 0.6,
        BorderColor = ThemeColorSetting.Custom("#123456"),
        BackgroundColor = ThemeColorSetting.SystemAccent(),
        MoreButtonColor = ThemeColorSetting.Auto(),
        HoverBackgroundColor = ThemeColorSetting.Custom("#ABCDEF")
    };

    var theme = service.Resolve(style);
    Assert(
        theme.BorderColor.R == 0x12
            && theme.BorderColor.G == 0x34
            && theme.BorderColor.B == 0x56
            && theme.BorderColor.A == 230,
        "Custom border color was not resolved.");
    Assert(
        theme.BackgroundColor.R == accent.R
            && theme.BackgroundColor.G == accent.G
            && theme.BackgroundColor.B == accent.B
            && theme.BackgroundColor.A == 153,
        "System accent or background opacity was not resolved.");
    Assert(theme.MoreButtonColor.A == 55, "Automatic more-button opacity changed.");
    Assert(
        theme.HoverBackgroundColor.R == 0xAB && theme.HoverBackgroundColor.A == 100,
        "Custom hover color was not resolved.");
    Assert(
        ThemeService.TryNormalizeHexColor(" 608ccb ", out var normalized)
            && normalized == "#608CCB",
        "A valid custom hex color was not normalized.");
    Assert(
        !ThemeService.TryNormalizeHexColor("#12GG00", out _),
        "An invalid custom hex color was accepted.");

    var highContrastTheme = new ThemeService(() => accent, () => true).Resolve(style);
    Assert(
        highContrastTheme.BackgroundColor.A == byte.MaxValue,
        "High contrast mode retained transparency.");
    return Task.CompletedTask;
}

static async Task ManagesContainerSettingsAsync()
{
    await WithTestDirectoryAsync(async directory =>
    {
        var logger = new TestLogger();
        var appDirectory = Path.Combine(directory, "AppData");
        var configService = new ConfigService(logger, appDirectory);
        var config = AppConfig.CreateDefault();
        var container = config.Containers[0];
        var managedDirectory = Path.Combine(appDirectory, "ManagedShortcuts");
        var desktopDirectory = Path.Combine(directory, "Desktop");
        Directory.CreateDirectory(managedDirectory);
        Directory.CreateDirectory(desktopDirectory);
        var managedShortcutPath = Path.Combine(managedDirectory, "settings-managed.lnk");
        var restoredShortcutPath = Path.Combine(desktopDirectory, "Settings managed.lnk");
        await File.WriteAllTextAsync(managedShortcutPath, "managed shortcut probe");
        for (var index = 0; index < 4; index++)
        {
            var item = new StoredIconItem
            {
                Id = $"settings-item-{index}",
                DisplayName = $"Settings item {index}",
                Kind = IconItemKind.File,
                SourcePath = Path.Combine(directory, $"item-{index}.txt"),
                AddedAt = DateTimeOffset.Now
            };
            if (index == 3)
            {
                item.SourcePath = managedShortcutPath;
                item.ManagedPath = managedShortcutPath;
                item.OriginalPath = restoredShortcutPath;
                item.StorageMode = IconStorageMode.MovedToManagedStorage;
                item.Kind = IconItemKind.Shortcut;
            }
            container.Items.Add(item);
            container.PinnedIconIds.Add(item.Id);
        }
        await configService.SaveAsync(config);

        var (importService, _) = CreateImportService(
            directory,
            [Path.Combine(directory, "Desktop")],
            appDirectory,
            logger);
        var themeService = new ThemeService(
            () => System.Windows.Media.Color.FromRgb(0x60, 0x8C, 0xCB),
            () => false);
        var viewModel = new ContainerViewModel(
            new DesktopGridService(),
            container,
            config.Behavior,
            importService,
            new IconLaunchService(logger),
            logger,
            () => configService.SaveAsync(config),
            themeService);

        var originalWidth = viewModel.WindowWidth;
        var originalHeight = viewModel.WindowHeight;
        var preview = new SettingsViewModel(viewModel)
        {
            Name = "Preview name",
            SizeMode = ContainerSizeMode.TwoByFour,
            Orientation = ContainerOrientation.Vertical,
            BorderThickness = 8,
            BackgroundOpacity = 0.7
        };
        preview.BorderColor.Mode = ThemeColorMode.Custom;
        preview.BorderColor.HexColor = "#123456";
        preview.IconItems[1].MoveUpCommand.Execute(null);
        preview.IconItems[0].IsPinned = false;
        preview.IconItems[^1].DeleteCommand.Execute(null);

        Assert(container.Name == "Preview name", "Settings name did not preview immediately.");
        Assert(viewModel.SizeMode == ContainerSizeMode.TwoByFour, "Size did not preview immediately.");
        Assert(
            viewModel.WindowWidth != originalWidth || viewModel.WindowHeight != originalHeight,
            "Preview did not refresh the compact layout.");
        Assert(container.Items.Count == 3, "Deletion did not preview immediately.");
        Assert(container.Style.BorderThickness == 8, "Border thickness did not preview.");
        preview.Cancel();

        Assert(container.Name == "收纳区", "Cancel did not restore the container name.");
        Assert(container.SizeMode == ContainerSizeMode.TwoByTwo, "Cancel did not restore size.");
        Assert(container.Items.Count == 4, "Cancel did not restore deleted items.");
        Assert(
            File.Exists(managedShortcutPath) && !File.Exists(restoredShortcutPath),
            "Cancel moved a managed shortcut during preview.");
        Assert(
            container.Items.Select(item => item.Id)
                .SequenceEqual(Enumerable.Range(0, 4).Select(index => $"settings-item-{index}")),
            "Cancel did not restore icon order.");
        Assert(container.Style.BorderThickness == 4, "Cancel did not restore style.");

        var saved = new SettingsViewModel(viewModel)
        {
            Name = "Saved container",
            SizeMode = ContainerSizeMode.TwoByFour,
            Orientation = ContainerOrientation.Vertical,
            BorderThickness = 6,
            BackgroundOpacity = 0.65
        };
        saved.BackgroundColor.Mode = ThemeColorMode.Custom;
        saved.BackgroundColor.HexColor = "#336699";
        saved.IconItems[2].MoveUpCommand.Execute(null);
        saved.IconItems[0].DisplayName = "Renamed icon";
        saved.IconItems[1].IsPinned = false;
        saved.IconItems[^1].DeleteCommand.Execute(null);
        await saved.SaveAsync();

        var reloaded = await new ConfigService(logger, appDirectory).LoadAsync();
        var actual = reloaded.Containers[0];
        Assert(actual.Name == "Saved container", "Saved settings name was not persisted.");
        Assert(
            actual.SizeMode == ContainerSizeMode.TwoByFour
                && actual.Orientation == ContainerOrientation.Vertical,
            "Saved layout was not persisted.");
        Assert(actual.Items.Count == 3, "Saved deletion was not persisted.");
        Assert(
            !File.Exists(managedShortcutPath) && File.Exists(restoredShortcutPath),
            "Deleting a managed item did not restore its shortcut.");
        Assert(actual.Items[0].DisplayName == "Renamed icon", "Icon rename was not persisted.");
        Assert(actual.Items[1].Id == "settings-item-2", "Icon ordering was not persisted.");
        Assert(!actual.PinnedIconIds.Contains("settings-item-2"), "External icon choice changed.");
        Assert(actual.Style.BorderThickness == 6, "Border thickness was not persisted.");
        Assert(actual.Style.BackgroundOpacity == 0.65, "Background opacity was not persisted.");
        Assert(
            actual.Style.BackgroundColor.Mode == ThemeColorMode.Custom
                && actual.Style.BackgroundColor.HexColor == "#336699",
            "Custom background color was not persisted.");
    });
}

static async Task RollsBackSettingsWhenPersistenceFailsAsync()
{
    await WithTestDirectoryAsync(async directory =>
    {
        var config = AppConfig.CreateDefault();
        var container = config.Containers[0];
        var managedPath = Path.Combine(directory, "Managed", "rollback.lnk");
        var originalPath = Path.Combine(directory, "Desktop", "Rollback.lnk");
        Directory.CreateDirectory(Path.GetDirectoryName(managedPath)!);
        await File.WriteAllTextAsync(managedPath, "rollback shortcut probe");
        container.Items.Add(new StoredIconItem
        {
            Id = "settings-rollback-item",
            DisplayName = "Rollback item",
            Kind = IconItemKind.Shortcut,
            SourcePath = managedPath,
            ManagedPath = managedPath,
            OriginalPath = originalPath,
            StorageMode = IconStorageMode.MovedToManagedStorage,
            AddedAt = DateTimeOffset.Now
        });
        container.PinnedIconIds.Add("settings-rollback-item");
        var logger = new TestLogger();
        var (importService, _) = CreateImportService(
            directory,
            [Path.Combine(directory, "Desktop")],
            logger: logger);
        var viewModel = new ContainerViewModel(
            new DesktopGridService(),
            container,
            config.Behavior,
            importService,
            new IconLaunchService(logger),
            logger,
            () => Task.FromException(new IOException("Simulated settings save failure.")));
        var settings = new SettingsViewModel(viewModel)
        {
            Name = "Must roll back",
            BorderThickness = 10
        };
        settings.IconItems[0].DeleteCommand.Execute(null);

        await AssertThrowsAsync<IOException>(settings.SaveAsync);

        Assert(container.Name == "收纳区", "Save failure did not restore the name.");
        Assert(container.Style.BorderThickness == 4, "Save failure did not restore style.");
        Assert(container.Items.Count == 1, "Save failure did not restore the removed item.");
        Assert(
            File.Exists(managedPath) && !File.Exists(originalPath),
            "Save failure did not roll back the restored managed shortcut.");
        Assert(!viewModel.HasActiveSettingsPreview, "Save failure left a preview transaction active.");
    });
}

static async Task PlansContainerReleaseAsync()
{
    await WithTestDirectoryAsync(async directory =>
    {
        var desktop = Path.Combine(directory, "Desktop");
        var publicDesktop = Path.Combine(directory, "PublicDesktop");
        var startMenu = Path.Combine(directory, "StartMenu");
        var appDirectory = Path.Combine(directory, "AppData");
        var managedDirectory = Path.Combine(appDirectory, "ManagedShortcuts");
        Directory.CreateDirectory(desktop);
        Directory.CreateDirectory(publicDesktop);
        Directory.CreateDirectory(managedDirectory);
        var existingReference = Path.Combine(desktop, "Already here.txt");
        var managedPath = Path.Combine(managedDirectory, "managed.lnk");
        await File.WriteAllTextAsync(existingReference, "existing desktop reference");
        await File.WriteAllTextAsync(Path.Combine(desktop, "Managed item.lnk"), "name conflict");
        await File.WriteAllTextAsync(managedPath, "managed shortcut");

        var container = ContainerConfig.CreateDefault();
        container.Items.Add(new StoredIconItem
        {
            Id = "existing", DisplayName = "Already here", Kind = IconItemKind.File,
            SourcePath = existingReference, LaunchTarget = existingReference,
            StorageMode = IconStorageMode.ReferenceOnly
        });
        container.Items.Add(new StoredIconItem
        {
            Id = "managed", DisplayName = "Managed item", Kind = IconItemKind.Shortcut,
            SourcePath = managedPath, ManagedPath = managedPath,
            OriginalPath = Path.Combine(desktop, "Managed item.lnk"),
            StorageMode = IconStorageMode.MovedToManagedStorage
        });
        var service = CreateReleaseService(
            appDirectory, desktop, publicDesktop, startMenu, new TestLogger());
        var plan = service.CreatePlan(container, 1);

        Assert(plan.TotalCount == 2 && plan.NoActionCount == 1, "Existing desktop reference was not skipped.");
        Assert(plan.DesktopCount == 1 && plan.StartMenuCount == 0, "Desktop assignment was incorrect.");
        Assert(plan.MaximumDesktopCount == 1 && plan.IsCapacityEstimate, "Safe capacity was incorrect.");
        Assert(
            plan.Actions.Single(action => action.ItemId == "managed").TargetPath
                == Path.Combine(desktop, "Managed item (2).lnk"),
            "Conflicting desktop filename was not suffixed.");
        Assert(!Directory.Exists(startMenu), "Creating a release plan modified the start menu.");
    });
}

static async Task ReleasesContainerItemsAsync()
{
    await WithTestDirectoryAsync(async directory =>
    {
        var desktop = Path.Combine(directory, "Desktop");
        var publicDesktop = Path.Combine(directory, "PublicDesktop");
        var startMenu = Path.Combine(directory, "StartMenu");
        var appDirectory = Path.Combine(directory, "AppData");
        var managedDirectory = Path.Combine(appDirectory, "ManagedShortcuts");
        Directory.CreateDirectory(desktop);
        Directory.CreateDirectory(publicDesktop);
        Directory.CreateDirectory(managedDirectory);
        var managedPath = Path.Combine(managedDirectory, "managed.lnk");
        var sourceFile = Path.Combine(directory, "Original document.txt");
        var sourceUrl = Path.Combine(directory, "Reference.url");
        await File.WriteAllTextAsync(managedPath, "managed shortcut payload");
        await File.WriteAllTextAsync(sourceFile, "original must remain");
        await File.WriteAllTextAsync(sourceUrl, "[InternetShortcut]\nURL=https://example.com/release\n");

        var logger = new TestLogger();
        var configService = new ConfigService(logger, appDirectory);
        var config = AppConfig.CreateDefault();
        var container = config.Containers[0];
        container.Items.Add(new StoredIconItem
        {
            Id = "release-managed", DisplayName = "Managed shortcut", Kind = IconItemKind.Shortcut,
            SourcePath = managedPath, ManagedPath = managedPath,
            OriginalPath = Path.Combine(desktop, "Managed shortcut.lnk"),
            StorageMode = IconStorageMode.MovedToManagedStorage
        });
        container.Items.Add(new StoredIconItem
        {
            Id = "release-file", DisplayName = "Original document", Kind = IconItemKind.File,
            SourcePath = sourceFile, LaunchTarget = sourceFile, WorkingDirectory = directory,
            StorageMode = IconStorageMode.ReferenceOnly
        });
        container.Items.Add(new StoredIconItem
        {
            Id = "release-url", DisplayName = "Reference URL", Kind = IconItemKind.Url,
            SourcePath = sourceUrl, LaunchTarget = sourceUrl,
            StorageMode = IconStorageMode.ReferenceOnly
        });
        await configService.SaveAsync(config);

        var service = CreateReleaseService(appDirectory, desktop, publicDesktop, startMenu, logger);
        var result = await service.ExecuteAsync(
            config,
            service.CreatePlan(container, 1),
            () => configService.SaveAsync(config));

        Assert(result.DesktopCount == 1 && result.StartMenuCount == 2, "Release result counts were incorrect.");
        Assert(File.Exists(Path.Combine(desktop, "Managed shortcut.lnk")), "Managed shortcut was not restored.");
        Assert(File.Exists(Path.Combine(startMenu, "Original document.lnk")), "File shortcut was not created.");
        Assert(File.Exists(Path.Combine(startMenu, "Reference URL.url")), "URL shortcut was not created.");
        Assert(File.Exists(sourceFile) && File.Exists(sourceUrl), "A referenced source was moved or deleted.");
        Assert(!File.Exists(managedPath), "Managed shortcut remained in managed storage.");
        Assert(!File.Exists(service.JournalPath), "Committed release journal was not cleaned up.");
        var reloaded = await new ConfigService(logger, appDirectory).LoadAsync();
        Assert(reloaded.Containers.Count == 0, "Closed container returned after configuration reload.");
        Assert(reloaded.Behavior.HasClosedAllContainers, "Closed-container state was not persisted.");
    });
}

static async Task RollsBackContainerReleaseAsync()
{
    await WithTestDirectoryAsync(async directory =>
    {
        var desktop = Path.Combine(directory, "Desktop");
        var publicDesktop = Path.Combine(directory, "PublicDesktop");
        var startMenu = Path.Combine(directory, "StartMenu");
        var appDirectory = Path.Combine(directory, "AppData");
        var managedDirectory = Path.Combine(appDirectory, "ManagedShortcuts");
        Directory.CreateDirectory(desktop);
        Directory.CreateDirectory(publicDesktop);
        Directory.CreateDirectory(managedDirectory);
        var paths = new[]
        {
            Path.Combine(managedDirectory, "first.lnk"),
            Path.Combine(managedDirectory, "second.lnk")
        };
        await File.WriteAllTextAsync(paths[0], "first");
        await File.WriteAllTextAsync(paths[1], "second");
        var config = AppConfig.CreateDefault();
        for (var index = 0; index < paths.Length; index++)
        {
            config.Containers[0].Items.Add(new StoredIconItem
            {
                Id = $"rollback-{index}", DisplayName = $"Rollback {index}", Kind = IconItemKind.Shortcut,
                SourcePath = paths[index], ManagedPath = paths[index],
                OriginalPath = Path.Combine(desktop, $"Rollback {index}.lnk"),
                StorageMode = IconStorageMode.MovedToManagedStorage
            });
        }

        var service = CreateReleaseService(
            appDirectory, desktop, publicDesktop, startMenu, new TestLogger(),
            (_, operationIndex) => operationIndex == 1
                ? new IOException("Simulated release write failure.")
                : null);
        await AssertThrowsAsync<IOException>(() => service.ExecuteAsync(
            config,
            service.CreatePlan(config.Containers[0], 2),
            () => Task.CompletedTask));

        Assert(config.Containers.Count == 1, "Failed release removed the container configuration.");
        Assert(paths.All(File.Exists), "Failed release did not restore managed files.");
        Assert(!Directory.EnumerateFiles(desktop).Any(), "Failed release left a desktop file behind.");
        Assert(!File.Exists(service.JournalPath), "Completed rollback left a recovery journal behind.");
    });
}

static async Task RecoversInterruptedContainerReleaseAsync()
{
    await WithTestDirectoryAsync(async directory =>
    {
        var desktop = Path.Combine(directory, "Desktop");
        var publicDesktop = Path.Combine(directory, "PublicDesktop");
        var appDirectory = Path.Combine(directory, "AppData");
        var managedDirectory = Path.Combine(appDirectory, "ManagedShortcuts");
        Directory.CreateDirectory(desktop);
        Directory.CreateDirectory(publicDesktop);
        Directory.CreateDirectory(managedDirectory);
        var managedPath = Path.Combine(managedDirectory, "recover.lnk");
        var releasedPath = Path.Combine(desktop, "Recover.lnk");
        await File.WriteAllTextAsync(releasedPath, "interrupted move payload");
        var config = AppConfig.CreateDefault();
        var service = CreateReleaseService(
            appDirectory, desktop, publicDesktop, Path.Combine(directory, "StartMenu"), new TestLogger());
        Directory.CreateDirectory(appDirectory);
        var journal = new ContainerReleaseJournal
        {
            ContainerId = config.Containers[0].Id,
            StartedAt = DateTimeOffset.UtcNow,
            Entries =
            [
                new ContainerReleaseJournalEntry
                {
                    Operation = ContainerReleaseOperation.MoveFile,
                    SourcePath = managedPath,
                    TargetPath = releasedPath,
                    Completed = true
                }
            ]
        };
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter());
        await File.WriteAllTextAsync(service.JournalPath, JsonSerializer.Serialize(journal, options));
        await service.RecoverIfNeededAsync(config);

        Assert(File.Exists(managedPath), "Startup recovery did not restore the managed shortcut.");
        Assert(!File.Exists(releasedPath), "Startup recovery left the interrupted desktop file behind.");
        Assert(!File.Exists(service.JournalPath), "Startup recovery did not remove the journal.");
    });
}

static async Task RollsBackReleaseWhenConfigurationSaveFailsAsync()
{
    await WithTestDirectoryAsync(async directory =>
    {
        var desktop = Path.Combine(directory, "Desktop");
        var publicDesktop = Path.Combine(directory, "PublicDesktop");
        var appDirectory = Path.Combine(directory, "AppData");
        var managedDirectory = Path.Combine(appDirectory, "ManagedShortcuts");
        Directory.CreateDirectory(desktop);
        Directory.CreateDirectory(publicDesktop);
        Directory.CreateDirectory(managedDirectory);
        var managedPath = Path.Combine(managedDirectory, "save-failure.lnk");
        await File.WriteAllTextAsync(managedPath, "save failure payload");
        var config = AppConfig.CreateDefault();
        config.Containers[0].Items.Add(new StoredIconItem
        {
            Id = "save-failure",
            DisplayName = "Save failure",
            Kind = IconItemKind.Shortcut,
            SourcePath = managedPath,
            ManagedPath = managedPath,
            OriginalPath = Path.Combine(desktop, "Save failure.lnk"),
            StorageMode = IconStorageMode.MovedToManagedStorage
        });
        var service = CreateReleaseService(
            appDirectory,
            desktop,
            publicDesktop,
            Path.Combine(directory, "StartMenu"),
            new TestLogger());

        await AssertThrowsAsync<IOException>(() => service.ExecuteAsync(
            config,
            service.CreatePlan(config.Containers[0], 1),
            () => Task.FromException(new IOException("Simulated configuration save failure."))));

        Assert(config.Containers.Count == 1, "Save failure did not restore the container in memory.");
        Assert(!config.Behavior.HasClosedAllContainers, "Save failure retained the closed-container flag.");
        Assert(File.Exists(managedPath), "Save failure did not restore the managed shortcut.");
        Assert(!Directory.EnumerateFiles(desktop).Any(), "Save failure left a desktop shortcut behind.");
        Assert(!File.Exists(service.JournalPath), "Save-failure rollback left a journal behind.");
    });
}

static ContainerReleaseService CreateReleaseService(
    string appDirectory,
    string desktop,
    string publicDesktop,
    string startMenu,
    TestLogger logger,
    Func<ContainerReleaseAction, int, Exception?>? failureInjector = null)
{
    return new ContainerReleaseService(
        appDirectory,
        new DesktopCapacityService(
            desktop,
            publicDesktop,
            () => new DesktopGridMetrics(100, 100, 32, 1),
            _ => new System.Drawing.Rectangle(0, 0, 800, 600),
            () => 0),
        new ShortcutService(),
        logger,
        startMenu,
        failureInjector);
}

static async Task WaitForFileAsync(string path, TimeSpan timeout)
{
    var deadline = DateTimeOffset.UtcNow + timeout;
    while (!File.Exists(path) && DateTimeOffset.UtcNow < deadline)
    {
        await Task.Delay(50);
    }

    Assert(File.Exists(path), $"Timed out waiting for launched process output: {path}");
}

static async Task WaitForFileUnlockedAsync(string path, TimeSpan timeout)
{
    var deadline = DateTimeOffset.UtcNow + timeout;
    while (DateTimeOffset.UtcNow < deadline)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return;
        }
        catch (IOException)
        {
            await Task.Delay(50);
        }
    }

    throw new IOException($"Timed out waiting for launched process to release: {path}");
}

static (DragDropImportService Service, IconExtractionService IconService) CreateImportService(
    string rootDirectory,
    IEnumerable<string> desktopDirectories,
    string? appDirectory = null,
    TestLogger? logger = null)
{
    logger ??= new TestLogger();
    appDirectory ??= Path.Combine(rootDirectory, "AppData");
    var iconService = new IconExtractionService(Path.Combine(appDirectory, "IconCache"), logger);
    var service = new DragDropImportService(
        appDirectory,
        iconService,
        new ShellLinkParser(),
        logger,
        desktopDirectories);
    return (service, iconService);
}

static void CreateShortcut(
    string shortcutPath,
    string targetPath,
    string arguments,
    string workingDirectory)
{
    var shellType = Type.GetTypeFromProgID("WScript.Shell", true)
        ?? throw new NotSupportedException("Windows Script Host is unavailable.");
    dynamic shell = Activator.CreateInstance(shellType)
        ?? throw new InvalidOperationException("Could not create WScript.Shell.");
    dynamic shortcut = shell.CreateShortcut(shortcutPath);
    shortcut.TargetPath = targetPath;
    shortcut.Arguments = arguments;
    shortcut.WorkingDirectory = workingDirectory;
    shortcut.Description = "DesktopOrganizer stage 3 test";
    shortcut.Save();
}

static void AssertPngFile(string path)
{
    Assert(File.Exists(path), $"PNG cache does not exist: {path}");
    var signature = File.ReadAllBytes(path).Take(8).ToArray();
    byte[] expected = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    Assert(signature.SequenceEqual(expected), $"Icon cache is not a valid PNG: {path}");
}

static async Task WithTestDirectoryAsync(Func<string, Task> test)
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        "DesktopOrganizer.Tests",
        Guid.NewGuid().ToString("N"));

    try
    {
        await test(directory);
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            await DeleteDirectoryWithRetryAsync(directory, TimeSpan.FromSeconds(5));
        }
    }
}

static async Task DeleteDirectoryWithRetryAsync(string directory, TimeSpan timeout)
{
    var deadline = DateTimeOffset.UtcNow + timeout;
    while (true)
    {
        try
        {
            Directory.Delete(directory, true);
            return;
        }
        catch (IOException) when (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(100);
        }
        catch (UnauthorizedAccessException) when (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(100);
        }
    }
}

static void AssertNoSaveArtifacts(string directory)
{
    Assert(
        Directory.GetFiles(directory, "*.tmp").Length == 0,
        "An atomic-save temporary file was left behind.");
    Assert(
        !File.Exists(Path.Combine(directory, "config.json.previous")),
        "An atomic-save backup file was left behind.");
}

static async Task AssertThrowsAsync<TException>(Func<Task> action)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name} was not thrown.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

internal sealed class TestLogger : ILogger
{
    public List<string> Errors { get; } = [];

    public void Info(string message)
    {
    }

    public void Error(string message, Exception? exception = null)
    {
        Errors.Add(message);
    }
}
