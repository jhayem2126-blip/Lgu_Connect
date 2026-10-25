Imports System.Drawing.Drawing2D
Imports System.Globalization
Imports TrinidadLGUConnect.Helpers
Imports TrinidadLGUConnect.Models

Namespace Services

    Public Enum TextAlign
        Left
        Center
        Right
    End Enum

    ''' <summary>Drawing surface in points (1/72 inch). Implemented for GDI+ (screen/printer) and PDFsharp.</summary>
    Public Interface IReportCanvas
        Sub FillRect(r As RectangleF, color As Color)
        Sub FillRoundRect(r As RectangleF, radius As Single, color As Color)
        Sub DrawRoundRect(r As RectangleF, radius As Single, color As Color, width As Single)
        Sub DrawLine(x1 As Single, y1 As Single, x2 As Single, y2 As Single, color As Color, width As Single)
        Sub DrawText(text As String, size As Single, bold As Boolean, color As Color, r As RectangleF, align As TextAlign)
        Function MeasureWidth(text As String, size As Single, bold As Boolean) As Single
        Sub DrawImage(img As Image, r As RectangleF)
    End Interface

    ''' <summary>
    ''' Lays out a <see cref="ReportResult"/> into pages and draws them on any <see cref="IReportCanvas"/>.
    ''' The on-screen preview, the printer and the PDF export all use this class, so they show the same data.
    ''' </summary>
    Public Class ReportRenderer

        Public Shared ReadOnly Navy As Color = Color.FromArgb(13, 43, 94)
        Public Shared ReadOnly Ink As Color = Color.FromArgb(31, 42, 68)
        Public Shared ReadOnly Muted As Color = Color.FromArgb(107, 122, 144)
        Public Shared ReadOnly Border As Color = Color.FromArgb(220, 226, 236)
        Public Shared ReadOnly HeaderFill As Color = Color.FromArgb(238, 242, 248)
        Public Shared ReadOnly Zebra As Color = Color.FromArgb(249, 250, 252)

        Public Const LetterWidth As Single = 612
        Public Const LetterHeight As Single = 792
        Private Const Margin As Single = 40
        Private Const RowHeight As Single = 17
        Private Const HeaderRowHeight As Single = 20

        Private ReadOnly _report As ReportResult
        Private ReadOnly _seal As Image
        Private ReadOnly _culture As CultureInfo = CultureInfo.GetCultureInfo("en-US")

        Private Class Column
            Public Header As String
            Public Weight As Single
            Public Align As TextAlign
            Public Sub New(h As String, w As Single, Optional a As TextAlign = TextAlign.Left)
                Header = h : Weight = w : Align = a
            End Sub
        End Class

        Private MustInherit Class Block
            Public MustOverride ReadOnly Property Height As Single
            Public MustOverride Sub Draw(c As IReportCanvas, x As Single, y As Single, w As Single)
            Public Overridable ReadOnly Property KeepWithNext As Boolean
                Get
                    Return False
                End Get
            End Property
        End Class

        Private Class ActionBlock
            Inherits Block
            Private ReadOnly _h As Single
            Private ReadOnly _draw As Action(Of IReportCanvas, Single, Single, Single)
            Private ReadOnly _keep As Boolean
            Public Sub New(h As Single, draw As Action(Of IReportCanvas, Single, Single, Single), Optional keepWithNext As Boolean = False)
                _h = h : _draw = draw : _keep = keepWithNext
            End Sub
            Public Overrides ReadOnly Property Height As Single
                Get
                    Return _h
                End Get
            End Property
            Public Overrides Sub Draw(c As IReportCanvas, x As Single, y As Single, w As Single)
                _draw(c, x, y, w)
            End Sub
            Public Overrides ReadOnly Property KeepWithNext As Boolean
                Get
                    Return _keep
                End Get
            End Property
        End Class

        ''' <summary>A table row; when a row starts a new page the table header is repeated.</summary>
        Private Class RowBlock
            Inherits ActionBlock
            Public ReadOnly HeaderBlock As Block
            Public Sub New(h As Single, draw As Action(Of IReportCanvas, Single, Single, Single), header As Block)
                MyBase.New(h, draw)
                HeaderBlock = header
            End Sub
        End Class

        Private Class PlacedBlock
            Public Block As Block
            Public Y As Single
        End Class

        Private _pages As List(Of List(Of PlacedBlock))
        Private _pageWidth As Single
        Private _pageHeight As Single

        Public Sub New(report As ReportResult, seal As Image)
            _report = report
            _seal = seal
        End Sub

        Public ReadOnly Property PageCount As Integer
            Get
                Return If(_pages Is Nothing, 0, _pages.Count)
            End Get
        End Property

        Public Sub Layout(pageWidth As Single, pageHeight As Single, measure As IReportCanvas)
            _pageWidth = pageWidth
            _pageHeight = pageHeight
            Dim blocks = BuildBlocks(measure, pageWidth - 2 * Margin)
            _pages = New List(Of List(Of PlacedBlock)) From {New List(Of PlacedBlock)()}
            Dim bottom = pageHeight - Margin - 24
            Dim y = Margin
            For i = 0 To blocks.Count - 1
                Dim b = blocks(i)
                Dim needed = b.Height
                If b.KeepWithNext AndAlso i + 1 < blocks.Count Then needed += blocks(i + 1).Height + If(i + 2 < blocks.Count, blocks(i + 2).Height, 0)
                If y + needed > bottom AndAlso _pages.Last().Count > 0 Then
                    _pages.Add(New List(Of PlacedBlock)())
                    y = Margin
                    Dim row = TryCast(b, RowBlock)
                    If row IsNot Nothing Then
                        _pages.Last().Add(New PlacedBlock With {.Block = row.HeaderBlock, .Y = y})
                        y += row.HeaderBlock.Height
                    End If
                End If
                _pages.Last().Add(New PlacedBlock With {.Block = b, .Y = y})
                y += b.Height
            Next
        End Sub

        Public Sub DrawPage(c As IReportCanvas, pageIndex As Integer)
            c.FillRect(New RectangleF(0, 0, _pageWidth, _pageHeight), Color.White)
            Dim w = _pageWidth - 2 * Margin
            For Each p In _pages(pageIndex)
                p.Block.Draw(c, Margin, p.Y, w)
            Next
            Dim fy = _pageHeight - Margin - 10
            c.DrawLine(Margin, fy - 6, _pageWidth - Margin, fy - 6, Border, 0.75F)
            c.DrawText($"Trinidad LGU Connect · {_report.Title.ToLowerInvariant()} · generated {_report.GeneratedAt.ToString("MMM dd, yyyy hh:mm tt", _culture)} by {_report.GeneratedBy}",
                       7, False, Muted, New RectangleF(Margin, fy, w - 80, 12), TextAlign.Left)
            c.DrawText($"Page {pageIndex + 1} of {_pages.Count}", 7, False, Muted, New RectangleF(_pageWidth - Margin - 80, fy, 80, 12), TextAlign.Right)
        End Sub

        ' ------------------------------------------------------------------ content

        Private Function BuildBlocks(m As IReportCanvas, width As Single) As List(Of Block)
            Dim r = _report
            Dim list As New List(Of Block)()

            ' Letterhead
            list.Add(New ActionBlock(118,
                Sub(c, x, y, w)
                    If _seal IsNot Nothing Then c.DrawImage(_seal, New RectangleF(x + 6, y, 66, 66))
                    c.DrawText("TRINIDAD LGU CONNECT", 16, True, Navy, New RectangleF(x + 86, y + 10, w - 200, 20), TextAlign.Left)
                    c.DrawText("Community Concern Reporting and Monitoring System", 9.5F, False, Ink, New RectangleF(x + 86, y + 32, w - 200, 14), TextAlign.Left)
                    c.DrawText("Municipality of Trinidad, Bohol", 8, False, Muted, New RectangleF(x + 86, y + 47, w - 200, 12), TextAlign.Left)
                    c.DrawText("Date Generated:", 8, False, Muted, New RectangleF(x + w - 120, y + 10, 120, 12), TextAlign.Right)
                    c.DrawText(r.GeneratedAt.ToString("MMM dd, yyyy", _culture), 9, True, Ink, New RectangleF(x + w - 120, y + 23, 120, 14), TextAlign.Right)
                    c.DrawText(r.Title, 13, True, Navy, New RectangleF(x, y + 76, w, 18), TextAlign.Center)
                    c.DrawText(r.RangeText, 9, False, Ink, New RectangleF(x, y + 95, w, 14), TextAlign.Center)
                End Sub))

            ' Summary boxes
            Dim boxes As New List(Of (Label As String, Value As Integer))()
            boxes.Add(("Total Concerns", r.Total))
            If r.Request.Type = ReportType.ConcernsByStatus Then
                boxes.Add(("Submitted", r.Submitted))
                boxes.Add(("Under Review", r.UnderReview))
                boxes.Add(("In Progress", r.InProgress))
            End If
            boxes.Add(("Resolved", r.Resolved))
            boxes.Add(("Closed", r.Closed))
            list.Add(New ActionBlock(74,
                Sub(c, x, y, w)
                    Dim gap = 12.0F
                    Dim bw = (w - gap * (boxes.Count - 1)) / boxes.Count
                    For i = 0 To boxes.Count - 1
                        Dim bx = x + i * (bw + gap)
                        Dim rect = New RectangleF(bx, y + 4, bw, 56)
                        c.DrawRoundRect(rect, 6, Border, 0.9F)
                        c.DrawText(boxes(i).Label, 8.5F, False, Ink, New RectangleF(bx, y + 12, bw, 12), TextAlign.Center)
                        c.DrawText(boxes(i).Value.ToString("N0", _culture), 18, True, Navy, New RectangleF(bx, y + 28, bw, 24), TextAlign.Center)
                    Next
                End Sub))

            Select Case r.Request.Type
                Case ReportType.ConcernsByStatus
                    AddStatusTable(list, m)
                    AddCategoryTable(list, m, detailed:=False)
                Case ReportType.ConcernsByCategory
                    AddCategoryTable(list, m, detailed:=True)
                Case ReportType.DateRangeSummary
                    AddTable(list, m, If((r.Request.ToDate - r.Request.FromDate).TotalDays > 62, "Concerns by Month", "Concerns by Date"),
                             {New Column("Period", 3), New Column("Total", 1, TextAlign.Center), New Column("Resolved", 1, TextAlign.Center), New Column("Closed", 1, TextAlign.Center)},
                             r.ByDay.Select(Function(d) New String() {d.Label, d.Total.ToString(), d.Resolved.ToString(), d.Closed.ToString()}).ToList(),
                             {"Total", r.ByDay.Sum(Function(d) d.Total).ToString(), r.ByDay.Sum(Function(d) d.Resolved).ToString(), r.ByDay.Sum(Function(d) d.Closed).ToString()})
                Case Else
                    AddCategoryTable(list, m, detailed:=False)
                    AddStatusTable(list, m)
            End Select

            AddTable(list, m, "Concern List",
                     {New Column("ID", 0.9F), New Column("Date Reported", 1.1F), New Column("Category", 1.7F), New Column("Location", 2.1F), New Column("Status", 1.1F), New Column("Resident", 1.5F)},
                     r.Concerns.Select(Function(cn) New String() {cn.ReportCode, TextFormat.IsoDate(cn.ReportedAt), cn.CategoryName, cn.LocationDisplay, cn.StatusName, cn.ResidentName}).ToList(),
                     Nothing, emptyText:="No concerns were reported in this date range.")
            Return list
        End Function

        Private Sub AddStatusTable(list As List(Of Block), m As IReportCanvas)
            AddTable(list, m, "Concerns by Status",
                     {New Column("Status", 3), New Column("Count", 1, TextAlign.Center), New Column("Percentage", 1, TextAlign.Center)},
                     _report.ByStatus.Select(Function(s) New String() {s.StatusName, s.Count.ToString(), s.Percent.ToString("0.0", _culture) & "%"}).ToList(),
                     {"Total", _report.Total.ToString(), If(_report.Total > 0, "100.0%", "0.0%")})
        End Sub

        Private Sub AddCategoryTable(list As List(Of Block), m As IReportCanvas, detailed As Boolean)
            Dim cats = _report.ByCategory
            If detailed Then
                AddTable(list, m, "Concerns by Category",
                         {New Column("Category", 2.6F), New Column("Total", 1, TextAlign.Center), New Column("Submitted", 1, TextAlign.Center), New Column("Under Review", 1.1F, TextAlign.Center),
                          New Column("In Progress", 1.1F, TextAlign.Center), New Column("Resolved", 1, TextAlign.Center), New Column("Closed", 1, TextAlign.Center)},
                         cats.Select(Function(c) New String() {c.Category, c.Total.ToString(), c.Submitted.ToString(), c.UnderReview.ToString(), c.InProgress.ToString(), c.Resolved.ToString(), c.Closed.ToString()}).ToList(),
                         {"Total", cats.Sum(Function(c) c.Total).ToString(), cats.Sum(Function(c) c.Submitted).ToString(), cats.Sum(Function(c) c.UnderReview).ToString(),
                          cats.Sum(Function(c) c.InProgress).ToString(), cats.Sum(Function(c) c.Resolved).ToString(), cats.Sum(Function(c) c.Closed).ToString()})
            Else
                AddTable(list, m, "Concerns by Category",
                         {New Column("Category", 3), New Column("Total", 1, TextAlign.Center), New Column("Resolved", 1, TextAlign.Center), New Column("Closed", 1, TextAlign.Center)},
                         cats.Select(Function(c) New String() {c.Category, c.Total.ToString(), c.Resolved.ToString(), c.Closed.ToString()}).ToList(),
                         {"Total", cats.Sum(Function(c) c.Total).ToString(), cats.Sum(Function(c) c.Resolved).ToString(), cats.Sum(Function(c) c.Closed).ToString()})
            End If
        End Sub

        Private Sub AddTable(list As List(Of Block), m As IReportCanvas, title As String, cols As Column(), rows As List(Of String()), totals As String(), Optional emptyText As String = "No data.")
            Dim totalWeight = cols.Sum(Function(c) c.Weight)
            Dim colRects = Function(x As Single, w As Single)
                               Dim res As New List(Of (X As Single, W As Single))()
                               Dim cx = x
                               For Each col In cols
                                   Dim cw = w * col.Weight / totalWeight
                                   res.Add((cx, cw))
                                   cx += cw
                               Next
                               Return res
                           End Function

            list.Add(New ActionBlock(28,
                Sub(c, x, y, w)
                    c.DrawText(title, 11, True, Navy, New RectangleF(x, y + 10, w, 16), TextAlign.Left)
                End Sub, keepWithNext:=True))

            Dim header As New ActionBlock(HeaderRowHeight,
                Sub(c, x, y, w)
                    c.FillRect(New RectangleF(x, y, w, HeaderRowHeight), HeaderFill)
                    Dim rects = colRects(x, w)
                    For i = 0 To cols.Length - 1
                        c.DrawText(cols(i).Header, 8, True, Ink, New RectangleF(rects(i).X + 5, y + 4, rects(i).W - 10, 12), cols(i).Align)
                    Next
                    c.DrawLine(x, y + HeaderRowHeight, x + w, y + HeaderRowHeight, Border, 0.75F)
                End Sub)
            list.Add(header)

            If rows.Count = 0 Then
                list.Add(New ActionBlock(RowHeight + 4,
                    Sub(c, x, y, w)
                        c.DrawText(emptyText, 8.5F, False, Muted, New RectangleF(x, y + 4, w, 12), TextAlign.Center)
                    End Sub))
            End If

            For idx = 0 To rows.Count - 1
                Dim cells = rows(idx)
                Dim isOddRow = idx Mod 2 = 1
                list.Add(New RowBlock(RowHeight,
                    Sub(c, x, y, w)
                        If isOddRow Then c.FillRect(New RectangleF(x, y, w, RowHeight), Zebra)
                        Dim rects = colRects(x, w)
                        For i = 0 To cols.Length - 1
                            Dim text = Ellipsize(c, If(cells(i), ""), 8, False, rects(i).W - 10)
                            c.DrawText(text, 8, False, Ink, New RectangleF(rects(i).X + 5, y + 3, rects(i).W - 10, 12), cols(i).Align)
                        Next
                        c.DrawLine(x, y + RowHeight, x + w, y + RowHeight, Border, 0.4F)
                    End Sub, header))
            Next

            If totals IsNot Nothing AndAlso rows.Count > 0 Then
                list.Add(New RowBlock(RowHeight + 2,
                    Sub(c, x, y, w)
                        c.FillRect(New RectangleF(x, y, w, RowHeight + 2), HeaderFill)
                        Dim rects = colRects(x, w)
                        For i = 0 To cols.Length - 1
                            c.DrawText(totals(i), 8, True, Navy, New RectangleF(rects(i).X + 5, y + 4, rects(i).W - 10, 12), cols(i).Align)
                        Next
                    End Sub, header))
            End If
            list.Add(New ActionBlock(6, Sub(c, x, y, w)
                                        End Sub))
        End Sub

        Private Shared Function Ellipsize(c As IReportCanvas, text As String, size As Single, bold As Boolean, maxWidth As Single) As String
            If c.MeasureWidth(text, size, bold) <= maxWidth Then Return text
            Dim s = text
            While s.Length > 1 AndAlso c.MeasureWidth(s & "…", size, bold) > maxWidth
                s = s.Substring(0, s.Length - 1)
            End While
            Return s.TrimEnd() & "…"
        End Function

    End Class

    ''' <summary>GDI+ canvas for the screen preview and the printer. The Graphics page unit must be points.</summary>
    Public Class GdiReportCanvas
        Implements IReportCanvas

        Private ReadOnly _g As Graphics
        Private ReadOnly _fonts As New Dictionary(Of String, Font)()

        Public Sub New(g As Graphics)
            _g = g
            _g.PageUnit = GraphicsUnit.Point
            _g.SmoothingMode = SmoothingMode.AntiAlias
            _g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit
            _g.InterpolationMode = InterpolationMode.HighQualityBicubic
        End Sub

        Private Function GetFont(size As Single, bold As Boolean) As Font
            Dim key = $"{size}-{bold}"
            Dim f As Font = Nothing
            If Not _fonts.TryGetValue(key, f) Then
                f = New Font("Segoe UI", size, If(bold, FontStyle.Bold, FontStyle.Regular), GraphicsUnit.Point)
                _fonts(key) = f
            End If
            Return f
        End Function

        Public Sub FillRect(r As RectangleF, color As Color) Implements IReportCanvas.FillRect
            Using b As New SolidBrush(color)
                _g.FillRectangle(b, r)
            End Using
        End Sub

        Public Sub FillRoundRect(r As RectangleF, radius As Single, color As Color) Implements IReportCanvas.FillRoundRect
            Using p = RoundPath(r, radius), b As New SolidBrush(color)
                _g.FillPath(b, p)
            End Using
        End Sub

        Public Sub DrawRoundRect(r As RectangleF, radius As Single, color As Color, width As Single) Implements IReportCanvas.DrawRoundRect
            Using p = RoundPath(r, radius), pen As New Pen(color, width)
                _g.DrawPath(pen, p)
            End Using
        End Sub

        Private Shared Function RoundPath(r As RectangleF, radius As Single) As GraphicsPath
            Dim p As New GraphicsPath()
            Dim d = radius * 2
            p.AddArc(r.X, r.Y, d, d, 180, 90)
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90)
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90)
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90)
            p.CloseFigure()
            Return p
        End Function

        Public Sub DrawLine(x1 As Single, y1 As Single, x2 As Single, y2 As Single, color As Color, width As Single) Implements IReportCanvas.DrawLine
            Using pen As New Pen(color, width)
                _g.DrawLine(pen, x1, y1, x2, y2)
            End Using
        End Sub

        Public Sub DrawText(text As String, size As Single, bold As Boolean, color As Color, r As RectangleF, align As TextAlign) Implements IReportCanvas.DrawText
            Using b As New SolidBrush(color), sf As New StringFormat(StringFormatFlags.NoWrap)
                sf.Alignment = If(align = TextAlign.Center, StringAlignment.Center, If(align = TextAlign.Right, StringAlignment.Far, StringAlignment.Near))
                sf.LineAlignment = StringAlignment.Near
                sf.Trimming = StringTrimming.EllipsisCharacter
                _g.DrawString(text, GetFont(size, bold), b, New RectangleF(r.X, r.Y - size * 0.15F, r.Width, r.Height + size * 0.5F), sf)
            End Using
        End Sub

        Public Function MeasureWidth(text As String, size As Single, bold As Boolean) As Single Implements IReportCanvas.MeasureWidth
            Using sf = CType(StringFormat.GenericTypographic.Clone(), StringFormat)
                Return _g.MeasureString(text, GetFont(size, bold), PointF.Empty, sf).Width
            End Using
        End Function

        Public Sub DrawImage(img As Image, r As RectangleF) Implements IReportCanvas.DrawImage
            _g.DrawImage(img, r)
        End Sub

        Public Sub DisposeFonts()
            For Each f In _fonts.Values : f.Dispose() : Next
            _fonts.Clear()
        End Sub
    End Class

End Namespace
