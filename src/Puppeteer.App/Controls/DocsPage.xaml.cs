using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Puppeteer.App.ViewModels;

namespace Puppeteer.App.Controls;

public partial class DocsPage : UserControl
{
    public DocsPage() => InitializeComponent();

    // A click anywhere on a doc row inspects that project. Clicks on its own buttons are handled there
    // and never bubble this far, so those keep working.
    private void Row_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProjectDocEntry entry }
            && Window.GetWindow(this)?.DataContext is MainViewModel vm
            && vm.SelectDocEntryCommand.CanExecute(entry))
            vm.SelectDocEntryCommand.Execute(entry);
    }
}
