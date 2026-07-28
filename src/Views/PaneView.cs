using System.Drawing;
using FoileBrowser.ViewModels;
using Hawkynt.NativeForms;

namespace FoileBrowser.Views;

/// <summary>
/// One browsing pane (PRD §6.1/§6.4): navigation bar with the breadcrumb path bar, the filter and
/// subtree-search row, this pane's navigation sidebar, the file list and a status line.
/// </summary>
public sealed class PaneView : Panel
{
    // The nav and search rows host real platform buttons and text fields, so their cells have to
    // clear the widget minimums the desktop theme imposes (GTK complains below roughly 34×26).
    private const int NavHeight = 34;
    private const int NavButtonWidth = 38;
    private const int SearchHeight = 30;
    private const int StatusHeight = 22;
    private const int SidebarWidth = 165;

    private readonly MainWindowViewModel _shell;
    private readonly FileTabViewModel _tab;
    private readonly List<Action> _cleanup = [];

    private readonly TableLayoutPanel _nav = new()
    {
        Dock = DockStyle.Top,
        Bounds = new(0, 0, 0, NavHeight),
        ColumnCount = 7,
        RowCount = 1,
    };

    private readonly TableLayoutPanel _searchRow = new()
    {
        Dock = DockStyle.Top,
        Bounds = new(0, 0, 0, SearchHeight),
        ColumnCount = 4,
        RowCount = 1,
    };

    private readonly StatusStrip _status = new() { Dock = DockStyle.Bottom, Bounds = new(0, 0, 0, StatusHeight) };
    private readonly ToolStripStatusLabel _statusLabel = new() { Spring = true };
    private readonly ToolStripStatusLabel _loadingLabel = new();

    private readonly Breadcrumb _breadcrumb = new() { Editable = true, TrimOnClick = false, Margin = new(2) };
    private readonly Button _back = new() { Image = Icons.BackIcon, Margin = new(1) };
    private readonly Button _forward = new() { Image = Icons.ForwardIcon, Margin = new(1) };
    private readonly Button _up = new() { Image = Icons.UpIcon, Margin = new(1) };
    private readonly Button _refresh = new() { Image = Icons.RefreshIcon, Margin = new(1) };
    private readonly CheckBox _hidden = new() { Text = "Hidden", Margin = new(2) };
    private readonly CheckBox _sidebarToggle = new() { Image = Icons.MenuIcon, Margin = new(2) };

    private readonly TextBox _filter = new() { PlaceholderText = "Filter this folder…", Margin = new(2) };
    private readonly TextBox _search = new() { PlaceholderText = "Search subtree (Enter, Esc to dismiss)…", Margin = new(2) };
    private readonly TextBox _extensions = new() { PlaceholderText = "ext,ext", Margin = new(2) };
    private readonly Button _stopSearch = new() { Image = Icons.CloseIcon, Margin = new(2) };

    private readonly SidebarView _sidebar;
    private readonly FileGridView _grid;
    private readonly SplitContainer _body;

    private bool _suppress;
    private bool _sidebarSized;

    public PaneView(MainWindowViewModel shell, FileTabViewModel tab)
    {
        _shell = shell;
        _tab = tab;

        _sidebar = new SidebarView(shell, tab) { Dock = DockStyle.Fill };
        _grid = new FileGridView(shell, tab) { Dock = DockStyle.Fill };

        // The sidebar and the file list share a splitter so the navigation pane can be dragged to
        // whatever width the user wants, rather than being pinned to one the view picked.
        _body = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            // The sidebar keeps its width when the pane resizes; without this both panels scale
            // proportionally and the navigation pane creeps wider every time the window grows.
            FixedPanel = FixedPanel.Panel1,
            SplitterDistance = SidebarWidth,
            Panel1MinSize = 90,
            Panel2MinSize = 160,
        };
        _body.Panel1.Controls.Add(_sidebar);
        _body.Panel2.Controls.Add(_grid);

        this.BuildNav();
        this.BuildSearchRow();
        this.BuildStatus();

        // Reverse order: the last child added claims its edge first (see Control.OnLayout).
        this.Controls.Add(_body);
        this.Controls.Add(_status);
        this.Controls.Add(_searchRow);
        this.Controls.Add(_nav);

        this.Wire();

