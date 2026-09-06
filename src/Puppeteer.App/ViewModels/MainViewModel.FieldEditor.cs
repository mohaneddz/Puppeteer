namespace Puppeteer.App.ViewModels;

/// <summary>The expanded editor for a single state-doc field — opened from the small scope button next
/// to each label in <see cref="Controls.ProjectDocEditor"/>. It edits the same bound property the inline
/// box does, just with more room and a markdown preview, so nothing here needs its own save path.</summary>
public sealed partial class MainViewModel
{
    public static readonly IReadOnlyList<string> FieldEditorModes = ["Edit", "Preview"];

    private bool _fieldEditorOpen;
    public bool FieldEditorOpen { get => _fieldEditorOpen; private set => Set(ref _fieldEditorOpen, value); }

    private string _fieldEditorSection = "";
    public string FieldEditorLabel => _fieldEditorSection;

    private string _fieldEditorMode = "Edit";
    public string FieldEditorMode { get => _fieldEditorMode; set => Set(ref _fieldEditorMode, value); }

    public string FieldEditorText
    {
        get => GetDocField(_fieldEditorSection);
        set { SetDocField(_fieldEditorSection, value); Raise(nameof(FieldEditorText)); }
    }

    private bool _fieldGenerating;
    public bool FieldGenerating { get => _fieldGenerating; private set => Set(ref _fieldGenerating, value); }

    public RelayCommand OpenFieldEditorCommand { get; private set; } = null!;
    public RelayCommand CloseFieldEditorCommand { get; private set; } = null!;
    public RelayCommand SetFieldEditorModeCommand { get; private set; } = null!;
    public AsyncRelayCommand GenerateDocFieldCommand { get; private set; } = null!;

    private void InitializeFieldEditor()
    {
        OpenFieldEditorCommand = new(p => OpenFieldEditor(p?.ToString() ?? ""), p => p is string { Length: > 0 });
        CloseFieldEditorCommand = new(_ => FieldEditorOpen = false);
        SetFieldEditorModeCommand = new(p => { if (p?.ToString() is { Length: > 0 } mode) FieldEditorMode = mode; });
        GenerateDocFieldCommand = new(p => GenerateFieldAsync(p?.ToString() ?? ""), p => p is string { Length: > 0 } && SelectedProject is not null && !FieldGenerating);
    }

    private void OpenFieldEditor(string section)
    {
        if (section.Length == 0) return;
        _fieldEditorSection = section;
        FieldEditorMode = "Edit";
        Raise(nameof(FieldEditorLabel));
        Raise(nameof(FieldEditorText));
        FieldEditorOpen = true;
    }

    /// <summary>Has the model read the project's own README and file layout, rather than write from
    /// the project name alone — a guess dressed up as a state doc is worse than an empty field.</summary>
    private async Task GenerateFieldAsync(string section)
    {
        if (section.Length == 0 || SelectedProject is not { } project) return;
        var apiKey = string.IsNullOrWhiteSpace(_groqApiKey) ? _config.GroqApiKeyFromEnv : _groqApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)) { Status = "Add a Groq API key in Settings to generate doc text."; return; }

        FieldGenerating = true;
        try
        {
            var text = await _fieldGenerator.GenerateAsync(section, project.Name, project.Path, apiKey);
            if (string.IsNullOrWhiteSpace(text)) { Status = $"Couldn't generate {section}."; return; }
            SetDocField(section, text);
            if (_fieldEditorSection == section) Raise(nameof(FieldEditorText));
            Status = $"Generated {section} for {project.Name}";
        }
        catch (Exception e) { Status = $"Generation failed: {e.Message}"; }
        finally { FieldGenerating = false; }
    }
}
