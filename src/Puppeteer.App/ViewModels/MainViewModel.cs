using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Puppeteer.App.Services;
using Puppeteer.Core;
namespace Puppeteer.App.ViewModels;
public sealed partial class MainViewModel:ObservableObject
{
 private readonly IProjectRepository _repository; private readonly IProjectBackupService _backup; private readonly IProjectScanner _scanner; private readonly ProjectSearchService _searchService; private readonly ITerminalService _terminalService; private readonly IProjectLauncher _launcher; private readonly IIconDiscoveryService _icons; private readonly IFilePicker _picker; private readonly IProjectClassifier _classifier; private readonly IDocFieldGenerator _fieldGenerator; private readonly AppConfig _config; private readonly IGitMetadataService _git; private readonly IStartupService _startup; private readonly object _writeGate=new(); private Task _pendingWrites=Task.CompletedTask;
 private readonly List<Project> _allProjects=[]; private readonly HashSet<Guid> _hiddenProjects=[]; private readonly HashSet<Guid> _archivedProjects=[]; private string _search=""; private string _selectedType="All"; private string _selectedTechnology="All"; private string _selectedCategory="All"; private string _selectedSpecial="All"; private string _currentPage="Projects"; private Project? _selectedProject; private TerminalSessionViewModel? _selectedSession; private bool _terminalOpen; private bool _isBusy; private string _status="Ready"; private string _defaultShell="powershell.exe";
 // Most-recently-visited first. A tab is pushed here when it stops being selected, so closing the
 // active tab can return to wherever focus actually came from, not just the next one in list order.
 private readonly List<TerminalSessionViewModel> _terminalHistory=[];
 public BatchCollection<Project> Projects{get;}=[]; public ObservableCollection<string> Types{get;}=[]; public ObservableCollection<string> TechnologyOptions{get;}=[]; public ObservableCollection<string> CategoryOptions{get;}=[]; public ObservableCollection<RootFolder> Roots{get;}=[]; public ObservableCollection<TerminalSessionViewModel> Sessions{get;}=[]; public ObservableCollection<IconCandidate> IconCandidates{get;}=[];
 public IReadOnlyList<string> SpecialFilters{get;}=["All","Favorites","Running","Git repositories","No Git repository","Git changes","Custom icon","Uncategorized","Never opened"];
 public IReadOnlyList<string> IconShapeOptions{get;}=["Rounded","Sharp","Circle","Squircle"]; public RelayCommand SetIconShapeCommand{get;}
 public string IconShape{get=>_selectedProject?.IconShape??"Rounded";set{if(_selectedProject is not { } project||!IconShapeOptions.Contains(value,StringComparer.OrdinalIgnoreCase)||string.Equals(project.IconShape,value,StringComparison.OrdinalIgnoreCase))return;var updated=project with{IconShape=value};_selectedProject=updated;var index=_allProjects.FindIndex(p=>p.Id==updated.Id);if(index>=0)_allProjects[index]=updated;_=QueueWriteAsync(()=>_repository.SetProjectIconShapeAsync(updated.Id,value));Raise(nameof(SelectedProject));Raise(nameof(IconShape));Refresh();}}
 public string Search{get=>_search;set{if(Set(ref _search,value)){Raise(nameof(ActiveSearch));QueueSearchRefresh();}}} public string ActiveSearch{get=>_currentPage=="Docs"?_docSearch:_search;set{if(_currentPage=="Docs")DocSearch=value;else Search=value;}} public string SearchPlaceholder=>_currentPage=="Docs"?"Search docs...":"Search projects..."; public string SelectedType{get=>_selectedType;set{if(Set(ref _selectedType,value))Refresh();}} public string SelectedTechnology{get=>_selectedTechnology;set{if(Set(ref _selectedTechnology,value))Refresh();}} public string SelectedCategory{get=>_selectedCategory;set{if(Set(ref _selectedCategory,value))Refresh();}} public string SelectedSpecial{get=>_selectedSpecial;set{if(Set(ref _selectedSpecial,value))Refresh();}} public string CurrentPage{get=>_currentPage;set{if(Set(ref _currentPage,value)){SavePref("LastPage",value);Raise(nameof(IsLibraryPage));Raise(nameof(ProjectPageTitle));Raise(nameof(ActiveSearch));Raise(nameof(SearchPlaceholder));Refresh();if(value=="Docs")RebuildDocEntries();}}} public bool IsLibraryPage=>_currentPage is "Projects" or "Archived" or "Hidden"; public string ProjectPageTitle=>_currentPage; public Project? SelectedProject{get=>_selectedProject;set{if(_refreshing)return;if(!Set(ref _selectedProject,value))return;IconCandidates.Clear();Raise(nameof(HasMoreGitFiles));Raise(nameof(MoreGitFileCount));Raise(nameof(IconFillsContainer));Raise(nameof(IconShape));_=LoadGitForAsync(value);}} public bool IconFillsContainer{get=>_selectedProject?.IconFill??false;set{if(_selectedProject is not { } project||project.IconFill==value)return;var updated=project with{IconFill=value};_selectedProject=updated;var index=_allProjects.FindIndex(p=>p.Id==updated.Id);if(index>=0)_allProjects[index]=updated;_=QueueWriteAsync(()=>_repository.SetProjectIconFillAsync(updated.Id,value));Raise(nameof(SelectedProject));Raise(nameof(IconFillsContainer));Refresh();}}
 /// <summary>Reads git status for every project off the UI thread, a few at a time, and folds the
 /// results back in one pass. Scanning used to do this inline — a child process per project, in
 /// series — which made adding a root feel frozen and left the status stale from then on.</summary>
 public async Task RefreshGitAsync()
 {
  if(!IsUiActive){_gitRefreshPending=true;return;}
  var targets=_allProjects.Where(p=>Directory.Exists(p.Path)).ToArray();
  if(targets.Length==0)return;
  using var gate=new SemaphoreSlim(Math.Min(2,Environment.ProcessorCount));
  var results=await Task.WhenAll(targets.Select(async project=>{
   await gate.WaitAsync();
   try{return (project.Id,Status:await _git.GetStatusAsync(project.Path));}
   catch{return (project.Id,Status:null);}
   finally{gate.Release();}
  }));
  var changed=false;
  foreach(var (id,status) in results)
  {
   if(status is null)continue;
   var i=_allProjects.FindIndex(p=>p.Id==id);
   if(i<0||_allProjects[i].Git==status)continue;
   _allProjects[i]=_allProjects[i] with{Git=status};
   changed=true;
  }
  if(changed)Refresh();
  await CaptureSnapshotsAsync();
 }
 private async Task LoadGitForAsync(Project? project){if(project is null||project.Git is not null||!Directory.Exists(project.Path))return;GitStatus? status;try{status=await _git.GetStatusAsync(project.Path);}catch{return;}if(status is null)return;var i=_allProjects.FindIndex(p=>p.Id==project.Id);if(i>=0)_allProjects[i]=_allProjects[i] with{Git=status};if(_selectedProject?.Id==project.Id){_selectedProject=_selectedProject with{Git=status};Raise(nameof(SelectedProject));Raise(nameof(HasMoreGitFiles));Raise(nameof(MoreGitFileCount));}}
 public bool HasMoreGitFiles=>_selectedProject?.Git is {Files: not null} g&&g.ModifiedFileCount>g.Files.Count;
 public int MoreGitFileCount=>_selectedProject?.Git is {Files: not null} g?Math.Max(0,g.ModifiedFileCount-g.Files.Count):0; public TerminalSessionViewModel? SelectedSession{get=>_selectedSession;set{var previous=_selectedSession;if(!Set(ref _selectedSession,value))return;if(previous is not null){previous.IsSelected=false;if(previous!=value&&Sessions.Contains(previous)){_terminalHistory.Remove(previous);_terminalHistory.Insert(0,previous);}}if(value is not null){value.IsSelected=true;TerminalOpen=true;_terminalHistory.Remove(value);}}} public bool TerminalOpen{get=>_terminalOpen;set=>Set(ref _terminalOpen,value);} public bool IsBusy{get=>_isBusy;set=>Set(ref _isBusy,value);} public string Status{get=>_status;set{if(Set(ref _status,value))ShowToast();}} public string DefaultShell{get=>_defaultShell;set{if(Set(ref _defaultShell,value))SavePref("DefaultShell",value);}}
 private string _viewMode="Grid"; public string ViewMode{get=>_viewMode;set{if(Set(ref _viewMode,value))SavePref("ViewMode",value);}}
 public IReadOnlyList<string> SortModes{get;}=["Name","Recent","Type"]; private string _sortMode="Name"; public string SortMode{get=>_sortMode;set{if(Set(ref _sortMode,value)){SavePref("SortMode",value);Refresh();}}}
 public void SavePref(string key,string? value)=>_=QueueWriteAsync(()=>_repository.SetSettingAsync(key,value));
 public Task SavePrefsAsync(IReadOnlyDictionary<string,string?> values)=>QueueWriteAsync(()=>_repository.SetSettingsAsync(values));
 public Task FlushPendingWritesAsync(){lock(_writeGate)return _pendingWrites;}
 private Task QueueWriteAsync(Func<Task> write){lock(_writeGate)return _pendingWrites=_pendingWrites.ContinueWith(_=>write(),CancellationToken.None,TaskContinuationOptions.None,TaskScheduler.Default).Unwrap();}
 public Task<string?> GetPrefAsync(string key)=>_repository.GetSettingAsync(key);
 private bool _statusVisible; public bool StatusVisible{get=>_statusVisible;set=>Set(ref _statusVisible,value);} private DispatcherTimer? _toast;
 private void ShowToast(){if(!IsUiActive)return;StatusVisible=true;(_toast??=CreateToast()).Stop();_toast.Start();}
 private DispatcherTimer CreateToast(){var t=new DispatcherTimer{Interval=TimeSpan.FromSeconds(3.2)};t.Tick+=(_,__)=>{StatusVisible=false;t.Stop();};return t;}
 public IReadOnlyList<string> Shells{get;}=["powershell.exe","cmd.exe","pwsh.exe","wsl.exe"];
 public string Version=>"v"+(System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(2)??"1.0");
 public int RunningCount=>Sessions.Count(s=>s.Running); public string RunningBadge=>RunningCount>0?RunningCount.ToString():"";
 public RelayCommand NavigateCommand{get;} public AsyncRelayCommand AddRootCommand{get;} public AsyncRelayCommand RemoveRootCommand{get;} public AsyncRelayCommand OpenTerminalCommand{get;} public RelayCommand OpenFolderCommand{get;} public RelayCommand OpenIdeCommand{get;} public RelayCommand CollapseTerminalCommand{get;} public AsyncRelayCommand FindIconCommand{get;} public AsyncRelayCommand ChangeIconCommand{get;} public AsyncRelayCommand ResetIconCommand{get;} public AsyncRelayCommand ChooseCandidateCommand{get;} public AsyncRelayCommand DeleteProjectCommand{get;}
 public RelayCommand CopyPathCommand{get;} public RelayCommand CopyRepositoryCommand{get;} public RelayCommand OpenRepositoryCommand{get;} public RelayCommand SetViewCommand{get;} public RelayCommand NewSessionCommand{get;} public RelayCommand CloseSessionCommand{get;} public RelayCommand ClearOutputCommand{get;} public RelayCommand RunPresetCommand{get;} public RelayCommand RestartSessionCommand{get;} public RelayCommand StopAllCommand{get;} public RelayCommand CopyOutputCommand{get;} public RelayCommand TogglePinCommand{get;} public RelayCommand ToggleHiddenCommand{get;} public RelayCommand ToggleArchivedCommand{get;}
 private bool _autoClassify=true; public bool AutoClassify{get=>_autoClassify;set{if(Set(ref _autoClassify,value))SavePref("AutoClassify",value?"1":"0");}}

