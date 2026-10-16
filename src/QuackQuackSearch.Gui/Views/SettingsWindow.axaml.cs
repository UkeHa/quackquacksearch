using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using QuackQuackSearch.Core.Config;
using QuackQuackSearch.Core.Crawler;
using QuackQuackSearch.Core.DBus;
using QuackQuackSearch.Gui.Localization;
using QuackQuackSearch.Gui.Models;
using QuackQuackSearch.Gui.Theming;
using Tmds.DBus;

namespace QuackQuackSearch.Gui.Views;

public partial class SettingsWindow : Window
{
    private readonly QuackConfig _config;
    private readonly ObservableCollection<PathItemViewModel> _paths = [];
    private readonly ObservableCollection<string> _currentExcludes = [];
    private readonly DispatcherTimer _statusTimer;
    private bool _isPollingStatus;
    private bool _isClosing;

    public SettingsWindow()
    {
        InitializeComponent();

        var loc = Loc.Instance;
        Title = loc.SettingsWindowTitle;

        _config = ConfigManager.LoadOrCreateDefault();
        foreach (var p in _config.Paths)
        {
            _paths.Add(new PathItemViewModel(p));
        }

        var pathsTabItem = this.FindControl<TabItem>("PathsTabItem");
        if (pathsTabItem != null) pathsTabItem.Header = loc.SettingsTabPaths;

        var appearanceTabItem = this.FindControl<TabItem>("AppearanceTabItem");
        if (appearanceTabItem != null) appearanceTabItem.Header = loc.SettingsTabAppearance;

        var sectionHeader = this.FindControl<TextBlock>("SectionPathsHeader");
        if (sectionHeader != null) sectionHeader.Text = loc.SettingsSectionPathsHeader;

        var excludesHeader = this.FindControl<TextBlock>("ExcludesHeaderLabel");
        if (excludesHeader != null) excludesHeader.Text = loc.ExcludesHeader;

        InitializeAppearanceTab(loc);

        var pathsListBox = this.FindControl<ListBox>("PathsListBox");
        var excludesListBox = this.FindControl<ListBox>("ExcludesListBox");
        var selectedPathLabel = this.FindControl<TextBlock>("SelectedPathLabel");
        var newExcludeTextBox = this.FindControl<TextBox>("NewExcludeTextBox");
        var errorLabel = this.FindControl<TextBlock>("ExcludeErrorLabel");

        if (selectedPathLabel != null) selectedPathLabel.Text = loc.SelectPathPrompt;
        if (newExcludeTextBox != null) newExcludeTextBox.PlaceholderText = loc.ExcludePlaceholder;

        if (pathsListBox != null)
        {
            pathsListBox.ItemsSource = _paths;
            pathsListBox.SelectionChanged += (_, _) =>
            {
                if (errorLabel != null) errorLabel.IsVisible = false;

                var selected = pathsListBox.SelectedItem as PathItemViewModel;
                _currentExcludes.Clear();

                if (selected != null)
                {
                    if (selectedPathLabel != null) selectedPathLabel.Text = selected.Path;
                    foreach (var exc in selected.CustomExcludes)
                    {
                        _currentExcludes.Add(exc);
                    }
                }
                else
                {
                    if (selectedPathLabel != null) selectedPathLabel.Text = loc.SelectPathPrompt;
                }
            };

            if (_paths.Count > 0)
            {
                pathsListBox.SelectedIndex = 0;
            }
        }

        if (newExcludeTextBox != null)
        {
            newExcludeTextBox.TextChanged += (_, _) =>
            {
                if (errorLabel != null) errorLabel.IsVisible = false;
            };
        }

        if (excludesListBox != null)
        {
            excludesListBox.ItemsSource = _currentExcludes;
        }

        var addBtn = this.FindControl<Button>("AddFolderButton");
        if (addBtn != null)
        {
            addBtn.Content = loc.AddFolderButton;
            addBtn.Click += async (_, _) => await AddFolderAsync();
        }

        var rescanBtn = this.FindControl<Button>("RescanFolderButton");
        if (rescanBtn != null)
        {
            rescanBtn.Content = loc.RescanFolderButton;
            rescanBtn.Click += (_, _) =>
            {
                if (pathsListBox?.SelectedItem is PathItemViewModel selected && selected.Enabled)
                {
                    selected.UpdateLiveStatus("Scanning", 0, selected.Path);
                    _ = TriggerRescanInBackgroundAsync(selected.Path);
                }
            };
        }

        var removeBtn = this.FindControl<Button>("RemoveFolderButton");
        if (removeBtn != null)
        {
            removeBtn.Content = loc.RemoveFolderButton;
            removeBtn.Click += async (_, _) => await RemoveSelectedFolderAsync();
        }

        var excludeFolderBtn = this.FindControl<Button>("ExcludeFolderPickerButton");
        if (excludeFolderBtn != null)
        {
            excludeFolderBtn.Content = loc.PickSubfolderButton;
            excludeFolderBtn.Click += async (_, _) =>
            {
                if (pathsListBox?.SelectedItem is not PathItemViewModel selected) return;
                await PickSubfolderToExcludeAsync(selected);
            };
        }

        var addExcludeTextBtn = this.FindControl<Button>("AddExcludeTextButton");
        if (addExcludeTextBtn != null)
        {
            addExcludeTextBtn.Content = loc.AddExcludeButton;
            addExcludeTextBtn.Click += async (_, _) =>
            {
                if (pathsListBox?.SelectedItem is not PathItemViewModel selected) return;
                string pattern = newExcludeTextBox?.Text?.Trim() ?? string.Empty;

                if (string.IsNullOrEmpty(pattern))
                {
                    await PickSubfolderToExcludeAsync(selected);
                }
                else
                {
                    bool added = await AddExcludeAsync(selected, pattern);
                    if (added && newExcludeTextBox != null)
                    {
                        newExcludeTextBox.Text = string.Empty;
                    }
                }
            };
        }

        var removeExcludeBtn = this.FindControl<Button>("RemoveExcludeButton");
        if (removeExcludeBtn != null)
        {
            removeExcludeBtn.Content = loc.RemoveExcludeButton;
            removeExcludeBtn.Click += async (_, _) =>
            {
                if (pathsListBox?.SelectedItem is not PathItemViewModel selected) return;
                if (excludesListBox?.SelectedItem is not string selectedExclude) return;

                selected.CustomExcludes.Remove(selectedExclude);
                _currentExcludes.Remove(selectedExclude);

                var daemon = await DaemonClient.TryGetServiceAsync();
                if (daemon != null)
                {
                    try { await daemon.RemoveExcludeAsync(selected.Path, selectedExclude); } catch { }
                }
                else
                {
                    ConfigManager.Save(_config);
                }
            };
        }

        var closeBtn = this.FindControl<Button>("CloseButton");
        if (closeBtn != null)
        {
            closeBtn.Content = loc.CloseButton;
            closeBtn.Click += async (_, _) => await SaveAndCloseAsync();
        }

        KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                await SaveAndCloseAsync();
            }
        };

        _statusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(750)
        };
        _statusTimer.Tick += async (_, _) => await RefreshPathStatusesAsync();

        Opened += async (_, _) =>
        {
            await RefreshPathStatusesAsync();
            _statusTimer.Start();
        };

        Closed += (_, _) =>
        {
            _statusTimer.Stop();
        };
    }

    private void InitializeAppearanceTab(Loc loc)
    {
        var header = this.FindControl<TextBlock>("SectionAppearanceHeader");
        if (header != null) header.Text = loc.SettingsSectionAppearanceHeader;

        var desc = this.FindControl<TextBlock>("AppearanceDescriptionLabel");
        if (desc != null) desc.Text = loc.AppearanceDescription;

        var autoTitle = this.FindControl<TextBlock>("ThemeAutoTitleLabel");
        if (autoTitle != null) autoTitle.Text = loc.ThemeAutoTitle;
        var autoSub = this.FindControl<TextBlock>("ThemeAutoSubtitleLabel");
        if (autoSub != null) autoSub.Text = loc.ThemeAutoSubtitle;

        var darkTitle = this.FindControl<TextBlock>("ThemeDarkTitleLabel");
        if (darkTitle != null) darkTitle.Text = loc.ThemeDarkTitle;
        var darkSub = this.FindControl<TextBlock>("ThemeDarkSubtitleLabel");
        if (darkSub != null) darkSub.Text = loc.ThemeDarkSubtitle;

        var lightTitle = this.FindControl<TextBlock>("ThemeLightTitleLabel");
        if (lightTitle != null) lightTitle.Text = loc.ThemeLightTitle;
        var lightSub = this.FindControl<TextBlock>("ThemeLightSubtitleLabel");
        if (lightSub != null) lightSub.Text = loc.ThemeLightSubtitle;

        var detectedPalette = SystemThemeDetector.DetectSystemPalette();
        var detectedLabel = this.FindControl<TextBlock>("DetectedSystemThemeLabel");
        if (detectedLabel != null)
        {
            detectedLabel.Text = loc.FormatDetectedSystemTheme(detectedPalette.SourceName, detectedPalette.IsDark);
        }

        var swatchWindow = this.FindControl<Border>("SwatchWindow");
        if (swatchWindow != null) swatchWindow.Background = new SolidColorBrush(detectedPalette.WindowBackground);
        var swatchView = this.FindControl<Border>("SwatchView");
        if (swatchView != null) swatchView.Background = new SolidColorBrush(detectedPalette.ViewBackground);
        var swatchAccent = this.FindControl<Border>("SwatchAccent");
        if (swatchAccent != null) swatchAccent.Background = new SolidColorBrush(detectedPalette.Accent);

        var autoRadio = this.FindControl<RadioButton>("ThemeAutoRadio");
        var darkRadio = this.FindControl<RadioButton>("ThemeDarkRadio");
        var lightRadio = this.FindControl<RadioButton>("ThemeLightRadio");

        string mode = (_config.Appearance?.ThemeMode ?? "auto").Trim().ToLowerInvariant();
        if (mode == "dark" && darkRadio != null)
            darkRadio.IsChecked = true;
        else if (mode == "light" && lightRadio != null)
            lightRadio.IsChecked = true;
        else if (autoRadio != null)
            autoRadio.IsChecked = true;

        if (autoRadio != null)
        {
            autoRadio.IsCheckedChanged += (_, _) =>
            {
                if (autoRadio.IsChecked == true) SetThemeMode("auto");
            };
        }

        if (darkRadio != null)
        {
            darkRadio.IsCheckedChanged += (_, _) =>
            {
                if (darkRadio.IsChecked == true) SetThemeMode("dark");
            };
        }

        if (lightRadio != null)
        {
            lightRadio.IsCheckedChanged += (_, _) =>
            {
                if (lightRadio.IsChecked == true) SetThemeMode("light");
            };
        }
    }

    private void SetThemeMode(string mode)
    {
        _config.Appearance.ThemeMode = mode;
        ConfigManager.Save(_config);
        App.ApplyTheme(mode);
    }

    private async Task SaveAndCloseAsync()
    {
        if (_isClosing) return;
        _isClosing = true;

        _statusTimer?.Stop();
        var daemon = await DaemonClient.TryGetServiceAsync();
        if (daemon != null)
        {
            try
            {
                foreach (var p in _paths)
                {
                    await daemon.SetPathEnabledAsync(p.Path, p.Enabled);
                }
            }
            catch { }
        }
        else
        {
            ConfigManager.Save(_config);
        }
        Close();
    }

    private async Task RefreshPathStatusesAsync()
    {
        if (_isPollingStatus) return;
        _isPollingStatus = true;

        try
        {
            var daemon = await DaemonClient.TryGetServiceAsync();
            if (daemon == null) return;

            var dtos = await daemon.ListPathsAsync();

            var map = dtos.ToDictionary(
                d => Path.GetFullPath(d.Path).TrimEnd(Path.DirectorySeparatorChar),
                d => d,
                StringComparer.Ordinal);

            foreach (var item in _paths)
            {
                string norm = Path.GetFullPath(item.Path).TrimEnd(Path.DirectorySeparatorChar);
                if (map.TryGetValue(norm, out var dto))
                {
                    item.UpdateLiveStatus(dto.Status, dto.CurrentCrawlFiles, dto.CurrentCrawlDirectory);
                }
            }
        }
        catch { }
        finally
        {
            _isPollingStatus = false;
        }
    }

    private async Task TriggerRescanInBackgroundAsync(string path)
    {
        try
        {
            var daemon = await DaemonClient.TryGetServiceAsync();
            if (daemon != null)
            {
                await daemon.TriggerRescanAsync(path);
                await RefreshPathStatusesAsync();
            }
        }
        catch { }
    }

    private async Task<bool> AddExcludeAsync(PathItemViewModel selected, string pattern)
    {
        var errorLabel = this.FindControl<TextBlock>("ExcludeErrorLabel");

        if (!PathExclusionValidator.IsValid(selected.Path, pattern, out string? error))
        {
            if (errorLabel != null)
            {
                errorLabel.Text = error ?? Loc.Instance.InvalidExclusionError;
                errorLabel.IsVisible = true;
            }
            return false;
        }

        if (errorLabel != null)
        {
            errorLabel.IsVisible = false;
        }

        if (!selected.CustomExcludes.Contains(pattern, StringComparer.OrdinalIgnoreCase))
        {
            selected.CustomExcludes.Add(pattern);
            _currentExcludes.Add(pattern);

            var daemon = await DaemonClient.TryGetServiceAsync();
            if (daemon != null)
            {
                try { await daemon.AddExcludeAsync(selected.Path, pattern); } catch { }
            }
            else
            {
                ConfigManager.Save(_config);
            }
        }

        return true;
    }

    private async Task PickSubfolderToExcludeAsync(PathItemViewModel selected)
    {
        var errorLabel = this.FindControl<TextBlock>("ExcludeErrorLabel");
        if (errorLabel != null) errorLabel.IsVisible = false;

        IStorageFolder? startFolder = null;
        try
        {
            startFolder = await StorageProvider.TryGetFolderFromPathAsync(selected.Path);
        }
        catch { }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Loc.Instance.FormatPickSubfolderTitle(selected.Path),
            SuggestedStartLocation = startFolder,
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            await AddExcludeAsync(selected, folders[0].Path.LocalPath);
        }
    }

    private async Task AddFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Loc.Instance.PickMonitoredFolderTitle,
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            string localPath = Path.GetFullPath(folders[0].Path.LocalPath).TrimEnd(Path.DirectorySeparatorChar);
            if (string.IsNullOrEmpty(localPath)) localPath = "/";

            if (!_paths.Any(p => Path.GetFullPath(p.Path).TrimEnd(Path.DirectorySeparatorChar).Equals(localPath, StringComparison.Ordinal)))
            {
                string detectedMode = DetectRecommendedMode(localPath);
                var entry = new PathConfigEntry
                {
                    Path = localPath,
                    Type = detectedMode,
                    Enabled = true
                };
                var vm = new PathItemViewModel(entry);
                vm.UpdateLiveStatus("Scanning", 0, localPath);
                _paths.Add(vm);
                _config.Paths.Add(entry);

                // Notify daemon in background so UI stays responsive and shows live crawl status
                _ = Task.Run(async () =>
                {
                    var daemon = await DaemonClient.TryGetServiceAsync();
                    if (daemon != null)
                    {
                        try { await daemon.AddPathAsync(localPath, detectedMode); } catch { }
                    }
                    else
                    {
                        ConfigManager.Save(_config);
                    }
                });
            }
        }
    }

    private static string DetectRecommendedMode(string path)
    {
        try
        {
            var mounts = Core.System.MountScanner.ScanMounts();
            foreach (var m in mounts)
            {
                if (m.SuggestedMonitoringMode == "network")
                {
                    string mp = m.MountPoint.TrimEnd('/');
                    if (path.Equals(mp, StringComparison.Ordinal) ||
                        path.StartsWith(mp + "/", StringComparison.Ordinal) ||
                        mp.StartsWith(path + "/", StringComparison.Ordinal))
                    {
                        return "network";
                    }
                }
            }
        }
        catch { }

        return "local";
    }

    private async Task RemoveSelectedFolderAsync()
    {
        var listBox = this.FindControl<ListBox>("PathsListBox");
        if (listBox?.SelectedItem is PathItemViewModel selected)
        {
            _paths.Remove(selected);
            _config.Paths.Remove(selected.Entry);

            var daemon = await DaemonClient.TryGetServiceAsync();
            if (daemon != null)
            {
                try { await daemon.RemovePathAsync(selected.Path); } catch { }
            }
            else
            {
                ConfigManager.Save(_config);
            }
        }
    }
}
