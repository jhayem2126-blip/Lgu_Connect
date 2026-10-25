Imports TrinidadLGUConnect.Models

Namespace Controls

    Public Class GridAction
        Public Property Key As String = ""
        Public Property GlyphText As String = ""
        Public Property Color As Color = Theme.Primary
        Public Property Tooltip As String = ""
        ''' <summary>Optional: hide the action for some rows.</summary>
        Public Property IsVisible As Func(Of Object, Boolean)
    End Class

    ''' <summary>
    ''' DataGridView styled like the reference tables, with status-badge columns, category-pill columns and
    ''' clickable icon action columns. Each row's Tag holds the underlying record.
    ''' </summary>
    Public Class ThemedGrid
        Inherits DataGridView

        Private ReadOnly _actions As New Dictionary(Of String, List(Of GridAction))()
        Private ReadOnly _statusColumns As New HashSet(Of String)()
        Private ReadOnly _pillColumns As New HashSet(Of String)()
        Private ReadOnly _boldColumns As New HashSet(Of String)()
        Private Const ActionSize As Integer = 28
        Private Const ActionGap As Integer = 6

        <ComponentModel.DesignerSerializationVisibility(ComponentModel.DesignerSerializationVisibility.Hidden)>
        Public Property EmptyText As String = "No records found."

        Public Event ActionClicked(sender As Object, row As DataGridViewRow, actionKey As String)
        Public Event RowActivated(sender As Object, row As DataGridViewRow)

        Public Sub New()
            DoubleBuffered = True
            BackgroundColor = Color.White
            BorderStyle = BorderStyle.None
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None
            EnableHeadersVisualStyles = False
            GridColor = Theme.TableLine
            RowHeadersVisible = False
            AllowUserToAddRows = False
            AllowUserToDeleteRows = False
            AllowUserToResizeRows = False
            AllowUserToResizeColumns = True
            AllowUserToOrderColumns = False
            [ReadOnly] = True
            MultiSelect = False
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
            ColumnHeadersHeight = 42
            RowTemplate.Height = 44
            ShowCellToolTips = True
            StandardTab = True
            With ColumnHeadersDefaultCellStyle
                .BackColor = Theme.TableHeader
                .ForeColor = Theme.Ink
                .Font = Theme.F(8.75F, FontStyle.Bold)
                .SelectionBackColor = Theme.TableHeader
                .SelectionForeColor = Theme.Ink
                .Padding = New Padding(10, 0, 6, 0)
                .Alignment = DataGridViewContentAlignment.MiddleLeft
                .WrapMode = DataGridViewTriState.False
            End With
            With DefaultCellStyle
                .BackColor = Color.White
                .ForeColor = Theme.Ink
                .Font = Theme.F(9)
                .SelectionBackColor = Theme.RowSelected
                .SelectionForeColor = Theme.Ink
                .Padding = New Padding(10, 0, 6, 0)
                .WrapMode = DataGridViewTriState.False
            End With
        End Sub

        Public Function AddText(name As String, header As String, Optional weight As Single = 100, Optional bold As Boolean = False, Optional minWidth As Integer = 60) As DataGridViewTextBoxColumn
            Dim c As New DataGridViewTextBoxColumn With {.Name = name, .HeaderText = header, .FillWeight = weight, .MinimumWidth = minWidth, .SortMode = DataGridViewColumnSortMode.NotSortable}
            Columns.Add(c)
            If bold Then _boldColumns.Add(name)
            Return c
        End Function

        Public Sub AddStatus(name As String, header As String, Optional weight As Single = 90)
            AddText(name, header, weight, minWidth:=96)
            _statusColumns.Add(name)
        End Sub

        Public Sub AddPill(name As String, header As String, Optional weight As Single = 90)
            AddText(name, header, weight, minWidth:=80)
            _pillColumns.Add(name)
        End Sub

        Public Sub AddActions(name As String, header As String, actions As IEnumerable(Of GridAction))
            Dim list = actions.ToList()
            Dim w = list.Count * (ActionSize + ActionGap) + 18
            Dim c As New DataGridViewTextBoxColumn With {.Name = name, .HeaderText = header, .AutoSizeMode = DataGridViewAutoSizeColumnMode.None, .Width = Math.Max(w, 92),
                .SortMode = DataGridViewColumnSortMode.NotSortable, .Resizable = DataGridViewTriState.False}
            c.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter
            Columns.Add(c)
            _actions(name) = list
        End Sub

        Private Function ActionRects(cellBounds As Rectangle, list As List(Of GridAction), record As Object) As List(Of (Action As GridAction, Bounds As Rectangle))
            Dim visible = list.Where(Function(a) a.IsVisible Is Nothing OrElse a.IsVisible(record)).ToList()
            Dim total = visible.Count * ActionSize + Math.Max(0, visible.Count - 1) * ActionGap
            Dim x = cellBounds.X + (cellBounds.Width - total) \ 2
            Dim y = cellBounds.Y + (cellBounds.Height - ActionSize) \ 2
            Dim res As New List(Of (Action As GridAction, Bounds As Rectangle))()
            For Each a In visible
                res.Add((a, New Rectangle(x, y, ActionSize, ActionSize)))
                x += ActionSize + ActionGap
            Next
            Return res
        End Function

        Protected Overrides Sub OnCellPainting(e As DataGridViewCellPaintingEventArgs)
            MyBase.OnCellPainting(e)
            If e.RowIndex < 0 OrElse e.ColumnIndex < 0 Then Return
            Dim col = Columns(e.ColumnIndex)
            Dim isStatus = _statusColumns.Contains(col.Name)
            Dim isPill = _pillColumns.Contains(col.Name)
            Dim isAction = _actions.ContainsKey(col.Name)
            If Not (isStatus OrElse isPill OrElse isAction OrElse _boldColumns.Contains(col.Name)) Then Return
            e.PaintBackground(e.CellBounds, True)
            Dim g = e.Graphics
            Theme.HighQuality(g)
            Dim inner = New Rectangle(e.CellBounds.X + 10, e.CellBounds.Y, e.CellBounds.Width - 16, e.CellBounds.Height)
            If isStatus AndAlso TypeOf e.Value Is ConcernStatus Then
                Dim st = DirectCast(e.Value, ConcernStatus)
                Dim c = Theme.BadgeColors(st)
                Theme.DrawPill(g, DisplayName(st), inner, c.Back, c.Fore, center:=False)
            ElseIf isPill AndAlso e.Value IsNot Nothing Then
                Theme.DrawPill(g, e.Value.ToString(), inner, Theme.CategoryPillBack, Theme.CategoryPillFore, center:=False)
            ElseIf isAction Then
                For Each ar In ActionRects(e.CellBounds, _actions(col.Name), Rows(e.RowIndex).Tag)
                    Using p = Theme.RoundedRect(ar.Bounds, 6), b As New SolidBrush(Theme.Blend(ar.Action.Color, Color.White, 0.88))
                        g.FillPath(b, p)
                    End Using
                    Theme.DrawGlyph(g, ar.Action.GlyphText, ar.Bounds, ar.Action.Color, 10)
                Next
            ElseIf e.Value IsNot Nothing Then
                TextRenderer.DrawText(g, e.Value.ToString(), Theme.F(9, FontStyle.Bold), inner, Theme.Ink, TextFormatFlags.NoPrefix Or TextFormatFlags.VerticalCenter Or TextFormatFlags.Left Or TextFormatFlags.EndEllipsis)
            End If
            e.Handled = True
        End Sub

        Private Function HitAction(rowIndex As Integer, colIndex As Integer, pt As Point) As GridAction
            If rowIndex < 0 OrElse colIndex < 0 Then Return Nothing
            Dim col = Columns(colIndex)
            If Not _actions.ContainsKey(col.Name) Then Return Nothing
            Dim bounds = GetCellDisplayRectangle(colIndex, rowIndex, False)
            For Each ar In ActionRects(bounds, _actions(col.Name), Rows(rowIndex).Tag)
                If Rectangle.Inflate(ar.Bounds, 2, 2).Contains(pt) Then Return ar.Action
            Next
            Return Nothing
        End Function

        Protected Overrides Sub OnCellMouseClick(e As DataGridViewCellMouseEventArgs)
            MyBase.OnCellMouseClick(e)
            If e.Button <> MouseButtons.Left OrElse e.RowIndex < 0 Then Return
            Dim bounds = GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, False)
            Dim a = HitAction(e.RowIndex, e.ColumnIndex, New Point(bounds.X + e.X, bounds.Y + e.Y))
            If a IsNot Nothing Then RaiseEvent ActionClicked(Me, Rows(e.RowIndex), a.Key)
        End Sub

        Protected Overrides Sub OnCellMouseMove(e As DataGridViewCellMouseEventArgs)
            MyBase.OnCellMouseMove(e)
            If e.RowIndex < 0 Then
                Cursor = Cursors.Default
                Return
            End If
            Dim bounds = GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, False)
            Dim a = HitAction(e.RowIndex, e.ColumnIndex, New Point(bounds.X + e.X, bounds.Y + e.Y))
            Cursor = If(a IsNot Nothing, Cursors.Hand, Cursors.Default)
        End Sub

        Protected Overrides Sub OnCellToolTipTextNeeded(e As DataGridViewCellToolTipTextNeededEventArgs)
            MyBase.OnCellToolTipTextNeeded(e)
            If e.RowIndex >= 0 AndAlso e.ColumnIndex >= 0 AndAlso _actions.ContainsKey(Columns(e.ColumnIndex).Name) Then
                e.ToolTipText = String.Join(" / ", _actions(Columns(e.ColumnIndex).Name).Select(Function(a) a.Tooltip))
            End If
        End Sub

        Protected Overrides Sub OnCellDoubleClick(e As DataGridViewCellEventArgs)
            MyBase.OnCellDoubleClick(e)
            If e.RowIndex >= 0 AndAlso Not _actions.ContainsKey(Columns(Math.Max(0, e.ColumnIndex)).Name) Then RaiseEvent RowActivated(Me, Rows(e.RowIndex))
        End Sub

        Protected Overrides Function ProcessDataGridViewKey(e As KeyEventArgs) As Boolean
            If e.KeyCode = Keys.Enter AndAlso CurrentRow IsNot Nothing Then
                RaiseEvent RowActivated(Me, CurrentRow)
                Return True
            End If
            Return MyBase.ProcessDataGridViewKey(e)
        End Function

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            MyBase.OnPaint(e)
            If Rows.Count = 0 AndAlso Not String.IsNullOrEmpty(EmptyText) Then
                Dim r = New Rectangle(0, ColumnHeadersHeight, Width, Math.Max(60, Height - ColumnHeadersHeight))
                Theme.DrawGlyph(e.Graphics, Glyph.Folder, New Rectangle(r.X, r.Y + r.Height \ 2 - 46, r.Width, 36), Theme.Placeholder, 20)
                TextRenderer.DrawText(e.Graphics, EmptyText, Theme.F(9.5F), New Rectangle(r.X, r.Y + r.Height \ 2 - 6, r.Width, 24), Theme.Muted, TextFormatFlags.NoPrefix Or TextFormatFlags.HorizontalCenter)
            End If
        End Sub

        ''' <summary>Fixed height that shows exactly <paramref name="rows"/> rows without a scrollbar.</summary>
        Public Function HeightFor(rows As Integer) As Integer
            Return ColumnHeadersHeight + rows * RowTemplate.Height + 2
        End Function

    End Class

End Namespace
