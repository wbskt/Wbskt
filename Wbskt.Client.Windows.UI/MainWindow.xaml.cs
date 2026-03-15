using System.Windows;
using Wbskt.Client.Windows.Models;

namespace Wbskt.Client.Windows.UI;

public partial class MainWindow : Window
{
    private readonly ConfigurationStore _store = new();
    private List<CommandMapping> _mappings = new();

    public MainWindow()
    {
        InitializeComponent();
        LoadData();
    }

    private void LoadData()
    {
        _mappings = _store.LoadMappings();
        MappingsGrid.ItemsSource = _mappings;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _store.SaveMappings(_mappings);
        MessageBox.Show("Mappings saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