        // Any interaction inside this pane makes it the active tab, so shell-level operations
        // (copy/move, inspector, properties) target it.
        this.Enter += (_, _) => _shell.ActivateTab(_tab);
    }

    /// <summary>
    /// Gives the sidebar its starting width the first time the pane actually has one. The splitter
    /// clamps a distance set before realization to whatever size the container had then — which is
    /// nothing — so it has to be applied once the layout is real. Only once: after that the width
    /// is the user's, dragged or not.
    /// </summary>
    public void ApplyInitialLayout()
    {
        if (_sidebarSized || _body.Width <= SidebarWidth + _body.Panel2MinSize)
            return;

        _sidebarSized = true;
        _body.SplitterDistance = SidebarWidth;
    }

    /// <summary>Row density for the file list (PRD §6.8).</summary>
    public int RowHeight
    {
        get => _grid.RowHeight;
        set => _grid.RowHeight = value;
    }

    /// <summary>Drops every subscription when the tab this pane rendered goes away.</summary>
    public void Detach()
    {
        foreach (var undo in _cleanup)
            undo();
        _cleanup.Clear();
        _sidebar.Detach();
        _grid.Detach();
        _tab.SearchFocusRequested -= this.OnSearchFocusRequested;
    }

    // ---- navigation bar ----

    private void BuildNav()
    {
        for (var i = 0; i < 4; ++i)
            _nav.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, NavButtonWidth));
        _nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _nav.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
        _nav.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));

        _back.Command = _tab.GoBackCommand;
        _forward.Command = _tab.GoForwardCommand;
        _up.Command = _tab.GoUpCommand;
        _refresh.Command = _tab.RefreshCommand;

        _breadcrumb.ItemClicked += (_, e) =>
        {
            if (e.Item.Tag is BreadcrumbSegment segment)
                _tab.NavigateBreadcrumbCommand.Execute(segment);
        };
        // Clicking the bar's empty space turns it into a path field; Enter commits, Escape reverts.
        _breadcrumb.PathEntered += (_, e) =>
        {
            _tab.PathBarText = e.Path;
            _tab.NavigatePathBarCommand.Execute(null);
        };

        _hidden.CheckedChanged += (_, _) =>
        {
            if (!_suppress)
                _tab.ShowHidden = _hidden.Checked;
        };
        _sidebarToggle.CheckedChanged += (_, _) =>
        {
            if (!_suppress)
                _tab.IsSidebarVisible = _sidebarToggle.Checked;
        };

        _nav.Controls.AddRange(_back, _forward, _up, _refresh, _breadcrumb, _hidden, _sidebarToggle);
    }

    // ---- filter / search row (PRD §6.4) ----

    private void BuildSearchRow()
    {
        _searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        _searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, NavButtonWidth));

        _filter.TextChanged += (_, _) =>
        {
            if (!_suppress)
                _tab.FilterText = _filter.Text;
        };

        _search.TextChanged += (_, _) =>
        {
            if (!_suppress)
                _tab.SearchQuery = _search.Text;
        };
        _search.KeyDown += this.OnSearchKeyDown;

        _extensions.TextChanged += (_, _) =>
        {
            if (!_suppress)
                _tab.SearchExtensions = _extensions.Text;
        };
        _extensions.KeyDown += this.OnSearchKeyDown;

        _stopSearch.Command = _tab.StopSearchCommand;

        _searchRow.Controls.AddRange(_filter, _search, _extensions, _stopSearch);
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Enter:
                _tab.StartSearchCommand.Execute(null);
                e.Handled = true;
                break;
            case Keys.Escape:
                // Collapses a bar that was only revealed for this search, then hands focus back.
                _shell.CollapseSearchBar();
                _grid.Focus();
                e.Handled = true;
                break;
        }
    }

    private void BuildStatus() => _status.Items.AddRange(_statusLabel, _loadingLabel);

    // ---- view-model wiring ----

    private void Wire()
    {
        _cleanup.Add(Ui.WatchList(_tab.Breadcrumbs, this.SyncBreadcrumbs));

        _cleanup.Add(Ui.Watch(_tab, () =>
        {
            _suppress = true;
            _hidden.Checked = _tab.ShowHidden;
            _sidebarToggle.Checked = _tab.IsSidebarVisible;
            if (_filter.Text != _tab.FilterText)
                _filter.Text = _tab.FilterText;
            if (_search.Text != _tab.SearchQuery)
                _search.Text = _tab.SearchQuery;
            if (_extensions.Text != _tab.SearchExtensions)
                _extensions.Text = _tab.SearchExtensions;
            _suppress = false;

            _statusLabel.Text = _tab.StatusLine;
            _loadingLabel.Text = _tab.IsLoading ? "Loading…" : string.Empty;
            _stopSearch.Enabled = _tab.IsSearching;
            _body.Panel1Collapsed = !_tab.IsSidebarVisible;
        }));

        _cleanup.Add(Ui.Watch(_tab, () =>
        {
            // Ctrl+L asks the view-model to edit the path; the breadcrumb owns the field itself.
            if (_tab.IsEditingPath && !_breadcrumb.IsEditing)
                _breadcrumb.BeginEdit();
            else if (!_tab.IsEditingPath && _breadcrumb.IsEditing)
                _breadcrumb.EndEdit(commit: false);
        }, nameof(FileTabViewModel.IsEditingPath)));

        _cleanup.Add(Ui.Watch(_shell, () => Ui.SetDockedExtent(_searchRow, _shell.IsSearchBarVisible, SearchHeight),
            nameof(MainWindowViewModel.IsSearchBarVisible)));

        _tab.SearchFocusRequested += this.OnSearchFocusRequested;
    }

    private void SyncBreadcrumbs()
    {
        _breadcrumb.Items.Clear();
        foreach (var segment in _tab.Breadcrumbs)
            _breadcrumb.Items.Add(new BreadcrumbItem(segment.Name) { Tag = segment });
    }

    private void OnSearchFocusRequested(object? sender, EventArgs e)
    {
        _search.Focus();
        _search.SelectAll();
    }
}
