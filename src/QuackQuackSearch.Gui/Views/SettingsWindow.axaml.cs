using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using QuackQuackSearch.Core.Config;
using QuackQuackSearch.Core.Crawler;
using QuackQuackSearch.Core.DBus;
using Tmds.DBus;

namespace QuackQuackSearch.Gui.Views;

public partial class SettingsWindow : Window
{
    private readonly QuackConfig _config;
    private readonly ObservableCollection<PathConfigEntry> _paths = [];
    private readonly ObservableCollection<string> _currentExcludes = [];

    public SettingsWindow()
    {
        InitializeComponent();

        _config = ConfigManager.LoadOrCreateDefault();
        foreach (var p in _config.Paths)
        {
            _paths.Add(p);
        }

        var pathsListBox = this.FindControl<ListBox>("PathsListBox");
        var excludesListBox = this.FindControl<ListBox>("ExcludesListBox");
        var selectedPathLabel = this.FindControl<TextBlock>("SelectedPathLabel");
        var newExcludeTextBox = this.FindControl<TextBox>("NewExcludeTextBox");
        var errorLabel = this.FindControl<TextBlock>("ExcludeErrorLabel");

        if (pathsListBox != null)
        {
            pathsListBox.ItemsSource = _paths;
            pathsListBox.SelectionChanged += (_, _) =>
            {
                if (errorLabel != null) errorLabel.IsVisible = false;

                var selected = pathsListBox.SelectedItem as PathConfigEntry;
                _currentExcludes.Clear();

                if (selected != null)
                {
                    if (selectedPathLabel != null) selectedPathLabel.Text = selected.Path;
                    selected.CustomExcludes ??= [];
                    foreach (var exc in selected.CustomExcludes)
                    {
                        _currentExcludes.Add(exc);
                    }
                }
                else
                {
                    if (selectedPathLabel != null) selectedPathLabel.Text = "Wähle links einen Pfad aus";
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
            addBtn.Click += async (_, _) => await AddFolderAsync();
        }

        var removeBtn = this.FindControl<Button>("RemoveFolderButton");
        if (removeBtn != null)
        {
            removeBtn.Click += async (_, _) => await RemoveSelectedFolderAsync();
        }

        var excludeFolderBtn = this.FindControl<Button>("ExcludeFolderPickerButton");
        if (excludeFolderBtn != null)
        {
            excludeFolderBtn.Click += async (_, _) =>
            {
                if (pathsListBox?.SelectedItem is not PathConfigEntry selected) return;
                await PickSubfolderToExcludeAsync(selected);
            };
        }

        var addExcludeTextBtn = this.FindControl<Button>("AddExcludeTextButton");
        if (addExcludeTextBtn != null)
        {
            addExcludeTextBtn.Click += async (_, _) =>
            {
                if (pathsListBox?.SelectedItem is not PathConfigEntry selected) return;
                string pattern = newExcludeTextBox?.Text?.Trim() ?? string.Empty;

                if (string.IsNullOrEmpty(pattern))
                {
                    // Wenn der Text leer ist, öffne die "Unterordner wählen..."-Maske
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
            removeExcludeBtn.Click += async (_, _) =>
            {
                if (pathsListBox?.SelectedItem is not PathConfigEntry selected) return;
                if (excludesListBox?.SelectedItem is not string selectedExclude) return;

                selected.CustomExcludes ??= [];
                selected.CustomExcludes.Remove(selectedExclude);
                _currentExcludes.Remove(selectedExclude);
                ConfigManager.Save(_config);

                try
                {
                    var conn = Connection.Session;
                    await conn.ConnectAsync();
                    if (await conn.IsServiceActiveAsync("org.quackquacksearch.Daemon"))
                    {
                        var daemon = conn.CreateProxy<IDaemonService>("org.quackquacksearch.Daemon", "/org/quackquacksearch/Daemon");
                        await daemon.RemoveExcludeAsync(selected.Path, selectedExclude);
                    }
                }
                catch { }
            };
        }

        var closeBtn = this.FindControl<Button>("CloseButton");
        if (closeBtn != null)
        {
            closeBtn.Click += async (_, _) =>
            {
                ConfigManager.Save(_config);
                try
                {
                    var conn = Connection.Session;
                    await conn.ConnectAsync();
                    if (await conn.IsServiceActiveAsync("org.quackquacksearch.Daemon"))
                    {
                        var daemon = conn.CreateProxy<IDaemonService>("org.quackquacksearch.Daemon", "/org/quackquacksearch/Daemon");
                        foreach (var p in _paths)
                        {
                            await daemon.SetPathEnabledAsync(p.Path, p.Enabled);
                        }
                    }
                }
                catch { }
                Close();
            };
        }
    }

    private async Task<bool> AddExcludeAsync(PathConfigEntry selected, string pattern)
    {
        var errorLabel = this.FindControl<TextBlock>("ExcludeErrorLabel");

        if (!PathExclusionValidator.IsValid(selected.Path, pattern, out string? error))
        {
            if (errorLabel != null)
            {
                errorLabel.Text = error ?? "Ungültiger Ausschluss.";
                errorLabel.IsVisible = true;
            }
            return false;
        }

        if (errorLabel != null)
        {
            errorLabel.IsVisible = false;
        }

        selected.CustomExcludes ??= [];
        if (!selected.CustomExcludes.Contains(pattern, StringComparer.OrdinalIgnoreCase))
        {
            selected.CustomExcludes.Add(pattern);
            _currentExcludes.Add(pattern);
            ConfigManager.Save(_config);

            try
            {
                var conn = Connection.Session;
                await conn.ConnectAsync();
                if (await conn.IsServiceActiveAsync("org.quackquacksearch.Daemon"))
                {
                    var daemon = conn.CreateProxy<IDaemonService>("org.quackquacksearch.Daemon", "/org/quackquacksearch/Daemon");
                    await daemon.AddExcludeAsync(selected.Path, pattern);
                }
            }
            catch { }
        }

        return true;
    }

    private async Task PickSubfolderToExcludeAsync(PathConfigEntry selected)
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
            Title = $"Unterordner zum Ausschließen aus '{selected.Path}' wählen",
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
            Title = "Überwachten Ordner auswählen",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            string localPath = folders[0].Path.LocalPath;
            if (!_paths.Any(p => p.Path.Equals(localPath, StringComparison.OrdinalIgnoreCase)))
            {
                var entry = new PathConfigEntry
                {
                    Path = localPath,
                    Type = "local",
                    Enabled = true
                };
                _paths.Add(entry);
                _config.Paths.Add(entry);
                ConfigManager.Save(_config);

                // Notify daemon if running
                try
                {
                    var conn = Connection.Session;
                    await conn.ConnectAsync();
                    if (await conn.IsServiceActiveAsync("org.quackquacksearch.Daemon"))
                    {
                        var daemon = conn.CreateProxy<IDaemonService>("org.quackquacksearch.Daemon", "/org/quackquacksearch/Daemon");
                        await daemon.AddPathAsync(localPath, "local");
                    }
                }
                catch { }
            }
        }
    }

    private async Task RemoveSelectedFolderAsync()
    {
        var listBox = this.FindControl<ListBox>("PathsListBox");
        if (listBox?.SelectedItem is PathConfigEntry selected)
        {
            _paths.Remove(selected);
            _config.Paths.Remove(selected);
            ConfigManager.Save(_config);

            try
            {
                var conn = Connection.Session;
                await conn.ConnectAsync();
                if (await conn.IsServiceActiveAsync("org.quackquacksearch.Daemon"))
                {
                    var daemon = conn.CreateProxy<IDaemonService>("org.quackquacksearch.Daemon", "/org/quackquacksearch/Daemon");
                    await daemon.RemovePathAsync(selected.Path);
                }
            }
            catch { }
        }
    }
}
