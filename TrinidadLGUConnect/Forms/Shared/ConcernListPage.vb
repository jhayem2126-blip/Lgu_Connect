Imports TrinidadLGUConnect.Controls
Imports TrinidadLGUConnect.Helpers
Imports TrinidadLGUConnect.Models
Imports TrinidadLGUConnect.Services

Namespace Forms

    ''' <summary>
    ''' Searchable, filterable, paged list of concerns. Filters are applied in the SQL query. For residents the
    ''' service restricts results to their own reports regardless of what the UI sends.
    ''' </summary>
    Public MustInherit Class ConcernListPage
        Inherits PageBase

        Protected ReadOnly Search As New InputField("Search by ID, category, or location...", Glyph.Search)
        Protected ReadOnly FilterButton As New ModernButton("Filter", ButtonKind.Outline, Glyph.Filter)
        Protected ReadOnly CategoryFilter As New SelectField()
        Protected ReadOnly StatusFilter As New SelectField()
        Protected ReadOnly BarangayFilter As New SelectField(editable:=True)
        Protected ReadOnly DateFrom As New DateField(allowEmpty:=True)
        Protected ReadOnly DateTo As New DateField(allowEmpty:=True)
        Protected ReadOnly ClearButton As New ModernButton("Clear", ButtonKind.Secondary)
        Protected ReadOnly RefreshButton As New ModernButton("", ButtonKind.Secondary, Glyph.Refresh) With {.AccessibleName = "Refresh"}
        Protected ReadOnly FilterPanel As New TableLayoutPanel With {.Dock = DockStyle.Top, .Height = 64, .ColumnCount = 7, .RowCount = 1, .BackColor = Color.Transparent}
        Protected ReadOnly Grid As New ThemedGrid()
        Protected ReadOnly Pager As New Pager With {.Dock = DockStyle.Bottom, .Height = 52}
        Private ReadOnly _debounce As New Timer With {.Interval = 400}
        Private _page As Integer = 1
        Private _pageSize As Integer = 10
        Private _categoriesLoaded As Boolean
        Private _suspendEvents As Boolean
        Private ReadOnly _initialStatus As ConcernStatus?
        Private ReadOnly _initialCategory As Integer?
        Private _loadVersion As Integer

        ''' <summary>The page title panel (derived pages may add header buttons to it).</summary>
        Protected ReadOnly Header As Control

        Protected Sub New(headerControl As Control, initialStatus As ConcernStatus?, initialCategory As Integer?, showFiltersInitially As Boolean)
            Header = headerControl
            _initialStatus = initialStatus
            _initialCategory = initialCategory
            AutoScrollMinSize = New Size(860, 560)
            Search.MaxLength = 100
            DateFrom.MaxDate = DateTime.Today
            DateTo.MaxDate = DateTime.Today

            ' Filter row: Category | Status | Location | Date From | Date To | Clear | Refresh
            Dim widths = {22.0F, 18.0F, 18.0F, 16.0F, 16.0F}
            For Each w In widths
                FilterPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, w))
            Next
            FilterPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 84))
            FilterPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 48))
            FilterPanel.Controls.Add(Labeled("Category", CategoryFilter), 0, 0)
            FilterPanel.Controls.Add(Labeled("Status", StatusFilter), 1, 0)
            FilterPanel.Controls.Add(Labeled("Barangay", BarangayFilter), 2, 0)
            FilterPanel.Controls.Add(Labeled("Date From", DateFrom), 3, 0)
            FilterPanel.Controls.Add(Labeled("Date To", DateTo), 4, 0)
            Dim clearHost = Labeled(" ", ClearButton)
            Dim refreshHost = Labeled(" ", RefreshButton)
            refreshHost.Margin = Padding.Empty
            FilterPanel.Controls.Add(clearHost, 5, 0)
            FilterPanel.Controls.Add(refreshHost, 6, 0)
            FilterPanel.Visible = showFiltersInitially OrElse initialStatus.HasValue OrElse initialCategory.HasValue

            Dim card As New CardPanel With {.Dock = DockStyle.Fill, .Padding = New Padding(14, 12, 14, 4)}
            Grid.Dock = DockStyle.Fill
            card.Controls.Add(Grid)
            card.Controls.Add(Pager)
            Controls.Add(card)
            AddTopDown(Me, headerControl, Gap(6), SearchRow(Search, FilterButton), Gap(10), FilterPanel, Gap(10))

            AddHandler FilterButton.Click, Sub()
                                               FilterPanel.Visible = Not FilterPanel.Visible
                                               If FilterPanel.Visible Then CategoryFilter.Focus()
                                           End Sub
            AddHandler Search.TextChanged, Sub()
                                               _debounce.Stop()
                                               _debounce.Start()
                                           End Sub
            AddHandler _debounce.Tick, Sub()
                                           _debounce.Stop()
                                           ReloadFromFirstPage()
                                       End Sub
            AddHandler CategoryFilter.SelectionChanged, Sub() ReloadFromFirstPage()
            AddHandler StatusFilter.SelectionChanged, Sub() ReloadFromFirstPage()
            AddHandler BarangayFilter.SelectionChanged, Sub() ReloadFromFirstPage()
            AddHandler BarangayFilter.Combo.Leave, Sub() ReloadFromFirstPage()
            AddHandler DateFrom.ValueChanged, Sub() ReloadFromFirstPage()
            AddHandler DateTo.ValueChanged, Sub() ReloadFromFirstPage()
            AddHandler ClearButton.Click, Sub() ClearFilters()
            AddHandler RefreshButton.Click, Async Sub() Await RefreshDataAsync()
            AddHandler Pager.PageRequested, Async Sub(s, p)
                                                _page = p
                                                Await LoadPageAsync()
                                            End Sub
            AddHandler Grid.RowActivated, Sub(s, row) OnOpen(TryCast(row.Tag, ConcernSummary))
            AddHandler Grid.ActionClicked, Sub(s, row, key) OnAction(TryCast(row.Tag, ConcernSummary), key)
        End Sub

        Protected MustOverride Sub OnOpen(c As ConcernSummary)
        Protected MustOverride Sub OnAction(c As ConcernSummary, key As String)

        Private Sub ReloadFromFirstPage()
            If _suspendEvents OrElse Not _categoriesLoaded Then Return
            _page = 1
            Dim unused = LoadPageAsync()
        End Sub

        Private Sub ClearFilters()
            _suspendEvents = True
            Search.Text = ""
            CategoryFilter.SelectedIndex = 0
            StatusFilter.SelectedIndex = 0
            BarangayFilter.SelectedIndex = 0
            DateFrom.Value = Nothing
            DateTo.Value = Nothing
            _suspendEvents = False
            _debounce.Stop()
            ReloadFromFirstPage()
        End Sub

        Public Overrides Async Function RefreshDataAsync() As Task
            If Not _categoriesLoaded Then
                Dim r = Await RunAsync(Async Function()
                                           Dim cats = Await Svc.Concerns.GetCategoriesAsync()
                                           Dim size = Await Svc.Settings.GetIntAsync(SettingKeys.ItemsPerPage, 10)
                                           Dim barangays = Await Svc.Concerns.GetBarangaysAsync()
                                           Return (cats, size, barangays)
                                       End Function)
                If Not r.Ok OrElse IsDisposed Then Return
                _pageSize = Math.Max(5, r.Value.size)
                _suspendEvents = True
                Fill(CategoryFilter, CategoryOptions(r.Value.cats), _initialCategory)
                Fill(StatusFilter, StatusOptions(), _initialStatus)
                Fill(BarangayFilter, BarangayOptions(r.Value.barangays, "All Barangays"), Nothing)
                _suspendEvents = False
                _categoriesLoaded = True
            End If
            Await LoadPageAsync()
        End Function

        Private Async Function LoadPageAsync() As Task
            If DateFrom.Value.HasValue AndAlso DateTo.Value.HasValue AndAlso DateFrom.Value > DateTo.Value Then
                DateTo.HasError = True
                Grid.Rows.Clear()
                Grid.EmptyText = "'Date From' must be on or before 'Date To'."
                Grid.Invalidate()
                Pager.SetState(1, 1, 0, 0, 0)
                Return
            End If
            DateTo.HasError = False
            Dim filter As New ConcernFilter With {
                .Search = If(String.IsNullOrWhiteSpace(Search.Text), Nothing, Search.Text.Trim()),
                .CategoryId = SelectedInt(CategoryFilter),
                .Status = SelectedStatus(StatusFilter),
                .BarangayId = SelectedInt(BarangayFilter),
                .DateFrom = DateFrom.Value,
                .DateTo = DateTo.Value,
                .Page = _page,
                .PageSize = _pageSize}
            _loadVersion += 1
            Dim version = _loadVersion
            Dim r = Await RunAsync(Function() Svc.Concerns.SearchAsync(Session, filter))
            If IsDisposed OrElse version <> _loadVersion Then Return
            If Not r.Ok Then
                Grid.Rows.Clear()
                Grid.EmptyText = "The reports could not be loaded. Click Refresh to try again."
                Grid.Invalidate()
                Return
            End If
            Dim res = r.Value
            _page = res.Page
            Dim filtered = filter.Search IsNot Nothing OrElse filter.CategoryId.HasValue OrElse filter.Status.HasValue OrElse filter.BarangayId.HasValue OrElse filter.DateFrom.HasValue OrElse filter.DateTo.HasValue
            Grid.EmptyText = If(filtered, "No reports match your search or filters.", EmptyListText)
            FillRows(res.Items)
            Pager.SetState(res.Page, res.TotalPages, res.TotalCount, res.FirstIndex, res.LastIndex)
        End Function

        Protected Overridable ReadOnly Property EmptyListText As String
            Get
                Return "No reports yet."
            End Get
        End Property

        Protected Overridable Sub FillRows(items As List(Of ConcernSummary))
            FillConcernRows(Grid, items)
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then _debounce.Dispose()
            MyBase.Dispose(disposing)
        End Sub

    End Class

    ''' <summary>My Reports (reference screen 6): the signed-in resident's own reports.</summary>
    Public Class MyReportsPage
        Inherits ConcernListPage

        Public Sub New(Optional initialStatus As ConcernStatus? = Nothing)
            MyBase.New(New HeaderHolder().Build("My Reports", "View and monitor the status of your submitted concerns."), initialStatus, Nothing, False)
            Grid.AddText("id", "ID", 70, minWidth:=82)
            Grid.AddText("category", "Category", 130)
            Grid.AddText("location", "Location", 130)
            Grid.AddStatus("status", "Status", 90)
            Grid.AddText("date", "Date Reported", 95)
            Grid.AddActions("actions", "Actions", {
                New GridAction With {.Key = "view", .GlyphText = Glyph.OpenWindow, .Tooltip = "View details and status"},
                New GridAction With {.Key = "photos", .GlyphText = Glyph.Photo, .Color = Theme.Success, .Tooltip = "View pictures",
                                     .IsVisible = Function(o) If(TryCast(o, ConcernSummary)?.AttachmentCount, 0) > 0}})
        End Sub

        Public Overrides ReadOnly Property Title As String
            Get
                Return "My Reports"
            End Get
        End Property

        Public Overrides ReadOnly Property NavKey As String
            Get
                Return "myreports"
            End Get
        End Property

        Protected Overrides ReadOnly Property EmptyListText As String
            Get
                Return "You have not submitted any reports yet."
            End Get
        End Property

        Protected Overrides Sub OnOpen(c As ConcernSummary)
            If c IsNot Nothing Then Shell.Navigate(New ResidentConcernDetailsPage(c.Id))
        End Sub

        Protected Overrides Async Sub OnAction(c As ConcernSummary, key As String)
            If c Is Nothing Then Return
            If key = "photos" Then
                Await OpenConcernPhotosAsync(Me, c.Id)
            Else
                OnOpen(c)
            End If
        End Sub
    End Class

    ''' <summary>Submitted Concerns (reference admin screen 3): all concerns for LGU personnel.</summary>
    Public Class SubmittedConcernsPage
        Inherits ConcernListPage

        Public Sub New(Optional initialStatus As ConcernStatus? = Nothing, Optional initialCategory As Integer? = Nothing)
            MyBase.New(New HeaderHolder().Build("Submitted Concerns", Nothing), initialStatus, initialCategory, True)
            Search.Box.PlaceholderText = "Search by ID, resident, category, location..."
            Grid.AddText("id", "ID", 64, minWidth:=82)
            Grid.AddText("resident", "Resident", 105)
            Grid.AddText("category", "Category", 120)
            Grid.AddText("location", "Location", 115)
            Grid.AddText("date", "Date Reported", 88)
            Grid.AddStatus("status", "Status", 92)
            Dim actions As New List(Of GridAction) From {
                New GridAction With {.Key = "view", .GlyphText = Glyph.Document, .Tooltip = "View details"},
                New GridAction With {.Key = "photos", .GlyphText = Glyph.Photo, .Color = Theme.Success, .Tooltip = "View pictures",
                                     .IsVisible = Function(o) If(TryCast(o, ConcernSummary)?.AttachmentCount, 0) > 0}}
            If AppServices.Instance.Session.Has(Permission.ConcernUpdateStatus) Then
                actions.Add(New GridAction With {.Key = "update", .GlyphText = Glyph.Edit, .Tooltip = "Update status",
                                                 .IsVisible = Function(o) TryCast(o, ConcernSummary) IsNot Nothing AndAlso DirectCast(o, ConcernSummary).Status <> ConcernStatus.Closed})
            End If
            Grid.AddActions("actions", "Actions", actions)

            ' Add Concern opens the same Report a Concern form residents use.
            If AppServices.Instance.Session.Has(Permission.ConcernCreate) Then
                Dim add As New ModernButton("Add Concern", ButtonKind.Primary, Glyph.Add) With {.Size = New Size(150, 40), .Anchor = AnchorStyles.Top Or AnchorStyles.Right}
                Header.Controls.Add(add)
                AddHandler Header.Resize, Sub() add.Location = New Point(Header.Width - add.Width, 0)
                AddHandler add.Click, Sub() Shell.Navigate(New ReportConcernPage())
            End If
        End Sub

        Public Overrides ReadOnly Property Title As String
            Get
                Return "Submitted Concerns"
            End Get
        End Property

        Public Overrides ReadOnly Property NavKey As String
            Get
                Return "concerns"
            End Get
        End Property

        Protected Overrides ReadOnly Property EmptyListText As String
            Get
                Return "No concerns have been submitted yet."
            End Get
        End Property

        Protected Overrides Sub FillRows(items As List(Of ConcernSummary))
            FillConcernRows(Grid, items, isoDates:=True)
        End Sub

        Protected Overrides Sub OnOpen(c As ConcernSummary)
            If c IsNot Nothing Then Shell.Navigate(New AdminConcernDetailsPage(c.Id))
        End Sub

        Protected Overrides Async Sub OnAction(c As ConcernSummary, key As String)
            If c Is Nothing Then Return
            Select Case key
                Case "photos" : Await OpenConcernPhotosAsync(Me, c.Id)
                Case "update" : Shell.Navigate(New UpdateStatusPage(c.Id))
                Case Else : OnOpen(c)
            End Select
        End Sub
    End Class

    ''' <summary>Builds a page header before the base constructor runs (VB cannot call instance methods there).</summary>
    Friend Class HeaderHolder
        Public Function Build(title As String, subtitle As String) As Control
            Dim p As New Panel With {.Dock = DockStyle.Top, .Height = If(subtitle Is Nothing, 40, 58), .BackColor = Color.Transparent}
            Dim x = 0
            If subtitle IsNot Nothing Then
                Dim icon As New Label With {.Text = Glyph.Document, .Font = Theme.IconFont(18), .ForeColor = Theme.Primary, .AutoSize = False,
                                            .Size = New Size(44, 44), .TextAlign = ContentAlignment.MiddleCenter, .BackColor = Color.FromArgb(226, 236, 252), .Location = New Point(0, 4)}
                p.Controls.Add(icon)
                x = 56
            End If
            Dim t = Heading(title, 15)
            t.Location = New Point(x, 2)
            p.Controls.Add(t)
            If subtitle IsNot Nothing Then
                Dim s = Subtle(subtitle, 9)
                s.Location = New Point(x + 2, 32)
                p.Controls.Add(s)
            End If
            Return p
        End Function
    End Class

End Namespace
