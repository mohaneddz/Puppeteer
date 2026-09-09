namespace Puppeteer.App.ViewModels;

/// <summary>An in-app confirm dialog, so a yes/no question wears the app's own chrome instead of a
/// bare Windows message box. Callers <c>await ConfirmAsync(...)</c>; the overlay resolves the awaited
/// task when the user answers.</summary>
public sealed partial class MainViewModel
{
    private TaskCompletionSource<bool>? _dialogAnswer;

    private bool _dialogOpen;
    public bool DialogOpen { get => _dialogOpen; private set => Set(ref _dialogOpen, value); }

    private string _dialogTitle = "";
    public string DialogTitle { get => _dialogTitle; private set => Set(ref _dialogTitle, value); }

    private string _dialogMessage = "";
    public string DialogMessage { get => _dialogMessage; private set => Set(ref _dialogMessage, value); }

    private string _dialogConfirmText = "Confirm";
    public string DialogConfirmText { get => _dialogConfirmText; private set => Set(ref _dialogConfirmText, value); }

    private string _dialogCancelText = "Cancel";
    public string DialogCancelText { get => _dialogCancelText; private set => Set(ref _dialogCancelText, value); }

    /// <summary>A destructive action colours its confirm button as a warning rather than the accent.</summary>
    private bool _dialogDanger;
    public bool DialogDanger { get => _dialogDanger; private set => Set(ref _dialogDanger, value); }

    public RelayCommand DialogConfirmCommand { get; private set; } = null!;
    public RelayCommand DialogCancelCommand { get; private set; } = null!;

    private void InitializeDialog()
    {
        DialogConfirmCommand = new(_ => Answer(true));
        DialogCancelCommand = new(_ => Answer(false));
    }

    /// <summary>Puts the question up and returns once it is answered. A second call while one is open
    /// is declined rather than stacked — there is a single dialog surface.</summary>
    public Task<bool> ConfirmAsync(string title, string message, string confirmText = "Confirm", string cancelText = "Cancel", bool danger = false)
    {
        if (DialogOpen) return Task.FromResult(false);
        DialogTitle = title;
        DialogMessage = message;
        DialogConfirmText = confirmText;
        DialogCancelText = cancelText;
        DialogDanger = danger;
        _dialogAnswer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        DialogOpen = true;
        return _dialogAnswer.Task;
    }

    private void Answer(bool confirmed)
    {
        if (!DialogOpen) return;
        DialogOpen = false;
        var answer = _dialogAnswer;
        _dialogAnswer = null;
        answer?.TrySetResult(confirmed);
    }

    /// <summary>Cancels an open dialog from the outside — the backdrop click and the Escape key.</summary>
    public void CancelDialog() => Answer(false);
}
