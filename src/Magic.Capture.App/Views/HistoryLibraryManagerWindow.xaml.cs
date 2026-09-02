using Magic.Capture.App.Persistence;
using Magic.Capture.Core.History;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Magic.Capture.App.Views;

public sealed partial class HistoryLibraryManagerWindow : Window
{
    private readonly HistoryLibraryStore _store;
    private readonly Guid[] _selectedAssetIds;
    private HistoryLibrarySnapshot _snapshot = HistoryLibrarySnapshot.Empty;
    private bool _updating;

    private sealed record Option(string Name, string? Id)
    {
        public override string ToString() => Name;
    }

    internal event EventHandler? LibraryChanged;

    internal HistoryLibraryManagerWindow(HistoryLibraryStore store, IEnumerable<Guid> selectedAssetIds)
    {
        InitializeComponent();
        _store = store;
        _selectedAssetIds = selectedAssetIds.Where(x => x != Guid.Empty).Distinct().Take(5_000).ToArray();
        SelectionCountText.Text = $"{_selectedAssetIds.Length:N0} capture(s) selected in History.";
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _updating = true;
        try
        {
            _snapshot = await _store.LoadAsync();
            var selectedWorkspace = (WorkspaceList.SelectedItem as HistoryWorkspace)?.Id;
            var selectedFolder = (FolderList.SelectedItem as HistoryFolder)?.Id;
            var selectedCollection = (CollectionList.SelectedItem as HistoryCollection)?.Id;
            WorkspaceList.ItemsSource = _snapshot.Workspaces;
            WorkspaceList.SelectedItem = _snapshot.Workspaces.FirstOrDefault(x => x.Id == selectedWorkspace) ?? _snapshot.Workspaces.FirstOrDefault();
            RefreshFolderList(selectedFolder);
            CollectionList.ItemsSource = _snapshot.Collections;
            CollectionList.SelectedItem = _snapshot.Collections.FirstOrDefault(x => x.Id == selectedCollection) ?? _snapshot.Collections.FirstOrDefault();
            AssignWorkspaceCombo.ItemsSource = new[] { new Option("No workspace", null) }.Concat(_snapshot.Workspaces.Select(x => new Option(x.Name, x.Id))).ToArray();
            AssignWorkspaceCombo.SelectedIndex = 0;
            AssignCollectionCombo.ItemsSource = _snapshot.Collections.Select(x => new Option(x.Name, x.Id)).ToArray();
            if (AssignCollectionCombo.Items.Count > 0) AssignCollectionCombo.SelectedIndex = 0;
            RefreshAssignFolders();
        }
        finally { _updating = false; }
    }

    private void RefreshFolderList(string? selectedFolderId = null)
    {
        var workspaceId = (WorkspaceList.SelectedItem as HistoryWorkspace)?.Id;
        var folders = workspaceId is null ? Array.Empty<HistoryFolder>() : _snapshot.Folders.Where(x => x.WorkspaceId == workspaceId).ToArray();
        FolderList.ItemsSource = folders;
        FolderList.SelectedItem = folders.FirstOrDefault(x => x.Id == selectedFolderId) ?? folders.FirstOrDefault();
    }

    private void RefreshAssignFolders()
    {
        var workspaceId = (AssignWorkspaceCombo.SelectedItem as Option)?.Id;
        var options = new List<Option> { new("No folder", null) };
        if (workspaceId is not null) options.AddRange(_snapshot.Folders.Where(x => x.WorkspaceId == workspaceId).Select(x => new Option(x.Name, x.Id)));
        AssignFolderCombo.ItemsSource = options;
        AssignFolderCombo.SelectedIndex = 0;
    }

