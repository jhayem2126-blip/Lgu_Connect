Imports System.ComponentModel
Imports System.Drawing.Drawing2D
Imports System.Globalization
Imports TrinidadLGUConnect.Models

Namespace Controls

    ''' <summary>
    ''' Vertical bar chart with value labels and rotated category labels ("Concerns by Category").
    ''' Owner-drawn with GDI+ so it matches the reference exactly and has no runtime dependency.
    ''' </summary>
    Public Class BarChart
        Inherits Control

        Private _items As New List(Of CountItem)()
        Private ReadOnly _bars As New List(Of (Bounds As RectangleF, Item As CountItem))()
        Private _hover As Integer = -1

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property ColorFor As Func(Of CountItem, Color) = Function(i) Theme.CardBlue
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property EmptyText As String = "No concerns recorded yet."
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property LabelAngle As Single = -40

        Public Event BarClicked(sender As Object, item As CountItem)

        Public Sub New()
            SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw Or ControlStyles.UserPaint, True)
            BackColor = Color.White
        End Sub

        Public Sub SetData(items As IEnumerable(Of CountItem))
            _items = items.ToList()
            AccessibleDescription = String.Join(", ", _items.Select(Function(i) $"{i.Label}: {i.Value}"))
            Invalidate()
        End Sub

        Public Shared Function NiceMax(v As Integer) As Integer
            If v <= 5 Then Return 5
            If v <= 10 Then Return 10
            Dim mag = CInt(Math.Pow(10, Math.Floor(Math.Log10(v))))
            For Each m In {1, 2, 2.5, 5, 10}
                Dim candidate = CInt(Math.Ceiling(mag * m))
                If candidate >= v Then Return candidate
            Next
            Return v
        End Function

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(BackColor)
            Theme.HighQuality(g)
            _bars.Clear()
            If _items.Count = 0 Then Return
            Dim maxV = NiceMax(_items.Max(Function(i) i.Value))
            Dim labelFont = Theme.F(8)
            Dim longest = _items.Max(Function(i) TextRenderer.MeasureText(i.Label, labelFont).Width)
            Dim bottomPad = CInt(Math.Min(110, Math.Abs(Math.Sin(LabelAngle * Math.PI / 180)) * longest + 18))
            ' Rotated labels extend to the left of their bar; keep the first one inside the control.
            Dim firstW = TextRenderer.MeasureText(_items(0).Label, labelFont).Width
            Dim cosA = Math.Abs(Math.Cos(LabelAngle * Math.PI / 180))
            Dim approxSlot = (Width - 44) / CDbl(_items.Count)
            Dim leftPad = CInt(Math.Max(34, cosA * firstW - approxSlot / 2 + 8))
            Dim plot As New RectangleF(leftPad, 14, Width - leftPad - 10, Height - 14 - bottomPad)
            If plot.Height < 30 OrElse plot.Width < 40 Then Return

            ' Grid and Y axis
            Using gridPen As New Pen(Theme.TableLine), axisPen As New Pen(Color.FromArgb(200, 208, 222))
                For i = 0 To 5
                    Dim v = maxV * i / 5.0
                    Dim y = plot.Bottom - CSng(plot.Height * i / 5.0)
                    g.DrawLine(If(i = 0, axisPen, gridPen), plot.Left, y, plot.Right, y)
                    TextRenderer.DrawText(g, v.ToString("0.#", CultureInfo.InvariantCulture), labelFont, New Rectangle(CInt(plot.Left) - 34, CInt(y) - 8, 30, 16), Theme.Muted,
                                          TextFormatFlags.NoPrefix Or TextFormatFlags.Right Or TextFormatFlags.VerticalCenter)
                Next
                g.DrawLine(axisPen, plot.Left, plot.Top, plot.Left, plot.Bottom)
            End Using

            Dim slot = plot.Width / _items.Count
            Dim barW = Math.Min(34.0F, slot * 0.56F)
            For i = 0 To _items.Count - 1
                Dim it = _items(i)
                Dim h = CSng(plot.Height * it.Value / CDbl(maxV))
                Dim x = plot.Left + slot * i + (slot - barW) / 2
                Dim r As New RectangleF(x, plot.Bottom - h, barW, h)
                Dim c = ColorFor(it)
                If i = _hover Then c = Theme.Blend(c, Color.Black, 0.12)
                If h > 0.5F Then
                    Using p As New GraphicsPath()
                        Dim rad = Math.Min(4.0F, h / 2)
                        p.AddArc(r.X, r.Y, rad * 2, rad * 2, 180, 90)
                        p.AddArc(r.Right - rad * 2, r.Y, rad * 2, rad * 2, 270, 90)
                        p.AddLine(r.Right, r.Bottom, r.X, r.Bottom)
                        p.CloseFigure()
                        Using b As New SolidBrush(c)
                            g.FillPath(b, p)
                        End Using
                    End Using
                End If
                _bars.Add((New RectangleF(plot.Left + slot * i, plot.Top, slot, plot.Height), it))
                TextRenderer.DrawText(g, it.Value.ToString(CultureInfo.InvariantCulture), Theme.F(8.25F, FontStyle.Bold), New Rectangle(CInt(x - 10), CInt(r.Y - 18), CInt(barW + 20), 16), Theme.Ink,
                                      TextFormatFlags.NoPrefix Or TextFormatFlags.HorizontalCenter Or TextFormatFlags.Bottom)
                ' Rotated category label anchored at the bar centre.
                Dim state = g.Save()
                g.TranslateTransform(x + barW / 2 + 4, plot.Bottom + 8)
                g.RotateTransform(LabelAngle)
                Using b As New SolidBrush(Theme.Ink), sf As New StringFormat With {.Alignment = StringAlignment.Far}
                    g.DrawString(it.Label, labelFont, b, 0, 0, sf)
                End Using
                g.Restore(state)
            Next
            If _items.All(Function(i) i.Value = 0) Then
                TextRenderer.DrawText(g, EmptyText, Theme.F(9), Rectangle.Round(plot), Theme.Muted, TextFormatFlags.NoPrefix Or TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End If
        End Sub

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            Dim idx = _bars.FindIndex(Function(b) b.Bounds.Contains(e.Location))
            Cursor = If(idx >= 0, Cursors.Hand, Cursors.Default)
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
            Dim idx = _bars.FindIndex(Function(b) b.Bounds.Contains(e.Location))
            If idx >= 0 Then RaiseEvent BarClicked(Me, _bars(idx).Item)
        End Sub
    End Class

    Public Enum LegendStyle
        ValueOnly
        ValueAndPercent
    End Enum

    ''' <summary>Donut (InnerRatio &gt; 0) or pie (InnerRatio = 0) chart with a legend on the right.</summary>
    Public Class DonutChart
        Inherits Control

        Private _items As New List(Of CountItem)()
        Private ReadOnly _slices As New List(Of (StartAngle As Single, Sweep As Single, Item As CountItem))()
        Private _legendHits As New List(Of (Bounds As Rectangle, Item As CountItem))()
        Private _center As PointF
        Private _radius As Single
        Private _hover As CountItem

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property InnerRatio As Single = 0.58F
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property CenterLabel As String = "Total"
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property Legend As LegendStyle = LegendStyle.ValueOnly
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property ShowSlicePercent As Boolean
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property LegendWidth As Integer = 170

        Public Event SliceClicked(sender As Object, item As CountItem)

        Public Sub New()
            SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw Or ControlStyles.UserPaint, True)
            BackColor = Color.White
        End Sub

        Public Sub SetData(items As IEnumerable(Of CountItem))
            _items = items.ToList()
            AccessibleDescription = String.Join(", ", _items.Select(Function(i) $"{i.Label}: {i.Value}"))
            Invalidate()
        End Sub

        Private ReadOnly Property Total As Integer
            Get
                Return _items.Sum(Function(i) i.Value)
            End Get
        End Property

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(BackColor)
            Theme.HighQuality(g)
            _slices.Clear()
            _legendHits.Clear()
            Dim legendW = Math.Min(LegendWidth, Width \ 2)
            Dim chartArea As New RectangleF(4, 4, Width - legendW - 16, Height - 8)
            Dim d = Math.Max(10, Math.Min(chartArea.Width, chartArea.Height))
            _radius = d / 2
            _center = New PointF(chartArea.X + chartArea.Width / 2, chartArea.Y + chartArea.Height / 2)
            Dim rect As New RectangleF(_center.X - _radius, _center.Y - _radius, d, d)
            Dim tot = Total

            If tot = 0 Then
                Using b As New SolidBrush(Color.FromArgb(232, 236, 243))
                    g.FillEllipse(b, rect)
                End Using
            Else
                Dim angle = -90.0F
                For Each it In _items
                    If it.Value = 0 Then Continue For
                    Dim sweep = CSng(360.0 * it.Value / tot)
                    Dim c = Theme.FromHex(it.ColorHex)
                    Dim r = rect
                    If _hover Is it Then r.Inflate(4, 4)
                    Using b As New SolidBrush(c)
                        If sweep >= 359.99F Then
                            g.FillEllipse(b, r)
                        Else
                            g.FillPie(b, r.X, r.Y, r.Width, r.Height, angle, sweep)
                        End If
                    End Using
                    If sweep < 359.99F Then
                        Using pen As New Pen(Color.White, 2)
                            g.DrawPie(pen, r.X, r.Y, r.Width, r.Height, angle, sweep)
                        End Using
                    End If
                    _slices.Add((angle, sweep, it))
                    angle += sweep
                Next
            End If

            If InnerRatio > 0 Then
                Dim ir = _radius * InnerRatio
                Using b As New SolidBrush(BackColor)
                    g.FillEllipse(b, _center.X - ir, _center.Y - ir, ir * 2, ir * 2)
                End Using
                TextRenderer.DrawText(g, tot.ToString("N0", CultureInfo.InvariantCulture), Theme.F(Math.Max(10, _radius / 4.2F), FontStyle.Bold),
                                      New Rectangle(CInt(_center.X - ir), CInt(_center.Y - ir * 0.62F), CInt(ir * 2), CInt(ir * 0.9F)), Theme.Navy,
                                      TextFormatFlags.NoPrefix Or TextFormatFlags.HorizontalCenter Or TextFormatFlags.Bottom)
                TextRenderer.DrawText(g, CenterLabel, Theme.F(8.5F), New Rectangle(CInt(_center.X - ir), CInt(_center.Y + ir * 0.25F), CInt(ir * 2), 18), Theme.Muted,
                                      TextFormatFlags.NoPrefix Or TextFormatFlags.HorizontalCenter)
            ElseIf ShowSlicePercent AndAlso tot > 0 Then
                For Each s In _slices
                    If s.Sweep < 14 Then Continue For
                    Dim mid = (s.StartAngle + s.Sweep / 2) * Math.PI / 180
                    Dim px = _center.X + CSng(Math.Cos(mid) * _radius * 0.62)
                    Dim py = _center.Y + CSng(Math.Sin(mid) * _radius * 0.62)
                    Dim pct = Math.Round(100.0 * s.Item.Value / tot).ToString(CultureInfo.InvariantCulture) & "%"
                    TextRenderer.DrawText(g, pct, Theme.F(8.5F, FontStyle.Bold), New Rectangle(CInt(px - 24), CInt(py - 9), 48, 18), Color.White,
                                          TextFormatFlags.NoPrefix Or TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
                Next
            End If
            If tot = 0 Then
                TextRenderer.DrawText(g, "No data", Theme.F(8.5F), Rectangle.Round(rect), Theme.Muted, TextFormatFlags.NoPrefix Or TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End If

            ' Legend
            Dim lx = CInt(chartArea.Right + 12)
            Dim rowH = 26
            Dim ly = CInt(_center.Y - _items.Count * rowH / 2.0)
            For Each it In _items
                Dim c = Theme.FromHex(it.ColorHex)
                Using b As New SolidBrush(c)
                    g.FillEllipse(b, lx, ly + rowH \ 2 - 5, 10, 10)
                End Using
                Dim valueText = If(Legend = LegendStyle.ValueAndPercent,
                                   $"{it.Value} ({Math.Round(TextFormatPercent(it.Value, tot))}%)",
                                   it.Value.ToString(CultureInfo.InvariantCulture))
                Dim valueW = TextRenderer.MeasureText(valueText, Theme.F(8.5F)).Width
                TextRenderer.DrawText(g, it.Label, Theme.F(8.5F), New Rectangle(lx + 16, ly, Width - lx - 20 - valueW, rowH), Theme.Ink, TextFormatFlags.NoPrefix Or TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis)
                TextRenderer.DrawText(g, valueText, Theme.F(8.5F), New Rectangle(Width - valueW - 6, ly, valueW + 4, rowH), Theme.Ink, TextFormatFlags.NoPrefix Or TextFormatFlags.VerticalCenter Or TextFormatFlags.Right)
                _legendHits.Add((New Rectangle(lx, ly, Width - lx, rowH), it))
                ly += rowH
            Next
        End Sub

        Private Shared Function TextFormatPercent(v As Integer, t As Integer) As Double
            Return If(t <= 0, 0, v * 100.0 / t)
        End Function

        Private Function HitItem(pt As Point) As CountItem
            For Each h In _legendHits
                If h.Bounds.Contains(pt) Then Return h.Item
            Next
            Dim dx = pt.X - _center.X, dy = pt.Y - _center.Y
            Dim dist = Math.Sqrt(dx * dx + dy * dy)
            If dist > _radius OrElse dist < _radius * InnerRatio Then Return Nothing
            Dim a = Math.Atan2(dy, dx) * 180 / Math.PI
            For Each s In _slices
                Dim rel = (a - s.StartAngle + 720) Mod 360
                If rel >= 0 AndAlso rel < s.Sweep Then Return s.Item
            Next
            Return Nothing
        End Function

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            Dim it = HitItem(e.Location)
            Cursor = If(it IsNot Nothing, Cursors.Hand, Cursors.Default)
            If it IsNot _hover Then
                _hover = it
                Invalidate()
            End If
        End Sub

        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            MyBase.OnMouseLeave(e)
            _hover = Nothing
            Invalidate()
        End Sub

        Protected Overrides Sub OnMouseClick(e As MouseEventArgs)
            MyBase.OnMouseClick(e)
            Dim it = HitItem(e.Location)
            If it IsNot Nothing Then RaiseEvent SliceClicked(Me, it)
        End Sub
    End Class

End Namespace
