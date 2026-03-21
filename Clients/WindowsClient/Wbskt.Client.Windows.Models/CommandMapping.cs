using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Wbskt.Client.Windows.Models;

public class CommandMapping(Guid id) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public Guid Id { get; } = id;

    private string _commandName = string.Empty;
    public string CommandName
    {
        get => _commandName;
        set => Set(ref _commandName, value);
    }

    private string _friendlyName = string.Empty;
    public string FriendlyName
    {
        get => _friendlyName;
        set => Set(ref _friendlyName, value);
    }

    private ActionType _actionType = ActionType.Toast;
    public ActionType ActionType
    {
        get => _actionType;
        set => Set(ref _actionType, value);
    }

    private bool _isEnabled = true;
    public bool IsEnabled
    {
        get => _isEnabled;
        set => Set(ref _isEnabled, value);
    }

    public Dictionary<string, string> Parameters { get; init; } = new();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    // Called from MainWindow if you prefer explicit notification over auto-property
    public void NotifyCommandNameChanged() =>
        PropertyChanged?.Invoke(this, new(nameof(CommandName)));
    public void NotifyActionTypeChanged() =>
        PropertyChanged?.Invoke(this, new(nameof(ActionType)));
}