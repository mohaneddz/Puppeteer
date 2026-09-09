using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Puppeteer.App.ViewModels;

namespace Puppeteer.App.Controls;

public partial class DocCard : UserControl
{
    public DocCard() => InitializeComponent();

    // Clicking anywhere on the card inspects the project, the same as selecting it on the Projects
    // page. Clicks on the Read / Create doc buttons are handled by those buttons and never reach here.
    private void Card_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ProjectDocEntry entry
            && Window.GetWindow(this)?.DataContext is MainViewModel vm
            && vm.SelectDocEntryCommand.CanExecute(entry))
            vm.SelectDocEntryCommand.Execute(entry);
    }
}