    private void WorkspaceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating) RefreshFolderList();
    }

    private void AssignWorkspaceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating) RefreshAssignFolders();
    }

    private async void CreateWorkspace_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async () => { await _store.CreateWorkspaceAsync(WorkspaceNameBox.Text); WorkspaceNameBox.Text = string.Empty; }, "Workspace created.");
    private async void CreateFolder_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async () =>
    {
        var workspace = WorkspaceList.SelectedItem as HistoryWorkspace ?? throw new InvalidOperationException("Select a workspace first.");
        await _store.CreateFolderAsync(workspace.Id, FolderNameBox.Text); FolderNameBox.Text = string.Empty;
    }, "Folder created.");
    private async void CreateCollection_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async () => { await _store.CreateCollectionAsync(CollectionNameBox.Text); CollectionNameBox.Text = string.Empty; }, "Collection created.");

    private async void RenameWorkspace_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async () => { var item = WorkspaceList.SelectedItem as HistoryWorkspace ?? throw new InvalidOperationException("Select a workspace."); await _store.RenameWorkspaceAsync(item.Id, WorkspaceNameBox.Text); }, "Workspace renamed.");
    private async void RenameFolder_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async () => { var item = FolderList.SelectedItem as HistoryFolder ?? throw new InvalidOperationException("Select a folder."); await _store.RenameFolderAsync(item.Id, FolderNameBox.Text); }, "Folder renamed.");
    private async void RenameCollection_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async () => { var item = CollectionList.SelectedItem as HistoryCollection ?? throw new InvalidOperationException("Select a collection."); await _store.RenameCollectionAsync(item.Id, CollectionNameBox.Text); }, "Collection renamed.");

    private async void DeleteWorkspace_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async () => { var item = WorkspaceList.SelectedItem as HistoryWorkspace ?? throw new InvalidOperationException("Select a workspace."); await _store.DeleteWorkspaceAsync(item.Id); }, "Workspace removed; captures were kept.");
    private async void DeleteFolder_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async () => { var item = FolderList.SelectedItem as HistoryFolder ?? throw new InvalidOperationException("Select a folder."); await _store.DeleteFolderAsync(item.Id); }, "Folder removed; captures were kept.");
    private async void DeleteCollection_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async () => { var item = CollectionList.SelectedItem as HistoryCollection ?? throw new InvalidOperationException("Select a collection."); await _store.DeleteCollectionAsync(item.Id); }, "Collection removed; captures were kept.");

    private async void AssignSelected_Click(object sender, RoutedEventArgs e) => await ExecuteAsync(async () =>
    {
        EnsureSelection();
        await _store.AssignWorkspaceFolderAsync(_selectedAssetIds, (AssignWorkspaceCombo.SelectedItem as Option)?.Id, (AssignFolderCombo.SelectedItem as Option)?.Id);
    }, "Selected captures assigned.");

    private async void AddSelectedToCollection_Click(object sender, RoutedEventArgs e) => await SetSelectedCollectionAsync(true);
    private async void RemoveSelectedFromCollection_Click(object sender, RoutedEventArgs e) => await SetSelectedCollectionAsync(false);
    private async Task SetSelectedCollectionAsync(bool isMember)
    {
        await ExecuteAsync(async () =>
        {
            EnsureSelection();
            var collectionId = (AssignCollectionCombo.SelectedItem as Option)?.Id ?? throw new InvalidOperationException("Select a collection.");
            await _store.SetCollectionMembershipAsync(_selectedAssetIds, collectionId, isMember);
        }, isMember ? "Selected captures added to collection." : "Selected captures removed from collection.");
    }

    private void EnsureSelection()
    {
        if (_selectedAssetIds.Length == 0) throw new InvalidOperationException("Select one or more captures in History before opening the Library manager.");
    }

    private async Task ExecuteAsync(Func<Task> action, string success)
    {
        try
        {
            await action();
            StatusText.Text = success;
            await RefreshAsync();
            LibraryChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (!Magic.Capture.Core.Platform.FatalExceptionPolicy.IsFatal(ex)) { StatusText.Text = ex.Message; }
    }
}
