using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Puppeteer.App.ViewModels;
using Puppeteer.Core;

namespace Puppeteer.App.Controls;

public partial class ProjectCard : UserControl
{
    public ProjectCard() => InitializeComponent();

    private void Kebab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    // Double-clicking a card is the fast path to a terminal in that project.
    private void Card_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is Project project
            && Window.GetWindow(this)?.DataContext is MainViewModel vm
            && vm.OpenTerminalCommand.CanExecute(project))
            vm.OpenTerminalCommand.Execute(project);
    }
}
