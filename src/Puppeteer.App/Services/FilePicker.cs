using Microsoft.Win32;
namespace Puppeteer.App.Services;
public interface IFilePicker { string? PickFolder(); string? PickIcon(); }
public sealed class FilePicker:IFilePicker
{
 public string? PickFolder(){var dialog=new OpenFolderDialog{Title="Add project root",Multiselect=false};return dialog.ShowDialog()==true?dialog.FolderName:null;}
 public string? PickIcon(){var dialog=new OpenFileDialog{Title="Choose project icon",Filter="Images|*.png;*.jpg;*.jpeg;*.webp;*.avif;*.ico;*.svg|All files|*.*"};return dialog.ShowDialog()==true?dialog.FileName:null;}
}
