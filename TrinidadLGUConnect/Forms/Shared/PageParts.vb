Imports TrinidadLGUConnect.Controls
Imports TrinidadLGUConnect.Helpers
Imports TrinidadLGUConnect.Models
Imports TrinidadLGUConnect.Services

Namespace Forms

    ''' <summary>Small builders shared by several pages.</summary>
    Public Module PageParts

        ''' <summary>A card whose title is drawn at the top-left; content should start at y = 50.</summary>
        Public Function TitledCard(title As String, Optional titleSize As Single = 11) As CardPanel
            Return New CardPanel With {.Dock = DockStyle.Fill, .Margin = New Padding(0), .Title = title, .TitleSize = titleSize}
        End Function

        ''' <summary>Two-column "Label:  value" table (auto-sized rows).</summary>
        Public Function InfoTable(labelWidth As Integer, width As Integer) As TableLayoutPanel
            Dim t As New TableLayoutPanel With {.ColumnCount = 2, .AutoSize = False, .Width = width, .Height = 0, .BackColor = Color.Transparent,
                                                .Margin = Padding.Empty, .Padding = Padding.Empty}
            t.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, labelWidth))
            t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            Return t
        End Function

        Public Sub AddInfoRow(t As TableLayoutPanel, label As String, value As Control, Optional rowHeight As Integer = 30)
            Dim l = Lbl(label, 9, FontStyle.Regular, Theme.Muted)
            l.Margin = New Padding(0, 7, 0, 0)
            If TypeOf value Is Pill Then
                value.Margin = New Padding(0, 3, 0, 0)
                value.Anchor = AnchorStyles.Left Or AnchorStyles.Top
            Else
                value.AutoSize = False
                value.Dock = DockStyle.Fill
                value.Margin = New Padding(0, 7, 0, 0)
            End If
            t.RowStyles.Add(New RowStyle(SizeType.Absolute, rowHeight))
            t.Controls.Add(l, 0, t.RowCount)
            t.Controls.Add(value, 1, t.RowCount)
            t.RowCount += 1
            t.Height += rowHeight
        End Sub

        Public Function ValueLabel(text As String, Optional bold As Boolean = False) As Label
            Dim l = Lbl(If(text, ""), 9.25F, If(bold, FontStyle.Bold, FontStyle.Regular), Theme.Ink)
            l.AutoEllipsis = True
            Return l
        End Function

        Public Function CategoryPill(name As String) As Pill
            Dim p As New Pill(name)
            p.FitWidth()
            Return p
        End Function

        Public Function StatusPill(status As ConcernStatus, Optional solid As Boolean = True) As Pill
            Dim p As New Pill()
            p.SetStatus(status, solid)
            Return p
        End Function

        ''' <summary>Opens the image viewer for a concern's saved attachments.</summary>
        Public Sub OpenAttachments(owner As IWin32Window, attachments As IList(Of AttachmentInfo), index As Integer)
            ImageViewerForm.Open(owner, attachments.Select(Function(a) ViewerImage.FromAttachment(a)), index)
        End Sub

        ''' <summary>Flow of clickable thumbnails with file name and size captions.</summary>
        ''' <summary>
        ''' Clickable thumbnails with file name and size. The panel is sized immediately for
        ''' <paramref name="maxWidth"/>, so callers can position controls below it straight away.
        ''' </summary>
        Public Function AttachmentStrip(owner As Control, attachments As IList(Of AttachmentInfo), tileSize As Size, maxWidth As Integer) As FlowLayoutPanel
            Dim sz = tileSize
            Dim flow As New FlowLayoutPanel With {.AutoSize = False, .WrapContents = True, .BackColor = Color.Transparent, .Margin = Padding.Empty}
            If attachments.Count = 0 Then
                flow.Controls.Add(Subtle("No pictures were attached to this report.", 9))
                flow.Size = New Size(maxWidth, 24)
                Return flow
            End If
            Dim itemW = sz.Width + 170
            For i = 0 To attachments.Count - 1
                Dim idx = i
                Dim a = attachments(i)
                Dim item As New Panel With {.Size = New Size(itemW, sz.Height + 6), .BackColor = Color.Transparent, .Margin = New Padding(0, 0, 12, 8)}
                Dim tile As New ThumbnailTile With {.Size = sz, .Location = New Point(0, 0), .Index = i, .AccessibleName = $"Open picture {a.FileName}"}
                tile.SetImageBytes(a.Thumbnail)
                AddHandler tile.Opened, Sub() OpenAttachments(owner.FindForm(), attachments, idx)
                Dim fileLabel = Lbl(a.FileName, 8.75F, FontStyle.Regular, Theme.Primary)
                fileLabel.AutoSize = False
                fileLabel.AutoEllipsis = True
                fileLabel.Cursor = Cursors.Hand
                fileLabel.SetBounds(sz.Width + 10, sz.Height \ 2 - 18, 158, 18)
                AddHandler fileLabel.Click, Sub() OpenAttachments(owner.FindForm(), attachments, idx)
                Dim sizeLabel = Subtle($"({a.SizeText}, {a.Width} × {a.Height})", 8)
                sizeLabel.Location = New Point(sz.Width + 10, sz.Height \ 2 + 2)
                item.Controls.Add(tile)
                item.Controls.Add(fileLabel)
                item.Controls.Add(sizeLabel)
                flow.Controls.Add(item)
            Next
            Dim perRow = Math.Max(1, (maxWidth + 12) \ (itemW + 12))
            Dim rows = CInt(Math.Ceiling(attachments.Count / CDbl(perRow)))
            flow.Size = New Size(maxWidth, rows * (sz.Height + 14))
            Return flow
        End Function

        ''' <summary>Text-only wrapping label for long descriptions.</summary>
        Public Function Paragraph(text As String, maxWidth As Integer) As Label
            Dim l = Lbl(text, 9.25F, FontStyle.Regular, Theme.Ink)
            l.MaximumSize = New Size(maxWidth, 0)
            Return l
        End Function

        ''' <summary>Search box + Filter toggle button row used by list pages.</summary>
        Public Function SearchRow(search As InputField, filterButton As ModernButton) As TableLayoutPanel
            Dim t As New TableLayoutPanel With {.Dock = DockStyle.Top, .Height = 42, .ColumnCount = 2, .RowCount = 1, .BackColor = Color.Transparent, .Margin = Padding.Empty}
            t.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            t.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 112))
            search.Dock = DockStyle.Fill
            search.Margin = New Padding(0, 0, 12, 0)
            filterButton.Dock = DockStyle.Fill
            filterButton.Margin = Padding.Empty
            t.Controls.Add(search, 0, 0)
            t.Controls.Add(filterButton, 1, 0)
            Return t
        End Function

        Public Function CategoryOptions(cats As IEnumerable(Of Category), Optional allText As String = "All Categories") As List(Of ListOption)
            Dim list As New List(Of ListOption) From {New ListOption(allText, Nothing)}
            list.AddRange(cats.Select(Function(c) New ListOption(c.Name, c.Id)))
            Return list
        End Function

        ''' <summary>Barangay choices; <paramref name="emptyText"/> is the "no selection" entry (e.g. "All Barangays").</summary>
        Public Function BarangayOptions(barangays As IEnumerable(Of Barangay), emptyText As String) As List(Of ListOption)
            Dim list As New List(Of ListOption)()
            If emptyText IsNot Nothing Then list.Add(New ListOption(emptyText, Nothing))
            list.AddRange(barangays.Select(Function(b) New ListOption(b.Name, b.Id)))
            Return list
        End Function

        Public Function StatusOptions(Optional allText As String = "All Status") As List(Of ListOption)
            Dim list As New List(Of ListOption) From {New ListOption(allText, Nothing)}
            list.AddRange(AllStatuses.Select(Function(s) New ListOption(DisplayName(s), s)))
            Return list
        End Function

        Public Sub Fill(field As SelectField, options As IEnumerable(Of ListOption), Optional selectValue As Object = Nothing)
            field.Items.Clear()
            For Each o In options : field.Items.Add(o) : Next
            Dim match = options.FirstOrDefault(Function(o) Equals(o.Value, selectValue))
            field.SelectedItem = If(match, options.FirstOrDefault())
        End Sub

        Public Function SelectedValue(field As SelectField) As Object
            Return TryCast(field.SelectedItem, ListOption)?.Value
        End Function

        Public Function SelectedInt(field As SelectField) As Integer?
            Dim v = SelectedValue(field)
            If v Is Nothing Then Return Nothing
            Return CInt(v)
        End Function

        Public Function SelectedStatus(field As SelectField) As ConcernStatus?
            Dim v = SelectedValue(field)
            If v Is Nothing Then Return Nothing
            Return CType(v, ConcernStatus)
        End Function

        ''' <summary>A label + control stacked vertically (used in filter rows).</summary>
        Public Function Labeled(caption As String, c As Control) As Panel
            Dim p As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.Transparent, .Margin = New Padding(0, 0, 12, 0)}
            Dim l = Lbl(caption, 8.75F, FontStyle.Bold, Theme.Ink)
            l.Location = New Point(0, 0)
            c.Location = New Point(0, 20)
            c.Height = 38
            c.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
            p.Controls.Add(l)
            p.Controls.Add(c)
            AddHandler p.Resize, Sub() c.Width = p.Width
            Return p
        End Function

        ''' <summary>Puts each concern in the grid. Row.Tag holds the ConcernSummary.</summary>
        Public Sub FillConcernRows(grid As ThemedGrid, items As IEnumerable(Of ConcernSummary), Optional isoDates As Boolean = False)
            grid.Rows.Clear()
            For Each c In items
                Dim i = grid.Rows.Add()
                Dim row = grid.Rows(i)
                row.Tag = c
                For Each col As DataGridViewColumn In grid.Columns
                    Select Case col.Name
                        Case "id" : row.Cells(col.Index).Value = c.ReportCode
                        Case "resident" : row.Cells(col.Index).Value = c.ResidentName
                        Case "category" : row.Cells(col.Index).Value = c.CategoryName
                        Case "location"
                            row.Cells(col.Index).Value = c.LocationListText
                            row.Cells(col.Index).ToolTipText = c.LocationDisplay
                        Case "status" : row.Cells(col.Index).Value = c.Status
                        Case "date" : row.Cells(col.Index).Value = If(isoDates, TextFormat.IsoDate(c.ReportedAt), TextFormat.ShortDate(c.ReportedAt))
                        Case "photos" : row.Cells(col.Index).Value = If(c.AttachmentCount = 0, "-", c.AttachmentCount.ToString())
                    End Select
                Next
            Next
        End Sub

        ''' <summary>Opens the image viewer for a concern row (loads the concern's attachment list first).</summary>
        Public Async Function OpenConcernPhotosAsync(page As PageBase, concernId As Integer) As Task
            Dim r = Await TryRunAsync(page, Function() AppServices.Instance.Concerns.GetDetailAsync(AppServices.Instance.Session, concernId))
            If Not r.Ok Then Return
            If r.Value.Attachments.Count = 0 Then
                Info(page.FindForm(), $"{r.Value.ReportCode} has no attached pictures.")
                Return
            End If
            OpenAttachments(page.FindForm(), r.Value.Attachments, 0)
        End Function

    End Module

End Namespace
