using System.Windows;
using System.Windows.Controls;

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
}
