using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using TraceZero.Models;
using TraceZero.Services;
using Velopack;

namespace TraceZero;

internal enum UpdateUiState
{
    MissingSource,
    InvalidSource,
    Ready,
    Checking,
    NotInstalled,
    UpToDate,
    UpdateAvailable,
    Downloading,
    Downloaded,
    Installing,
    Failed,
    NoPendingUpdate,
}

public partial class MainWindow : Window, INotifyPropertyChanged
{
    // NIST SP 800-88: one random pass defeats recovery on modern drives; more passes only cost time and SSD wear.
    private const int OverwritePassCount = 1;
    private readonly SecureDeleteService _secureDeleteService = new();
    private readonly FreeSpaceWipeService _freeSpaceWipeService = new();
    private readonly TraceCleanupService _traceCleanupService = new();
    private readonly AppUpdateService? _appUpdateService;
    private readonly string _configuredUpdateSource;
    private readonly UiTextProvider _ui = new();
    private CancellationTokenSource? _deletionCts;
    private string _summaryText = string.Empty;
    private string _statusText = string.Empty;
    private string _versionText = string.Empty;
    private string _updateStatusText = string.Empty;
    private string _updateSourceText = string.Empty;
    private Visibility _emptyQueueVisibility = Visibility.Visible;
    private double _overallProgress;
    private bool _isDeleting;
    private bool _isCheckingUpdates;
    private bool _startupUpdateCheckPending = true;
    private UpdateUiState _updateUiState;
    private string? _updateStateVersion;
    private string? _updateStateMessage;

