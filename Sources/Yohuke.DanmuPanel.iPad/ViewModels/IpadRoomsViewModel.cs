using Yohuke.DanmuPanel.I18n;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuPanel.Models.State;
using Yohuke.DanmuPanel.ViewModels.Workspace;

namespace Yohuke.DanmuPanel.iPad.ViewModels;

/// <summary>管理 iPad 工作区和房间编辑，复用共享搜索与房间校验。</summary>
public sealed partial class IpadRoomsViewModel : ObservableObject, IDisposable
{
    private readonly Func<string, string, Task<bool>> confirm;
    private readonly CancellationTokenSource lifetime = new();
    private bool disposed;
    [ObservableProperty] private string workspaceName = "";
    [ObservableProperty] private RoomTabState? selectedRoom;
    [ObservableProperty] private RoomEditorViewModel? editor;
    [ObservableProperty] private bool isEditing;
    public IpadShellViewModel Shell { get; }
    public IEnumerable<RoomTabState> WorkspaceRooms => Shell.SelectedWorkspace?.Rooms ?? [];
    public IAsyncRelayCommand AddWorkspaceCommand { get; }
    public IAsyncRelayCommand RenameWorkspaceCommand { get; }
    public IAsyncRelayCommand DeleteWorkspaceCommand { get; }
    public IRelayCommand AddRoomCommand { get; }
    public IRelayCommand EditRoomCommand { get; }
    public IAsyncRelayCommand DeleteRoomCommand { get; }

    /// <summary>连接页面确认交互与共享房间运行时。</summary>
    public IpadRoomsViewModel(IpadShellViewModel shell, Func<string, string, Task<bool>> confirm)
    {
        Shell = shell;
        this.confirm = confirm;
        AddWorkspaceCommand = new AsyncRelayCommand(() => Shell.RunAsync(AddWorkspaceAsync));
        RenameWorkspaceCommand = new AsyncRelayCommand(() => Shell.RunAsync(RenameWorkspaceAsync));
        DeleteWorkspaceCommand = new AsyncRelayCommand(() => Shell.RunAsync(DeleteWorkspaceAsync));
        AddRoomCommand = new RelayCommand(() => OpenEditor(null));
        EditRoomCommand = new RelayCommand(() => OpenEditor(SelectedRoom), () => SelectedRoom is not null);
        DeleteRoomCommand = new AsyncRelayCommand(() => Shell.RunAsync(DeleteRoomAsync), () => SelectedRoom is not null);
        Shell.PropertyChanged += OnShellChanged;
        RefreshWorkspace();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        disposed = true;
        Shell.PropertyChanged -= OnShellChanged;
        lifetime.Cancel();
        Editor?.Dispose();
        lifetime.Dispose();
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(Shell.SelectedWorkspace))
        {
            CloseEditor();
            RefreshWorkspace();
        }
    }

    private void RefreshWorkspace()
    {
        WorkspaceName = Shell.SelectedWorkspace?.Name ?? "";
        SelectedRoom = Shell.SelectedWorkspace?.Rooms.FirstOrDefault(room => room.Id == Shell.SelectedRoom?.Id)
            ?? Shell.SelectedWorkspace?.Rooms.FirstOrDefault();
        OnPropertyChanged(nameof(WorkspaceRooms));
    }

    private async Task AddWorkspaceAsync()
    {
        var workspace = new WorkspaceState
        {
            Name = string.IsNullOrWhiteSpace(WorkspaceName) ? Localization.Get("Panel.NewWorkspace") : WorkspaceName.Trim()
        };
        Shell.Workspaces.Add(workspace);
        Shell.SelectedWorkspace = workspace;
        await SaveAsync();
    }

    private async Task RenameWorkspaceAsync()
    {
        if (Shell.SelectedWorkspace is not { } workspace || string.IsNullOrWhiteSpace(WorkspaceName))
        {
            throw new InvalidOperationException(Localization.Get("iPad.WorkspaceNameRequired"));
        }
        workspace.Name = WorkspaceName.Trim();
        await SaveAsync();
    }

    private async Task DeleteWorkspaceAsync()
    {
        if (Shell.SelectedWorkspace is not { } workspace || !await confirm(Localization.Get("iPad.DeleteWorkspace"), Localization.Format("iPad.DeleteWorkspaceConfirm", workspace.Name)))
        {
            return;
        }
        foreach (var room in workspace.Rooms)
        {
            await Shell.Runtime.StopRoomAsync(room);
            await Shell.Runtime.Messages.ClearRoomAsync(room.Id);
        }
        Shell.Workspaces.Remove(workspace);
        if (Shell.Workspaces.Count == 0)
        {
            Shell.Workspaces.Add(new()
            {
                Name = Localization.Get("Panel.DefaultWorkspace")
            });
        }
        Shell.SelectedWorkspace = Shell.Workspaces[0];
        await SaveAsync();
    }

    private void OpenEditor(RoomTabState? existing)
    {
        if (Shell.SelectedWorkspace is not { } workspace)
        {
            return;
        }
        CloseEditor();
        var room = existing ?? new RoomTabState();
        var wasListening = existing is not null && Shell.Runtime.IsListening(existing);
        var completed = false;
        Editor = new(Shell.Runtime, workspace, room, existing is not null,
            accepted =>
            {
                if (completed || disposed)
                {
                    return;
                }
                completed = true;
                CloseEditor();
                if (!accepted)
                {
                    return;
                }
                _ = Shell.RunAsync(async () =>
                {
                    if (existing is null)
                    {
                        workspace.Rooms.Add(room);
                    }
                    if (wasListening)
                    {
                        await Shell.Runtime.StopRoomAsync(room);
                        await Shell.Runtime.ToggleListeningAsync(workspace, room);
                    }
                    await SaveAsync();
                    if (!disposed && ReferenceEquals(Shell.SelectedWorkspace, workspace))
                    {
                        Shell.RefreshRooms();
                        Shell.SelectedRoom = room;
                        RefreshWorkspace();
                    }
                });
            }, lifetime.Token);
        IsEditing = true;
    }

    private async Task DeleteRoomAsync()
    {
        if (SelectedRoom is not { } room || Shell.SelectedWorkspace is not { } workspace ||
            !await confirm(Localization.Get("iPad.DeleteRoom"), Localization.Format("iPad.DeleteRoomConfirm", room.Name)))
        {
            return;
        }
        await Shell.Runtime.StopRoomAsync(room);
        await Shell.Runtime.Messages.ClearRoomAsync(room.Id);
        workspace.Rooms.Remove(room);
        Shell.RefreshRooms();
        RefreshWorkspace();
        await SaveAsync();
    }

    private Task SaveAsync()
    {
        return Shell.Runtime.ConfigurationStore.SaveAsync(Shell.Runtime.Configuration);
    }

    private void CloseEditor()
    {
        Editor?.Dispose();
        Editor = null;
        IsEditing = false;
    }

    partial void OnSelectedRoomChanged(RoomTabState? value)
    {
        EditRoomCommand.NotifyCanExecuteChanged();
        DeleteRoomCommand.NotifyCanExecuteChanged();
    }
}
