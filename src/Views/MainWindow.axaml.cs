using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Sextant.Services;
using Sextant.ViewModels;
using System.ComponentModel;

namespace Sextant.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnTunnelKey, RoutingStrategies.Tunnel);
        Activated += (_, _) => (DataContext as MainViewModel)?.OnWindowActivated();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is not MainViewModel vm)
            return;
        if (vm.PinsWidth >= 160)
            PinColumn.Width = new GridLength(vm.PinsWidth);
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

    private ColumnDefinition PinColumn => Root.ColumnDefinitions[0];

    private void ReadWidths(MainViewModel vm)
    {
        if (PinColumn.Width.GridUnitType == GridUnitType.Pixel && PinColumn.Width.Value >= 160)
            vm.PinsWidth = PinColumn.Width.Value;
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
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (ctrl && e.Key == Key.P)
        {
            vm.TogglePalette();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && vm.PaletteOpen)
        {
            vm.ClosePalette();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.W)
        {
            if (vm.PaletteOpen)
                vm.ClosePalette();
            else
                vm.CloseActive();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.Tab)
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

    private void OnMenuOpening(object? sender, CancelEventArgs e)
    {
        if (sender is ContextMenu menu && menu.PlacementTarget is Control target)
            menu.DataContext = target.DataContext;
    }
}
