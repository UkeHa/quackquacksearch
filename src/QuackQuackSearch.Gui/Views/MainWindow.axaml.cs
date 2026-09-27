using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using QuackQuackSearch.Gui.ViewModels;

namespace QuackQuackSearch.Gui.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainWindowViewModel();
        DataContext = vm;

        var settingsBtn = this.FindControl<Button>("SettingsButton");
        if (settingsBtn != null)
        {
            settingsBtn.Click += async (_, _) =>
            {
                var settingsWin = new SettingsWindow();
                await settingsWin.ShowDialog(this);
                // Refresh after settings closed
                await vm.InitializeAsync();
            };
        }

        Opened += (_, _) =>
        {
            var searchBox = this.FindControl<TextBox>("SearchInputBox");
            searchBox?.Focus();

            var panel = this.FindControl<StackPanel>("FilterPanel");
            if (panel?.Children.FirstOrDefault() is Button firstBtn)
            {
                UpdateFilterButtonsHighlight(firstBtn);
            }
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                vm.ClearSearch();
                e.Handled = true;
            }
        };
    }

    private void FilterButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            ViewModel.ActiveFilter = tag;
            UpdateFilterButtonsHighlight(btn);
        }
    }

    private void UpdateFilterButtonsHighlight(Button activeBtn)
    {
        var panel = this.FindControl<StackPanel>("FilterPanel");
        if (panel == null) return;

        foreach (var child in panel.Children)
        {
            if (child is Button b)
            {
                if (b == activeBtn)
                {
                    b.FontWeight = Avalonia.Media.FontWeight.Bold;
                    b.Opacity = 1.0;
                }
                else
                {
                    b.FontWeight = Avalonia.Media.FontWeight.Normal;
                    b.Opacity = 0.75;
                }
            }
        }
    }

    private void DataGrid_DoubleTapped(object? sender, TappedEventArgs e)
    {
        ViewModel.OpenFile(ViewModel.SelectedItem);
    }

    private void DataGrid_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ViewModel.OpenFile(ViewModel.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.O && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ViewModel.OpenContainingFolder(ViewModel.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            ViewModel.CopyPath(ViewModel.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ViewModel.CopyName(ViewModel.SelectedItem);
            e.Handled = true;
        }
    }

    private void MenuOpen_Click(object? sender, RoutedEventArgs e) => ViewModel.OpenFile(ViewModel.SelectedItem);
    private void MenuOpenFolder_Click(object? sender, RoutedEventArgs e) => ViewModel.OpenContainingFolder(ViewModel.SelectedItem);
    private void MenuCopyPath_Click(object? sender, RoutedEventArgs e) => ViewModel.CopyPath(ViewModel.SelectedItem);
    private void MenuCopyName_Click(object? sender, RoutedEventArgs e) => ViewModel.CopyName(ViewModel.SelectedItem);
}
