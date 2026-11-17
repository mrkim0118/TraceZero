using Velopack;

namespace TraceZero.Services;

public enum AppUpdateStatus
{
    UpdateSourceMissing,
    NotInstalled,
    UpToDate,
    UpdateAvailable,
    Downloaded,
    Installing,
    Failed,
}

public sealed record AppUpdateResult(
    AppUpdateStatus Status,
    string? Version = null,
    string? Message = null,
    UpdateInfo? UpdateInfo = null);

public sealed class AppUpdateService
{
    private readonly string _updateSource;

    public AppUpdateService(string updateSource)
    {
        _updateSource = updateSource;
    }

    public string UpdateSource => _updateSource;

    public async Task<AppUpdateResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_updateSource))
        {
            return new AppUpdateResult(AppUpdateStatus.UpdateSourceMissing);
        }

        try
        {
            var manager = new UpdateManager(_updateSource);

            if (!manager.IsInstalled)
            {
                return new AppUpdateResult(AppUpdateStatus.NotInstalled);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var updateInfo = await manager.CheckForUpdatesAsync();
            if (updateInfo is null)
            {
                return new AppUpdateResult(AppUpdateStatus.UpToDate);
            }

            return new AppUpdateResult(
                AppUpdateStatus.UpdateAvailable,
                updateInfo.TargetFullRelease.Version?.ToString() ?? updateInfo.TargetFullRelease.FileName,
                UpdateInfo: updateInfo);
        }
        catch (Exception ex)
        {
            return new AppUpdateResult(AppUpdateStatus.Failed, Message: ex.Message);
        }
    }

    public async Task<AppUpdateResult> DownloadUpdateAsync(
        UpdateInfo updateInfo,
        Action<int>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_updateSource))
        {
            return new AppUpdateResult(AppUpdateStatus.UpdateSourceMissing);
        }

        try
        {
            var manager = new UpdateManager(_updateSource);

            if (!manager.IsInstalled)
            {
                return new AppUpdateResult(AppUpdateStatus.NotInstalled);
            }

            await manager.DownloadUpdatesAsync(updateInfo, progressCallback, cancellationToken);
            var pendingRestart = manager.UpdatePendingRestart;

            if (pendingRestart is null)
            {
                return new AppUpdateResult(
                    AppUpdateStatus.Failed,
                    Message: "업데이트를 다운로드했지만 적용 대기 패키지를 찾지 못했습니다.",
                    UpdateInfo: updateInfo);
            }

            return new AppUpdateResult(
                AppUpdateStatus.Downloaded,
                pendingRestart.Version?.ToString() ?? pendingRestart.FileName ?? updateInfo.TargetFullRelease.FileName,
                UpdateInfo: updateInfo);
        }
        catch (Exception ex)
        {
            return new AppUpdateResult(AppUpdateStatus.Failed, Message: ex.Message, UpdateInfo: updateInfo);
        }
    }

    public AppUpdateResult ApplyDownloadedUpdateAndRestart()
    {
        if (string.IsNullOrWhiteSpace(_updateSource))
        {
            return new AppUpdateResult(AppUpdateStatus.UpdateSourceMissing);
        }

        try
        {
            var manager = new UpdateManager(_updateSource);

            if (!manager.IsInstalled)
            {
                return new AppUpdateResult(AppUpdateStatus.NotInstalled);
            }

            var pendingRestart = manager.UpdatePendingRestart;
            if (pendingRestart is null)
            {
                return new AppUpdateResult(
                    AppUpdateStatus.Failed,
                    Message: "적용 대기 중인 업데이트가 없습니다.");
            }

            manager.WaitExitThenApplyUpdates(pendingRestart, false, true, Array.Empty<string>());

            return new AppUpdateResult(
                AppUpdateStatus.Installing,
                pendingRestart.Version?.ToString() ?? pendingRestart.FileName);
        }
        catch (Exception ex)
        {
            return new AppUpdateResult(AppUpdateStatus.Failed, Message: ex.Message);
        }
    }
}
