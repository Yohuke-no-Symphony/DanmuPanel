using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuPanel.I18n;
using Yohuke.DanmuPanel.Models.State;
using Yohuke.DanmuPanel.Services;
using Yohuke.DanmuPanel.ViewModels.Workspace;

namespace Yohuke.DanmuPanel.iPad.ViewModels;

/// <summary>将共享工作区和房间节点投影为 iPad 侧栏，并沿用阅读台的选择与监听状态。</summary>
public sealed class IpadWorkspaceSidebarViewModel : IDisposable
{
    private readonly IpadShellViewModel shell;
    private readonly IRoomAvatarService avatars;
    private readonly HashSet<WorkspaceState> observedWorkspaces = [];
    private readonly Dictionary<RoomTreeItemViewModel, (string RoomId, string OwnerUid)> avatarIdentities = [];
    private bool disposed;

    public ObservableCollection<WorkspaceTreeItemViewModel> Workspaces { get; } = [];
    public IRelayCommand<WorkspaceTreeItemViewModel> SelectWorkspaceCommand { get; }
    public IRelayCommand<RoomTreeItemViewModel> SelectRoomCommand { get; }
    public event Action? RoomSelected;

    /// <summary>复用共享树节点和头像缓存服务，维护侧栏选择并异步补齐主播头像。</summary>
    public IpadWorkspaceSidebarViewModel(IpadShellViewModel shell, IRoomAvatarService? avatarService = null)
    {
        this.shell = shell;
        avatars = avatarService ?? shell.Runtime.Avatars;
        SelectWorkspaceCommand = new RelayCommand<WorkspaceTreeItemViewModel>(item =>
        {
            if (item is not null)
            {
                shell.SelectedWorkspace = item.State;
            }
        });
        SelectRoomCommand = new RelayCommand<RoomTreeItemViewModel>(item =>
        {
            if (item is null)
            {
                return;
            }
            shell.SelectedWorkspace = item.Workspace;
            shell.SelectedRoom = item.State;
            RoomSelected?.Invoke();
        });
        shell.Workspaces.CollectionChanged += OnWorkspacesChanged;
        shell.RoomChanged += Refresh;
        shell.StatusChanged += RefreshStatus;
        Refresh();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        shell.Workspaces.CollectionChanged -= OnWorkspacesChanged;
        shell.RoomChanged -= Refresh;
        shell.StatusChanged -= RefreshStatus;
        foreach (var workspace in observedWorkspaces)
        {
            workspace.Rooms.CollectionChanged -= OnRoomsChanged;
        }
        observedWorkspaces.Clear();
        foreach (var room in Workspaces.SelectMany(workspace => workspace.Rooms))
        {
            ReleaseRoom(room);
        }
        Workspaces.Clear();
    }

    private void Refresh()
    {
        if (disposed)
        {
            return;
        }
        foreach (var workspace in observedWorkspaces.Where(workspace => !shell.Workspaces.Contains(workspace)).ToArray())
        {
            workspace.Rooms.CollectionChanged -= OnRoomsChanged;
            observedWorkspaces.Remove(workspace);
        }
        foreach (var item in Workspaces.Where(item => !shell.Workspaces.Contains(item.State)).ToArray())
        {
            foreach (var room in item.Rooms)
            {
                ReleaseRoom(room);
            }
            Workspaces.Remove(item);
        }
        foreach (var state in shell.Workspaces)
        {
            if (observedWorkspaces.Add(state))
            {
                state.Rooms.CollectionChanged += OnRoomsChanged;
            }
            var workspace = Workspaces.FirstOrDefault(item => ReferenceEquals(item.State, state));
            if (workspace is null)
            {
                workspace = new(state);
                Workspaces.Add(workspace);
            }
            foreach (var item in workspace.Rooms.Where(item => !state.Rooms.Contains(item.State)).ToArray())
            {
                ReleaseRoom(item);
                workspace.Rooms.Remove(item);
            }
            foreach (var room in state.Rooms)
            {
                var node = workspace.Rooms.FirstOrDefault(item => ReferenceEquals(item.State, room));
                if (node is not null && avatarIdentities[node] != (room.RoomId, room.OwnerUid))
                {
                    ReleaseRoom(node);
                    workspace.Rooms.Remove(node);
                    room.AvatarPath = "";
                    node = null;
                }
                if (node is null)
                {
                    node = new(state, room);
                    avatarIdentities.Add(node, (room.RoomId, room.OwnerUid));
                    workspace.Rooms.Add(node);
                    _ = LoadAvatarAsync(node);
                }
            }
            workspace.RoomCountText = state.Rooms.Count.ToString();
        }
        RefreshStatus();
    }

    private async Task LoadAvatarAsync(RoomTreeItemViewModel room)
    {
        try
        {
            var account = PanelAccountSelector.Resolve(shell.Runtime.Configuration, room.Workspace, room.State);
            if (await room.LoadAvatarAsync(avatars, account?.Cookie) && !disposed && avatarIdentities.ContainsKey(room))
            {
                avatarIdentities[room] = (room.State.RoomId, room.State.OwnerUid);
                shell.RefreshStatus();
                await shell.Runtime.ConfigurationStore.SaveAsync(shell.Runtime.Configuration);
            }
        }
        catch (Exception exception)
        {
            if (!disposed && avatarIdentities.ContainsKey(room))
            {
                shell.ReportError(exception);
            }
        }
    }

    private void ReleaseRoom(RoomTreeItemViewModel room)
    {
        avatarIdentities.Remove(room);
        room.Dispose();
    }

    private void OnWorkspacesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        Refresh();
    }

    private void OnRoomsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        Refresh();
    }

    private void RefreshStatus()
    {
        if (disposed)
        {
            return;
        }
        foreach (var workspace in Workspaces)
        {
            workspace.IsSelected = ReferenceEquals(workspace.State, shell.SelectedWorkspace);
            foreach (var room in workspace.Rooms)
            {
                room.IsSelected = ReferenceEquals(room.State, shell.SelectedRoom);
                room.IsListening = shell.Runtime.IsListening(room.State);
                room.ConnectionStatus = Localization.Get(shell.Runtime.GetStatus(room.State));
                room.AvatarPlaceholderText = string.IsNullOrEmpty(room.State.Name) ? "" : room.State.Name[..1];
            }
        }
    }
}
