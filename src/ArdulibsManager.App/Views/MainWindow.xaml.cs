using System.Windows;
using System.Windows.Controls;
using ArdulibsManager.Models;
using ArdulibsManager.ViewModels;

namespace ArdulibsManager.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.InitializeCommand.Execute(null);
    }

    private void VersionComboBox_DropDownOpened(object sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is ComboBox comboBox && comboBox.DataContext is SearchResult result)
            vm.LoadVersionsCommand.Execute(result);
    }
}
