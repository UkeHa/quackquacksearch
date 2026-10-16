using System.ComponentModel;
using System.Runtime.CompilerServices;
using QuackQuackSearch.Core.Config;
using QuackQuackSearch.Gui.Localization;

namespace QuackQuackSearch.Gui.Models;

public sealed class PathItemViewModel : INotifyPropertyChanged
{
    public PathConfigEntry Entry { get; }

    private string _status = "Active";
    private long _currentCrawlFiles;
    private string _currentCrawlDirectory = string.Empty;

    public PathItemViewModel(PathConfigEntry entry)
    {
        Entry = entry;
        _status = entry.Enabled ? "Active" : "Disabled";
    }

    public string Path => Entry.Path;

    public string Type => Entry.Type;

    public List<string> CustomExcludes
    {
        get => Entry.CustomExcludes ??= [];
        set => Entry.CustomExcludes = value;
    }

    public bool Enabled
    {
        get => Entry.Enabled;
        set
        {
            if (Entry.Enabled != value)
            {
                Entry.Enabled = value;
                _status = value ? "Active" : "Disabled";
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusBadgeText));
                OnPropertyChanged(nameof(IsScanning));
            }
        }
    }

    public string Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusBadgeText));
                OnPropertyChanged(nameof(IsScanning));
            }
        }
    }

    public long CurrentCrawlFiles
    {
        get => _currentCrawlFiles;
        set
        {
            if (_currentCrawlFiles != value)
            {
                _currentCrawlFiles = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusBadgeText));
            }
        }
    }

    public string CurrentCrawlDirectory
    {
        get => _currentCrawlDirectory;
        set
        {
            if (_currentCrawlDirectory != value)
            {
                _currentCrawlDirectory = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsScanning => Enabled && string.Equals(_status, "Scanning", StringComparison.OrdinalIgnoreCase);

    public string StatusBadgeText => Loc.Instance.FormatPathTypeAndStatus(Entry.Type, Entry.Enabled, _status, _currentCrawlFiles);

    public string EnabledLabel => Loc.Instance.PathEnabledCheckbox;

    public void UpdateLiveStatus(string status, long currentCrawlFiles, string currentCrawlDirectory)
    {
        bool changed = false;
        if (_status != status)
        {
            _status = status;
            changed = true;
        }
        if (_currentCrawlFiles != currentCrawlFiles)
        {
            _currentCrawlFiles = currentCrawlFiles;
            changed = true;
        }
        if (_currentCrawlDirectory != currentCrawlDirectory)
        {
            _currentCrawlDirectory = currentCrawlDirectory;
            OnPropertyChanged(nameof(CurrentCrawlDirectory));
        }
        if (changed)
        {
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(CurrentCrawlFiles));
            OnPropertyChanged(nameof(StatusBadgeText));
            OnPropertyChanged(nameof(IsScanning));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
