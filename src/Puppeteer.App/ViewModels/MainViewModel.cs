using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Puppeteer.App.Services;
using Puppeteer.Core;
namespace Puppeteer.App.ViewModels;
public sealed class MainViewModel:ObservableObject
{
 private readonly IProjectRepository _repository; private readonly IProjectScanner _scanner; private readonly ProjectSearchService _searchService; private readonly ITerminalService _terminalService; private readonly IProjectLauncher _launcher; private readonly IIconDiscoveryService _icons; private readonly IFilePicker _picker; private readonly IProjectClassifier _classifier; private readonly AppConfig _config;
 private readonly List<Project> _allProjects=[]; private string _search=""; private string _selectedType="All"; private string _selectedTechnology="All"; private string _selectedCategory="All"; private string _currentPage="Projects"; private Project? _selectedProject; private TerminalSessionViewModel? _selectedSession; private bool _terminalOpen; private bool _isBusy; private string _status="Ready"; private string _defaultShell="powershell.exe";
 public ObservableCollection<Project> Projects{get;}=[]; public ObservableCollection<string> Types{get;}=[]; public ObservableCollection<string> TechnologyOptions{get;}=[]; public ObservableCollection<string> CategoryOptions{get;}=[]; public ObservableCollection<RootFolder> Roots{get;}=[]; public ObservableCollection<TerminalSessionViewModel> Sessions{get;}=[]; public ObservableCollection<IconCandidate> IconCandidates{get;}=[];
 public string Search{get=>_search;set{if(Set(ref _search,value))Refresh();}} public string SelectedType{get=>_selectedType;set{if(Set(ref _selectedType,value))Refresh();}} public string SelectedTechnology{get=>_selectedTechnology;set{if(Set(ref _selectedTechnology,value))Refresh();}} public string SelectedCategory{get=>_selectedCategory;set{if(Set(ref _selectedCategory,value))Refresh();}} public string CurrentPage{get=>_currentPage;set=>Set(ref _currentPage,value);} public Project? SelectedProject{get=>_selectedProject;set{Set(ref _selectedProject,value);IconCandidates.Clear();Raise(nameof(HasMoreGitFiles));Raise(nameof(MoreGitFileCount));}}
 public bool HasMoreGitFiles=>_selectedProject?.Git is {Files: not null} g&&g.ModifiedFileCount>g.Files.Count;
 public int MoreGitFileCount=>_selectedProject?.Git is {Files: not null} g?Math.Max(0,g.ModifiedFileCount-g.Files.Count):0; public TerminalSessionViewModel? SelectedSession{get=>_selectedSession;set{if(Set(ref _selectedSession,value)&&value is not null)TerminalOpen=true;}} public bool TerminalOpen{get=>_terminalOpen;set=>Set(ref _terminalOpen,value);} public bool IsBusy{get=>_isBusy;set=>Set(ref _isBusy,value);} public string Status{get=>_status;set{if(Set(ref _status,value))ShowToast();}} public string DefaultShell{get=>_defaultShell;set=>Set(ref _defaultShell,value);}
 private string _viewMode="Grid"; public string ViewMode{get=>_viewMode;set=>Set(ref _viewMode,value);}
 private bool _statusVisible; public bool StatusVisible{get=>_statusVisible;set=>Set(ref _statusVisible,value);} private DispatcherTimer? _toast;
 private void ShowToast(){StatusVisible=true;(_toast??=CreateToast()).Stop();_toast.Start();}
 private DispatcherTimer CreateToast(){var t=new DispatcherTimer{Interval=TimeSpan.FromSeconds(3.2)};t.Tick+=(_,__)=>{StatusVisible=false;t.Stop();};return t;}
 public IReadOnlyList<string> Shells{get;}=["powershell.exe","cmd.exe","pwsh.exe","wsl.exe"];
 public int RunningCount=>Sessions.Count(s=>s.Running); public string RunningBadge=>RunningCount>0?RunningCount.ToString():"";
 public RelayCommand NavigateCommand{get;} public AsyncRelayCommand AddRootCommand{get;} public AsyncRelayCommand RemoveRootCommand{get;} public AsyncRelayCommand OpenTerminalCommand{get;} public RelayCommand OpenFolderCommand{get;} public RelayCommand OpenIdeCommand{get;} public RelayCommand CollapseTerminalCommand{get;} public AsyncRelayCommand FindIconCommand{get;} public AsyncRelayCommand ChangeIconCommand{get;} public AsyncRelayCommand ResetIconCommand{get;} public AsyncRelayCommand ChooseCandidateCommand{get;}
 public RelayCommand CopyPathCommand{get;} public RelayCommand SetViewCommand{get;} public RelayCommand NewSessionCommand{get;} public RelayCommand CloseSessionCommand{get;} public RelayCommand ClearOutputCommand{get;} public RelayCommand RunPresetCommand{get;}
 private string _groqApiKey=""; public string GroqApiKey{get=>_groqApiKey;set{if(Set(ref _groqApiKey,value)){_=SaveGroqKeyAsync();Raise(nameof(UsingEnvGroqKey));}}}
 public bool UsingEnvGroqKey=>string.IsNullOrWhiteSpace(_groqApiKey)&&!string.IsNullOrWhiteSpace(_config.GroqApiKeyFromEnv);
 public RelayCommand SelectFolderCommand{get;} public RelayCommand ClearFolderCommand{get;} public RelayCommand ToggleTerminalMaxCommand{get;} public RelayCommand ToggleSplitCommand{get;} public RelayCommand ToggleSidebarCommand{get;} public RelayCommand ToggleDetailsCommand{get;}
 public ObservableCollection<FolderNode> FolderTree{get;}=[];
 private FolderNode? _selectedFolder; public FolderNode? SelectedFolder{get=>_selectedFolder;set{if(Set(ref _selectedFolder,value)){Raise(nameof(FolderFilterActive));Refresh();}}}
 public bool FolderFilterActive=>_selectedFolder is not null;
 private bool _terminalMaximized; public bool TerminalMaximized{get=>_terminalMaximized;set=>Set(ref _terminalMaximized,value);}
 private bool _terminalSplit; public bool TerminalSplit{get=>_terminalSplit;set=>Set(ref _terminalSplit,value);}
 private bool _sidebarCollapsed; public bool SidebarCollapsed{get=>_sidebarCollapsed;set=>Set(ref _sidebarCollapsed,value);}
 private bool _detailsCollapsed; public bool DetailsCollapsed{get=>_detailsCollapsed;set=>Set(ref _detailsCollapsed,value);}
 public MainViewModel(IProjectRepository repository,IProjectScanner scanner,ProjectSearchService searchService,ITerminalService terminalService,IProjectLauncher launcher,IIconDiscoveryService icons,IFilePicker picker,IProjectClassifier classifier,AppConfig config)
 {
  _repository=repository;_scanner=scanner;_searchService=searchService;_terminalService=terminalService;_launcher=launcher;_icons=icons;_picker=picker;_classifier=classifier;_config=config;
  Sessions.CollectionChanged+=(_,__)=>{Raise(nameof(RunningCount));Raise(nameof(RunningBadge));};
  NavigateCommand=new(p=>CurrentPage=p?.ToString()??"Projects");AddRootCommand=new(_=>AddRootAsync());RemoveRootCommand=new(p=>RemoveRootAsync(p as RootFolder),p=>p is RootFolder);OpenTerminalCommand=new(p=>OpenTerminalAsync(p as Project??SelectedProject),p=>(p as Project??SelectedProject) is not null);OpenFolderCommand=new(p=>OpenFolder(p as Project??SelectedProject),p=>(p as Project??SelectedProject) is not null);OpenIdeCommand=new(p=>OpenIde(p as Project??SelectedProject),p=>(p as Project??SelectedProject) is not null);CollapseTerminalCommand=new(_=>TerminalOpen=false);FindIconCommand=new(_=>FindIconsAsync(),_=>SelectedProject is not null);ChangeIconCommand=new(_=>ChangeIconAsync(),_=>SelectedProject is not null);ResetIconCommand=new(_=>SetIconAsync(null),_=>SelectedProject is not null);ChooseCandidateCommand=new(p=>SetIconAsync((p as IconCandidate)?.Path),p=>p is IconCandidate);
  CopyPathCommand=new(p=>CopyPath(p as Project??SelectedProject),p=>(p as Project??SelectedProject) is not null);SetViewCommand=new(p=>ViewMode=p?.ToString()??"Grid");NewSessionCommand=new(_=>NewSession(),_=>SelectedProject is not null);CloseSessionCommand=new(p=>CloseSession(p as TerminalSessionViewModel),p=>p is TerminalSessionViewModel);ClearOutputCommand=new(_=>{SelectedSession?.Output.Clear();Status="Terminal cleared";},_=>SelectedSession is not null);RunPresetCommand=new(p=>RunPreset(p as CommandPreset),p=>p is CommandPreset&&SelectedProject is not null);
  SelectFolderCommand=new(p=>SelectedFolder=p as FolderNode);ClearFolderCommand=new(_=>SelectedFolder=null);ToggleTerminalMaxCommand=new(_=>TerminalMaximized=!TerminalMaximized);ToggleSplitCommand=new(_=>TerminalSplit=!TerminalSplit,_=>Sessions.Count>0);ToggleSidebarCommand=new(_=>SidebarCollapsed=!SidebarCollapsed);ToggleDetailsCommand=new(_=>DetailsCollapsed=!DetailsCollapsed);
 }
 public async Task LoadAsync(){_groqApiKey=await _repository.GetSettingAsync("GroqApiKey")??"";Raise(nameof(GroqApiKey));Raise(nameof(UsingEnvGroqKey));Roots.Clear();foreach(var root in await _repository.GetRootsAsync())Roots.Add(root);_allProjects.Clear();_allProjects.AddRange(await _repository.GetProjectsAsync());RebuildTree();Refresh();SelectedProject=Projects.FirstOrDefault();_=ClassifyUncategorizedAsync();}
 private Task SaveGroqKeyAsync()=>_repository.SetSettingAsync("GroqApiKey",_groqApiKey.Trim());
 private async Task ClassifyUncategorizedAsync()
 {
  var key=string.IsNullOrWhiteSpace(_groqApiKey)?_config.GroqApiKeyFromEnv:_groqApiKey;
  if(string.IsNullOrWhiteSpace(key))return;
  var pending=_allProjects.Where(p=>string.IsNullOrWhiteSpace(p.Category)&&Directory.Exists(p.Path)).ToArray();
  if(pending.Length==0)return;
  using var gate=new SemaphoreSlim(4);
  await Task.WhenAll(pending.Select(async project=>{
   await gate.WaitAsync();
   try{var category=await _classifier.ClassifyAsync(project.Path,key!);if(!string.IsNullOrWhiteSpace(category)){await _repository.SetProjectCategoryAsync(project.Id,category);await Application.Current.Dispatcher.InvokeAsync(()=>ApplyCategory(project.Id,category));}}
   catch{}
   finally{gate.Release();}
  }));
  await Application.Current.Dispatcher.InvokeAsync(Refresh);
 }
 private void ApplyCategory(Guid id,string category){var i=_allProjects.FindIndex(p=>p.Id==id);if(i>=0)_allProjects[i]=_allProjects[i] with{Category=category};}
 private void RebuildTree(){FolderTree.Clear();foreach(var node in FolderNode.Build(Roots,_allProjects))FolderTree.Add(node);if(_selectedFolder is not null&&_allProjects.All(p=>!p.Path.StartsWith(_selectedFolder.Path,StringComparison.OrdinalIgnoreCase)))SelectedFolder=null;}
 private async Task AddRootAsync(){var path=_picker.PickFolder();if(string.IsNullOrWhiteSpace(path))return;IsBusy=true;Status="Scanning root…";try{var root=new RootFolder(Guid.NewGuid(),Path.GetFullPath(path),DateTimeOffset.UtcNow);await _repository.AddRootAsync(root);Roots.Add(root);var scanned=await _scanner.ScanAsync(root);await _repository.UpsertProjectsAsync(scanned);_allProjects.RemoveAll(p=>p.RootId==root.Id);_allProjects.AddRange(scanned);RebuildTree();Refresh();SelectedProject=Projects.FirstOrDefault();Status=$"Found {scanned.Count} projects";_=ClassifyUncategorizedAsync();}catch(Exception e){Status=e.Message;}finally{IsBusy=false;}}
 private async Task RemoveRootAsync(RootFolder? root){if(root is null)return;await _repository.RemoveRootAsync(root.Id);Roots.Remove(root);_allProjects.RemoveAll(p=>p.RootId==root.Id);RebuildTree();Refresh();SelectedProject=Projects.FirstOrDefault();Status="Root removed from Puppeteer; project files were not changed.";}
 private void OpenFolder(Project? project){if(project is null)return;if(Directory.Exists(project.Path)){_launcher.OpenFolder(project.Path);MarkOpened(project);Status=$"Opened {project.Name} in Explorer";}else Status=$"{project.Name} no longer exists on disk.";}
 private void OpenIde(Project? project){if(project is null)return;if(Directory.Exists(project.Path)){_launcher.OpenInIde(project.Path);MarkOpened(project);Status=$"Opening {project.Name} in your IDE";}else Status=$"{project.Name} no longer exists on disk.";}
 private void MarkOpened(Project project){var i=_allProjects.FindIndex(p=>p.Id==project.Id);if(i>=0)_allProjects[i]=_allProjects[i] with{LastOpenedAt=DateTimeOffset.UtcNow};_=_repository.SetProjectOpenedAsync(project.Id,DateTimeOffset.UtcNow);}
 private void CopyPath(Project? project){if(project is null)return;try{Clipboard.SetText(project.Path);Status=$"Copied path: {project.Path}";}catch{Status="Couldn't access the clipboard";}}
 private void NewSession(){if(SelectedProject is null){Status="Select a project first";return;}_=OpenTerminalAsync(SelectedProject);}
 private void CloseSession(TerminalSessionViewModel? session){if(session is null||!Sessions.Contains(session))return;try{session.Session?.StopAsync();}catch{}var name=session.Name;Sessions.Remove(session);if(_selectedSession==session)SelectedSession=Sessions.FirstOrDefault();if(Sessions.Count==0)TerminalOpen=false;Status=$"Closed session {name}";}
 private async Task OpenTerminalAsync(Project? project,string? command=null,string? name=null){if(project is null)return;if(!Directory.Exists(project.Path)){Status="Project folder no longer exists on disk.";return;}try{var session=await _terminalService.CreateAsync(new(project.Path,DefaultShell,command,name??$"shell {Sessions.Count+1}"));MarkOpened(project);var vm=new TerminalSessionViewModel(session);vm.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(TerminalSessionViewModel.Running)){Raise(nameof(RunningCount));Raise(nameof(RunningBadge));Refresh();}};Sessions.Add(vm);SelectedSession=vm;TerminalOpen=true;Status=command is null?$"Terminal started in {project.Name}":$"Running “{command}” in {project.Name}";}catch(Exception e){Status=e.Message;}}
 private void RunPreset(CommandPreset? preset){if(preset is null||SelectedProject is null)return;_=OpenTerminalAsync(SelectedProject,preset.Command,preset.Name);}
 private async Task FindIconsAsync(){if(SelectedProject is null)return;IconCandidates.Clear();foreach(var icon in await _icons.FindAsync(SelectedProject.Path))IconCandidates.Add(icon);Status=$"Found {IconCandidates.Count} icon candidates";}
 private Task ChangeIconAsync()=>SetIconAsync(_picker.PickIcon());
 private async Task SetIconAsync(string? path){if(SelectedProject is null||path is "")return;await _repository.SetProjectIconAsync(SelectedProject.Id,path);var updated=SelectedProject with{CustomIconPath=path};var index=_allProjects.FindIndex(p=>p.Id==updated.Id);if(index>=0)_allProjects[index]=updated;Refresh();SelectedProject=Projects.FirstOrDefault(p=>p.Id==updated.Id);Status=path is null?"Framework icon restored":"Project icon updated";}
 private bool _refreshing;
 private void Refresh()
 {
  // Rebuilding a bound ComboBox's items nulls its SelectedItem, which writes back and re-enters
  // Refresh; guard against that and only rebuild each option list when it actually changed.
  if(_refreshing)return;
  _refreshing=true;
  try
  {
   _selectedType=Sync(Types,_searchService.BuildTypes(_allProjects),_selectedType);Raise(nameof(SelectedType));
   _selectedTechnology=Sync(TechnologyOptions,_searchService.BuildTechnologies(_allProjects),_selectedTechnology);Raise(nameof(SelectedTechnology));
   _selectedCategory=Sync(CategoryOptions,_searchService.BuildCategories(_allProjects),_selectedCategory);Raise(nameof(SelectedCategory));
   var running=Sessions.Where(s=>s.Session is not null&&s.Running).Select(s=>_allProjects.FirstOrDefault(p=>p.Path.Equals(s.Session!.ProjectPath,StringComparison.OrdinalIgnoreCase))?.Id??Guid.Empty).ToHashSet();
   var scoped=_selectedFolder is null?_allProjects:_allProjects.Where(p=>p.Path.StartsWith(_selectedFolder.Path,StringComparison.OrdinalIgnoreCase));
   var filtered=_searchService.Filter(scoped,Search,_selectedType,_selectedTechnology,_selectedCategory,running);
   Projects.Clear();foreach(var p in filtered)Projects.Add(p);
  }
  finally{_refreshing=false;}
 }
 private static string Sync(ObservableCollection<string> target,IReadOnlyList<string> values,string keep)
 {
  if(!target.SequenceEqual(values,StringComparer.OrdinalIgnoreCase)){target.Clear();foreach(var v in values)target.Add(v);}
  return values.Contains(keep,StringComparer.OrdinalIgnoreCase)?keep:"All";
 }
}
