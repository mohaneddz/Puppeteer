using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Puppeteer.App.Services;
using Puppeteer.Core;
namespace Puppeteer.App.ViewModels;
public sealed class MainViewModel:ObservableObject
{
 private readonly IProjectRepository _repository; private readonly IProjectScanner _scanner; private readonly ProjectSearchService _searchService; private readonly ITerminalService _terminalService; private readonly IProjectLauncher _launcher; private readonly IIconDiscoveryService _icons; private readonly IFilePicker _picker; private readonly IProjectClassifier _classifier; private readonly AppConfig _config; private readonly IGitMetadataService _git; private readonly IStartupService _startup;
 private readonly List<Project> _allProjects=[]; private string _search=""; private string _selectedType="All"; private string _selectedTechnology="All"; private string _selectedCategory="All"; private string _currentPage="Projects"; private Project? _selectedProject; private TerminalSessionViewModel? _selectedSession; private bool _terminalOpen; private bool _isBusy; private string _status="Ready"; private string _defaultShell="powershell.exe";
 public ObservableCollection<Project> Projects{get;}=[]; public ObservableCollection<string> Types{get;}=[]; public ObservableCollection<string> TechnologyOptions{get;}=[]; public ObservableCollection<string> CategoryOptions{get;}=[]; public ObservableCollection<RootFolder> Roots{get;}=[]; public ObservableCollection<TerminalSessionViewModel> Sessions{get;}=[]; public ObservableCollection<IconCandidate> IconCandidates{get;}=[];
 public string Search{get=>_search;set{if(Set(ref _search,value))QueueSearchRefresh();}} public string SelectedType{get=>_selectedType;set{if(Set(ref _selectedType,value))Refresh();}} public string SelectedTechnology{get=>_selectedTechnology;set{if(Set(ref _selectedTechnology,value))Refresh();}} public string SelectedCategory{get=>_selectedCategory;set{if(Set(ref _selectedCategory,value))Refresh();}} public string CurrentPage{get=>_currentPage;set{if(Set(ref _currentPage,value))SavePref("LastPage",value);}} public Project? SelectedProject{get=>_selectedProject;set{if(_refreshing)return;if(!Set(ref _selectedProject,value))return;IconCandidates.Clear();Raise(nameof(HasMoreGitFiles));Raise(nameof(MoreGitFileCount));_=LoadGitForAsync(value);}}
 /// <summary>Reads git status for every project off the UI thread, a few at a time, and folds the
 /// results back in one pass. Scanning used to do this inline — a child process per project, in
 /// series — which made adding a root feel frozen and left the status stale from then on.</summary>
 public async Task RefreshGitAsync()
 {
  var targets=_allProjects.Where(p=>Directory.Exists(p.Path)).ToArray();
  if(targets.Length==0)return;
  using var gate=new SemaphoreSlim(Math.Max(2,Environment.ProcessorCount/2));
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
 }
 private async Task LoadGitForAsync(Project? project){if(project is null||project.Git is not null||!Directory.Exists(project.Path))return;GitStatus? status;try{status=await _git.GetStatusAsync(project.Path);}catch{return;}if(status is null)return;var i=_allProjects.FindIndex(p=>p.Id==project.Id);if(i>=0)_allProjects[i]=_allProjects[i] with{Git=status};if(_selectedProject?.Id==project.Id){_selectedProject=_selectedProject with{Git=status};Raise(nameof(SelectedProject));Raise(nameof(HasMoreGitFiles));Raise(nameof(MoreGitFileCount));}}
 public bool HasMoreGitFiles=>_selectedProject?.Git is {Files: not null} g&&g.ModifiedFileCount>g.Files.Count;
 public int MoreGitFileCount=>_selectedProject?.Git is {Files: not null} g?Math.Max(0,g.ModifiedFileCount-g.Files.Count):0; public TerminalSessionViewModel? SelectedSession{get=>_selectedSession;set{if(Set(ref _selectedSession,value)&&value is not null)TerminalOpen=true;}} public bool TerminalOpen{get=>_terminalOpen;set=>Set(ref _terminalOpen,value);} public bool IsBusy{get=>_isBusy;set=>Set(ref _isBusy,value);} public string Status{get=>_status;set{if(Set(ref _status,value))ShowToast();}} public string DefaultShell{get=>_defaultShell;set{if(Set(ref _defaultShell,value))SavePref("DefaultShell",value);}}
 private string _viewMode="Grid"; public string ViewMode{get=>_viewMode;set{if(Set(ref _viewMode,value))SavePref("ViewMode",value);}}
 public IReadOnlyList<string> SortModes{get;}=["Name","Recent","Type"]; private string _sortMode="Name"; public string SortMode{get=>_sortMode;set{if(Set(ref _sortMode,value)){SavePref("SortMode",value);Refresh();}}}
 public void SavePref(string key,string? value)=>_=_repository.SetSettingAsync(key,value);
 public Task SavePrefsAsync(IReadOnlyDictionary<string,string?> values)=>_repository.SetSettingsAsync(values);
 public Task<string?> GetPrefAsync(string key)=>_repository.GetSettingAsync(key);
 private bool _statusVisible; public bool StatusVisible{get=>_statusVisible;set=>Set(ref _statusVisible,value);} private DispatcherTimer? _toast;
 private void ShowToast(){StatusVisible=true;(_toast??=CreateToast()).Stop();_toast.Start();}
 private DispatcherTimer CreateToast(){var t=new DispatcherTimer{Interval=TimeSpan.FromSeconds(3.2)};t.Tick+=(_,__)=>{StatusVisible=false;t.Stop();};return t;}
 public IReadOnlyList<string> Shells{get;}=["powershell.exe","cmd.exe","pwsh.exe","wsl.exe"];
 public string Version=>"v"+(System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(2)??"1.0");
 public int RunningCount=>Sessions.Count(s=>s.Running); public string RunningBadge=>RunningCount>0?RunningCount.ToString():"";
 public RelayCommand NavigateCommand{get;} public AsyncRelayCommand AddRootCommand{get;} public AsyncRelayCommand RemoveRootCommand{get;} public AsyncRelayCommand OpenTerminalCommand{get;} public RelayCommand OpenFolderCommand{get;} public RelayCommand OpenIdeCommand{get;} public RelayCommand CollapseTerminalCommand{get;} public AsyncRelayCommand FindIconCommand{get;} public AsyncRelayCommand ChangeIconCommand{get;} public AsyncRelayCommand ResetIconCommand{get;} public AsyncRelayCommand ChooseCandidateCommand{get;}
 public RelayCommand CopyPathCommand{get;} public RelayCommand SetViewCommand{get;} public RelayCommand NewSessionCommand{get;} public RelayCommand CloseSessionCommand{get;} public RelayCommand ClearOutputCommand{get;} public RelayCommand RunPresetCommand{get;} public RelayCommand RestartSessionCommand{get;} public RelayCommand StopAllCommand{get;} public RelayCommand CopyOutputCommand{get;} public RelayCommand TogglePinCommand{get;}
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
 private bool _confirmExitWithSessions=true; public bool ConfirmExitWithSessions{get=>_confirmExitWithSessions;set{if(Set(ref _confirmExitWithSessions,value))SavePref("ConfirmExitWithSessions",value?"1":"0");}}
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
 public RelayCommand OpenDataFolderCommand{get;}
 public static string DataFolder=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Puppeteer");
 public RelayCommand ResetLayoutCommand{get;}
 private string _groqApiKey=""; public string GroqApiKey{get=>_groqApiKey;set{if(Set(ref _groqApiKey,value)){_=SaveGroqKeyAsync();Raise(nameof(UsingEnvGroqKey));}}}
 private bool _showGroqApiKey; public bool ShowGroqApiKey{get=>_showGroqApiKey;set=>Set(ref _showGroqApiKey,value);}
 public RelayCommand ToggleGroqKeyVisibilityCommand{get;}
 public bool UsingEnvGroqKey=>string.IsNullOrWhiteSpace(_groqApiKey)&&!string.IsNullOrWhiteSpace(_config.GroqApiKeyFromEnv);
 public RelayCommand SelectFolderCommand{get;} public RelayCommand ClearFolderCommand{get;} public RelayCommand ToggleTerminalMaxCommand{get;} public RelayCommand ToggleSplitCommand{get;} public RelayCommand ToggleSidebarCommand{get;} public RelayCommand ToggleDetailsCommand{get;} public RelayCommand ToggleTerminalCommand{get;} public AsyncRelayCommand RescanCommand{get;}
 public ObservableCollection<FolderNode> FolderTree{get;}=[];
 private FolderNode? _selectedFolder; public FolderNode? SelectedFolder{get=>_selectedFolder;set{if(Set(ref _selectedFolder,value)){Raise(nameof(FolderFilterActive));Refresh();}}}
 public bool FolderFilterActive=>_selectedFolder is not null;
 public bool HasAnyProjects=>_allProjects.Count>0;
 public int TotalProjects=>_allProjects.Count;
 public string LibrarySummary=>$"{_allProjects.Count} project{(_allProjects.Count==1?"":"s")} in {Roots.Count} root{(Roots.Count==1?"":"s")}";
 public RelayCommand ClearFiltersCommand{get;}
 private bool _terminalMaximized; public bool TerminalMaximized{get=>_terminalMaximized;set=>Set(ref _terminalMaximized,value);}
 private bool _terminalSplit; public bool TerminalSplit{get=>_terminalSplit;set=>Set(ref _terminalSplit,value);}
 private bool _sidebarCollapsed; public bool SidebarCollapsed{get=>_sidebarCollapsed;set=>Set(ref _sidebarCollapsed,value);}
 private bool _detailsCollapsed; public bool DetailsCollapsed{get=>_detailsCollapsed;set=>Set(ref _detailsCollapsed,value);}
 public MainViewModel(IProjectRepository repository,IProjectScanner scanner,ProjectSearchService searchService,ITerminalService terminalService,IProjectLauncher launcher,IIconDiscoveryService icons,IFilePicker picker,IProjectClassifier classifier,AppConfig config,IGitMetadataService git,IStartupService startup)
 {
  _repository=repository;_scanner=scanner;_searchService=searchService;_terminalService=terminalService;_launcher=launcher;_icons=icons;_picker=picker;_classifier=classifier;_config=config;_git=git;_startup=startup;
  Sessions.CollectionChanged+=(_,__)=>{Raise(nameof(RunningCount));Raise(nameof(RunningBadge));Refresh();};
  StartDurationTicker();
  NavigateCommand=new(p=>CurrentPage=p?.ToString()??"Projects");AddRootCommand=new(_=>AddRootAsync());RemoveRootCommand=new(p=>RemoveRootAsync(p as RootFolder),p=>p is RootFolder);OpenTerminalCommand=new(p=>OpenTerminalAsync(p as Project??SelectedProject),p=>(p as Project??SelectedProject) is not null);OpenFolderCommand=new(p=>OpenFolder(p as Project??SelectedProject),p=>(p as Project??SelectedProject) is not null);OpenIdeCommand=new(p=>OpenIde(p as Project??SelectedProject),p=>(p as Project??SelectedProject) is not null);CollapseTerminalCommand=new(_=>TerminalOpen=false);FindIconCommand=new(_=>FindIconsAsync(),_=>SelectedProject is not null);ChangeIconCommand=new(_=>ChangeIconAsync(),_=>SelectedProject is not null);ResetIconCommand=new(_=>SetIconAsync(null),_=>SelectedProject is not null);ChooseCandidateCommand=new(p=>SetIconAsync((p as IconCandidate)?.Path),p=>p is IconCandidate);
  CopyPathCommand=new(p=>CopyPath(p as Project??SelectedProject),p=>(p as Project??SelectedProject) is not null);SetViewCommand=new(p=>ViewMode=p?.ToString()??"Grid");NewSessionCommand=new(_=>NewSession(),_=>SelectedProject is not null);CloseSessionCommand=new(p=>CloseSession(p as TerminalSessionViewModel),p=>p is TerminalSessionViewModel);ClearOutputCommand=new(_=>{SelectedSession?.Output.Clear();Status="Terminal cleared";},_=>SelectedSession is not null);RunPresetCommand=new(p=>RunPreset(p as CommandPreset),p=>p is CommandPreset&&SelectedProject is not null);RestartSessionCommand=new(p=>RestartSession(p as TerminalSessionViewModel??SelectedSession),p=>(p as TerminalSessionViewModel??SelectedSession) is not null);StopAllCommand=new(_=>{foreach(var s in Sessions.ToArray())s.StopCommand.Execute(null);Status="Stopped all sessions";},_=>Sessions.Any(s=>s.Running));CopyOutputCommand=new(_=>CopyOutput(),_=>SelectedSession is not null);TogglePinCommand=new(p=>TogglePin(p as Project??SelectedProject),p=>(p as Project??SelectedProject) is not null);ResetLayoutCommand=new(_=>{_=_repository.SetSettingsAsync(new Dictionary<string,string?>{["WindowWidth"]=null,["WindowHeight"]=null,["SidebarWidth"]=null,["DetailsWidth"]=null,["TerminalHeight"]=null,["Maximized"]=null});Status="Window layout will reset next launch";});
  SelectFolderCommand=new(p=>SelectedFolder=p as FolderNode);ClearFolderCommand=new(_=>SelectedFolder=null);ToggleTerminalMaxCommand=new(_=>TerminalMaximized=!TerminalMaximized);ToggleSplitCommand=new(_=>TerminalSplit=!TerminalSplit,_=>Sessions.Count>0);ToggleSidebarCommand=new(_=>SidebarCollapsed=!SidebarCollapsed);ToggleDetailsCommand=new(_=>DetailsCollapsed=!DetailsCollapsed);ToggleTerminalCommand=new(_=>TerminalOpen=!TerminalOpen);ToggleGroqKeyVisibilityCommand=new(_=>ShowGroqApiKey=!ShowGroqApiKey);OpenDataFolderCommand=new(_=>{_launcher.OpenFolder(DataFolder);Status="Opened Puppeteer's data folder";});RescanCommand=new(_=>RescanAllAsync(),_=>Roots.Count>0&&!IsBusy);
  ClearFiltersCommand=new(_=>{_selectedFolder=null;Raise(nameof(SelectedFolder));Raise(nameof(FolderFilterActive));_selectedType="All";_selectedTechnology="All";_selectedCategory="All";_search="";Raise(nameof(Search));Refresh();});
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
  _durations=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
  _durations.Tick+=(_,__)=>{ foreach(var session in Sessions) session.TickDuration(); };
  _durations.Start();
 }
 private async Task RescanAllAsync()
 {
  if(Roots.Count==0){Status="No roots to rescan — add one first.";return;}
  IsBusy=true;Status="Rescanning roots…";
  try
  {
   foreach(var root in Roots.ToArray())await _repository.UpsertProjectsAsync(await _scanner.ScanAsync(root));
   var current=SelectedProject?.Id;
   _allProjects.Clear();_allProjects.AddRange(await _repository.GetProjectsAsync());
   RebuildTree();Refresh();
   SelectedProject=Projects.FirstOrDefault(p=>p.Id==current)??Projects.FirstOrDefault();
   Status=$"Rescanned {_allProjects.Count} projects";
   _=ClassifyUncategorizedAsync();_=RefreshGitAsync();
  }
  catch(Exception e){Status=e.Message;}
  finally{IsBusy=false;}
 }
 public async Task LoadAsync(){_groqApiKey=await _repository.GetSettingAsync("GroqApiKey")??"";Raise(nameof(GroqApiKey));Raise(nameof(UsingEnvGroqKey));
  _defaultShell=await _repository.GetSettingAsync("DefaultShell")??_defaultShell;Raise(nameof(DefaultShell));
  _viewMode=await _repository.GetSettingAsync("ViewMode")??_viewMode;Raise(nameof(ViewMode));
  _sortMode=await _repository.GetSettingAsync("SortMode")??_sortMode;Raise(nameof(SortMode));
  _currentPage=await _repository.GetSettingAsync("LastPage")??_currentPage;Raise(nameof(CurrentPage));
  _autoClassify=await _repository.GetSettingAsync("AutoClassify")!="0";Raise(nameof(AutoClassify));
  _startMinimized=await _repository.GetSettingAsync("StartMinimized")=="1";Raise(nameof(StartMinimized));
  _minimizeToTray=await _repository.GetSettingAsync("MinimizeToTray")!="0";Raise(nameof(MinimizeToTray));
  _closeToTray=await _repository.GetSettingAsync("CloseToTray")=="1";Raise(nameof(CloseToTray));
  _confirmExitWithSessions=await _repository.GetSettingAsync("ConfirmExitWithSessions")!="0";Raise(nameof(ConfirmExitWithSessions));
  _ideCommand=await _repository.GetSettingAsync("IdeCommand")??"";Raise(nameof(IdeCommand));
  _density=await _repository.GetSettingAsync("Density")??_density;Raise(nameof(Density));RaiseCardMetrics();
  _scrollback=await _repository.GetSettingAsync("Scrollback")??_scrollback;Raise(nameof(Scrollback));ApplyScrollback();
  // The registry is the source of truth for this one — the user may have removed the entry from Task
  // Manager's Startup tab since we last wrote it, and the checkbox should reflect what is real.
  _launchAtStartup=_startup.IsEnabled;Raise(nameof(LaunchAtStartup));
  Converters.PinStore.Ids.Clear();foreach(var id in (await _repository.GetSettingAsync("Pinned")??"").Split(',',StringSplitOptions.RemoveEmptyEntries))if(Guid.TryParse(id,out var g))Converters.PinStore.Ids.Add(g);
  Roots.Clear();foreach(var root in await _repository.GetRootsAsync())Roots.Add(root);_allProjects.Clear();_allProjects.AddRange(await _repository.GetProjectsAsync());await BackfillCategoriesAsync();RebuildTree();Refresh();SelectedProject=Projects.FirstOrDefault();_=ClassifyUncategorizedAsync();_=RefreshGitAsync();}
 // Projects indexed before categories existed carry a null category; fill in what the offline path
 // heuristic can decide so filters are useful immediately, without waiting on a rescan or the LLM.
 private async Task BackfillCategoriesAsync(){for(var i=0;i<_allProjects.Count;i++){var p=_allProjects[i];if(!string.IsNullOrWhiteSpace(p.Category))continue;var category=ProjectCategoryRules.FromPath(p.Path);if(category is null)continue;_allProjects[i]=p with{Category=category};await _repository.SetProjectCategoryAsync(p.Id,category);}}
 private Task SaveGroqKeyAsync()=>_repository.SetSettingAsync("GroqApiKey",_groqApiKey.Trim());
 private async Task ClassifyUncategorizedAsync()
 {
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
 private void RebuildTree(){FolderTree.Clear();foreach(var node in FolderNode.Build(Roots,_allProjects))FolderTree.Add(node);if(_selectedFolder is not null&&_allProjects.All(p=>!p.Path.StartsWith(_selectedFolder.Path,StringComparison.OrdinalIgnoreCase)))SelectedFolder=null;}
 private Task AddRootAsync(){var path=_picker.PickFolder();return string.IsNullOrWhiteSpace(path)?Task.CompletedTask:AddRootPathAsync(path);}
 public async Task AddRootPathAsync(string path){
  if(string.IsNullOrWhiteSpace(path)||!Directory.Exists(path)){Status="That folder doesn't exist.";return;}
  var full=Path.GetFullPath(path);
  if(Roots.Any(r=>r.Path.Equals(full,StringComparison.OrdinalIgnoreCase))){Status="That folder is already a root.";return;}
  IsBusy=true;Status="Scanning root…";
  try{var root=new RootFolder(Guid.NewGuid(),full,DateTimeOffset.UtcNow);await _repository.AddRootAsync(root);Roots.Add(root);var scanned=await _scanner.ScanAsync(root);await _repository.UpsertProjectsAsync(scanned);_allProjects.RemoveAll(p=>p.RootId==root.Id);_allProjects.AddRange(scanned);await BackfillCategoriesAsync();RebuildTree();Refresh();SelectedProject=Projects.FirstOrDefault();Status=$"Found {scanned.Count} projects";_=ClassifyUncategorizedAsync();_=RefreshGitAsync();}
  catch(Exception e){Status=e.Message;}finally{IsBusy=false;}}
 private async Task RemoveRootAsync(RootFolder? root){if(root is null)return;await _repository.RemoveRootAsync(root.Id);Roots.Remove(root);_allProjects.RemoveAll(p=>p.RootId==root.Id);RebuildTree();Refresh();SelectedProject=Projects.FirstOrDefault();Status="Root removed from Puppeteer; project files were not changed.";}
 private void OpenFolder(Project? project){if(project is null)return;if(Directory.Exists(project.Path)){_launcher.OpenFolder(project.Path);MarkOpened(project);Status=$"Opened {project.Name} in Explorer";}else Status=$"{project.Name} no longer exists on disk.";}
 private void OpenIde(Project? project){if(project is null)return;if(Directory.Exists(project.Path)){_launcher.OpenInIde(project.Path,_ideCommand);MarkOpened(project);Status=$"Opening {project.Name} in your IDE";}else Status=$"{project.Name} no longer exists on disk.";}
 private void MarkOpened(Project project){var i=_allProjects.FindIndex(p=>p.Id==project.Id);if(i>=0)_allProjects[i]=_allProjects[i] with{LastOpenedAt=DateTimeOffset.UtcNow};_=_repository.SetProjectOpenedAsync(project.Id,DateTimeOffset.UtcNow);}
 private void CopyPath(Project? project){if(project is null)return;try{Clipboard.SetText(project.Path);Status=$"Copied path: {project.Path}";}catch{Status="Couldn't access the clipboard";}}
 private void CopyOutput(){if(SelectedSession is null)return;try{Clipboard.SetText(string.Join(Environment.NewLine,SelectedSession.Output));Status="Copied terminal output";}catch{Status="Couldn't access the clipboard";}}
 private void TogglePin(Project? project){if(project is null)return;var ids=Converters.PinStore.Ids;if(!ids.Add(project.Id))ids.Remove(project.Id);_=_repository.SetSettingAsync("Pinned",string.Join(',',ids));Refresh();Raise(nameof(SelectedProject));Status=ids.Contains(project.Id)?$"Pinned {project.Name}":$"Unpinned {project.Name}";}
 private void NewSession(){if(SelectedProject is null){Status="Select a project first";return;}_=OpenTerminalAsync(SelectedProject);}
 private void CloseSession(TerminalSessionViewModel? session){if(session is null||!Sessions.Contains(session))return;try{session.Session?.StopAsync();}catch{}var name=session.Name;Sessions.Remove(session);if(_selectedSession==session)SelectedSession=Sessions.FirstOrDefault();if(Sessions.Count==0)TerminalOpen=false;Status=$"Closed session {name}";}
 private async Task OpenTerminalAsync(Project? project,string? command=null,string? name=null){if(project is null)return;if(!Directory.Exists(project.Path)){Status="Project folder no longer exists on disk.";return;}try{var session=await _terminalService.CreateAsync(new(project.Path,DefaultShell,command,name??$"shell {Sessions.Count+1}"));MarkOpened(project);var vm=new TerminalSessionViewModel(session);vm.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(TerminalSessionViewModel.Running)){Raise(nameof(RunningCount));Raise(nameof(RunningBadge));Refresh();}};Sessions.Add(vm);SelectedSession=vm;TerminalOpen=true;Status=command is null?$"Terminal started in {project.Name}":$"Running “{command}” in {project.Name}";}catch(Exception e){Status=e.Message;}}
 private void RunPreset(CommandPreset? preset){if(preset is null||SelectedProject is null)return;_=OpenTerminalAsync(SelectedProject,preset.Command,preset.Name);}
 private void RestartSession(TerminalSessionViewModel? session){if(session?.Session is null)return;var project=_allProjects.FirstOrDefault(p=>p.Path.Equals(session.Session.ProjectPath,StringComparison.OrdinalIgnoreCase));if(project is null){Status="Can't restart — project not found.";return;}var command=session.Session.Command is {Length:>0} c?c:null;CloseSession(session);_=OpenTerminalAsync(project,command,session.Name);}
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
   var byPath=SessionsByProjectPath();
   var running=_allProjects.Where(p=>byPath.TryGetValue(p.Path,out var s)&&s.Any(x=>x.Running)).Select(p=>p.Id).ToHashSet();
   var scoped=_selectedFolder is null?_allProjects:_allProjects.Where(p=>p.Path.StartsWith(_selectedFolder.Path,StringComparison.OrdinalIgnoreCase));
   var filtered=_searchService.Filter(scoped,Search,_selectedType,_selectedTechnology,_selectedCategory,running);
   var sorted=_sortMode switch{
    "Recent"=>filtered.OrderByDescending(p=>p.LastOpenedAt??DateTimeOffset.MinValue).ThenBy(p=>p.Name,StringComparer.OrdinalIgnoreCase),
    "Type"=>filtered.OrderBy(ProjectTypeRules.Of,StringComparer.OrdinalIgnoreCase).ThenBy(p=>p.Name,StringComparer.OrdinalIgnoreCase),
    _=>filtered.OrderBy(p=>p.Name,StringComparer.OrdinalIgnoreCase).AsEnumerable()};
   var ordered=sorted.OrderByDescending(p=>Converters.PinStore.Ids.Contains(p.Id));
   // Live session state lives in the session view models, not in the stored project rows, so graft it
   // on here — it is what drives the running dot on a card and the Sessions block in the inspector.
   var previousId=_selectedProject?.Id;
   Projects.Clear();
   foreach(var p in ordered)Projects.Add(byPath.TryGetValue(p.Path,out var sessions)?p with{Sessions=sessions}:p);
   // Clearing the list makes the ListBox write a null selection back; restore it quietly so typing in
   // the search box doesn't blank the inspector on every keystroke.
   _selectedProject=previousId is Guid id?Projects.FirstOrDefault(p=>p.Id==id):null;
   Raise(nameof(SelectedProject));
   Raise(nameof(HasAnyProjects));Raise(nameof(TotalProjects));Raise(nameof(LibrarySummary));
  }
  finally{_refreshing=false;}
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
