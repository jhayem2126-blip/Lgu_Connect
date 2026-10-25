Imports System.ComponentModel
Imports System.Drawing.Drawing2D

Namespace Controls

    Public Class NavItem
        Public Property Key As String = ""
        Public Property Text As String = ""
        Public Property GlyphText As String = ""
        Friend Bounds As Rectangle
    End Class

    ''' <summary>The navy sidebar: profile header, navigation items and Logout at the bottom.</summary>
    Public Class Sidebar
        Inherits Control

        Private ReadOnly _items As New List(Of NavItem)()
        Private _selectedKey As String = ""
        Private _hoverKey As String = ""
        Private _focusIndex As Integer = -1
        Private _logoutBounds As Rectangle

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property Title As String = ""
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property Subtitle As String = ""
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property AvatarImage As Image

        Public Event ItemSelected(sender As Object, key As String)
        Public Event LogoutClicked As EventHandler

        Public Sub New()
            SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw Or ControlStyles.UserPaint Or ControlStyles.Selectable, True)
            Width = 222
            Dock = DockStyle.Left
            TabStop = True
            AccessibleRole = AccessibleRole.MenuBar
            AccessibleName = "Navigation"
        End Sub

        Public Sub AddItem(key As String, text As String, glyphText As String)
            _items.Add(New NavItem With {.Key = key, .Text = text, .GlyphText = glyphText})
            Invalidate()
        End Sub

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property SelectedKey As String
            Get
                Return _selectedKey
            End Get
            Set(value As String)
                _selectedKey = If(value, "")
                Invalidate()
            End Set
        End Property

        Public ReadOnly Property Items As IReadOnlyList(Of NavItem)
            Get
                Return _items
            End Get
        End Property

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            Theme.HighQuality(g)
            Using br As New LinearGradientBrush(ClientRectangle, Theme.SidebarTop, Theme.SidebarBottom, LinearGradientMode.Vertical)
                g.FillRectangle(br, ClientRectangle)
            End Using

            ' Header
            Dim av As New Rectangle(16, 18, 46, 46)
            Using b As New SolidBrush(Color.FromArgb(230, 236, 246))
                g.FillEllipse(b, av)
            End Using
            If AvatarImage IsNot Nothing Then
                Using p As New GraphicsPath()
                    p.AddEllipse(av)
                    Dim oldClip = g.Clip
                    g.SetClip(p)
                    g.DrawImage(AvatarImage, av)
                    g.Clip = oldClip
                End Using
            Else
                Theme.DrawGlyph(g, Glyph.Contact, av, Color.FromArgb(120, 134, 158), 18)
            End If
            Using pen As New Pen(Color.FromArgb(240, 200, 60), 1.6F)
                g.DrawEllipse(pen, av)
            End Using
            TextRenderer.DrawText(g, Title, Theme.F(9.5F, FontStyle.Bold), New Rectangle(70, 22, Width - 76, 20), Color.White, TextFormatFlags.NoPrefix Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPadding)
            TextRenderer.DrawText(g, Subtitle, Theme.F(8.25F), New Rectangle(70, 43, Width - 76, 18), Theme.SidebarText, TextFormatFlags.NoPrefix Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPadding)

            Dim y = 92
            For i = 0 To _items.Count - 1
                Dim it = _items(i)
                it.Bounds = New Rectangle(10, y, Width - 20, 42)
                DrawItem(g, it, it.Key = _selectedKey, it.Key = _hoverKey, Focused AndAlso i = _focusIndex)
                y += 46
            Next

            _logoutBounds = New Rectangle(10, Height - 62, Width - 20, 42)
            Dim logout As New NavItem With {.Key = "__logout", .Text = "Logout", .GlyphText = Glyph.SignOut, .Bounds = _logoutBounds}
            DrawItem(g, logout, False, _hoverKey = "__logout", Focused AndAlso _focusIndex = _items.Count)
        End Sub

        Private Sub DrawItem(g As Graphics, it As NavItem, selected As Boolean, hover As Boolean, focus As Boolean)
            If selected OrElse hover Then
                Using p = Theme.RoundedRect(it.Bounds, 7), b As New SolidBrush(If(selected, Theme.SidebarActive, Theme.SidebarHover))
                    g.FillPath(b, p)
                End Using
            End If
            If focus Then
                Using p = Theme.RoundedRect(Rectangle.Inflate(it.Bounds, -2, -2), 6), pen As New Pen(Color.FromArgb(160, 200, 255)) With {.DashStyle = DashStyle.Dot}
                    g.DrawPath(pen, p)
                End Using
            End If
            Theme.DrawGlyph(g, it.GlyphText, New Rectangle(it.Bounds.X + 10, it.Bounds.Y, 24, it.Bounds.Height), Color.White, 11)
            TextRenderer.DrawText(g, it.Text, Theme.F(9.25F, If(selected, FontStyle.Bold, FontStyle.Regular)),
                                  New Rectangle(it.Bounds.X + 44, it.Bounds.Y, it.Bounds.Width - 48, it.Bounds.Height), Color.White,
                                  TextFormatFlags.NoPrefix Or TextFormatFlags.VerticalCenter Or TextFormatFlags.Left Or TextFormatFlags.EndEllipsis)
        End Sub

        Private Function HitTest(pt As Point) As String
            For Each it In _items
                If it.Bounds.Contains(pt) Then Return it.Key
            Next
            If _logoutBounds.Contains(pt) Then Return "__logout"
            Return ""
        End Function

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            Dim k = HitTest(e.Location)
            If k <> _hoverKey Then
                _hoverKey = k
                Cursor = If(k.Length > 0, Cursors.Hand, Cursors.Default)
                Invalidate()
            End If
        End Sub

        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            MyBase.OnMouseLeave(e)
            _hoverKey = ""
            Invalidate()
        End Sub

        Protected Overrides Sub OnMouseClick(e As MouseEventArgs)
            MyBase.OnMouseClick(e)
            Activate(HitTest(e.Location))
        End Sub

        Private Sub Activate(k As String)
            If k = "__logout" Then
                RaiseEvent LogoutClicked(Me, EventArgs.Empty)
            ElseIf k.Length > 0 Then
                RaiseEvent ItemSelected(Me, k)
            End If
        End Sub

        Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
            If keyData = Keys.Up OrElse keyData = Keys.Down OrElse keyData = Keys.Enter OrElse keyData = Keys.Space Then Return True
            Return MyBase.IsInputKey(keyData)
        End Function

        Protected Overrides Sub OnGotFocus(e As EventArgs)
            MyBase.OnGotFocus(e)
            If _focusIndex < 0 Then _focusIndex = Math.Max(0, _items.FindIndex(Function(i) i.Key = _selectedKey))
            Invalidate()
        End Sub

        Protected Overrides Sub OnLostFocus(e As EventArgs)
            MyBase.OnLostFocus(e)
            Invalidate()
        End Sub

        Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
            MyBase.OnKeyDown(e)
            Dim count = _items.Count + 1
            Select Case e.KeyCode
                Case Keys.Down : _focusIndex = (_focusIndex + 1) Mod count : Invalidate()
                Case Keys.Up : _focusIndex = (_focusIndex - 1 + count) Mod count : Invalidate()
                Case Keys.Enter, Keys.Space
                    Activate(If(_focusIndex = _items.Count, "__logout", If(_focusIndex >= 0, _items(_focusIndex).Key, "")))
            End Select
        End Sub
    End Class

    ''' <summary>Coloured statistic card (title, big number, icon). Clickable.</summary>
    Public Class StatCard
        Inherits Control

        Private _hover As Boolean
        Private _value As String = "0"

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property CardColor As Color = Theme.CardBlue
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property Title As String = ""
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property GlyphText As String = ""

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property Value As String
            Get
                Return _value
            End Get
            Set(v As String)
                _value = v
                AccessibleName = $"{Title}: {v}"
                Invalidate()
            End Set
        End Property

        Public Sub New(title As String, color As Color, glyphText As String)
            SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw Or ControlStyles.UserPaint Or ControlStyles.Selectable, True)
            Me.Title = title
            CardColor = color
            Me.GlyphText = glyphText
            Cursor = Cursors.Hand
            Size = New Size(180, 92)
            TabStop = True
            AccessibleRole = AccessibleRole.PushButton
            AccessibleName = title
        End Sub

        Protected Overrides Sub OnMouseEnter(e As EventArgs)
            _hover = True : Invalidate() : MyBase.OnMouseEnter(e)
        End Sub
        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            _hover = False : Invalidate() : MyBase.OnMouseLeave(e)
        End Sub
        Protected Overrides Sub OnGotFocus(e As EventArgs)
            Invalidate() : MyBase.OnGotFocus(e)
        End Sub
        Protected Overrides Sub OnLostFocus(e As EventArgs)
            Invalidate() : MyBase.OnLostFocus(e)
        End Sub
        Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
            MyBase.OnKeyDown(e)
            If e.KeyCode = Keys.Enter OrElse e.KeyCode = Keys.Space Then OnClick(EventArgs.Empty)
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            Theme.PaintParent(Me, g)
            Theme.HighQuality(g)
            Dim r As New RectangleF(0.5F, 0.5F, Width - 1.5F, Height - 1.5F)
            Dim c1 = If(_hover, Theme.Blend(CardColor, Color.Black, 0.06), CardColor)
            Using p = Theme.RoundedRect(r, 10), br As New LinearGradientBrush(ClientRectangle, Theme.Blend(c1, Color.White, 0.08), c1, LinearGradientMode.ForwardDiagonal)
                g.FillPath(br, p)
                ' Soft decorative circle like the reference cards.
                Dim oldClip = g.Clip
                g.SetClip(p)
                Using circle As New SolidBrush(Color.FromArgb(28, Color.White))
                    g.FillEllipse(circle, Width - 70, -30, 110, 110)
                End Using
                g.Clip = oldClip
            End Using
            If Focused Then
                Using pen As New Pen(Color.FromArgb(200, Color.White), 1.5F) With {.DashStyle = DashStyle.Dot}
                    g.DrawPath(pen, Theme.RoundedRect(New RectangleF(3, 3, Width - 7, Height - 7), 8))
                End Using
            End If
            TextRenderer.DrawText(g, Title, Theme.F(10), New Rectangle(16, 14, Width - 24, 22), Color.White, TextFormatFlags.NoPrefix Or TextFormatFlags.EndEllipsis Or TextFormatFlags.NoPadding)
            TextRenderer.DrawText(g, _value, Theme.F(22, FontStyle.Bold), New Rectangle(14, 36, Width - 60, 44), Color.White, TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding Or TextFormatFlags.VerticalCenter)
            If GlyphText.Length > 0 Then
                Theme.DrawGlyph(g, GlyphText, New Rectangle(Width - 46, Height - 44, 34, 34), Color.FromArgb(235, Color.White), 16)
            End If
        End Sub
    End Class

    ''' <summary>Rounded status/category pill as a standalone control.</summary>
    Public Class Pill
        Inherits Control

        Public Property PillBack As Color = Theme.CategoryPillBack
        Public Property PillFore As Color = Theme.CategoryPillFore

        Public Sub New(Optional text As String = "")
            SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw Or ControlStyles.UserPaint, True)
            Me.Text = text
            Font = Theme.F(8.25F, FontStyle.Bold)
            Height = 24
        End Sub

        Public Sub SetStatus(status As Models.ConcernStatus, Optional solid As Boolean = False)
            Text = Models.DisplayName(status)
            If solid Then
                PillBack = Theme.StatusSolid(status)
                PillFore = Color.White
            Else
                Dim c = Theme.BadgeColors(status)
                PillBack = c.Back
                PillFore = c.Fore
            End If
            FitWidth()
            Invalidate()
        End Sub

        Public Sub FitWidth()
            Width = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding).Width + 22
        End Sub

        Protected Overrides Sub OnTextChanged(e As EventArgs)
            MyBase.OnTextChanged(e)
            FitWidth()
            Invalidate()
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Theme.PaintParent(Me, e.Graphics)
            Theme.DrawPill(e.Graphics, Text, ClientRectangle, PillBack, PillFore, Font, center:=False)
        End Sub
    End Class

    ''' <summary>"Showing 1 to 5 of 48 entries" plus  ‹ 1 2 3 4 5 › page buttons.</summary>
    Public Class Pager
        Inherits Control

        Private _page As Integer = 1
        Private _totalPages As Integer = 1
        Private _total As Integer
        Private _first As Integer
        Private _last As Integer
        Private ReadOnly _hits As New List(Of (Bounds As Rectangle, Page As Integer, Enabled As Boolean))()
        Private _hover As Integer = -1

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property ShowSummary As Boolean = True

        Public Event PageRequested(sender As Object, page As Integer)

        Public Sub New()
            SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw Or ControlStyles.UserPaint, True)
            Height = 40
        End Sub

        Public Sub SetState(page As Integer, totalPages As Integer, total As Integer, first As Integer, last As Integer)
            _page = page : _totalPages = Math.Max(1, totalPages) : _total = total : _first = first : _last = last
            Invalidate()
        End Sub

        Public ReadOnly Property CurrentPage As Integer
            Get
                Return _page
            End Get
        End Property

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            Theme.PaintParent(Me, g)
            Theme.HighQuality(g)
            If ShowSummary Then
                Dim text = If(_total = 0, "No entries to show", $"Showing {_first} to {_last} of {_total} entries")
                TextRenderer.DrawText(g, text, Theme.F(8.75F), New Rectangle(0, 0, Width \ 2, Height), Theme.Muted, TextFormatFlags.NoPrefix Or TextFormatFlags.VerticalCenter Or TextFormatFlags.Left)
            End If
            _hits.Clear()
            ' Up to 5 numbered buttons around the current page.
            Dim startP = Math.Max(1, Math.Min(_page - 2, _totalPages - 4))
            Dim endP = Math.Min(_totalPages, startP + 4)
            Dim buttons As New List(Of (Text As String, Page As Integer, Enabled As Boolean, IsCurrent As Boolean, IsGlyph As Boolean))()
            buttons.Add((Glyph.ChevronLeft, _page - 1, _page > 1, False, True))
            For p = startP To endP
                buttons.Add((p.ToString(), p, True, p = _page, False))
            Next
            buttons.Add((Glyph.ChevronRight, _page + 1, _page < _totalPages, False, True))
            Dim size = 30, gap = 6
            Dim x = Width - (buttons.Count * (size + gap) - gap)
            Dim y = (Height - size) \ 2
            For i = 0 To buttons.Count - 1
                Dim bt = buttons(i)
                Dim r As New Rectangle(x, y, size, size)
                Dim fill = If(bt.IsCurrent, Theme.Primary, If(_hover = i AndAlso bt.Enabled, Color.FromArgb(240, 245, 252), Color.White))
                Using p = Theme.RoundedRect(r, 6), b As New SolidBrush(fill), pen As New Pen(If(bt.IsCurrent, Theme.Primary, Theme.InputBorder))
                    g.FillPath(b, p)
                    g.DrawPath(pen, p)
                End Using
                Dim fore = If(bt.IsCurrent, Color.White, If(bt.Enabled, Theme.Ink, Theme.Placeholder))
                If bt.IsGlyph Then
                    Theme.DrawGlyph(g, bt.Text, r, fore, 8)
                Else
                    TextRenderer.DrawText(g, bt.Text, Theme.F(9, FontStyle.Bold), r, fore, TextFormatFlags.NoPrefix Or TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
                End If
                _hits.Add((r, bt.Page, bt.Enabled AndAlso Not bt.IsCurrent))
                x += size + gap
            Next
        End Sub

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            Dim idx = _hits.FindIndex(Function(h) h.Bounds.Contains(e.Location))
            Cursor = If(idx >= 0 AndAlso _hits(idx).Enabled, Cursors.Hand, Cursors.Default)
            If idx <> _hover Then
                _hover = idx
                Invalidate()
            End If
        End Sub

        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            MyBase.OnMouseLeave(e)
            _hover = -1
            Invalidate()
        End Sub

        Protected Overrides Sub OnMouseClick(e As MouseEventArgs)
            MyBase.OnMouseClick(e)
            For Each h In _hits
                If h.Bounds.Contains(e.Location) AndAlso h.Enabled Then
                    RaiseEvent PageRequested(Me, h.Page)
                    Return
                End If
            Next
        End Sub
    End Class

    ''' <summary>Thin indeterminate progress strip shown while a page loads data.</summary>
    Public Class BusyBar
        Inherits Control

        Private ReadOnly _timer As New Timer With {.Interval = 30}
        Private _offset As Integer

        Public Sub New()
            SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.UserPaint, True)
            Height = 3
            Dock = DockStyle.Top
            Visible = False
            AddHandler _timer.Tick, Sub()
                                        _offset = (_offset + 12) Mod Math.Max(1, Width + 200)
                                        Invalidate()
                                    End Sub
        End Sub

        Protected Overrides Sub OnVisibleChanged(e As EventArgs)
            MyBase.OnVisibleChanged(e)
            _timer.Enabled = Visible
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            e.Graphics.Clear(Color.FromArgb(214, 228, 250))
            Using b As New SolidBrush(Theme.Primary)
                e.Graphics.FillRectangle(b, _offset - 200, 0, 200, Height)
            End Using
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then _timer.Dispose()
            MyBase.Dispose(disposing)
        End Sub
    End Class

End Namespace
