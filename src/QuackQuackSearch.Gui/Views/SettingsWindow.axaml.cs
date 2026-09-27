using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using QuackQuackSearch.Core.Config;
using QuackQuackSearch.Core.DBus;
using Tmds.DBus;

namespace QuackQuackSearch.Gui.Views;

public partial class SettingsWindow : Window
{
    private readonly QuackConfig _config;
    private readonly ObservableCollection<PathConfigEntry> _paths = [];

    public SettingsWindow()
    {
        InitializeComponent();

        _config = ConfigManager.LoadOrCreateDefault();
        foreach (var p in _config.Paths)
        {
            _paths.Add(p);
        }

        var listBox = this.FindControl<ListBox>("PathsListBox");
        if (listBox != null)
        {
            listBox.ItemsSource = _paths;
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
