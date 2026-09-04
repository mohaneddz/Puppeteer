using Microsoft.Win32;
namespace Puppeteer.App.Services;
public interface IFilePicker { string? PickFolder(); string? PickIcon(); string? PickMarkdown(); }
public sealed class FilePicker:IFilePicker
{
 public string? PickFolder(){var dialog=new OpenFolderDialog{Title="Choose a folder",Multiselect=false};return dialog.ShowDialog()==true?dialog.FolderName:null;}
 public string? PickMarkdown(){var dialog=new OpenFileDialog{Title="Choose a state doc",Filter="Markdown|*.md|All files|*.*"};return dialog.ShowDialog()==true?dialog.FileName:null;}
 public string? PickIcon(){var dialog=new OpenFileDialog{Title="Choose project icon",Filter="Images|*.png;*.jpg;*.jpeg;*.webp;*.avif;*.ico;*.svg|All files|*.*"};return dialog.ShowDialog()==true?dialog.FileName:null;}
}
