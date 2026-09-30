using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sextant.Git;
using System.Windows.Input;

namespace Sextant.ViewModels;

public partial class PinViewModel : ObservableObject
{
    public PinViewModel(string path, string name, Func<PinViewModel, Task> open, Action<PinViewModel> unpin)
    {
        Path = path;
        Name = string.IsNullOrWhiteSpace(name) ? System.IO.Path.GetFileName(path) : name;
        if (string.IsNullOrWhiteSpace(Name))
            Name = path;
        OpenCommand = new AsyncRelayCommand(() => open(this));
        UnpinCommand = new RelayCommand(() => unpin(this));
    }

    [ObservableProperty]
    public partial string Path { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Badge { get; set; } = "";

    [ObservableProperty]
    public partial bool HasError { get; set; }

    public ICommand OpenCommand { get; }

    public ICommand UnpinCommand { get; }

    public void ApplyBadge(RepoBadge badge)
    {
        if (!badge.Available)
        {
            Badge = string.IsNullOrWhiteSpace(badge.Error) ? "Unavailable" : badge.Error;
            HasError = true;
            return;
        }

        HasError = false;
        var text = badge.Detached ? "detached" : badge.Branch ?? "HEAD";
        if (badge.Conflicted)
            text += " · conflict";
        else if (badge.Dirty)
            text += " · dirty";
        if (badge.Ahead != 0 || badge.Behind != 0)
            text += $" · ↑{badge.Ahead} ↓{badge.Behind}";
        Badge = text;
    }
}
