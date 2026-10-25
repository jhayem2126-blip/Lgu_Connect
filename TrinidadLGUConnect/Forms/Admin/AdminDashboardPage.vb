Imports TrinidadLGUConnect.Controls
Imports TrinidadLGUConnect.Models
Imports TrinidadLGUConnect.Services

Namespace Forms

    ''' <summary>Admin Dashboard (reference admin screen 2). Counts and charts are live MySQL aggregates.</summary>
    Public Class AdminDashboardPage
        Inherits PageBase

        Private ReadOnly _welcome As Label = Subtle("", 9.5F)
        Private ReadOnly _total As New StatCard("Total Concerns", Theme.CardBlue, "")
        Private ReadOnly _submitted As New StatCard("Submitted", Theme.CardAmber, "")
        Private ReadOnly _progress As New StatCard("In Progress", Theme.CardGreen, "")
        Private ReadOnly _resolved As New StatCard("Resolved", Theme.CardPurple, "")
        Private ReadOnly _closed As New StatCard("Closed", Theme.CardRed, "")
        Private ReadOnly _bar As New BarChart With {.Dock = DockStyle.Fill}
        Private ReadOnly _donut As New DonutChart With {.Dock = DockStyle.Fill, .LegendWidth = 150}
        Private ReadOnly _updated As Label = Subtle("", 8)
        Private ReadOnly _auto As New Timer With {.Interval = 60000}
        Private ReadOnly _layoutHeader As Action
        Private ReadOnly _chip As AccountChip

        Public Sub New()
            AutoScrollMinSize = New Size(900, 620)
            Dim root As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 3, .BackColor = Color.Transparent}
            root.RowStyles.Add(New RowStyle(SizeType.Absolute, 72))
            root.RowStyles.Add(New RowStyle(SizeType.Absolute, 112))
            root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
            Controls.Add(root)

            ' Header with account chip on the right
            Dim header As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent, .Margin = Padding.Empty}
            Dim t = Heading("Dashboard", 16)
            t.Location = New Point(0, 2)
            _welcome.Location = New Point(2, 38)
            header.Controls.Add(t)
            header.Controls.Add(_welcome)
            Dim chip As New AccountChip(Session.FullName, Session.RoleTitle) With {.Anchor = AnchorStyles.Top Or AnchorStyles.Right, .Size = New Size(230, 48)}
            header.Controls.Add(chip)
            _chip = chip
            Dim refresh As New ModernButton("", ButtonKind.Secondary, Glyph.Refresh) With {.Size = New Size(38, 34), .Anchor = AnchorStyles.Top Or AnchorStyles.Right, .AccessibleName = "Refresh dashboard"}
            header.Controls.Add(refresh)
            _updated.Anchor = AnchorStyles.Top Or AnchorStyles.Right
            header.Controls.Add(_updated)
            _layoutHeader = Sub()
                                chip.Location = New Point(header.Width - chip.Width, 4)
                                refresh.Location = New Point(chip.Left - refresh.Width - 12, 10)
                                _updated.Location = New Point(refresh.Left - _updated.PreferredWidth - 8, 20)
                            End Sub
            AddHandler header.Resize, Sub() _layoutHeader()
            AddHandler refresh.Click, Async Sub() Await RefreshDataAsync()
            AddHandler chip.MenuRequested, Sub() ShowAccountMenu(chip)
            root.Controls.Add(header, 0, 0)

            Dim cards As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 5, .RowCount = 1, .BackColor = Color.Transparent, .Margin = New Padding(0, 0, 0, 12)}
            Dim all = {_total, _submitted, _progress, _resolved, _closed}
            For i = 0 To 4
                cards.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 20))
                all(i).Dock = DockStyle.Fill
                all(i).Margin = New Padding(If(i = 0, 0, 7), 0, If(i = 4, 0, 7), 0)
                cards.Controls.Add(all(i), i, 0)
            Next
            root.Controls.Add(cards, 0, 1)
            AddHandler _total.Click, Sub() Shell.NavigateReplace(New SubmittedConcernsPage())
            AddHandler _submitted.Click, Sub() Shell.NavigateReplace(New SubmittedConcernsPage(ConcernStatus.Submitted))
            AddHandler _progress.Click, Sub() Shell.NavigateReplace(New SubmittedConcernsPage(ConcernStatus.InProgress))
            AddHandler _resolved.Click, Sub() Shell.NavigateReplace(New SubmittedConcernsPage(ConcernStatus.Resolved))
            AddHandler _closed.Click, Sub() Shell.NavigateReplace(New SubmittedConcernsPage(ConcernStatus.Closed))

            Dim charts As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1, .BackColor = Color.Transparent}
            charts.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 58))
            charts.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 42))
            Dim barCard = TitledCard("Concerns by Category", 10.5F)
            barCard.Margin = New Padding(0, 0, 8, 0)
            barCard.Padding = New Padding(14, 50, 14, 10)
            barCard.Controls.Add(_bar)
            Dim donutCard = TitledCard("Concerns by Status", 10.5F)
            donutCard.Margin = New Padding(8, 0, 0, 0)
            donutCard.Padding = New Padding(14, 50, 14, 14)
            donutCard.Controls.Add(_donut)
            charts.Controls.Add(barCard, 0, 0)
            charts.Controls.Add(donutCard, 1, 0)
            root.Controls.Add(charts, 0, 2)
            _bar.ColorFor = Function(i) If(i.Key = 8, Theme.CardPurple, Theme.CardBlue)
            AddHandler _bar.BarClicked, Sub(s, item) Shell.NavigateReplace(New SubmittedConcernsPage(initialCategory:=item.Key))
            AddHandler _donut.SliceClicked, Sub(s, item) Shell.NavigateReplace(New SubmittedConcernsPage(CType(item.Key, ConcernStatus)))
            AddHandler _auto.Tick, Async Sub() Await RefreshDataAsync()
            _auto.Start()
        End Sub

        Public Overrides ReadOnly Property Title As String
            Get
                Return "Admin Dashboard"
            End Get
        End Property

        Public Overrides ReadOnly Property NavKey As String
            Get
                Return "dashboard"
            End Get
        End Property

        Public Overrides Async Function RefreshDataAsync() As Task
            Dim r = Await RunAsync(Async Function()
                                       Dim stats = Await Svc.Concerns.GetAdminDashboardAsync(Session)
                                       Dim place = Await Svc.Settings.GetStringAsync(SettingKeys.Municipality, "Trinidad, Bohol")
                                       Dim photo = Await Svc.Users.GetMyPhotoAsync(Session)
                                       Return (stats, place, photo)
                                   End Function)
            If Not r.Ok OrElse IsDisposed Then Return
            Dim s = r.Value.stats
            Dim first = Session.FullName.Split(" "c)(0)
            _welcome.Text = $"Welcome, {first}!   |   {r.Value.place}"
            _chip.Photo = AttachmentService.TryDecode(r.Value.photo)
            _total.Value = s.Total.ToString()
            _submitted.Value = s.Count(ConcernStatus.Submitted).ToString()
            _progress.Value = s.Count(ConcernStatus.InProgress).ToString()
            _resolved.Value = s.Count(ConcernStatus.Resolved).ToString()
            _closed.Value = s.Count(ConcernStatus.Closed).ToString()
            _bar.SetData(s.ByCategory)
            _donut.SetData(AllStatuses.Select(Function(st) New CountItem With {
                .Label = DisplayName(st), .Value = s.Count(st), .Key = CInt(st),
                .ColorHex = ColorTranslator.ToHtml(Theme.StatusChart(st))}))
            _updated.Text = $"Updated {DateTime.Now.ToString("hh:mm tt", Globalization.CultureInfo.GetCultureInfo("en-US"))}"
            _layoutHeader()
        End Function

        Private Sub ShowAccountMenu(chip As Control)
            Dim menu As New ContextMenuStrip With {.ShowImageMargin = False, .Font = Theme.F(9)}
            menu.Items.Add(New ToolStripLabel($"{Session.FullName} ({Session.Username})") With {.Font = Theme.F(9, FontStyle.Bold)})
            menu.Items.Add(New ToolStripSeparator())
            Dim profile As New ToolStripMenuItem("My Profile")
            AddHandler profile.Click, Sub() Shell.NavigateRoot("profile")
            menu.Items.Add(profile)
            Dim settings As New ToolStripMenuItem("Settings")
            AddHandler settings.Click, Sub() Shell.NavigateRoot("settings")
            Dim pwd As New ToolStripMenuItem("Change Password...")
            AddHandler pwd.Click, Sub()
                                      Using dlg As New ChangePasswordDialog()
                                          dlg.ShowDialog(FindForm())
                                      End Using
                                  End Sub
            menu.Items.Add(settings)
            menu.Items.Add(pwd)
            AddHandler menu.Closed, Sub() BeginInvoke(Sub() menu.Dispose())
            menu.Show(chip, New Point(0, chip.Height))
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then _auto.Dispose()
            MyBase.Dispose(disposing)
        End Sub

    End Class

    ''' <summary>Avatar + name + role shown at the top-right of the admin dashboard; opens an account menu.</summary>
    Public Class AccountChip
        Inherits Control

        Private ReadOnly _name As String
        Private ReadOnly _role As String
        Private _hover As Boolean
        Private _photo As Image

        ''' <summary>Profile photo shown in the circle (owned by the chip).</summary>
        <ComponentModel.DesignerSerializationVisibility(ComponentModel.DesignerSerializationVisibility.Hidden)>
        Public Property Photo As Image
            Get
                Return _photo
            End Get
            Set(value As Image)
                If _photo IsNot Nothing AndAlso _photo IsNot value Then _photo.Dispose()
                _photo = value
                Invalidate()
            End Set
        End Property

        Public Event MenuRequested As EventHandler

        Public Sub New(name As String, role As String)
            _name = name
            _role = role
            SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.UserPaint Or ControlStyles.ResizeRedraw Or ControlStyles.Selectable, True)
            Cursor = Cursors.Hand
            TabStop = True
            AccessibleName = $"Account menu for {name}"
            AccessibleRole = AccessibleRole.ButtonMenu
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            Theme.PaintParent(Me, g)
            Theme.HighQuality(g)
            If _hover OrElse Focused Then
                Using p = Theme.RoundedRect(New RectangleF(0, 0, Width - 1, Height - 1), 8), b As New SolidBrush(Color.FromArgb(230, 237, 248))
                    g.FillPath(b, p)
                End Using
            End If
            Dim av As New Rectangle(6, (Height - 38) \ 2, 38, 38)
            Using b As New SolidBrush(Color.FromArgb(210, 222, 240))
                g.FillEllipse(b, av)
            End Using
            If _photo IsNot Nothing Then
                Using p As New Drawing2D.GraphicsPath()
                    p.AddEllipse(av)
                    Dim old = g.Clip
                    g.SetClip(p)
                    g.DrawImage(_photo, av)
                    g.Clip = old
                End Using
            Else
                Theme.DrawGlyph(g, Glyph.Contact, av, Theme.Navy, 13)
            End If
            TextRenderer.DrawText(g, _name, Theme.F(9, FontStyle.Bold), New Rectangle(52, Height \ 2 - 18, Width - 56, 18), Theme.Ink, TextFormatFlags.NoPrefix Or TextFormatFlags.EndEllipsis)
            TextRenderer.DrawText(g, _role, Theme.F(8.25F), New Rectangle(52, Height \ 2 + 1, Width - 56, 18), Theme.Muted, TextFormatFlags.NoPrefix Or TextFormatFlags.EndEllipsis)
        End Sub

        Protected Overrides Sub OnMouseEnter(e As EventArgs)
            _hover = True : Invalidate() : MyBase.OnMouseEnter(e)
        End Sub
        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            _hover = False : Invalidate() : MyBase.OnMouseLeave(e)
        End Sub
        Protected Overrides Sub OnClick(e As EventArgs)
            MyBase.OnClick(e)
            RaiseEvent MenuRequested(Me, EventArgs.Empty)
        End Sub
        Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
            MyBase.OnKeyDown(e)
            If e.KeyCode = Keys.Enter OrElse e.KeyCode = Keys.Space Then RaiseEvent MenuRequested(Me, EventArgs.Empty)
        End Sub
    End Class

End Namespace
