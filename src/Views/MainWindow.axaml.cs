using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Sextant;
using Sextant.Services;
using Sextant.ViewModels;
using System.ComponentModel;

namespace Sextant.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        OpenRepositoryItem.InputGesture = AppGestures.CommandKey(Key.O);
        BranchMenuItem.InputGesture = AppGestures.CommandKey(Key.B);
        StashMenuItem.InputGesture = AppGestures.CommandKey(Key.S, KeyModifiers.Shift);
        SearchMenuItem.InputGesture = AppGestures.CommandKey(Key.F);
        AddHandler(KeyDownEvent, OnTunnelKey, RoutingStrategies.Tunnel);
        Activated += (_, _) => (DataContext as MainViewModel)?.OnWindowActivated();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is not MainViewModel vm)
            return;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        vm.Attach(new AvaloniaDialogService(this));
        _ = vm.InitializeAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            ReadWidths(vm);
            vm.Shutdown();
        }

        base.OnClosed(e);
    }

    private void ReadWidths(MainViewModel vm)
    {
        foreach (var view in this.GetVisualDescendants().OfType<RepositoryView>())
            view.ReadWidths(vm);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.PaletteOpen) && sender is MainViewModel { PaletteOpen: true })
            PaletteBox.Focus();
    }

    private void OnTunnelKey(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;
        if (AppGestures.Matches(e, Key.P))
        {
            vm.TogglePalette();
            e.Handled = true;
            return;
        }

        if (AppGestures.Matches(e, Key.O))
        {
            if (vm.CanUseGit)
                vm.OpenFolderCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (AppGestures.Matches(e, Key.F) && vm.ActiveTab is { } searchTab)
        {
            searchTab.ToggleHistorySearchCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (AppGestures.Matches(e, Key.B) && vm.ActiveTab is { } branchTab)
        {
            if (branchTab.CanRunCommands)
                branchTab.CreateBranchCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (AppGestures.Matches(e, Key.S, KeyModifiers.Shift) && vm.ActiveTab is { } stashTab)
        {
            if (stashTab.CanRunCommands)
                stashTab.StashCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && vm.PaletteOpen)
        {
            vm.ClosePalette();
            e.Handled = true;
            return;
        }

        if (AppGestures.Matches(e, Key.W))
        {
            if (vm.PaletteOpen)
                vm.ClosePalette();
            else
                vm.CloseActive();
            e.Handled = true;
            return;
        }

        // Command+Tab is the macOS application switcher, so next tab is Control+Tab everywhere.
        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.Control)
        {
            vm.NextTab();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F5)
        {
            vm.RefreshActive();
            e.Handled = true;
        }
    }

    private void OnPaletteKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;
        if (e.Key == Key.Enter)
        {
            _ = vm.RunPaletteAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            vm.MovePalette(1);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            vm.MovePalette(-1);
            e.Handled = true;
        }
    }

    private void OnPaletteChosen(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            _ = vm.RunPaletteAsync();
    }

    private void OnPaletteBackdrop(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source == sender && DataContext is MainViewModel vm)
            vm.ClosePalette();
    }

    private void OnPaletteCardPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;
}
