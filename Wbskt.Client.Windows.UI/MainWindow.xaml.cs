using System.Collections.ObjectModel;
using System.Windows;
using Wbskt.Client.Windows.Models;

namespace Wbskt.Client.Windows.UI;

public partial class MainWindow : Window
{
    private readonly ConfigurationStore _store = new();
    private ObservableCollection<CommandMapping> _mappings = new();

    public MainWindow()
    {
        InitializeComponent();
        LoadData();
    }

    private void LoadData()
    {
        var saved = _store.LoadMappings();
        _mappings = new ObservableCollection<CommandMapping>(saved);
        MappingsGrid.ItemsSource = _mappings;
    }

    private void OnAddNew(object sender, RoutedEventArgs e)
    {
        var newMapping = new CommandMapping(Guid.NewGuid())
        {
            CommandName = "new.command",
            ActionType = ActionType.Toast
        };
        _mappings.Add(newMapping);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _store.SaveMappings(_mappings.ToList());
        MessageBox.Show("Mappings saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
