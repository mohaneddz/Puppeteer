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

    private void Card_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindParent<Button>(source) is not null) return;
        if (DataContext is ProjectDocEntry entry
            && Window.GetWindow(this)?.DataContext is MainViewModel vm
            && vm.OpenReaderCommand.CanExecute(entry))
            vm.OpenReaderCommand.Execute(entry);
    }

    private static T? FindParent<T>(DependencyObject source) where T : DependencyObject
    {
        for (var current = source; current is not null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
            if (current is T match) return match;
        return null;
    }
}
