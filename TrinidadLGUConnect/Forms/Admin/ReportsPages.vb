Imports System.Drawing.Printing
Imports TrinidadLGUConnect.Controls
Imports TrinidadLGUConnect.Helpers
Imports TrinidadLGUConnect.Models
Imports TrinidadLGUConnect.Services

Namespace Forms

    ''' <summary>Export/print actions shared by the Reports page and the Report Preview page.</summary>
    Friend Module ReportActions

        Public Async Function ExportAsync(page As PageBase, report As ReportResult, format As String) As Task
            Dim ok = Await TryRunAsync(page, Function() AppServices.Instance.Reports.DemandExportAsync(AppServices.Instance.Session))
            If Not ok Then Return
            Dim ext = format.ToLowerInvariant()
            Dim filter = If(ext = "pdf", "PDF document (*.pdf)|*.pdf", If(ext = "xlsx", "Excel workbook (*.xlsx)|*.xlsx", "CSV file (*.csv)|*.csv"))
            Dim baseName = $"TrinidadLGUConnect_{ReportingService.TypeText(report.Request.Type).Replace(" ", "")}_{report.Request.FromDate:yyyyMMdd}-{report.Request.ToDate:yyyyMMdd}"
            Dim path As String
            Using dlg As New SaveFileDialog With {.Filter = filter, .FileName = baseName & "." & ext, .AddExtension = True, .OverwritePrompt = True, .Title = "Export report"}
                If dlg.ShowDialog(page.FindForm()) <> DialogResult.OK Then Return
                path = dlg.FileName
            End Using
            Dim seal = Assets.Seal
            Dim written = Await TryRunAsync(page,
                Function() Task.Run(Sub()
                                        Select Case ext
                                            Case "pdf" : ExportService.ExportPdf(report, seal, path)
                                            Case "xlsx" : ExportService.ExportExcel(report, path)
                                            Case Else : ExportService.ExportCsv(report, path)
                                        End Select
                                    End Sub))
            If Not written Then Return
            Dim size = New IO.FileInfo(path).Length
            Await TryRunAsync(page, Function() AppServices.Instance.Reports.AuditExportAsync(AppServices.Instance.Session, "report.exported", report, $"{ext.ToUpperInvariant()} {IO.Path.GetFileName(path)}"), showErrors:=False)
            If MessageBox.Show(page.FindForm(), $"The report was exported to:{vbLf}{path}{vbLf}({TextFormat.FileSize(size)}){vbLf}{vbLf}Open the file now?", AppTitle,
                               MessageBoxButtons.YesNo, MessageBoxIcon.Information) = DialogResult.Yes Then
                Try
                    Process.Start(New ProcessStartInfo(path) With {.UseShellExecute = True})
                Catch ex As Exception When TypeOf ex Is ComponentModel.Win32Exception OrElse TypeOf ex Is InvalidOperationException
                    Warn(page.FindForm(), "No application is installed that can open this file type.")
                End Try
            End If
        End Function

        ''' <summary>PrintDocument that draws the report with the shared renderer.</summary>
        Public Function CreatePrintDocument(report As ReportResult) As PrintDocument
            Dim doc As New PrintDocument With {.DocumentName = $"Trinidad LGU Connect - {report.Title}"}
            Dim renderer As ReportRenderer = Nothing
            Dim pageIndex = 0
            AddHandler doc.BeginPrint, Sub()
                                           renderer = Nothing
                                           pageIndex = 0
                                       End Sub
            AddHandler doc.PrintPage, Sub(s, e)
                                          Dim g = e.Graphics
                                          Dim canvas As New GdiReportCanvas(g)
                                          ' Page bounds are in 1/100 inch; the renderer works in points (1/72 inch).
                                          Dim wPt = e.PageBounds.Width * 0.72F
                                          Dim hPt = e.PageBounds.Height * 0.72F
                                          g.TranslateTransform(-e.PageSettings.HardMarginX * 0.72F, -e.PageSettings.HardMarginY * 0.72F)
                                          If renderer Is Nothing Then
                                              renderer = New ReportRenderer(report, Assets.Seal)
                                              renderer.Layout(wPt, hPt, canvas)
                                          End If
                                          renderer.DrawPage(canvas, pageIndex)
                                          canvas.DisposeFonts()
                                          pageIndex += 1
                                          e.HasMorePages = pageIndex < renderer.PageCount
                                      End Sub
            Return doc
        End Function

        Public Async Function PrintAsync(page As PageBase, report As ReportResult, preview As Boolean) As Task
            Dim ok = Await TryRunAsync(page, Function() AppServices.Instance.Reports.DemandExportAsync(AppServices.Instance.Session))
            If Not ok Then Return
            If PrinterSettings.InstalledPrinters.Count = 0 Then
                Warn(page.FindForm(), "No printer is installed on this computer. Install a printer (or 'Microsoft Print to PDF') and try again, or use Export to PDF.")
                Return
            End If
            Using doc = CreatePrintDocument(report)
                Try
                    If preview Then
                        Using dlg As New PrintPreviewDialog With {.Document = doc, .Width = 1000, .Height = 800, .UseAntiAlias = True, .Text = "Print Preview"}
                            dlg.ShowDialog(page.FindForm())
                        End Using
                        Return
                    End If
                    Using dlg As New PrintDialog With {.Document = doc, .UseEXDialog = True, .AllowSomePages = False}
                        If dlg.ShowDialog(page.FindForm()) <> DialogResult.OK Then Return
                    End Using
                    doc.Print()
                Catch ex As InvalidPrinterException
                    Warn(page.FindForm(), "The selected printer is not available: " & ex.Message)
                    Return
                Catch ex As ComponentModel.Win32Exception
                    Warn(page.FindForm(), "Printing failed: " & ex.Message)
                    Return
                End Try
            End Using
            Await TryRunAsync(page, Function() AppServices.Instance.Reports.AuditExportAsync(AppServices.Instance.Session, "report.printed", report, "sent to printer"), showErrors:=False)
            Info(page.FindForm(), "The report was sent to the printer.")
        End Function

    End Module

    ''' <summary>Reports and Analytics (reference admin screen 6).</summary>
    Public Class ReportsPage
        Inherits PageBase

        Private ReadOnly _type As New SelectField()
        Private ReadOnly _range As New SelectField()
        Private ReadOnly _from As New DateField()
        Private ReadOnly _to As New DateField()
        Private ReadOnly _customRow As New Panel With {.Visible = False, .BackColor = Color.Transparent}
        Private ReadOnly _generate As New ModernButton("Generate")
        Private ReadOnly _chartCard As CardPanel = TitledCard("Concerns by Status", 10.5F)
        Private ReadOnly _pie As New DonutChart With {.Dock = DockStyle.Fill, .InnerRatio = 0, .ShowSlicePercent = True, .Legend = LegendStyle.ValueAndPercent, .LegendWidth = 190}
        Private ReadOnly _bar As New BarChart With {.Dock = DockStyle.Fill, .Visible = False}
        Private ReadOnly _sumTotal As Label = Heading("0", 16)
        Private ReadOnly _sumResolved As Label = Heading("0", 16)
        Private ReadOnly _sumClosed As Label = Heading("0", 16)
        Private ReadOnly _sumOther As Label = Subtle("", 8.5F)
        Private ReadOnly _rangeLabel As Label = Subtle("", 8.5F)
        Private _result As ReportResult

        Public Sub New()
            ' Narrow enough that the whole filter row fits beside the sidebar at the smallest window size (1024 px).
            AutoScrollMinSize = New Size(720, 640)
            For Each t In {ReportType.ConcernsByStatus, ReportType.ConcernsByCategory, ReportType.DateRangeSummary, ReportType.ConcernSummary}
                _type.Items.Add(New ListOption(ReportingService.TypeText(t), t))
            Next
            _type.SelectedIndex = 0
            For Each p In [Enum].GetValues(Of DateRangePreset)()
                _range.Items.Add(New ListOption(ReportingService.PresetText(p), p))
            Next
            _range.SelectedIndex = CInt(DateRangePreset.Last30Days)
            _from.MaxDate = DateTime.Today
            _to.MaxDate = DateTime.Today
            Dim def = ReportingService.ResolveRange(DateRangePreset.Last30Days, DateTime.Today)
            _from.Value = def.FromDate
            _to.Value = def.ToDate

            Dim root As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 3, .BackColor = Color.Transparent}
            root.RowStyles.Add(New RowStyle(SizeType.Absolute, 96))
            root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
            root.RowStyles.Add(New RowStyle(SizeType.Absolute, 132))
            Controls.Add(root)
            AddTopDown(Me, BuildHeader("Reports", "Generate and view system reports and summaries."), Gap(8))

            ' Filter row: one grid with a caption row and a control row, so Report Type, Date Range, From, To
            ' and Generate share one baseline, one height and one gap, and the columns resize with the window.
            Dim filterCard As New CardPanel With {.Dock = DockStyle.Fill, .Margin = New Padding(0, 0, 0, 12), .Padding = New Padding(16, 12, 16, 12)}
            Dim filters As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 5, .RowCount = 2, .BackColor = Color.Transparent, .Margin = Padding.Empty}
            For Each pct In {27.0F, 20.0F, 20.0F, 20.0F, 13.0F}
                filters.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, pct))
            Next
            filters.RowStyles.Add(New RowStyle(SizeType.Absolute, 22))
            filters.RowStyles.Add(New RowStyle(SizeType.Absolute, 38))
            Const FilterGap As Integer = 14
            Dim captions = {"Report Type", "Date Range", "From", "To", ""}
            Dim inputs As Control() = {_type, _range, _from, _to, _generate}
            For i = 0 To inputs.Length - 1
                Dim rightGap = If(i = inputs.Length - 1, 0, FilterGap)
                If captions(i).Length > 0 Then
                    Dim cap = Lbl(captions(i), 8.75F, FontStyle.Bold, Theme.Ink)
                    cap.Anchor = AnchorStyles.Left Or AnchorStyles.Top
                    cap.Margin = New Padding(0, 0, rightGap, 0)
                    filters.Controls.Add(cap, i, 0)
                End If
                inputs(i).Dock = DockStyle.Fill
                inputs(i).Margin = New Padding(0, 0, rightGap, 0)
                filters.Controls.Add(inputs(i), i, 1)
            Next
            _generate.Font = Theme.F(10.5F, FontStyle.Bold)
            filterCard.Controls.Add(filters)
            root.Controls.Add(filterCard, 0, 0)

            ' Chart + summary
            Dim mid As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1, .BackColor = Color.Transparent, .Margin = New Padding(0, 0, 0, 12)}
            mid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 68))
            mid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 32))
            _chartCard.Margin = New Padding(0, 0, 8, 0)
            _chartCard.Padding = New Padding(16, 50, 16, 14)
            _chartCard.Controls.Add(_pie)
            _chartCard.Controls.Add(_bar)
            _rangeLabel.Anchor = AnchorStyles.Top Or AnchorStyles.Right
            _chartCard.Controls.Add(_rangeLabel)
            AddHandler _chartCard.Resize, Sub() _rangeLabel.Location = New Point(_chartCard.Width - _rangeLabel.PreferredWidth - 18, 18)
            mid.Controls.Add(_chartCard, 0, 0)
            Dim sumCard = TitledCard("Summary", 10.5F)
            sumCard.Margin = New Padding(8, 0, 0, 0)
            Dim rows = {("Total Concerns", _sumTotal), ("Resolved", _sumResolved), ("Closed", _sumClosed)}
            Dim y = 56
            For i = 0 To rows.Length - 1
                Dim l = Lbl(rows(i).Item1, 9.5F, FontStyle.Regular, Theme.Ink)
                l.Location = New Point(18, y + 10)
                sumCard.Controls.Add(l)
                Dim v = rows(i).Item2
                v.Anchor = AnchorStyles.Top Or AnchorStyles.Right
                sumCard.Controls.Add(v)
                Dim vy = y
                AddHandler sumCard.Resize, Sub() v.Location = New Point(sumCard.Width - v.PreferredWidth - 20, vy)
                If i < rows.Length - 1 Then
                    Dim sep As New Panel With {.Location = New Point(18, y + 48), .Height = 1, .BackColor = Theme.CardBorder, .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right}
                    sumCard.Controls.Add(sep)
                    AddHandler sumCard.Resize, Sub() sep.Width = sumCard.Width - 36
                End If
                y += 58
            Next
            _sumOther.Location = New Point(18, y + 6)
            _sumOther.MaximumSize = New Size(260, 0)
            sumCard.Controls.Add(_sumOther)
            AddHandler sumCard.Resize, Sub() _sumOther.MaximumSize = New Size(Math.Max(120, Math.Min(260, sumCard.Width - 36)), 0)
            mid.Controls.Add(sumCard, 1, 0)
            root.Controls.Add(mid, 0, 1)

            ' Reports Available
            Dim avail As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent}
            Dim at = Lbl("Reports Available", 10.5F, FontStyle.Bold, Theme.Navy)
            at.Location = New Point(0, 0)
            avail.Controls.Add(at)
            Dim tiles As New TableLayoutPanel With {.Location = New Point(0, 30), .Height = 70, .ColumnCount = 4, .RowCount = 1, .BackColor = Color.Transparent,
                                                    .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right}
            Dim tileDefs = {
                ("Status Report", Theme.CardBlue, Glyph.Document, "status"),
                ("Category Report", Theme.CardGreen, Glyph.Grid, "category"),
                ("Date Range Report", Theme.CardAmber, Glyph.Calendar, "daterange"),
                ("Export to PDF", Theme.CardRed, Glyph.Download, "pdf")}
            For i = 0 To 3
                tiles.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 25))
                Dim key = tileDefs(i).Item4
                Dim tile As New ActionTile(tileDefs(i).Item1, tileDefs(i).Item2, tileDefs(i).Item3) With {.Dock = DockStyle.Fill, .Margin = New Padding(If(i = 0, 0, 7), 0, If(i = 3, 0, 7), 0)}
                AddHandler tile.Click, Async Sub() Await OnTile(key)
                tiles.Controls.Add(tile, i, 0)
            Next
            avail.Controls.Add(tiles)
            AddHandler avail.Resize, Sub() tiles.Width = avail.Width
            root.Controls.Add(avail, 0, 2)

            AddHandler _range.SelectionChanged, Sub() OnRangeChanged()
            AddHandler _generate.Click, Async Sub() Await GenerateAsync()
            OnRangeChanged()
        End Sub

        Public Overrides ReadOnly Property Title As String
            Get
                Return "Reports"
            End Get
        End Property

        Public Overrides ReadOnly Property NavKey As String
            Get
                Return "reports"
            End Get
        End Property

        Private Sub OnRangeChanged()
            Dim preset = CType(SelectedValue(_range), DateRangePreset)
            Dim isCustom = preset = DateRangePreset.Custom
            _from.Enabled = isCustom
            _to.Enabled = isCustom
            If Not isCustom Then
                Dim r = ReportingService.ResolveRange(preset, DateTime.Today)
                _from.Value = r.FromDate
                _to.Value = r.ToDate
            End If
        End Sub

        Private Function CurrentRequest(Optional overrideType As ReportType? = Nothing) As ReportRequest
            Return New ReportRequest With {
                .Type = If(overrideType, CType(SelectedValue(_type), ReportType)),
                .FromDate = If(_from.Value, DateTime.Today),
                .ToDate = If(_to.Value, DateTime.Today)}
        End Function

        Public Overrides Async Function RefreshDataAsync() As Task
            Await GenerateAsync()
        End Function

        Private Async Function GenerateAsync() As Task
            Dim req = CurrentRequest()
            If req.FromDate > req.ToDate Then
                _to.HasError = True
                Warn(FindForm(), "The start date must be on or before the end date.")
                Return
            End If
            Dim r = Await RunAsync(Function() Svc.Reports.GenerateAsync(Session, req), _generate)
            If Not r.Ok OrElse IsDisposed Then Return
            _result = r.Value
            ShowResult(_result)
        End Function

        Private Sub ShowResult(res As ReportResult)
            _sumTotal.Text = res.Total.ToString("N0")
            _sumResolved.Text = res.Resolved.ToString("N0")
            _sumClosed.Text = res.Closed.ToString("N0")
            _sumOther.Text = $"Submitted {res.Submitted}  ·  Under Review {res.UnderReview}  ·  In Progress {res.InProgress}"
            _rangeLabel.Text = res.RangeText
            _rangeLabel.Left = _chartCard.Width - _rangeLabel.PreferredWidth - 18
            ' Re-anchor the summary values after their text changed.
            For Each v In {_sumTotal, _sumResolved, _sumClosed}
                v.Left = v.Parent.Width - v.PreferredWidth - 20
            Next
            Select Case res.Request.Type
                Case ReportType.ConcernsByCategory
                    _chartCard.Title = "Concerns by Category"
                    _bar.SetData(res.ByCategory.Select(Function(c) New CountItem With {.Label = c.Category, .Value = c.Total, .Key = c.CategoryId, .ColorHex = c.ColorHex}))
                    _bar.ColorFor = Function(i) Theme.FromHex(i.ColorHex)
                    _bar.Visible = True
                    _pie.Visible = False
                Case ReportType.DateRangeSummary
                    _chartCard.Title = If((res.Request.ToDate - res.Request.FromDate).TotalDays > 62, "Concerns by Month", "Concerns by Date")
                    _bar.SetData(res.ByDay.Select(Function(d) New CountItem With {.Label = If((res.Request.ToDate - res.Request.FromDate).TotalDays > 62, d.Label, d.Day.ToString("MMM dd")), .Value = d.Total}))
                    _bar.ColorFor = Function(i) Theme.CardBlue
                    _bar.Visible = True
                    _pie.Visible = False
                Case Else
                    _chartCard.Title = "Concerns by Status"
                    _pie.SetData(res.ByStatus.Select(Function(s) New CountItem With {.Label = s.StatusName, .Value = s.Count, .Key = CInt(s.Status), .ColorHex = ColorTranslator.ToHtml(Theme.StatusChart(s.Status))}))
                    _pie.Visible = True
                    _bar.Visible = False
            End Select
        End Sub

        Private Async Function OnTile(key As String) As Task
            Select Case key
                Case "status" : Shell.Navigate(New ReportPreviewPage(CurrentRequest(ReportType.ConcernsByStatus)))
                Case "category" : Shell.Navigate(New ReportPreviewPage(CurrentRequest(ReportType.ConcernsByCategory)))
                Case "daterange" : Shell.Navigate(New ReportPreviewPage(CurrentRequest(ReportType.DateRangeSummary)))
                Case "pdf"
                    If _result Is Nothing Then Await GenerateAsync()
                    If _result IsNot Nothing Then Await ExportAsync(Me, _result, "pdf")
            End Select
        End Function

    End Class

    ''' <summary>White tile with a coloured icon square (the "Reports Available" buttons).</summary>
    Public Class ActionTile
        Inherits Control

        Private ReadOnly _color As Color
        Private ReadOnly _glyph As String
        Private _hover As Boolean

        Public Sub New(text As String, color As Color, glyphText As String)
            Me.Text = text
            _color = color
            _glyph = glyphText
            SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.UserPaint Or ControlStyles.ResizeRedraw Or ControlStyles.Selectable, True)
            Cursor = Cursors.Hand
            TabStop = True
            AccessibleName = text
            AccessibleRole = AccessibleRole.PushButton
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            Theme.PaintParent(Me, g)
            Theme.HighQuality(g)
            Using p = Theme.RoundedRect(New RectangleF(0.5F, 0.5F, Width - 1.5F, Height - 1.5F), 10), b As New SolidBrush(If(_hover, Color.FromArgb(246, 249, 255), Color.White)),
                  pen As New Pen(If(_hover OrElse Focused, Theme.Blend(_color, Color.White, 0.4), Theme.CardBorder))
                g.FillPath(b, p)
                g.DrawPath(pen, p)
            End Using
            Dim sz = TextRenderer.MeasureText(Text, Theme.F(9.25F, FontStyle.Bold))
            Dim total = 34 + 12 + sz.Width
            Dim x = Math.Max(10, (Width - total) \ 2)
            Dim box As New Rectangle(x, (Height - 34) \ 2, 34, 34)
            Using p = Theme.RoundedRect(box, 7), b As New SolidBrush(_color)
                g.FillPath(b, p)
            End Using
            Theme.DrawGlyph(g, _glyph, box, Color.White, 11)
            TextRenderer.DrawText(g, Text, Theme.F(9.25F, FontStyle.Bold), New Rectangle(box.Right + 12, 0, Width - box.Right - 14, Height), Theme.Ink, TextFormatFlags.NoPrefix Or TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis)
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
    End Class

    ''' <summary>Report Preview (reference admin screen 8): the generated report as it will print, with export and print.</summary>
    Public Class ReportPreviewPage
        Inherits PageBase

        Private ReadOnly _request As ReportRequest
        Private ReadOnly _preview As New ReportPreviewControl With {.Dock = DockStyle.Fill}
        Private ReadOnly _print As New ModernButton("Print", ButtonKind.Primary, Glyph.Print) With {.Size = New Size(120, 40)}
        Private ReadOnly _printPreview As New ModernButton("Print Preview", ButtonKind.Outline, Glyph.Page) With {.Size = New Size(150, 40)}
        Private ReadOnly _pdf As New ModernButton("Export PDF", ButtonKind.Outline, Glyph.Download) With {.Size = New Size(140, 40)}
        Private ReadOnly _xlsx As New ModernButton("Export Excel", ButtonKind.Outline, Glyph.Grid) With {.Size = New Size(146, 40)}
        Private ReadOnly _csv As New ModernButton("Export CSV", ButtonKind.Outline, Glyph.Document) With {.Size = New Size(136, 40)}
        Private ReadOnly _info As Label = Subtle("", 8.75F)
        Private _report As ReportResult

        Public Sub New(request As ReportRequest)
            _request = request
            AutoScrollMinSize = New Size(860, 560)
            Dim card As New CardPanel With {.Dock = DockStyle.Fill, .Padding = New Padding(10)}
            card.Controls.Add(_preview)
            Dim bar As New Panel With {.Dock = DockStyle.Bottom, .Height = 60, .BackColor = Color.Transparent}
            Dim buttons As Control() = {_csv, _xlsx, _pdf, _printPreview, _print}
            For Each b In buttons
                b.Anchor = AnchorStyles.Top Or AnchorStyles.Right
                bar.Controls.Add(b)
            Next
            _info.Location = New Point(0, 24)
            bar.Controls.Add(_info)
            AddHandler bar.Resize, Sub()
                                       Dim x = bar.Width
                                       For i = buttons.Length - 1 To 0 Step -1
                                           x -= buttons(i).Width
                                           buttons(i).Location = New Point(x, 12)
                                           x -= 10
                                       Next
                                       _info.AutoSize = False
                                       _info.AutoEllipsis = True
                                       _info.SetBounds(0, 22, Math.Max(0, x - 6), 20)
                                   End Sub
            Controls.Add(card)
            Controls.Add(bar)
            Dim back As New LinkText("‹ Back to Reports", 9.5F)
            Dim top As New Panel With {.Dock = DockStyle.Top, .Height = 64, .BackColor = Color.Transparent}
            Dim t = Heading("Report Preview", 15)
            t.Location = New Point(0, 24)
            back.Location = New Point(0, 0)
            top.Controls.Add(back)
            top.Controls.Add(t)
            AddTopDown(Me, top, Gap(6))
            AddHandler back.Click, Sub() Shell.GoBack()
            AddHandler _print.Click, Async Sub() Await RunReportAction(Function() PrintAsync(Me, _report, preview:=False))
            AddHandler _printPreview.Click, Async Sub() Await RunReportAction(Function() PrintAsync(Me, _report, preview:=True))
            AddHandler _pdf.Click, Async Sub() Await RunReportAction(Function() ExportAsync(Me, _report, "pdf"))
            AddHandler _xlsx.Click, Async Sub() Await RunReportAction(Function() ExportAsync(Me, _report, "xlsx"))
            AddHandler _csv.Click, Async Sub() Await RunReportAction(Function() ExportAsync(Me, _report, "csv"))
            SetButtons(False)
        End Sub

        Public Overrides ReadOnly Property Title As String
            Get
                Return "Report Preview"
            End Get
        End Property

        Public Overrides ReadOnly Property NavKey As String
            Get
                Return "reports"
            End Get
        End Property

        Private Sub SetButtons(enabled As Boolean)
            For Each b In {_print, _printPreview, _pdf, _xlsx, _csv}
                b.Enabled = enabled
            Next
        End Sub

        Private Async Function RunReportAction(action As Func(Of Task)) As Task
            If _report Is Nothing Then Return
            SetButtons(False)
            Try
                Await action()
            Finally
                If Not IsDisposed Then SetButtons(True)
            End Try
        End Function

        Public Overrides Async Function RefreshDataAsync() As Task
            Dim r = Await RunAsync(Function() Svc.Reports.GenerateAsync(Session, _request))
            If Not r.Ok OrElse IsDisposed Then Return
            _report = r.Value
            _preview.SetReport(_report)
            _info.Text = $"{_report.Title.ToLowerInvariant()} · {_report.RangeText} · {_report.Total} concern(s) · {_preview.PageCount} page(s)"
            SetButtons(True)
        End Function

    End Class

End Namespace
