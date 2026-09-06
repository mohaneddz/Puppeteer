using Microsoft.Win32;
namespace Puppeteer.App.Services;
public interface IFilePicker { string? PickFolder(); string? PickIcon(); string? PickMarkdown(); string? PickBackupForSave(); string? PickBackupForRestore(); }
public sealed class FilePicker:IFilePicker
{
 public string? PickFolder(){var dialog=new OpenFolderDialog{Title="Choose a folder",Multiselect=false};return dialog.ShowDialog()==true?dialog.FolderName:null;}
 public string? PickMarkdown(){var dialog=new OpenFileDialog{Title="Choose a state doc",Filter="Markdown|*.md|All files|*.*"};return dialog.ShowDialog()==true?dialog.FileName:null;}
 public string? PickIcon(){var dialog=new OpenFileDialog{Title="Choose project icon",Filter="Images|*.png;*.jpg;*.jpeg;*.webp;*.avif;*.ico;*.svg|All files|*.*"};return dialog.ShowDialog()==true?dialog.FileName:null;}
 public string? PickBackupForSave(){var dialog=new SaveFileDialog{Title="Save Puppeteer backup",Filter="Puppeteer backup|*.puppeteer-backup",DefaultExt=".puppeteer-backup",AddExtension=true,FileName=$"puppeteer-backup-{DateTime.Now:yyyy-MM-dd}.puppeteer-backup"};return dialog.ShowDialog()==true?dialog.FileName:null;}
 public string? PickBackupForRestore(){var dialog=new OpenFileDialog{Title="Restore Puppeteer backup",Filter="Puppeteer backup|*.puppeteer-backup;*.zip|All files|*.*",Multiselect=false};return dialog.ShowDialog()==true?dialog.FileName:null;}
}