 // ---- Startup and notification area ----
 private bool _launchAtStartup; public bool LaunchAtStartup{get=>_launchAtStartup;set{if(!Set(ref _launchAtStartup,value))return;SavePref("LaunchAtStartup",value?"1":"0");ApplyStartupRegistration();}}
 private bool _startMinimized; public bool StartMinimized{get=>_startMinimized;set{if(!Set(ref _startMinimized,value))return;SavePref("StartMinimized",value?"1":"0");ApplyStartupRegistration();}}
 private bool _minimizeToTray=true; public bool MinimizeToTray{get=>_minimizeToTray;set{if(Set(ref _minimizeToTray,value))SavePref("MinimizeToTray",value?"1":"0");}}
 private bool _closeToTray; public bool CloseToTray{get=>_closeToTray;set{if(Set(ref _closeToTray,value))SavePref("CloseToTray",value?"1":"0");}}
 // Keeping the registry entry in step with both toggles: enabling "start minimized" has to rewrite
 // the recorded command line, not just the flag we read back at launch.
 private void ApplyStartupRegistration()=>_startup.SetEnabled(_launchAtStartup,_startMinimized);

 // ---- Terminal ----
 public IReadOnlyList<string> ScrollbackOptions{get;}=["500","2000","5000","20000"];
 private string _scrollback="2000";
 public string Scrollback{get=>_scrollback;set{if(!Set(ref _scrollback,value))return;SavePref("Scrollback",value);ApplyScrollback();}}
 private void ApplyScrollback(){if(!int.TryParse(_scrollback,out var lines))return;TerminalSessionViewModel.MaxOutputLines=lines;foreach(var session in Sessions)session.TrimOutput();}

