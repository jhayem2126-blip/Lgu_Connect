Imports System.Globalization
Imports System.Text
Imports ClosedXML.Excel
Imports PdfSharp.Drawing
Imports PdfSharp.Fonts
Imports PdfSharp.Pdf
Imports TrinidadLGUConnect.Helpers
Imports TrinidadLGUConnect.Models

Namespace Services

    ''' <summary>Writes a generated report to CSV, Excel (.xlsx) or PDF. Each method writes the whole file or throws.</summary>
    Public NotInheritable Class ExportService

        Private Shared ReadOnly Inv As CultureInfo = CultureInfo.InvariantCulture

        Private Sub New()
        End Sub

        ' ------------------------------------------------------------------ CSV

        ''' <summary>RFC 4180 quoting plus protection against spreadsheet formula injection.</summary>
        Public Shared Function CsvField(value As String) As String
            Dim v = If(value, "")
            If v.Length > 0 AndAlso "=+-@".Contains(v(0)) Then v = "'" & v
            If v.Length > 0 AndAlso (v(0) = ControlChars.Tab OrElse v(0) = ControlChars.Cr) Then v = "'" & v
            If v.IndexOfAny({","c, """"c, ControlChars.Cr, ControlChars.Lf}) >= 0 Then
                v = """" & v.Replace("""", """""") & """"
            End If
            Return v
        End Function

        Private Shared Sub Line(sb As StringBuilder, ParamArray fields As String())
            sb.Append(String.Join(",", fields.Select(AddressOf CsvField))).Append(vbCrLf)
        End Sub

        Public Shared Function BuildCsv(r As ReportResult) As String
            Dim sb As New StringBuilder()
            Line(sb, "Trinidad LGU Connect - " & r.Title)
            Line(sb, "Date range", r.RangeText)
            Line(sb, "Generated", r.GeneratedAt.ToString("yyyy-MM-dd HH:mm", Inv), r.GeneratedBy)
            sb.Append(vbCrLf)
            Line(sb, "Summary")
            Line(sb, "Total Concerns", r.Total.ToString(Inv))
            Line(sb, "Submitted", r.Submitted.ToString(Inv))
            Line(sb, "Under Review", r.UnderReview.ToString(Inv))
            Line(sb, "In Progress", r.InProgress.ToString(Inv))
            Line(sb, "Resolved", r.Resolved.ToString(Inv))
            Line(sb, "Closed", r.Closed.ToString(Inv))
            sb.Append(vbCrLf)
            Line(sb, "Concerns by Category")
            Line(sb, "Category", "Total", "Submitted", "Under Review", "In Progress", "Resolved", "Closed")
            For Each c In r.ByCategory
                Line(sb, c.Category, c.Total.ToString(Inv), c.Submitted.ToString(Inv), c.UnderReview.ToString(Inv), c.InProgress.ToString(Inv), c.Resolved.ToString(Inv), c.Closed.ToString(Inv))
            Next
            sb.Append(vbCrLf)
            Line(sb, "Concerns by Status")
            Line(sb, "Status", "Count", "Percent")
            For Each s In r.ByStatus
                Line(sb, s.StatusName, s.Count.ToString(Inv), s.Percent.ToString("0.0", Inv))
            Next
            sb.Append(vbCrLf)
            Line(sb, "Concerns by Period")
            Line(sb, "Period", "Total", "Resolved", "Closed")
            For Each d In r.ByDay
                Line(sb, d.Label, d.Total.ToString(Inv), d.Resolved.ToString(Inv), d.Closed.ToString(Inv))
            Next
            sb.Append(vbCrLf)
            Line(sb, "Concern List")
            Line(sb, "Report ID", "Date Reported", "Category", "Subcategory", "Barangay", "Location", "Status", "Resident", "Attachments")
            For Each c In r.Concerns
                Line(sb, c.ReportCode, TextFormat.IsoDateTime(c.ReportedAt), c.CategoryName, c.SubcategoryName, c.BarangayName, c.Location, c.StatusName, c.ResidentName, c.AttachmentCount.ToString(Inv))
            Next
            Return sb.ToString()
        End Function

        Public Shared Sub ExportCsv(r As ReportResult, filePath As String)
            ' UTF-8 with BOM so Excel shows Filipino names (ñ) correctly.
            WriteAtomically(filePath, Sub(tmp) File.WriteAllText(tmp, BuildCsv(r), New UTF8Encoding(True)))
        End Sub

        ' ------------------------------------------------------------------ Excel

        Public Shared Sub ExportExcel(r As ReportResult, filePath As String)
            WriteAtomically(filePath,
                Sub(tmp)
                    Using wb As New XLWorkbook()
                        Dim ws = wb.Worksheets.Add("Summary")
                        ws.Cell(1, 1).Value = "TRINIDAD LGU CONNECT"
                        ws.Cell(1, 1).Style.Font.Bold = True
                        ws.Cell(1, 1).Style.Font.FontSize = 14
                        ws.Cell(2, 1).Value = r.Title
                        ws.Cell(3, 1).Value = r.RangeText
                        ws.Cell(4, 1).Value = $"Generated {r.GeneratedAt:yyyy-MM-dd HH:mm} by {r.GeneratedBy}"
                        Dim row = 6
                        For Each item In {("Total Concerns", r.Total), ("Submitted", r.Submitted), ("Under Review", r.UnderReview), ("In Progress", r.InProgress), ("Resolved", r.Resolved), ("Closed", r.Closed)}
                            ws.Cell(row, 1).Value = item.Item1
                            ws.Cell(row, 2).Value = item.Item2
                            row += 1
                        Next
                        ws.Range(6, 1, row - 1, 1).Style.Font.Bold = True
                        row += 1
                        ws.Cell(row, 1).Value = "Concerns by Category" : ws.Cell(row, 1).Style.Font.Bold = True : row += 1
                        Dim catHeader = row
                        Dim heads = {"Category", "Total", "Submitted", "Under Review", "In Progress", "Resolved", "Closed"}
                        For i = 0 To heads.Length - 1 : ws.Cell(row, i + 1).Value = heads(i) : Next
                        row += 1
                        For Each c In r.ByCategory
                            ws.Cell(row, 1).Value = c.Category
                            ws.Cell(row, 2).Value = c.Total
                            ws.Cell(row, 3).Value = c.Submitted
                            ws.Cell(row, 4).Value = c.UnderReview
                            ws.Cell(row, 5).Value = c.InProgress
                            ws.Cell(row, 6).Value = c.Resolved
                            ws.Cell(row, 7).Value = c.Closed
                            row += 1
                        Next
                        StyleTable(ws.Range(catHeader, 1, row - 1, 7))
                        row += 1
                        ws.Cell(row, 1).Value = "Concerns by Status" : ws.Cell(row, 1).Style.Font.Bold = True : row += 1
                        Dim stHeader = row
                        ws.Cell(row, 1).Value = "Status" : ws.Cell(row, 2).Value = "Count" : ws.Cell(row, 3).Value = "Percent"
                        row += 1
                        For Each s In r.ByStatus
                            ws.Cell(row, 1).Value = s.StatusName
                            ws.Cell(row, 2).Value = s.Count
                            ws.Cell(row, 3).Value = s.Percent / 100.0
                            ws.Cell(row, 3).Style.NumberFormat.Format = "0.0%"
                            row += 1
                        Next
                        StyleTable(ws.Range(stHeader, 1, row - 1, 3))
                        ws.Columns().AdjustToContents()

                        Dim wsP = wb.Worksheets.Add("By Period")
                        wsP.Cell(1, 1).Value = "Period" : wsP.Cell(1, 2).Value = "Total" : wsP.Cell(1, 3).Value = "Resolved" : wsP.Cell(1, 4).Value = "Closed"
                        Dim pr = 2
                        For Each d In r.ByDay
                            wsP.Cell(pr, 1).Value = d.Label : wsP.Cell(pr, 2).Value = d.Total : wsP.Cell(pr, 3).Value = d.Resolved : wsP.Cell(pr, 4).Value = d.Closed
                            pr += 1
                        Next
                        StyleTable(wsP.Range(1, 1, pr - 1, 4))
                        wsP.Columns().AdjustToContents()

                        Dim wsL = wb.Worksheets.Add("Concerns")
                        Dim lh = {"Report ID", "Date Reported", "Category", "Subcategory", "Barangay", "Location", "Status", "Resident", "Attachments"}
                        For i = 0 To lh.Length - 1 : wsL.Cell(1, i + 1).Value = lh(i) : Next
                        Dim lr = 2
                        For Each c In r.Concerns
                            wsL.Cell(lr, 1).Value = c.ReportCode
                            wsL.Cell(lr, 2).Value = TextFormat.ToLocal(c.ReportedAt)
                            wsL.Cell(lr, 2).Style.DateFormat.Format = "yyyy-mm-dd hh:mm AM/PM"
                            ' Text values are written as strings, so nothing a resident typed is evaluated as a formula.
                            wsL.Cell(lr, 3).SetValue(c.CategoryName)
                            wsL.Cell(lr, 4).SetValue(If(c.SubcategoryName, ""))
                            wsL.Cell(lr, 5).SetValue(If(c.BarangayName, ""))
                            wsL.Cell(lr, 6).SetValue(c.Location)
                            wsL.Cell(lr, 7).SetValue(c.StatusName)
                            wsL.Cell(lr, 8).SetValue(c.ResidentName)
                            wsL.Cell(lr, 9).Value = c.AttachmentCount
                            lr += 1
                        Next
                        StyleTable(wsL.Range(1, 1, Math.Max(1, lr - 1), lh.Length))
                        wsL.Columns().AdjustToContents()
                        wsL.SheetView.FreezeRows(1)
                        ' Saved through a stream because ClosedXML infers the format from a file extension.
                        Using fs = File.Create(tmp)
                            wb.SaveAs(fs)
                        End Using
                    End Using
                End Sub)
        End Sub

        Private Shared Sub StyleTable(range As IXLRange)
            range.FirstRow().Style.Font.Bold = True
            range.FirstRow().Style.Fill.BackgroundColor = XLColor.FromArgb(238, 242, 248)
            range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin
            range.Style.Border.InsideBorder = XLBorderStyleValues.Hair
        End Sub

        ' ------------------------------------------------------------------ PDF

        Private Shared ReadOnly FontGate As New Object()
        Private Shared _fontsConfigured As Boolean

        ''' <summary>Supplies Segoe UI (or Arial as a fallback) from the Windows fonts folder to PDFsharp.</summary>
        Private Class WindowsFontResolver
            Implements IFontResolver

            Private Shared ReadOnly FontsDir As String = Environment.GetFolderPath(Environment.SpecialFolder.Fonts)

            Private Shared Function Pick(ParamArray files As String()) As String
                Return files.FirstOrDefault(Function(f) File.Exists(Path.Combine(FontsDir, f)))
            End Function

            Public Function ResolveTypeface(familyName As String, bold As Boolean, italic As Boolean) As FontResolverInfo Implements IFontResolver.ResolveTypeface
                Dim face = If(bold, Pick("segoeuib.ttf", "arialbd.ttf"), Pick("segoeui.ttf", "arial.ttf"))
                If face Is Nothing Then Return Nothing
                Return New FontResolverInfo(face)
            End Function

            Public Function GetFont(faceName As String) As Byte() Implements IFontResolver.GetFont
                Return File.ReadAllBytes(Path.Combine(FontsDir, Path.GetFileName(faceName)))
            End Function
        End Class

        Public Shared Sub ExportPdf(r As ReportResult, seal As Image, filePath As String)
            SyncLock FontGate
                If Not _fontsConfigured Then
                    If GlobalFontSettings.FontResolver Is Nothing Then GlobalFontSettings.FontResolver = New WindowsFontResolver()
                    _fontsConfigured = True
                End If
            End SyncLock
            WriteAtomically(filePath,
                Sub(tmp)
                    Using doc As New PdfDocument()
                        doc.Info.Title = "Trinidad LGU Connect - " & r.Title
                        doc.Info.Author = r.GeneratedBy
                        doc.Info.Subject = r.RangeText
                        Dim renderer As New ReportRenderer(r, seal)
                        Dim measurePage = doc.AddPage()
                        measurePage.Width = XUnit.FromPoint(ReportRenderer.LetterWidth)
                        measurePage.Height = XUnit.FromPoint(ReportRenderer.LetterHeight)
                        Using gx = XGraphics.FromPdfPage(measurePage)
                            Dim canvas As New PdfReportCanvas(gx)
                            renderer.Layout(ReportRenderer.LetterWidth, ReportRenderer.LetterHeight, canvas)
                            renderer.DrawPage(canvas, 0)
                        End Using
                        For i = 1 To renderer.PageCount - 1
                            Dim page = doc.AddPage()
                            page.Width = XUnit.FromPoint(ReportRenderer.LetterWidth)
                            page.Height = XUnit.FromPoint(ReportRenderer.LetterHeight)
                            Using gx = XGraphics.FromPdfPage(page)
                                renderer.DrawPage(New PdfReportCanvas(gx), i)
                            End Using
                        Next
                        doc.Save(tmp)
                    End Using
                End Sub)
        End Sub

        Private Class PdfReportCanvas
            Implements IReportCanvas
            Private ReadOnly _g As XGraphics
            Private ReadOnly _fonts As New Dictionary(Of String, XFont)()
            Private ReadOnly _images As New Dictionary(Of Image, XImage)()

            Public Sub New(g As XGraphics)
                _g = g
            End Sub

            Private Function F(size As Single, bold As Boolean) As XFont
                Dim key = $"{size}-{bold}"
                Dim font As XFont = Nothing
                If Not _fonts.TryGetValue(key, font) Then
                    font = New XFont("Segoe UI", size, If(bold, XFontStyleEx.Bold, XFontStyleEx.Regular))
                    _fonts(key) = font
                End If
                Return font
            End Function

            Private Shared Function C(col As Color) As XColor
                Return XColor.FromArgb(col.A, col.R, col.G, col.B)
            End Function

            Public Sub FillRect(r As RectangleF, color As Color) Implements IReportCanvas.FillRect
                _g.DrawRectangle(New XSolidBrush(C(color)), r.X, r.Y, r.Width, r.Height)
            End Sub

            Public Sub FillRoundRect(r As RectangleF, radius As Single, color As Color) Implements IReportCanvas.FillRoundRect
                _g.DrawRoundedRectangle(New XSolidBrush(C(color)), r.X, r.Y, r.Width, r.Height, radius * 2, radius * 2)
            End Sub

            Public Sub DrawRoundRect(r As RectangleF, radius As Single, color As Color, width As Single) Implements IReportCanvas.DrawRoundRect
                _g.DrawRoundedRectangle(New XPen(C(color), width), r.X, r.Y, r.Width, r.Height, radius * 2, radius * 2)
            End Sub

            Public Sub DrawLine(x1 As Single, y1 As Single, x2 As Single, y2 As Single, color As Color, width As Single) Implements IReportCanvas.DrawLine
                _g.DrawLine(New XPen(C(color), width), x1, y1, x2, y2)
            End Sub

            Public Sub DrawText(text As String, size As Single, bold As Boolean, color As Color, r As RectangleF, align As TextAlign) Implements IReportCanvas.DrawText
                If String.IsNullOrEmpty(text) Then Return
                Dim fmt As New XStringFormat With {.LineAlignment = XLineAlignment.Near}
                fmt.Alignment = If(align = TextAlign.Center, XStringAlignment.Center, If(align = TextAlign.Right, XStringAlignment.Far, XStringAlignment.Near))
                _g.DrawString(text, F(size, bold), New XSolidBrush(C(color)), New XRect(r.X, r.Y, r.Width, r.Height), fmt)
            End Sub

            Public Function MeasureWidth(text As String, size As Single, bold As Boolean) As Single Implements IReportCanvas.MeasureWidth
                If String.IsNullOrEmpty(text) Then Return 0
                Return CSng(_g.MeasureString(text, F(size, bold)).Width)
            End Function

            Public Sub DrawImage(img As Image, r As RectangleF) Implements IReportCanvas.DrawImage
                Dim x As XImage = Nothing
                If Not _images.TryGetValue(img, x) Then
                    Dim ms As New MemoryStream()
                    img.Save(ms, Imaging.ImageFormat.Png)
                    ms.Position = 0
                    x = XImage.FromStream(ms)
                    _images(img) = x
                End If
                _g.DrawImage(x, r.X, r.Y, r.Width, r.Height)
            End Sub
        End Class

        ' ------------------------------------------------------------------ helpers

        ''' <summary>Writes to a temporary file first so a failed export never leaves a truncated file behind.</summary>
        Private Shared Sub WriteAtomically(filePath As String, write As Action(Of String))
            Dim dir = Path.GetDirectoryName(Path.GetFullPath(filePath))
            Dim tmp = Path.Combine(dir, "." & Path.GetFileName(filePath) & "." & Guid.NewGuid().ToString("N").Substring(0, 8) & ".tmp")
            Try
                write(tmp)
                File.Move(tmp, filePath, overwrite:=True)
            Finally
                If File.Exists(tmp) Then
                    Try
                        File.Delete(tmp)
                    Catch ex As IOException
                    End Try
                End If
            End Try
        End Sub

    End Class

End Namespace
