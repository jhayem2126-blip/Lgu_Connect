Imports System.Globalization
Imports TrinidadLGUConnect.Controls
Imports TrinidadLGUConnect.Helpers
Imports TrinidadLGUConnect.Models
Imports TrinidadLGUConnect.Services

Namespace Forms

    ''' <summary>Resident Dashboard (reference screen 3). All counts come from the resident's own records.</summary>
    Public Class ResidentDashboardPage
        Inherits PageBase

        Private ReadOnly _welcome As Label = Heading("Welcome!", 16)
        Private ReadOnly _date As Label = Lbl("", 9.5F, FontStyle.Regular, Theme.Ink)
        Private ReadOnly _time As Label = Lbl("", 9.5F, FontStyle.Bold, Theme.Ink)
        Private ReadOnly _bell As New NotificationBell()
        Private ReadOnly _total As New StatCard("Total Reports", Theme.CardBlue, Glyph.Document)
        Private ReadOnly _progress As New StatCard("In Progress", Theme.CardAmber, Glyph.Clock)
        Private ReadOnly _resolved As New StatCard("Resolved", Theme.CardGreen, Glyph.CheckMark)
        Private ReadOnly _closed As New StatCard("Closed", Theme.CardRed, Glyph.Cancel)
        Private ReadOnly _grid As New ThemedGrid With {.EmptyText = "You have not submitted any reports yet. Click 'Report a Concern' to submit one."}
        Private ReadOnly _clock As New Timer With {.Interval = 15000}

        Public Overrides ReadOnly Property Title As String
            Get
                Return "Resident Dashboard"
            End Get
        End Property

        Public Overrides ReadOnly Property NavKey As String
            Get
                Return "dashboard"
            End Get
        End Property

        Public Sub New()
            AutoScrollMinSize = New Size(780, 600)
            Dim root As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 3, .BackColor = Color.Transparent}
            root.RowStyles.Add(New RowStyle(SizeType.Absolute, 80))
            root.RowStyles.Add(New RowStyle(SizeType.Absolute, 120))
            root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
            Controls.Add(root)

            ' Header
            Dim header As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent, .Margin = Padding.Empty}
            _welcome.Location = New Point(0, 6)
            Dim tagline = Subtle("Together we can build a cleaner, safer and better Trinidad.", 9.5F)
            tagline.Location = New Point(2, 40)
            header.Controls.Add(_welcome)
            header.Controls.Add(tagline)
            _date.Anchor = AnchorStyles.Top Or AnchorStyles.Right
            _time.Anchor = AnchorStyles.Top Or AnchorStyles.Right
            _bell.Anchor = AnchorStyles.Top Or AnchorStyles.Right
            header.Controls.Add(_date)
            header.Controls.Add(_time)
            header.Controls.Add(_bell)
            AddHandler header.Resize, Sub()
                                          _bell.Location = New Point(header.Width - _bell.Width, 8)
                                          Dim sep = _bell.Left - 18
                                          _date.Location = New Point(sep - _date.PreferredWidth, 10)
                                          _time.Location = New Point(sep - _time.PreferredWidth, 32)
                                      End Sub
            root.Controls.Add(header, 0, 0)

            ' Statistic cards
            Dim cards As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 4, .RowCount = 1, .BackColor = Color.Transparent, .Margin = New Padding(0, 4, 0, 12)}
            Dim allCards = {_total, _progress, _resolved, _closed}
            For i = 0 To 3
                cards.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 25))
                allCards(i).Dock = DockStyle.Fill
                allCards(i).Margin = New Padding(If(i = 0, 0, 8), 0, If(i = 3, 0, 8), 0)
                cards.Controls.Add(allCards(i), i, 0)
            Next
            root.Controls.Add(cards, 0, 1)
            AddHandler _total.Click, Sub() Shell.NavigateReplace(New MyReportsPage())
            AddHandler _progress.Click, Sub() Shell.NavigateReplace(New MyReportsPage(ConcernStatus.InProgress))
            AddHandler _resolved.Click, Sub() Shell.NavigateReplace(New MyReportsPage(ConcernStatus.Resolved))
            AddHandler _closed.Click, Sub() Shell.NavigateReplace(New MyReportsPage(ConcernStatus.Closed))

            ' Recent reports
            Dim recent = TitledCard("Recent Reports")
            Dim viewAll As New LinkText("View All")
            viewAll.Anchor = AnchorStyles.Top Or AnchorStyles.Right
            recent.Controls.Add(viewAll)
            AddHandler recent.Resize, Sub() viewAll.Location = New Point(recent.Width - viewAll.PreferredWidth - 18, 16)
            AddHandler viewAll.Click, Sub() Shell.NavigateRoot("myreports")
            _grid.AddText("id", "ID", 70, bold:=False)
            _grid.AddText("category", "Category", 130)
            _grid.AddText("location", "Location", 130)
            _grid.AddStatus("status", "Status", 90)
            _grid.AddText("date", "Date Reported", 95)
            _grid.AddActions("actions", "Actions", {New GridAction With {.Key = "view", .GlyphText = Glyph.OpenWindow, .Tooltip = "View details"}})
            _grid.Location = New Point(16, 50)
            _grid.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom
            recent.Controls.Add(_grid)
            AddHandler recent.Resize, Sub() _grid.Size = New Size(recent.Width - 32, recent.Height - 66)
            AddHandler _grid.RowActivated, Sub(s, row) OpenDetails(row)
            AddHandler _grid.ActionClicked, Sub(s, row, key) OpenDetails(row)
            root.Controls.Add(recent, 0, 2)

            AddHandler _bell.NotificationClicked, Sub(s, concernId) Shell.Navigate(New ResidentConcernDetailsPage(concernId))
            AddHandler _clock.Tick, Sub() UpdateClock()
            UpdateClock()
            _clock.Start()
        End Sub

        Private Sub UpdateClock()
            Dim c = CultureInfo.GetCultureInfo("en-US")
            _date.Text = DateTime.Now.ToString("MMM dd, yyyy", c)
            _time.Text = DateTime.Now.ToString("hh:mm tt", c)
            Dim header = _date.Parent
            If header IsNot Nothing Then
                Dim sep = _bell.Left - 18
                _date.Left = sep - _date.PreferredWidth
                _time.Left = sep - _time.PreferredWidth
            End If
        End Sub

        Private Sub OpenDetails(row As DataGridViewRow)
            Dim c = TryCast(row?.Tag, ConcernSummary)
            If c IsNot Nothing Then Shell.Navigate(New ResidentConcernDetailsPage(c.Id))
        End Sub

        Public Overrides Async Function RefreshDataAsync() As Task
            _welcome.Text = $"Welcome, {Session.FullName}!"
            Dim r = Await RunAsync(Function() Svc.Concerns.GetResidentDashboardAsync(Session))
            If Not r.Ok OrElse IsDisposed Then Return
            Dim s = r.Value
            _total.Value = s.Total.ToString()
            _progress.Value = s.Count(ConcernStatus.InProgress).ToString()
            _resolved.Value = s.Count(ConcernStatus.Resolved).ToString()
            _closed.Value = s.Count(ConcernStatus.Closed).ToString()
            FillConcernRows(_grid, s.Recent)
            Await _bell.RefreshAsync()
        End Function

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then _clock.Dispose()
            MyBase.Dispose(disposing)
        End Sub

    End Class

    ''' <summary>Bell icon with an unread badge; lists recent status updates on the resident's reports.</summary>
    Public Class NotificationBell
        Inherits Control

        Private _items As New List(Of StatusHistoryEntry)()
        Private _unread As Integer
        Private ReadOnly _menu As New ContextMenuStrip With {.ShowImageMargin = False, .Font = Theme.F(9)}

        Public Event NotificationClicked(sender As Object, concernId As Integer)

        Public Sub New()
            SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.UserPaint Or ControlStyles.Selectable, True)
            Size = New Size(40, 40)
            Cursor = Cursors.Hand
            TabStop = True
            AccessibleName = "Notifications"
            AccessibleRole = AccessibleRole.PushButton
        End Sub

        Public Async Function RefreshAsync() As Task
            Dim r = Await TryRunAsync(Me, Function() AppServices.Instance.Concerns.GetResidentNotificationsAsync(AppServices.Instance.Session), showErrors:=False)
            If Not r.Ok OrElse IsDisposed Then Return
            _items = r.Value.Items
            _unread = r.Value.UnreadCount
            AccessibleName = $"Notifications, {_unread} unread"
            Invalidate()
        End Function

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Theme.PaintParent(Me, e.Graphics)
            Theme.HighQuality(e.Graphics)
            Theme.DrawGlyph(e.Graphics, Glyph.Bell, New Rectangle(0, 2, 36, 36), Theme.Navy, 15)
            If _unread > 0 Then
                Dim r As New Rectangle(Width - 18, 2, 16, 16)
                Using b As New SolidBrush(Theme.Danger)
                    e.Graphics.FillEllipse(b, r)
                End Using
                TextRenderer.DrawText(e.Graphics, If(_unread > 9, "9+", _unread.ToString()), Theme.F(7, FontStyle.Bold), r, Color.White, TextFormatFlags.NoPrefix Or TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End If
        End Sub

        Protected Overrides Sub OnClick(e As EventArgs)
            MyBase.OnClick(e)
            ShowMenu()
        End Sub

        Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
            MyBase.OnKeyDown(e)
            If e.KeyCode = Keys.Enter OrElse e.KeyCode = Keys.Space Then ShowMenu()
        End Sub

        Private Async Sub ShowMenu()
            _menu.Items.Clear()
            Dim header As New ToolStripLabel("Status updates on your reports") With {.Font = Theme.F(9, FontStyle.Bold), .ForeColor = Theme.Navy}
            _menu.Items.Add(header)
            _menu.Items.Add(New ToolStripSeparator())
            If _items.Count = 0 Then
                _menu.Items.Add(New ToolStripLabel("No updates yet.") With {.ForeColor = Theme.Muted})
            Else
                For Each it In _items
                    Dim id = it.ConcernId
                    Dim text = $"{it.ChangedByDisplay} is now {DisplayName(it.ToStatus)}  ·  {TextFormat.DateTimeText(it.ChangedAt)}"
                    If Not String.IsNullOrWhiteSpace(it.Remarks) Then text &= vbLf & "      " & If(it.Remarks.Length > 70, it.Remarks.Substring(0, 70) & "…", it.Remarks)
                    Dim mi As New ToolStripMenuItem(text) With {.ForeColor = Theme.StatusSolid(it.ToStatus)}
                    AddHandler mi.Click, Sub() RaiseEvent NotificationClicked(Me, id)
                    _menu.Items.Add(mi)
                Next
            End If
            _menu.Show(Me, New Point(Width - 380, Height + 4))
            If _unread > 0 Then
                _unread = 0
                Invalidate()
                Await TryRunAsync(Me, Function() AppServices.Instance.Concerns.MarkNotificationsSeenAsync(AppServices.Instance.Session), showErrors:=False)
            End If
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then _menu.Dispose()
            MyBase.Dispose(disposing)
        End Sub
    End Class

End Namespace