 // ---- Appearance ----
 public IReadOnlyList<string> Densities{get;}=["Comfortable","Compact"];
 private string _density="Comfortable";
 public string Density{get=>_density;set{if(!Set(ref _density,value))return;SavePref("Density",value);RaiseCardMetrics();}}
 private void RaiseCardMetrics(){Raise(nameof(CardWidth));Raise(nameof(CardHeight));Raise(nameof(CardPadding));Raise(nameof(CardGap));}
 private bool Compact=>_density=="Compact";
 // The cell and the card's own spacing move together: shrinking only the cell clipped the card's
 // footer, because the content still wanted the comfortable card's height.
 public double CardWidth=>Compact?266:316;
 public double CardHeight=>Compact?158:182;
 public Thickness CardPadding=>Compact?new(13,11,13,11):new(16,15,16,15);
 public Thickness CardGap=>Compact?new(0,9,0,0):new(0,14,0,0);

 // ---- Tools ----
 private string _ideCommand=""; public string IdeCommand{get=>_ideCommand;set{if(Set(ref _ideCommand,value))SavePref("IdeCommand",value.Trim());}}
 public RelayCommand OpenDataFolderCommand{get;} public AsyncRelayCommand CreateBackupCommand{get;} public AsyncRelayCommand RestoreBackupCommand{get;}
 public static string DataFolder=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Puppeteer");
 public RelayCommand ResetLayoutCommand{get;}
 private string _groqApiKey=""; public string GroqApiKey{get=>_groqApiKey;set{if(Set(ref _groqApiKey,value)){_=SaveGroqKeyAsync();Raise(nameof(UsingEnvGroqKey));}}}
 private bool _showGroqApiKey; public bool ShowGroqApiKey{get=>_showGroqApiKey;set=>Set(ref _showGroqApiKey,value);}
 public RelayCommand ToggleGroqKeyVisibilityCommand{get;}
 public bool UsingEnvGroqKey=>string.IsNullOrWhiteSpace(_groqApiKey)&&!string.IsNullOrWhiteSpace(_config.GroqApiKeyFromEnv);
 public RelayCommand SelectFolderCommand{get;} public RelayCommand ClearFolderCommand{get;} public RelayCommand HideFolderProjectsCommand{get;} public RelayCommand ToggleFolderExpansionCommand{get;} public RelayCommand ToggleTerminalMaxCommand{get;} public RelayCommand ToggleSplitCommand{get;} public RelayCommand SetSplitColumnsCommand{get;} public RelayCommand ToggleSidebarCommand{get;} public RelayCommand ToggleDetailsCommand{get;} public RelayCommand ToggleTerminalCommand{get;} public AsyncRelayCommand RescanCommand{get;}
 public ObservableCollection<FolderNode> FolderTree{get;}=[]; public bool AreAllFoldersCollapsed=>FolderTree.Count>0&&AllFolders().All(folder=>!folder.IsExpanded); public string FolderExpandToggleToolTip=>AreAllFoldersCollapsed?"Expand all folders":"Collapse all folders";
 private FolderNode? _selectedFolder; public FolderNode? SelectedFolder{get=>_selectedFolder;set{if(Set(ref _selectedFolder,value)){Raise(nameof(FolderFilterActive));Refresh();}}}
 public bool FolderFilterActive=>_selectedFolder is not null;
 public bool HasAnyProjects=>_allProjects.Count>0;
 public int TotalProjects=>_allProjects.Count;
 public string LibrarySummary=>$"{_allProjects.Count} project{(_allProjects.Count==1?"":"s")} in {Roots.Count} root{(Roots.Count==1?"":"s")}";
 public RelayCommand ClearFiltersCommand{get;}
 private bool _terminalMaximized; public bool TerminalMaximized{get=>_terminalMaximized;set=>Set(ref _terminalMaximized,value);}
 private bool _terminalSplit; public bool TerminalSplit{get=>_terminalSplit;set=>Set(ref _terminalSplit,value);}
 private int _splitColumns; public int SplitColumns{get=>_splitColumns;set{if(Set(ref _splitColumns,value))SavePref("TerminalSplitColumns",value.ToString());}}
 private bool _sidebarCollapsed; public bool SidebarCollapsed{get=>_sidebarCollapsed;set{if(Set(ref _sidebarCollapsed,value))SavePref("SidebarCollapsed",value?"1":"0");}}
 private bool _detailsCollapsed; public bool DetailsCollapsed{get=>_detailsCollapsed;set{if(Set(ref _detailsCollapsed,value))SavePref("DetailsCollapsed",value?"1":"0");}}
 public MainViewModel(IProjectRepository repository,IProjectBackupService backup,IProjectScanner scanner,ProjectSearchService searchService,ITerminalService terminalService,IProjectLauncher launcher,IIconDiscoveryService icons,IFilePicker picker,IProjectClassifier classifier,IDocFieldGenerator fieldGenerator,AppConfig config,IGitMetadataService git,IStartupService startup,IProjectDocVault vault)
 {
  _repository=repository;_backup=backup;_scanner=scanner;_searchService=searchService;_terminalService=terminalService;_launcher=launcher;_icons=icons;_picker=picker;_classifier=classifier;_fieldGenerator=fieldGenerator;_config=config;_git=git;_startup=startup;_vault=vault;InitializeDocs();InitializeFieldEditor();InitializeDialog();
  Sessions.CollectionChanged+=(_,__)=>{Raise(nameof(RunningCount));Raise(nameof(RunningBadge));Refresh();};
  StartDurationTicker();
  NavigateCommand=new(p=>CurrentPage=p?.ToString()??"Projects");AddRootCommand=new(_=>AddRootAsync());RemoveRootCommand=new(p=>RemoveRootAsync(p as RootFolder),p=>p is RootFolder);OpenTerminalCommand=new(p=>OpenTerminalAsync(p as Project??SelectedProject),p=>(p as Project??SelectedProject) is not null);OpenFolderCommand=new(p=>OpenFolder(p as Project??SelectedProject),p=>(p as Project??SelectedProject) is not null);OpenIdeCommand=new(p=>OpenIde(p as Project??SelectedProject),p=>(p as Project??SelectedProject) is not null);CollapseTerminalCommand=new(_=>TerminalOpen=false);FindIconCommand=new(_=>FindIconsAsync(),_=>SelectedProject is not null);ChangeIconCommand=new(_=>ChangeIconAsync(),_=>SelectedProject is not null);ResetIconCommand=new(_=>SetIconAsync(null),_=>SelectedProject is not null);ChooseCandidateCommand=new(p=>SetIconAsync((p as IconCandidate)?.Path),p=>p is IconCandidate);DeleteProjectCommand=new(p=>DeleteProjectAsync(p as Project??SelectedProject),p=>(p as Project??SelectedProject) is not null);
  CopyPathCommand=new(p=>CopyPath(p as Project??SelectedProject),p=>(p as Project??SelectedProject) is not null);CopyRepositoryCommand=new(p=>CopyRepository(p as Project??SelectedProject));OpenRepositoryCommand=new(p=>OpenRepository(p as Project??SelectedProject));SetViewCommand=new(p=>ViewMode=p?.ToString()??"Grid");NewSessionCommand=new(_=>NewSession(),_=>SelectedProject is not null);CloseSessionCommand=new(p=>CloseSession(p as TerminalSessionViewModel),p=>p is TerminalSessionViewModel);ClearOutputCommand=new(_=>{SelectedSession?.ClearOutput();Status="Terminal cleared";},_=>SelectedSession is not null);RunPresetCommand=new(p=>RunPreset(p as CommandPreset),p=>p is CommandPreset&&SelectedProject is not null);RestartSessionCommand=new(p=>RestartSession(p as TerminalSessionViewModel??SelectedSession),p=>(p as TerminalSessionViewModel??SelectedSession) is not null);StopAllCommand=new(_=>{foreach(var s in Sessions.ToArray())s.StopCommand.Execute(null);Status="Stopped all sessions";},_=>Sessions.Any(s=>s.Running));CopyOutputCommand=new(_=>CopyOutput(),_=>SelectedSession is not null);TogglePinCommand=new(p=>TogglePin(p as Project??SelectedProject),p=>(p as Project??SelectedProject) is not null);ToggleHiddenCommand=new(p=>ToggleProjectState(p as Project??SelectedProject,_hiddenProjects,"Hidden","Shown"),p=>(p as Project??SelectedProject) is not null);ToggleArchivedCommand=new(p=>ToggleProjectState(p as Project??SelectedProject,_archivedProjects,"Archived","Restored"),p=>(p as Project??SelectedProject) is not null);ResetLayoutCommand=new(_=>{_=QueueWriteAsync(()=>_repository.SetSettingsAsync(new Dictionary<string,string?>{["WindowPlacement"]=null,["SidebarWidth"]=null,["DetailsWidth"]=null,["TerminalHeight"]=null,["SidebarCollapsed"]=null,["DetailsCollapsed"]=null}));Status="Window layout will reset next launch";});
  SelectFolderCommand=new(p=>SelectedFolder=p as FolderNode);ClearFolderCommand=new(_=>SelectedFolder=null);HideFolderProjectsCommand=new(p=>HideFolderProjects(p as FolderNode));SetIconShapeCommand=new(p=>IconShape=p?.ToString()??"Rounded");ToggleFolderExpansionCommand=new(_=>ToggleFolderExpansion());ToggleTerminalMaxCommand=new(_=>TerminalMaximized=!TerminalMaximized);ToggleSplitCommand=new(_=>TerminalSplit=!TerminalSplit,_=>Sessions.Count>0);SetSplitColumnsCommand=new(p=>SplitColumns=int.TryParse(p?.ToString(),out var columns)&&columns is >=0 and <=3?columns:0);ToggleSidebarCommand=new(_=>SidebarCollapsed=!SidebarCollapsed);ToggleDetailsCommand=new(_=>DetailsCollapsed=!DetailsCollapsed);ToggleTerminalCommand=new(_=>TerminalOpen=!TerminalOpen);ToggleGroqKeyVisibilityCommand=new(_=>ShowGroqApiKey=!ShowGroqApiKey);OpenDataFolderCommand=new(_=>{_launcher.OpenFolder(DataFolder);Status="Opened Puppeteer's data folder";});CreateBackupCommand=new(_=>CreateBackupAsync(),_=>!IsBusy);RestoreBackupCommand=new(_=>RestoreBackupAsync(),_=>!IsBusy);RescanCommand=new(_=>RescanAllAsync(),_=>Roots.Count>0&&!IsBusy);
  ClearFiltersCommand=new(_=>{_selectedFolder=null;Raise(nameof(SelectedFolder));Raise(nameof(FolderFilterActive));_selectedType="All";_selectedTechnology="All";_selectedCategory="All";_selectedSpecial="All";Raise(nameof(SelectedSpecial));_search="";Raise(nameof(Search));Raise(nameof(ActiveSearch));Refresh();});
 }
 private async Task CreateBackupAsync()
 {
  var path=_picker.PickBackupForSave();if(string.IsNullOrWhiteSpace(path))return;
  IsBusy=true;Status="Creating backup…";
  try{await FlushPendingWritesAsync();var result=await _backup.CreateAsync(path);Status=$"Backed up {result.ProjectCount} project{(result.ProjectCount==1?"":"s")} and {result.RootCount} root{(result.RootCount==1?"":"s")}.";}
  catch(Exception e){Status=$"Couldn't create backup: {e.Message}";}
  finally{IsBusy=false;}
 }
 private async Task RestoreBackupAsync()
 {
  var path=_picker.PickBackupForRestore();if(string.IsNullOrWhiteSpace(path))return;
  if(!await ConfirmAsync("Restore backup?","Your current Puppeteer library data, links, customizations and settings will be replaced. Project folders and their files will not be changed.","Restore","Cancel",danger:true))return;
  IsBusy=true;Status="Restoring backup…";
  try{await FlushPendingWritesAsync();var result=await _backup.RestoreAsync(path);await LoadAsync();Status=$"Restored {result.ProjectCount} project{(result.ProjectCount==1?"":"s")} and {result.RootCount} root{(result.RootCount==1?"":"s")}.";}
  catch(Exception e){Status=$"Couldn't restore backup: {e.Message}";}
  finally{IsBusy=false;}
 }
 // Each keystroke re-filters, re-derives the three option lists and rebuilds the whole grid. That is
 // affordable once, not once per character on a fast typist's search term, so coalesce a burst of
 // keystrokes into a single refresh shortly after typing stops.
 private DispatcherTimer? _searchDebounce;
 private void QueueSearchRefresh()
 {
  _searchDebounce??=CreateSearchDebounce();
  _searchDebounce.Stop();
  _searchDebounce.Start();
 }
 private DispatcherTimer CreateSearchDebounce()
 {
  var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(140)};
  timer.Tick+=(_,__)=>{timer.Stop();Refresh();};
  return timer;
 }
 // One shared tick advances every session's age label. Sessions don't each own a timer, and the tick
 // does nothing at all while nothing is running, so an idle app stays idle.
 private DispatcherTimer? _durations;
 private void StartDurationTicker()
 {
  _durations=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(250)};
  _durations.Tick+=(_,__)=>{ foreach(var session in Sessions){session.FlushOutput();session.TickDuration();} UpdateDisplayTimer(); };
  Sessions.CollectionChanged+=(_,__)=>UpdateDisplayTimer();
 }
 private async Task RescanAllAsync()
 {
  if(Roots.Count==0){Status="No roots to rescan — add one first.";return;}
  IsBusy=true;Status="Rescanning roots…";
  try
  {
   var removed=0;foreach(var root in Roots.ToArray()){var scanned=await _scanner.ScanAsync(root);var paths=scanned.Select(p=>p.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);var missing=_allProjects.Where(p=>p.RootId==root.Id&&!paths.Contains(p.Path)).Select(p=>p.Id).ToArray();await _repository.UpsertProjectsAsync(scanned);await _repository.DeleteProjectsAsync(missing);removed+=missing.Length;}
   var current=SelectedProject?.Id;
   _allProjects.Clear();_allProjects.AddRange(await _repository.GetProjectsAsync());
   RebuildTree();Refresh();
   SelectedProject=Projects.FirstOrDefault(p=>p.Id==current)??Projects.FirstOrDefault();
   Status=removed==0?$"Rescanned {_allProjects.Count} projects":$"Rescanned {_allProjects.Count} projects; removed {removed} missing project{(removed==1?"":"s")}";
   _=ClassifyUncategorizedAsync();_=RefreshGitAsync();_=LoadDocsAsync();
  }
  catch(Exception e){Status=e.Message;}
  finally{IsBusy=false;}
 }
 public async Task LoadAsync(){_groqApiKey=await _repository.GetSettingAsync("GroqApiKey")??"";Raise(nameof(GroqApiKey));Raise(nameof(UsingEnvGroqKey));
  _defaultShell=await _repository.GetSettingAsync("DefaultShell")??_defaultShell;Raise(nameof(DefaultShell));
  _viewMode=await _repository.GetSettingAsync("ViewMode")??_viewMode;Raise(nameof(ViewMode));
  _sortMode=await _repository.GetSettingAsync("SortMode")??_sortMode;Raise(nameof(SortMode));
  _currentPage=await _repository.GetSettingAsync("LastPage")??_currentPage;Raise(nameof(CurrentPage));Raise(nameof(IsLibraryPage));Raise(nameof(ProjectPageTitle));Raise(nameof(ActiveSearch));Raise(nameof(SearchPlaceholder));
  _autoClassify=await _repository.GetSettingAsync("AutoClassify")!="0";Raise(nameof(AutoClassify));
  _startMinimized=await _repository.GetSettingAsync("StartMinimized")=="1";Raise(nameof(StartMinimized));
  _minimizeToTray=await _repository.GetSettingAsync("MinimizeToTray")!="0";Raise(nameof(MinimizeToTray));
  _closeToTray=await _repository.GetSettingAsync("CloseToTray")=="1";Raise(nameof(CloseToTray));
  _sidebarCollapsed=await _repository.GetSettingAsync("SidebarCollapsed")=="1";Raise(nameof(SidebarCollapsed));
  _detailsCollapsed=await _repository.GetSettingAsync("DetailsCollapsed")=="1";Raise(nameof(DetailsCollapsed));
  _ideCommand=await _repository.GetSettingAsync("IdeCommand")??"";Raise(nameof(IdeCommand));
  _density=await _repository.GetSettingAsync("Density")??_density;Raise(nameof(Density));RaiseCardMetrics();
  _scrollback=await _repository.GetSettingAsync("Scrollback")??_scrollback;Raise(nameof(Scrollback));ApplyScrollback();
  _splitColumns=int.TryParse(await _repository.GetSettingAsync("TerminalSplitColumns"),out var splitColumns)&&splitColumns is >=0 and <=3?splitColumns:0;Raise(nameof(SplitColumns));
  _terminalScrollable=await _repository.GetSettingAsync("TerminalScrollable")!="0";Raise(nameof(TerminalScrollable));
  _terminalHorizontalScrollable=await _repository.GetSettingAsync("TerminalHorizontalScrollable")=="1";Raise(nameof(TerminalHorizontalScrollable));
  // The registry is the source of truth for this one — the user may have removed the entry from Task
  // Manager's Startup tab since we last wrote it, and the checkbox should reflect what is real.
  _launchAtStartup=_startup.IsEnabled;Raise(nameof(LaunchAtStartup));
  Converters.PinStore.Ids.Clear();foreach(var id in (await _repository.GetSettingAsync("Pinned")??"").Split(',',StringSplitOptions.RemoveEmptyEntries))if(Guid.TryParse(id,out var g))Converters.PinStore.Ids.Add(g);
  await LoadProjectStateAsync("HiddenProjects",_hiddenProjects);await LoadProjectStateAsync("ArchivedProjects",_archivedProjects);
  Roots.Clear();foreach(var root in await _repository.GetRootsAsync())Roots.Add(root);_allProjects.Clear();_allProjects.AddRange(await _repository.GetProjectsAsync());await BackfillCategoriesAsync();RebuildTree();Refresh();SelectedProject=Projects.FirstOrDefault();await LoadDocsPreferencesAsync();_=ClassifyUncategorizedAsync();_=RefreshGitAsync();}
 // Projects indexed before categories existed carry a null category; fill in what the offline path
 // heuristic can decide so filters are useful immediately, without waiting on a rescan or the LLM.
 private async Task BackfillCategoriesAsync(){for(var i=0;i<_allProjects.Count;i++){var p=_allProjects[i];if(!string.IsNullOrWhiteSpace(p.Category))continue;var category=ProjectCategoryRules.FromPath(p.Path);if(category is null)continue;_allProjects[i]=p with{Category=category};await _repository.SetProjectCategoryAsync(p.Id,category);}}
 private Task SaveGroqKeyAsync()=>QueueWriteAsync(()=>_repository.SetSettingAsync("GroqApiKey",_groqApiKey.Trim()));
 private async Task ClassifyUncategorizedAsync()
 {
  if(!IsUiActive){_classificationPending=true;return;}
  if(!_autoClassify)return;
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
 private IEnumerable<FolderNode> AllFolders()=>FlattenFolders(FolderTree);
 private static IEnumerable<FolderNode> FlattenFolders(IEnumerable<FolderNode> folders)=>folders.SelectMany(folder=>new[]{folder}.Concat(FlattenFolders(folder.Children)));
 private void ToggleFolderExpansion(){var folders=AllFolders().ToArray();if(folders.Length==0)return;var expand=folders.All(folder=>!folder.IsExpanded);foreach(var folder in folders)folder.IsExpanded=expand;Raise(nameof(AreAllFoldersCollapsed));Raise(nameof(FolderExpandToggleToolTip));}
 private void HideFolderProjects(FolderNode? folder){if(folder is null)return;var projectIds=_allProjects.Where(project=>IsWithinFolder(project.Path,folder.Path)).Select(project=>project.Id).ToArray();if(projectIds.Length==0)return;var restore=folder.IsHidden;foreach(var id in projectIds){if(restore)_hiddenProjects.Remove(id);else _hiddenProjects.Add(id);}UpdateFolderHiddenStates();SavePref("HiddenProjects",string.Join(',',_hiddenProjects));Refresh();if(!restore&&_selectedProject is not null&&_hiddenProjects.Contains(_selectedProject.Id))SelectedProject=Projects.FirstOrDefault();Status=restore?$"Shown {projectIds.Length} project{(projectIds.Length==1?"":"s")} in {folder.Name}":$"Hidden {projectIds.Length} project{(projectIds.Length==1?"":"s")} in {folder.Name}";}
 private static bool IsWithinFolder(string projectPath,string folderPath){var relative=Path.GetRelativePath(Path.GetFullPath(folderPath),Path.GetFullPath(projectPath));return relative=="."||(!relative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal)&&!Path.IsPathRooted(relative));}
 private void OnFolderExpansionChanged(object? sender,EventArgs e){Raise(nameof(AreAllFoldersCollapsed));Raise(nameof(FolderExpandToggleToolTip));}
 private void UpdateFolderHiddenStates(){foreach(var folder in AllFolders()){var projects=_allProjects.Where(project=>IsWithinFolder(project.Path,folder.Path)).ToArray();folder.IsHidden=projects.Length>0&&projects.All(project=>_hiddenProjects.Contains(project.Id));}}
 private void RebuildTree(){FolderTree.Clear();foreach(var node in FolderNode.Build(Roots,_allProjects))FolderTree.Add(node);UpdateFolderHiddenStates();foreach(var folder in AllFolders())folder.ExpansionChanged+=OnFolderExpansionChanged;Raise(nameof(AreAllFoldersCollapsed));Raise(nameof(FolderExpandToggleToolTip));if(_selectedFolder is not null&&_allProjects.All(p=>!p.Path.StartsWith(_selectedFolder.Path,StringComparison.OrdinalIgnoreCase)))SelectedFolder=null;}
 private Task AddRootAsync(){var path=_picker.PickFolder();return string.IsNullOrWhiteSpace(path)?Task.CompletedTask:AddRootPathAsync(path);}
 public async Task AddRootPathAsync(string path){
  if(string.IsNullOrWhiteSpace(path)||!Directory.Exists(path)){Status="That folder doesn't exist.";return;}
  var full=Path.GetFullPath(path);
  if(Roots.Any(r=>r.Path.Equals(full,StringComparison.OrdinalIgnoreCase))){Status="That folder is already a root.";return;}
  IsBusy=true;Status="Scanning root…";
  try{var root=new RootFolder(Guid.NewGuid(),full,DateTimeOffset.UtcNow);await _repository.AddRootAsync(root);Roots.Add(root);var scanned=await _scanner.ScanAsync(root);await _repository.UpsertProjectsAsync(scanned);_allProjects.Clear();_allProjects.AddRange(await _repository.GetProjectsAsync());await BackfillCategoriesAsync();RebuildTree();Refresh();SelectedProject=Projects.FirstOrDefault();Status=$"Found {scanned.Count} projects";_=ClassifyUncategorizedAsync();_=RefreshGitAsync();}
  catch(Exception e){Status=e.Message;}finally{IsBusy=false;}}
 private async Task RemoveRootAsync(RootFolder? root){if(root is null)return;await _repository.RemoveRootAsync(root.Id);Roots.Remove(root);_allProjects.RemoveAll(p=>p.RootId==root.Id);RebuildTree();Refresh();SelectedProject=Projects.FirstOrDefault();Status="Root removed from Puppeteer; project files were not changed.";}
 private async Task DeleteProjectAsync(Project? project){if(project is null)return;if(Roots.Any(root=>string.Equals(Path.GetFullPath(root.Path).TrimEnd(Path.DirectorySeparatorChar),Path.GetFullPath(project.Path).TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase))){Status="A root folder cannot be deleted as a project.";return;}if(!await ConfirmAsync("Delete project?",$"Permanently delete the project folder?\n\n{project.Path}\n\nThis cannot be undone.","Delete","Cancel",danger:true))return;try{foreach(var session in Sessions.Where(s=>s.Session?.ProjectPath.Equals(project.Path,StringComparison.OrdinalIgnoreCase)==true).ToArray())CloseSession(session);if(Directory.Exists(project.Path))Directory.Delete(project.Path,true);await _repository.ForgetProjectsAsync([project.Id]);_allProjects.RemoveAll(p=>p.Id==project.Id);_hiddenProjects.Remove(project.Id);_archivedProjects.Remove(project.Id);RebuildTree();Refresh();SelectedProject=Projects.FirstOrDefault();Status=$"Deleted {project.Name} and its project files.";}catch(Exception e){Status=$"Couldn't delete {project.Name}: {e.Message}";}}
 private void OpenFolder(Project? project){if(project is null)return;if(Directory.Exists(project.Path)){_launcher.OpenFolder(project.Path);MarkOpened(project);Status=$"Opened {project.Name} in Explorer";}else Status=$"{project.Name} no longer exists on disk.";}
 private void OpenIde(Project? project){if(project is null)return;if(Directory.Exists(project.Path)){try{_launcher.OpenInIde(project.Path,_ideCommand);MarkOpened(project);Status=$"Opening {project.Name} in your IDE";}catch(Exception e){Status=$"Couldn't open the editor: {e.Message}";}}else Status=$"{project.Name} no longer exists on disk.";}
 private void MarkOpened(Project project){var openedAt=DateTimeOffset.UtcNow;var i=_allProjects.FindIndex(p=>p.Id==project.Id);if(i>=0)_allProjects[i]=_allProjects[i] with{LastOpenedAt=openedAt};_=QueueWriteAsync(()=>_repository.SetProjectOpenedAsync(project.Id,openedAt));}
 private void CopyPath(Project? project){if(project is null)return;try{Clipboard.SetText(project.Path);Status=$"Copied path: {project.Path}";}catch{Status="Couldn't access the clipboard";}}
 private void CopyRepository(Project? project){var remote=project?.Git?.RemoteUrl;if(string.IsNullOrWhiteSpace(remote)){Status="No repository remote is configured for this project.";return;}try{Clipboard.SetText(remote);Status="Copied repository remote";}catch{Status="Couldn't access the clipboard";}}
 private void OpenRepository(Project? project){var remote=project?.Git?.RemoteUrl;if(string.IsNullOrWhiteSpace(remote)){Status="No repository remote is configured for this project.";return;}var url=RepositoryWebUrl(remote);if(url is null){Status="This repository remote doesn't have a web URL.";return;}try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url){UseShellExecute=true});Status="Opened repository in your browser";}catch{Status="Couldn't open the repository URL.";}}
 private static string? RepositoryWebUrl(string remote){var value=remote.Trim();if(value.StartsWith("git@",StringComparison.OrdinalIgnoreCase)){var colon=value.IndexOf(':');if(colon>0)value="https://"+value[4..colon]+"/"+value[(colon+1)..];}else if(value.StartsWith("ssh://git@",StringComparison.OrdinalIgnoreCase))value="https://"+value[10..];if(!value.StartsWith("https://",StringComparison.OrdinalIgnoreCase)&&!value.StartsWith("http://",StringComparison.OrdinalIgnoreCase))return null;return value.EndsWith(".git",StringComparison.OrdinalIgnoreCase)?value[..^4]:value;}
 private void CopyOutput(){if(SelectedSession is null)return;try{SelectedSession.FlushOutput();Clipboard.SetText(string.Join(Environment.NewLine,SelectedSession.Output));Status="Copied terminal output";}catch{Status="Couldn't access the clipboard";}}
 private void TogglePin(Project? project){if(project is null)return;var ids=Converters.PinStore.Ids;if(!ids.Add(project.Id))ids.Remove(project.Id);SavePref("Pinned",string.Join(',',ids));Refresh();Raise(nameof(SelectedProject));Status=ids.Contains(project.Id)?$"Pinned {project.Name}":$"Unpinned {project.Name}";}
 private async Task LoadProjectStateAsync(string key,HashSet<Guid> target){target.Clear();foreach(var id in (await _repository.GetSettingAsync(key)??"").Split(',',StringSplitOptions.RemoveEmptyEntries))if(Guid.TryParse(id,out var parsed))target.Add(parsed);}
 private void ToggleProjectState(Project? project,HashSet<Guid> state,string addedVerb,string removedVerb){if(project is null)return;var added=state.Add(project.Id);if(!added)state.Remove(project.Id);var key=ReferenceEquals(state,_hiddenProjects)?"HiddenProjects":"ArchivedProjects";SavePref(key,string.Join(',',state));Refresh();SelectedProject=Projects.FirstOrDefault();Status=$"{(added?addedVerb:removedVerb)} {project.Name}";}
 private void NewSession(){if(SelectedProject is null){Status="Select a project first";return;}_=OpenTerminalAsync(SelectedProject);}
 private void CloseSession(TerminalSessionViewModel? session){if(session is null||!Sessions.Contains(session))return;_closedTerminals.Add(session);if(_closedTerminals.Count>10)_closedTerminals.RemoveAt(0);try{session.Session?.StopAsync();}catch{}var name=session.Name;var wasSelected=_selectedSession==session;_terminalHistory.Remove(session);Sessions.Remove(session);if(wasSelected){var next=_terminalHistory.FirstOrDefault(s=>Sessions.Contains(s));if(next is not null)_terminalHistory.Remove(next);SelectedSession=next??Sessions.LastOrDefault();}if(Sessions.Count==0)TerminalOpen=false;Status=$"Closed session {name}";}
 private async Task OpenTerminalAsync(Project? project,string? command=null,string? name=null,TerminalSessionViewModel? restore=null,int? insertAt=null){if(project is null)return;if(!Directory.Exists(project.Path)){Status="Project folder no longer exists on disk.";return;}try{var session=await _terminalService.CreateAsync(new(project.Path,restore?.Session?.Shell??DefaultShell,command,name??project.Name));MarkOpened(project);var vm=new TerminalSessionViewModel(session,project);if(restore is not null)vm.RestoreFrom(restore);vm.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(TerminalSessionViewModel.Running)){Raise(nameof(RunningCount));Raise(nameof(RunningBadge));Refresh();}};if(insertAt is {} at&&at>=0&&at<=Sessions.Count)Sessions.Insert(at,vm);else Sessions.Add(vm);SelectedSession=vm;TerminalOpen=true;Status=command is null?$"Terminal started in {project.Name}":$"Running “{command}” in {project.Name}";}catch(Exception e){Status=e.Message;}}
 private void RunPreset(CommandPreset? preset){if(preset is null||SelectedProject is null)return;_=OpenTerminalAsync(SelectedProject,preset.Command,preset.Name);}
 private void RestartSession(TerminalSessionViewModel? session){if(session?.Session is null)return;var project=_allProjects.FirstOrDefault(p=>p.Path.Equals(session.Session.ProjectPath,StringComparison.OrdinalIgnoreCase));if(project is null){Status="Can't restart — project not found.";return;}var command=session.Session.Command is {Length:>0} c?c:null;var index=Sessions.IndexOf(session);CloseSession(session);_=OpenTerminalAsync(project,command,session.Name,insertAt:index);}
 private async Task FindIconsAsync(){if(SelectedProject is null)return;var project=SelectedProject;IconCandidates.Clear();try{foreach(var icon in await _icons.FindAsync(project.Path))IconCandidates.Add(icon);Status=$"Found {IconCandidates.Count} icon candidates";}catch(Exception e){Status=$"Couldn't find icons for {project.Name}: {e.Message}";}}
 private Task ChangeIconAsync()=>SetIconAsync(_picker.PickIcon());
 private async Task SetIconAsync(string? path){if(SelectedProject is null||path is "")return;await _repository.SetProjectIconAsync(SelectedProject.Id,path);var updated=SelectedProject with{CustomIconPath=path};var index=_allProjects.FindIndex(p=>p.Id==updated.Id);if(index>=0)_allProjects[index]=updated;Refresh();SelectedProject=Projects.FirstOrDefault(p=>p.Id==updated.Id);Status=path is null?"Framework icon restored":"Project icon updated";}
 private bool _refreshing;
 private void Refresh()
 {
  // Rebuilding a bound ComboBox's items nulls its SelectedItem, which writes back and re-enters
  // Refresh; guard against that and only rebuild each option list when it actually changed.
  if(!IsUiActive){_refreshPending=true;return;}
  if(_refreshing)return;
  _refreshing=true;
  try
  {
   _selectedType=Sync(Types,_searchService.BuildTypes(_allProjects),_selectedType);Raise(nameof(SelectedType));
   _selectedTechnology=Sync(TechnologyOptions,_searchService.BuildTechnologies(_allProjects),_selectedTechnology);Raise(nameof(SelectedTechnology));
   _selectedCategory=Sync(CategoryOptions,_searchService.BuildCategories(_allProjects),_selectedCategory);Raise(nameof(SelectedCategory));
   var byPath=SessionsByProjectPath();
   var running=_allProjects.Where(p=>byPath.TryGetValue(p.Path,out var s)&&s.Any(x=>x.Running)).Select(p=>p.Id).ToHashSet();
   IEnumerable<Project> visible=_currentPage switch{"Archived"=>_allProjects.Where(p=>_archivedProjects.Contains(p.Id)&&!_hiddenProjects.Contains(p.Id)),"Hidden"=>_allProjects.Where(p=>_hiddenProjects.Contains(p.Id)),_=>_allProjects.Where(p=>!_archivedProjects.Contains(p.Id)&&!_hiddenProjects.Contains(p.Id))};
   var scoped=_selectedFolder is null?visible:visible.Where(p=>p.Path.StartsWith(_selectedFolder.Path,StringComparison.OrdinalIgnoreCase));
   var filtered=_searchService.Filter(scoped,Search,_selectedType,_selectedTechnology,_selectedCategory,running).Where(p=>_selectedSpecial switch{
    "Favorites"=>Converters.PinStore.Ids.Contains(p.Id),
    "Git repositories"=>p.Git is not null,
    "No Git repository"=>p.Git is null,
    "Git changes"=>p.HasGitChanges,
    "Custom icon"=>!string.IsNullOrWhiteSpace(p.CustomIconPath),
    "Running"=>running.Contains(p.Id),
    "Uncategorized"=>string.IsNullOrWhiteSpace(p.Category),
    "Never opened"=>p.LastOpenedAt is null,
    _=>true});
   var sorted=_sortMode switch{
    "Recent"=>filtered.OrderByDescending(p=>p.LastOpenedAt??DateTimeOffset.MinValue).ThenBy(p=>p.Name,StringComparer.OrdinalIgnoreCase),
    "Type"=>filtered.OrderBy(ProjectTypeRules.Of,StringComparer.OrdinalIgnoreCase).ThenBy(p=>p.Name,StringComparer.OrdinalIgnoreCase),
    _=>filtered.OrderBy(p=>p.Name,StringComparer.OrdinalIgnoreCase).AsEnumerable()};
   var ordered=sorted.OrderByDescending(p=>Converters.PinStore.Ids.Contains(p.Id));
   // Live session state lives in the session view models, not in the stored project rows, so graft it
   // on here — it is what drives the running dot on a card and the Sessions block in the inspector.
   var previousId=_selectedProject?.Id;

   Projects.ReplaceAll(ordered.Select(p=>byPath.TryGetValue(p.Path,out var sessions)?p with{Sessions=sessions}:p));
   // Clearing the list makes the ListBox write a null selection back; restore it quietly so typing in
   // the search box doesn't blank the inspector on every keystroke.
   _selectedProject=previousId is Guid id?Projects.FirstOrDefault(p=>p.Id==id):null;
   Raise(nameof(SelectedProject));
   Raise(nameof(HasAnyProjects));Raise(nameof(TotalProjects));Raise(nameof(LibrarySummary));
  }
  finally{_refreshing=false;}
  RebuildDocEntries();
 }
 private Dictionary<string,IReadOnlyList<ProjectSession>> SessionsByProjectPath()=>
  Sessions.Where(s=>s.Session is not null)
   .GroupBy(s=>s.Session!.ProjectPath,StringComparer.OrdinalIgnoreCase)
   .ToDictionary(g=>g.Key,g=>(IReadOnlyList<ProjectSession>)g.Select(s=>new ProjectSession(s.Name,s.Command,s.Duration,s.Running)).ToArray(),StringComparer.OrdinalIgnoreCase);
 private static string Sync(ObservableCollection<string> target,IReadOnlyList<string> values,string keep)
 {
  if(!target.SequenceEqual(values,StringComparer.OrdinalIgnoreCase)){target.Clear();foreach(var v in values)target.Add(v);}
  return values.Contains(keep,StringComparer.OrdinalIgnoreCase)?keep:"All";
 }
}
