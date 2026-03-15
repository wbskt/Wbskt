using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wbskt.Client.Windows.Models;

namespace Wbskt.Client.Windows.UI;

public partial class MainWindow : Window
{
    private readonly ConfigurationStore _store = new();
    private ObservableCollection<CommandMapping> _mappings = new();
    private CommandMapping? _selectedMapping;

    // Cached styles — FindResource is a dictionary lookup; no need to repeat
    // it on every dynamic parameter rebuild.
    private Style? _textBoxStyle;
    private Style? _comboBoxStyle;

    public MainWindow()
    {
        InitializeComponent();
        ComboActionType.ItemsSource = Enum.GetValues(typeof(ActionType));
        LoadData();
    }

    private void LoadData()
    {
        _mappings = new ObservableCollection<CommandMapping>(_store.LoadMappings());
        MappingsGrid.ItemsSource = _mappings;
    }

    private void OnAddNew(object sender, RoutedEventArgs e)
    {
        var newMapping = new CommandMapping(Guid.NewGuid())
        {
            CommandName = "new.command",
            ActionType  = ActionType.Toast
        };
        _mappings.Add(newMapping);
        MappingsGrid.SelectedItem = newMapping;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _store.SaveMappings(_mappings.ToList());
        MessageBox.Show("All mappings saved successfully.", "Saved",
                        MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ── Selection ────────────────────────────────────────────────────────────

    private void MappingsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedMapping = MappingsGrid.SelectedItem as CommandMapping;

        if (_selectedMapping is null)
        {
            NoSelectionText.Visibility = Visibility.Visible;
            EditorStack.Visibility     = Visibility.Collapsed;
            return;
        }

        NoSelectionText.Visibility = Visibility.Collapsed;
        EditorStack.Visibility     = Visibility.Visible;

        // Suppress change handlers while we populate the fields.
        using (SuspendChangeHandlers())
        {
            TxtCommandName.Text          = _selectedMapping.CommandName;
            TxtFriendlyName.Text         = _selectedMapping.FriendlyName;
            ComboActionType.SelectedItem = _selectedMapping.ActionType;
        }

        RefreshDynamicParameters();
    }

    // ── Property panel handlers ───────────────────────────────────────────────

    private void ComboActionType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selectedMapping is null || _changeSuspended) return;

        _selectedMapping.ActionType = (ActionType)ComboActionType.SelectedItem;
        RefreshDynamicParameters();

        // ActionType is displayed in the grid column — notify it changed.
        _selectedMapping.NotifyActionTypeChanged();
    }

    private void Property_Changed(object sender, TextChangedEventArgs e)
    {
        if (_selectedMapping is null || _changeSuspended) return;

        // Name-based switch is refactor-safe; reference equality (sender == TxtX)
        // breaks silently if controls are ever recreated or renamed in XAML.
        if (sender is TextBox tb)
        {
            switch (tb.Name)
            {
                case nameof(TxtCommandName):
                    _selectedMapping.CommandName = tb.Text;
                    break;
                case nameof(TxtFriendlyName):
                    _selectedMapping.FriendlyName = tb.Text;
                    break;
            }
        }

        // CommandName is displayed in the grid — notify it changed.
        _selectedMapping.NotifyCommandNameChanged();
    }

    // ── Dynamic parameter builder ─────────────────────────────────────────────

    private void RefreshDynamicParameters()
    {
        if (_selectedMapping is null) return;

        DynamicParametersStack.Children.Clear();

        var definition = ActionRegistry.Definitions
            .FirstOrDefault(d => d.Type == _selectedMapping.ActionType);

        if (definition is null) return;

        // Resolve styles once per rebuild, not once per parameter.
        _textBoxStyle  ??= (Style)FindResource("PropertyTextBox");
        _comboBoxStyle ??= (Style)FindResource("PropertyComboBox");

        var mutedBrush = (SolidColorBrush)FindResource("TextMuted");

        foreach (var param in definition.ParameterSchema)
        {
            // Ensure the key exists so bindings below never throw.
            _selectedMapping.Parameters.TryAdd(param.Key, param.DefaultValue ?? "");

            DynamicParametersStack.Children.Add(new Label
            {
                Content    = param.Label,
                Foreground = mutedBrush,
                Padding    = new Thickness(0, 0, 0, 5)
            });

            DynamicParametersStack.Children.Add(
                param.InputType == ParameterInputType.Dropdown && param.Options is not null
                    ? BuildDropdown(param)
                    : BuildTextBox(param));
        }
    }

    private ComboBox BuildDropdown(ParameterDefinition param)
    {
        var combo = new ComboBox
        {
            Style        = _comboBoxStyle,
            ItemsSource  = param.Options,
            SelectedItem = _selectedMapping!.Parameters[param.Key]
        };
        combo.SelectionChanged += (_, _) =>
            _selectedMapping!.Parameters[param.Key] = combo.SelectedItem?.ToString() ?? "";
        return combo;
    }

    private TextBox BuildTextBox(ParameterDefinition param)
    {
        var textBox = new TextBox
        {
            Style = _textBoxStyle,
            Text  = _selectedMapping!.Parameters[param.Key],
            Tag   = param.Key
        };
        textBox.TextChanged += (_, _) =>
            _selectedMapping!.Parameters[param.Key] = textBox.Text;
        return textBox;
    }

    // ── Change-suspension scope guard ─────────────────────────────────────────
    // Using a disposable means the flag is always cleared even if an exception
    // is thrown mid-update — a plain bool flag would leave the UI frozen.

    private bool _changeSuspended;

    private IDisposable SuspendChangeHandlers()
    {
        _changeSuspended = true;
        return new ActionDisposable(() => _changeSuspended = false);
    }

    private sealed class ActionDisposable(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}