    public MainWindow()
    {
        StartupDiagnostics.Log("MainWindow constructor entered.");

        Ui = _ui;
        Files = new ObservableCollection<FileDeletionItem>();
        LogEntries = new ObservableCollection<string>();

        _configuredUpdateSource = AppRuntimeDefaults.GetDefaultUpdateSource();
        _appUpdateService = TryResolveUpdateSource(_configuredUpdateSource, out var resolvedUpdateSource)
            ? new AppUpdateService(resolvedUpdateSource)
            : null;

        DataContext = this;
        InitializeComponent();
        StartupDiagnostics.Log("MainWindow InitializeComponent complete.");

        _ui.PropertyChanged += Ui_PropertyChanged;
        Files.CollectionChanged += Files_CollectionChanged;

        VersionText = $"v{GetDisplayVersion()}";
        InitializeUpdateState();
        ApplyLocalizedUi();
        RefreshSummary();

        Log(T("LogReady"));
        Log(T("LogSsd"));
        // Auto update is disabled; see Window_Loaded.
        // if (_appUpdateService is not null)
        // {
        //     Log(T("LogUpdateFeedConfigured", _appUpdateService.UpdateSource));
        // }

        Loaded += (_, _) => StartupDiagnostics.Log("MainWindow Loaded event fired.");
        ContentRendered += (_, _) => StartupDiagnostics.Log("MainWindow ContentRendered event fired.");
        SourceInitialized += (_, _) => StartupDiagnostics.Log("MainWindow SourceInitialized event fired.");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public UiTextProvider Ui { get; }

    public ObservableCollection<FileDeletionItem> Files { get; }

    public ObservableCollection<string> LogEntries { get; }

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value, nameof(SummaryText));
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value, nameof(StatusText));
    }

    public string VersionText
    {
        get => _versionText;
        private set => SetProperty(ref _versionText, value, nameof(VersionText));
    }

    public string UpdateStatusText
    {
        get => _updateStatusText;
        private set => SetProperty(ref _updateStatusText, value, nameof(UpdateStatusText));
    }

    public string UpdateSourceText
    {
        get => _updateSourceText;
        private set => SetProperty(ref _updateSourceText, value, nameof(UpdateSourceText));
    }

    public Visibility EmptyQueueVisibility
    {
        get => _emptyQueueVisibility;
        private set => SetProperty(ref _emptyQueueVisibility, value, nameof(EmptyQueueVisibility));
    }

    public double OverallProgress
    {
        get => _overallProgress;
        private set => SetProperty(ref _overallProgress, value, nameof(OverallProgress));
    }

    public bool IsDeleting
    {
        get => _isDeleting;
        private set
        {
            if (!SetProperty(ref _isDeleting, value, nameof(IsDeleting)))
            {
                return;
            }

            NotifyCommandStateChanged();
        }
    }

    public bool CanEditQueue => !IsDeleting && !_isCheckingUpdates;

    public bool CanStartDeletion => Files.Count > 0 && !IsDeleting && !_isCheckingUpdates;

    public bool CanCancelDeletion => IsDeleting;

    public bool CanCheckUpdates => !IsDeleting && !_isCheckingUpdates;

    public bool CanApplyDownloadedUpdate => _updateUiState == UpdateUiState.Downloaded && !IsDeleting && !_isCheckingUpdates;

    private void Ui_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ApplyLocalizedUi();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_startupUpdateCheckPending)
        {
            return;
        }

        _startupUpdateCheckPending = false;

        // Auto update is disabled until an update feed is hosted. Uncomment to re-enable the startup check.
        // await Task.Delay(700);
        // await CheckForUpdatesAsync(silentWhenNoUpdate: true);
        await Task.CompletedTask;
    }

    private void ToggleLanguageButton_Click(object sender, RoutedEventArgs e)
    {
        Ui.ToggleLanguage();
        Log(T("LogLanguageChangedFormat", Ui.IsEnglish ? T("CurrentLanguageEnglish") : T("CurrentLanguageKorean")));
    }

    private void AddFilesButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = T("AddFilesDialogTitle"),
            Multiselect = true,
            Filter = T("AllFilesFilter"),
        };

        if (dialog.ShowDialog(this) == true)
        {
            AddPaths(dialog.FileNames);
        }
    }

    private void RemoveSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditQueue)
        {
            return;
        }

        var selectedItems = FileGrid.SelectedItems.Cast<FileDeletionItem>().ToList();
        if (selectedItems.Count == 0)
        {
            return;
        }

        foreach (var item in selectedItems)
        {
            Files.Remove(item);
        }

        Log(T("LogRemovedSelectedFormat", selectedItems.Count));
    }

    private void ClearCompletedButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditQueue)
        {
            return;
        }

        var completedItems = Files
            .Where(item => item.StatusKind is FileQueueStatusKind.Completed or FileQueueStatusKind.CompletedWithResidue or FileQueueStatusKind.Missing)
            .ToList();

        if (completedItems.Count == 0)
        {
            Log(T("LogNoCompletedItems"));
            return;
        }

        foreach (var item in completedItems)
        {
            Files.Remove(item);
        }

        Log(T("LogClearedCompletedFormat", completedItems.Count));
    }

    private void ClearAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditQueue || Files.Count == 0)
        {
            return;
        }

        if (MessageBox.Show(
                this,
                T("ClearQueueConfirm"),
                T("ClearQueueTitle"),
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            return;
        }

        Files.Clear();
        Log(T("LogClearedAll"));
    }

    private void SaveLogButton_Click(object sender, RoutedEventArgs e)
    {
        if (LogEntries.Count == 0)
        {
            MessageBox.Show(this, T("NoLogsMessage"), T("SaveLogDialogTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = T("SaveLogDialogTitle"),
            FileName = $"secure-delete-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            Filter = T("TextFilesFilter"),
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.WriteAllLines(dialog.FileName, LogEntries);
            Log(T("LogSavedFormat", dialog.FileName));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, T("ErrorSaveLogFormat", ex.Message), T("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void StartDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        await StartDeletionAsync();
    }

    private async void WipeFreeSpaceButton_Click(object sender, RoutedEventArgs e)
    {
        await WipeFreeSpaceAsync();
    }

    private void CancelDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        _deletionCts?.Cancel();
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync(silentWhenNoUpdate: false);
    }

    private void ApplyDownloadedUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyDownloadedUpdate();
    }

    private void FileGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (IsDeleting)
        {
            return;
        }

        UpdateSelectionStatus();
    }

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!CanEditQueue || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] droppedPaths)
        {
            AddPaths(droppedPaths);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!IsDeleting)
        {
            base.OnClosing(e);
            return;
        }

        var shouldClose = MessageBox.Show(
            this,
            T("CloseWhileDeletingConfirm"),
            T("CloseWhileDeletingTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (shouldClose != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        _deletionCts?.Cancel();
        StatusText = T("ExitStatus");
        Log(T("LogExitRequested"));
        e.Cancel = true;
    }

    private async Task StartDeletionAsync()
    {
        var targets = Files.Where(item => File.Exists(item.FullPath)).ToList();
        foreach (var missing in Files.Except(targets))
        {
            SetItemStatus(missing, FileQueueStatusKind.Missing);
        }

        if (targets.Count == 0)
        {
            MessageBox.Show(this, T("NoRealFilesMessage"), T("DeleteConfirmTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var totalBytes = targets.Sum(item => item.Size);
        var overwriteLabel = T("PassStrong");
        var cloudCount = targets.Count(item => TraceCleanupService.IsInCloudSyncFolder(item.FullPath));
        var confirmMessage = T("DeleteConfirmMessage", targets.Count, FileDeletionItem.FormatBytes(totalBytes), overwriteLabel);
        if (cloudCount > 0)
        {
            confirmMessage += T("CloudSyncWarningFormat", cloudCount);
        }

        var confirmation = MessageBox.Show(
            this,
            confirmMessage,
            T("DeleteConfirmTitle"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.OK)
        {
            return;
        }

        var runTraceCleanup = MessageBox.Show(
            this,
            T("TraceCleanupPrompt"),
            T("TraceCleanupTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;

        IsDeleting = true;
        OverallProgress = 0;
        StatusText = T("StatusDeleting");
        _deletionCts = new CancellationTokenSource();

        var successCount = 0;
        var failureCount = 0;
        var erasedPaths = new List<string>();

        try
        {
            for (var index = 0; index < targets.Count; index++)
            {
                var item = targets[index];
                var position = index;
                SetItemStatus(item, FileQueueStatusKind.Preparing);

                var progress = new Progress<SecureDeleteProgress>(report =>
                {
                    ApplyDeletionProgress(item, report);
                    StatusText = T("StatusProcessingFormat", position + 1, targets.Count, item.Name, item.Status);
                    OverallProgress = ((position + (report.Percent / 100d)) / targets.Count) * 100d;
                });

                try
                {
                    var outcome = await _secureDeleteService.DeleteFileAsync(
                        item.FullPath,
                        OverwritePassCount,
                        progress,
                        _deletionCts.Token);

                    successCount++;
                    erasedPaths.Add(item.FullPath);
                    switch (outcome)
                    {
                        case SecureDeleteOutcome.ErasedWithResidueRisk:
                            SetItemStatus(item, FileQueueStatusKind.CompletedWithResidue);
                            Log(T("LogResidueWarningFormat", item.FullPath));
                            break;
                        case SecureDeleteOutcome.LinkRemoved:
                            SetItemStatus(item, FileQueueStatusKind.Completed);
                            Log(T("LogLinkRemovedFormat", item.FullPath));
                            break;
                        default:
                            SetItemStatus(item, FileQueueStatusKind.Completed);
                            Log(T("LogCompletedFormat", item.FullPath));
                            break;
                    }

                    await EraseRecentShortcutAsync(item.FullPath);
                }
                catch (OperationCanceledException)
                {
                    SetItemStatus(item, FileQueueStatusKind.Cancelled);
                    Log(T("LogCancelled"));
                    throw;
                }
                catch (Exception ex)
                {
                    failureCount++;
                    SetItemStatus(item, FileQueueStatusKind.Failed, detail: ex.Message);
                    Log(T("LogFailedFormat", item.FullPath, ex.Message));
                }

                OverallProgress = ((index + 1d) / targets.Count) * 100d;
            }

            StatusText = failureCount == 0
                ? T("StatusDoneFormat", successCount)
                : T("StatusDoneWithFailuresFormat", successCount, failureCount);
        }
        catch (OperationCanceledException)
        {
            StatusText = T("StatusCancelled");
        }
        finally
        {
            _deletionCts?.Dispose();
            _deletionCts = null;
        }

        try
        {
            // Runs after a cancel too: files erased before the cancel still left snapshots and journal entries.
            if (runTraceCleanup && erasedPaths.Count > 0)
            {
                await RunTraceCleanupAsync(erasedPaths);
            }

            if (erasedPaths.Count > 0)
            {
                Log(T("LogFreeSpaceWipeRecommended"));
            }
        }
        finally
        {
            IsDeleting = false;
            RefreshSummary();
        }
    }

    private async Task RunTraceCleanupAsync(IReadOnlyList<string> erasedPaths)
    {
        var statusBefore = StatusText;
        StatusText = T("StatusTraceCleanup");
        Log(T("LogTraceCleanupStarted"));
        try
        {
            var output = await _traceCleanupService.RunElevatedAsync(erasedPaths, CancellationToken.None);
            if (output is null)
            {
                Log(T("LogTraceCleanupDeclined"));
                return;
            }

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                Log(line);
            }

            Log(T("LogTraceCleanupDone"));
        }
        catch (Exception ex)
        {
            Log(T("LogTraceCleanupFailedFormat", ex.Message));
        }
        finally
        {
            StatusText = statusBefore;
        }
    }

    // The Recent shortcut stores the erased file's full path; erase it the same way so no trace of the name remains.
    private async Task EraseRecentShortcutAsync(string erasedPath)
    {
        var shortcutPath = TraceCleanupService.GetRecentShortcutPath(erasedPath);
        if (!File.Exists(shortcutPath))
        {
            return;
        }

        try
        {
            await _secureDeleteService.DeleteFileAsync(shortcutPath, OverwritePassCount, null, CancellationToken.None);
            Log(T("LogRecentShortcutErasedFormat", shortcutPath));
        }
        catch (Exception ex)
        {
            Log(T("LogFailedFormat", shortcutPath, ex.Message));
        }
    }

    private async Task WipeFreeSpaceAsync()
    {
        if (!CanEditQueue)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = T("WipeFreeSpaceDialogTitle"),
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var folderPath = dialog.FolderName;
        var rootPath = Path.GetPathRoot(Path.GetFullPath(folderPath)) ?? folderPath;
        var confirmation = MessageBox.Show(
            this,
            T("WipeFreeSpaceConfirmMessage", rootPath),
            T("WipeFreeSpaceConfirmTitle"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.OK)
        {
            return;
        }

        IsDeleting = true;
        OverallProgress = 0;
        StatusText = T("StatusWipingFreeSpaceFormat", 0);
        _deletionCts = new CancellationTokenSource();
        Log(T("LogFreeSpaceWipeStartedFormat", rootPath));

        var progress = new Progress<FreeSpaceWipeProgress>(report =>
        {
            OverallProgress = report.Percent;
            StatusText = T("StatusWipingFreeSpaceFormat", Math.Round(report.Percent));
        });

        try
        {
            await _freeSpaceWipeService.WipeAsync(folderPath, progress, _deletionCts.Token);
            OverallProgress = 100;
            StatusText = T("StatusFreeSpaceWipeDone");
            Log(T("LogFreeSpaceWipeCompletedFormat", rootPath));
        }
        catch (OperationCanceledException)
        {
            StatusText = T("StatusCancelled");
            Log(T("LogCancelled"));
        }
        catch (Exception ex)
        {
            StatusText = T("StatusFreeSpaceWipeFailed");
            Log(T("LogFreeSpaceWipeFailedFormat", rootPath, ex.Message));
            MessageBox.Show(this, T("ErrorFreeSpaceWipeFormat", ex.Message), T("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _deletionCts?.Dispose();
            _deletionCts = null;
            IsDeleting = false;
            RefreshSummary();
        }
    }

    private async Task CheckForUpdatesAsync(bool silentWhenNoUpdate)
    {
        if (_isCheckingUpdates || IsDeleting)
        {
            return;
        }

        if (_appUpdateService is null)
        {
            if (!silentWhenNoUpdate)
            {
                MessageBox.Show(this, UpdateStatusText, T("UpdateDialogTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return;
        }

        SetUpdateCheckState(true);
        SetUpdateState(UpdateUiState.Checking);

        try
        {
            var result = await _appUpdateService.CheckForUpdatesAsync();
            switch (result.Status)
            {
                case AppUpdateStatus.NotInstalled:
                    SetUpdateState(UpdateUiState.NotInstalled);
                    if (!silentWhenNoUpdate)
                    {
                        MessageBox.Show(this, T("UpdateDialogInstalledOnly"), T("UpdateDialogTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    break;

                case AppUpdateStatus.UpToDate:
                    SetUpdateState(UpdateUiState.UpToDate);
                    if (!silentWhenNoUpdate)
                    {
                        MessageBox.Show(this, T("UpdateDialogUpToDate"), T("UpdateDialogTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    break;

                case AppUpdateStatus.UpdateAvailable:
                    SetUpdateState(UpdateUiState.UpdateAvailable, result.Version);

                    if (result.UpdateInfo is null)
                    {
                        break;
                    }

                    var confirmDownload = MessageBox.Show(
                        this,
                        T("UpdateAvailablePrompt", result.Version ?? VersionText),
                        T("UpdateDialogTitle"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (confirmDownload == MessageBoxResult.Yes)
                    {
                        await DownloadUpdateAsync(result.UpdateInfo, result.Version);
                    }
                    break;

                case AppUpdateStatus.Failed:
                    SetUpdateState(UpdateUiState.Failed, message: result.Message);
                    if (!silentWhenNoUpdate)
                    {
                        MessageBox.Show(this, UpdateStatusText, T("UpdateDialogTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    break;

                case AppUpdateStatus.UpdateSourceMissing:
                    SetUpdateState(UpdateUiState.MissingSource);
                    break;
            }
        }
        finally
        {
            SetUpdateCheckState(false);
        }
    }

    private async Task DownloadUpdateAsync(UpdateInfo updateInfo, string? version)
    {
        if (_appUpdateService is null)
        {
            return;
        }

        SetUpdateCheckState(true);
        SetUpdateState(UpdateUiState.Downloading, version, "0");

        try
        {
            var result = await _appUpdateService.DownloadUpdateAsync(
                updateInfo,
                progress => Dispatcher.Invoke(() => SetUpdateState(UpdateUiState.Downloading, version, progress.ToString())));

            switch (result.Status)
            {
                case AppUpdateStatus.Downloaded:
                    SetUpdateState(UpdateUiState.Downloaded, result.Version);

                    var applyNow = MessageBox.Show(
                        this,
                        T("UpdateDownloadedPrompt", result.Version ?? VersionText),
                        T("UpdateDialogTitle"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (applyNow == MessageBoxResult.Yes)
                    {
                        ApplyDownloadedUpdate();
                    }
                    break;

                case AppUpdateStatus.NotInstalled:
                    SetUpdateState(UpdateUiState.NotInstalled);
                    break;

                case AppUpdateStatus.Failed:
                    SetUpdateState(UpdateUiState.Failed, message: result.Message);
                    MessageBox.Show(this, UpdateStatusText, T("UpdateDialogTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                    break;
            }
        }
        finally
        {
            SetUpdateCheckState(false);
        }
    }

    private void ApplyDownloadedUpdate()
    {
        if (_appUpdateService is null)
        {
            return;
        }

        var result = _appUpdateService.ApplyDownloadedUpdateAndRestart();
        switch (result.Status)
        {
            case AppUpdateStatus.Installing:
                SetUpdateState(UpdateUiState.Installing, result.Version);
                break;

            case AppUpdateStatus.NotInstalled:
                SetUpdateState(UpdateUiState.NotInstalled);
                MessageBox.Show(this, T("UpdateDialogInstalledOnly"), T("UpdateDialogTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                break;

            case AppUpdateStatus.Failed:
                SetUpdateState(UpdateUiState.Failed, message: result.Message);
                MessageBox.Show(this, UpdateStatusText, T("UpdateDialogTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                break;

            default:
                SetUpdateState(UpdateUiState.NoPendingUpdate);
                break;
        }
    }

    private void AddPaths(IEnumerable<string> inputPaths)
    {
        if (!CanEditQueue)
        {
            return;
        }

        var knownPaths = new HashSet<string>(Files.Select(item => item.FullPath), StringComparer.OrdinalIgnoreCase);
        var addedCount = 0;
        var skippedCount = 0;
        var folderCount = 0;

        foreach (var rawPath in inputPaths)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                skippedCount++;
                continue;
            }

            if (File.Exists(rawPath))
            {
                if (TryAddFile(rawPath, knownPaths))
                {
                    addedCount++;
                }
                else
                {
                    skippedCount++;
                }

                continue;
            }

            if (!Directory.Exists(rawPath))
            {
                skippedCount++;
                continue;
            }

            folderCount++;
            foreach (var file in EnumerateFilesSafely(rawPath))
            {
                if (TryAddFile(file, knownPaths))
                {
                    addedCount++;
                }
                else
                {
                    skippedCount++;
                }
            }
        }

        if (addedCount > 0)
        {
            Log(folderCount > 0
                ? T("LogAddedFilesWithFoldersFormat", addedCount, folderCount)
                : T("LogAddedFilesFormat", addedCount));
        }

        if (skippedCount > 0)
        {
            Log(T("LogSkippedPathsFormat", skippedCount));
        }

        RefreshSummary();
    }

    private bool TryAddFile(string path, ISet<string> knownPaths)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!knownPaths.Add(fullPath))
            {
                return false;
            }

            var item = FileDeletionItem.FromPath(fullPath);
            SetItemStatus(item, FileQueueStatusKind.Queued);
            Files.Add(item);
            return true;
        }
        catch (Exception ex)
        {
            Log(T("LogPathAddFailedFormat", path, ex.Message));
            return false;
        }
    }

    private IEnumerable<string> EnumerateFilesSafely(string directoryPath)
    {
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(directoryPath);

        while (pendingDirectories.Count > 0)
        {
            var currentDirectory = pendingDirectories.Pop();

            string[] files;
            try
            {
                files = Directory.GetFiles(currentDirectory);
            }
            catch (Exception ex)
            {
                Log(T("LogFolderScanFailedFormat", currentDirectory, ex.Message));
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            string[] childDirectories;
            try
            {
                childDirectories = Directory.GetDirectories(currentDirectory);
            }
            catch (Exception ex)
            {
                Log(T("LogChildFolderScanFailedFormat", currentDirectory, ex.Message));
                continue;
            }

            foreach (var childDirectory in childDirectories)
            {
                // Junctions and directory symlinks point outside the dropped folder (and can loop), so never follow them.
                if (new DirectoryInfo(childDirectory).Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    Log(T("LogSkippedLinkFolderFormat", childDirectory));
                    continue;
                }

                pendingDirectories.Push(childDirectory);
            }
        }
    }

    private void Files_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshSummary();
    }

    private void RefreshSummary()
    {
        EmptyQueueVisibility = Files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        SummaryText = Files.Count == 0
            ? T("SummaryEmpty")
            : T("SummaryFormat", Files.Count, FileDeletionItem.FormatBytes(Files.Sum(item => item.Size)));

        if (!IsDeleting)
        {
            UpdateSelectionStatus();
        }

        OnPropertyChanged(nameof(CanStartDeletion));
    }

    private void UpdateSelectionStatus()
    {
        if (FileGrid.SelectedItems.Count > 0)
        {
            StatusText = T("StatusSelectedFormat", FileGrid.SelectedItems.Count);
            return;
        }

        StatusText = Files.Count == 0 ? T("StatusIdle") : T("StatusReview");
    }

    private void ApplyLocalizedUi()
    {
        Title = T("WindowTitle");
        NameColumn.Header = T("FileNameHeader");
        SizeColumn.Header = T("FileSizeHeader");
        StatusColumn.Header = T("FileStatusHeader");

        RefreshItemStatusTexts();
        RefreshSummary();
        RefreshUpdateStatusText();
    }

    private void InitializeUpdateState()
    {
        if (string.IsNullOrWhiteSpace(_configuredUpdateSource))
        {
            _updateUiState = UpdateUiState.MissingSource;
        }
        else if (_appUpdateService is null)
        {
            _updateUiState = UpdateUiState.InvalidSource;
        }
        else
        {
            _updateUiState = UpdateUiState.Ready;
        }

        RefreshUpdateStatusText();
    }

    private void SetUpdateState(UpdateUiState state, string? version = null, string? message = null)
    {
        _updateUiState = state;
        _updateStateVersion = version;
        _updateStateMessage = message;
        RefreshUpdateStatusText();
        NotifyCommandStateChanged();
    }

    private void RefreshUpdateStatusText()
    {
        UpdateStatusText = _updateUiState switch
        {
            UpdateUiState.MissingSource => T("UpdateMissingSource"),
            UpdateUiState.InvalidSource => T("UpdateInvalidSource"),
            UpdateUiState.Ready => T("UpdateReady"),
            UpdateUiState.Checking => T("UpdateChecking"),
            UpdateUiState.NotInstalled => T("UpdateInstalledOnly"),
            UpdateUiState.UpToDate => T("UpdateUpToDate"),
            UpdateUiState.UpdateAvailable => T("UpdateAvailableFormat", _updateStateVersion ?? VersionText),
            UpdateUiState.Downloading => T("UpdateDownloadingFormat", _updateStateVersion ?? VersionText, _updateStateMessage ?? "0"),
            UpdateUiState.Downloaded => T("UpdateDownloadedFormat", _updateStateVersion ?? VersionText),
            UpdateUiState.Installing => T("UpdateInstallingFormat", _updateStateVersion ?? VersionText),
            UpdateUiState.Failed => T("UpdateFailedFormat", _updateStateMessage ?? "Unknown error"),
            UpdateUiState.NoPendingUpdate => T("UpdateNoPending"),
            _ => T("UpdateReady"),
        };

        var source = _appUpdateService?.UpdateSource ?? _configuredUpdateSource;
        UpdateSourceText = string.IsNullOrWhiteSpace(source)
            ? T("UpdateSourceLabel")
            : $"{T("UpdateSourceLabel")}: {source}";
    }

    private void SetUpdateCheckState(bool value)
    {
        if (_isCheckingUpdates == value)
        {
            return;
        }

        _isCheckingUpdates = value;
        NotifyCommandStateChanged();
    }

    private void NotifyCommandStateChanged()
    {
        OnPropertyChanged(nameof(CanEditQueue));
        OnPropertyChanged(nameof(CanStartDeletion));
        OnPropertyChanged(nameof(CanCancelDeletion));
        OnPropertyChanged(nameof(CanCheckUpdates));
        OnPropertyChanged(nameof(CanApplyDownloadedUpdate));
    }

    private void ApplyDeletionProgress(FileDeletionItem item, SecureDeleteProgress report)
    {
        var kind = report.Stage switch
        {
            SecureDeleteStage.Preparing => FileQueueStatusKind.Preparing,
            SecureDeleteStage.AlternateStreams => FileQueueStatusKind.AlternateStreams,
            SecureDeleteStage.OverwritePass => FileQueueStatusKind.OverwritePass,
            SecureDeleteStage.Truncating => FileQueueStatusKind.Truncating,
            SecureDeleteStage.MetadataCleanup => FileQueueStatusKind.MetadataCleanup,
            SecureDeleteStage.Renaming => FileQueueStatusKind.Renaming,
            SecureDeleteStage.Deleting => FileQueueStatusKind.Deleting,
            SecureDeleteStage.Completed => FileQueueStatusKind.Completed,
            _ => FileQueueStatusKind.Preparing,
        };

        SetItemStatus(item, kind, report.CurrentStep, report.TotalSteps);
    }

    private void SetItemStatus(FileDeletionItem item, FileQueueStatusKind kind, int currentStep = 0, int totalSteps = 0, string? detail = null)
    {
        item.SetStatusState(kind, BuildItemStatusText(kind, currentStep, totalSteps, detail), currentStep, totalSteps, detail);
    }

    private void RefreshItemStatusTexts()
    {
        foreach (var item in Files)
        {
            item.RefreshStatusText(BuildItemStatusText(item.StatusKind, item.CurrentStep, item.TotalSteps, item.StatusDetail));
        }
    }

    private string BuildItemStatusText(FileQueueStatusKind kind, int currentStep = 0, int totalSteps = 0, string? detail = null)
    {
        return kind switch
        {
            FileQueueStatusKind.Queued => T("QueueStatusQueued"),
            FileQueueStatusKind.Missing => T("QueueStatusMissing"),
            FileQueueStatusKind.Preparing => T("QueueStatusPreparing"),
            FileQueueStatusKind.AlternateStreams => T("QueueStatusAdsFormat", currentStep, totalSteps),
            FileQueueStatusKind.OverwritePass => T("QueueStatusOverwriteFormat", currentStep, totalSteps),
            FileQueueStatusKind.Truncating => T("QueueStatusTruncating"),
            FileQueueStatusKind.MetadataCleanup => T("QueueStatusMetadata"),
            FileQueueStatusKind.Renaming => T("QueueStatusRenameFormat", currentStep, totalSteps),
            FileQueueStatusKind.Deleting => T("QueueStatusDeleting"),
            FileQueueStatusKind.Completed => T("QueueStatusCompleted"),
            FileQueueStatusKind.CompletedWithResidue => T("QueueStatusCompletedResidue"),
            FileQueueStatusKind.Cancelled => T("QueueStatusCancelled"),
            FileQueueStatusKind.Failed => T("QueueStatusFailedFormat", detail ?? "Unknown error"),
            _ => T("QueueStatusQueued"),
        };
    }

    private string T(string key)
    {
        return Ui[key];
    }

    private string T(string key, params object[] args)
    {
        return Ui.Format(key, args);
    }

    private bool SetProperty<TValue>(ref TValue field, TValue value, string propertyName)
    {
        if (EqualityComparer<TValue>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        LogEntries.Insert(0, line);
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static bool TryResolveUpdateSource(string input, out string source)
    {
        source = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                source = uri.AbsoluteUri;
                return true;
            }

            if (string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
            {
                source = uri.LocalPath;
                return true;
            }
        }

        try
        {
            source = Path.IsPathRooted(input)
                ? Path.GetFullPath(input)
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, input));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetDisplayVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version is null)
        {
            return "1.0.0";
        }

        return version.Build >= 0
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : $"{version.Major}.{version.Minor}";
    }
}
