using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Puppeteer.App.ViewModels;
using Puppeteer.Core;

namespace Puppeteer.App.Controls;

public partial class ProjectRow : UserControl
{
    public ProjectRow() => InitializeComponent();

    private void Kebab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    private void Row_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is Project project
            && Window.GetWindow(this)?.DataContext is MainViewModel vm
            && vm.OpenTerminalCommand.CanExecute(project))
            vm.OpenTerminalCommand.Execute(project);
    }
}
