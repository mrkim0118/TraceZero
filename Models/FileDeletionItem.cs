using System.ComponentModel;
using System.IO;

namespace SecureFileEraser.Models;

public sealed class FileDeletionItem : INotifyPropertyChanged
{
    private string _status = "Queued";

    private FileDeletionItem(string fullPath, long size)
    {
        FullPath = fullPath;
        Size = size;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name => Path.GetFileName(FullPath);

    public string FullPath { get; }

    public long Size { get; }

    public string SizeText => FormatBytes(Size);

    public FileQueueStatusKind StatusKind { get; private set; } = FileQueueStatusKind.Queued;

    public int CurrentStep { get; private set; }

    public int TotalSteps { get; private set; }

    public string? StatusDetail { get; private set; }

    public string Status
    {
        get => _status;
        private set
        {
            if (_status == value)
            {
                return;
            }

            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    }

    public static FileDeletionItem FromPath(string path)
    {
        var info = new FileInfo(path);
        return new FileDeletionItem(info.FullName, info.Exists ? info.Length : 0);
    }

    public void SetStatusState(
        FileQueueStatusKind kind,
        string displayText,
        int currentStep = 0,
        int totalSteps = 0,
        string? detail = null)
    {
        StatusKind = kind;
        CurrentStep = currentStep;
        TotalSteps = totalSteps;
        StatusDetail = detail;
        Status = displayText;
    }

    public void RefreshStatusText(string displayText)
    {
        Status = displayText;
    }

    public static string FormatBytes(long size)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = size;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }
}
