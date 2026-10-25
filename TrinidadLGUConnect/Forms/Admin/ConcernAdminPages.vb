Imports TrinidadLGUConnect.Controls
Imports TrinidadLGUConnect.Helpers
Imports TrinidadLGUConnect.Models
Imports TrinidadLGUConnect.Services

Namespace Forms

    ''' <summary>Admin Concern Details (reference admin screen 4) for the selected report.</summary>
    Public Class AdminConcernDetailsPage
        Inherits PageBase

        Private ReadOnly _concernId As Integer
        Private ReadOnly _card As New CardPanel With {.Dock = DockStyle.Fill, .AutoScroll = True}
        Private ReadOnly _close As New ModernButton("Close", ButtonKind.Secondary) With {.Size = New Size(110, 40)}
        Private ReadOnly _update As New ModernButton("Update Status", ButtonKind.Primary, Glyph.Edit) With {.Size = New Size(160, 40)}
        Private _detail As ConcernDetail

        Public Sub New(concernId As Integer)
            _concernId = concernId
            AutoScrollMinSize = New Size(820, 560)
            Dim bar As New Panel With {.Dock = DockStyle.Bottom, .Height = 58, .BackColor = Color.Transparent}
            _close.Anchor = AnchorStyles.Top Or AnchorStyles.Right
            _update.Anchor = AnchorStyles.Top Or AnchorStyles.Right
            bar.Controls.Add(_close)
            bar.Controls.Add(_update)
            AddHandler bar.Resize, Sub()
                                       _close.Location = New Point(bar.Width - _close.Width, 12)
                                       _update.Location = New Point(_close.Left - _update.Width - 12, 12)
                                   End Sub
            Controls.Add(_card)
            Controls.Add(bar)
            AddTopDown(Me, BuildHeader("Concern Details"), Gap(8))
            _update.Visible = AppServices.Instance.Session.Has(Permission.ConcernUpdateStatus)
            AddHandler _close.Click, Sub() Shell.GoBack()
            AddHandler _update.Click, Sub() Shell.Navigate(New UpdateStatusPage(_concernId))
            AddHandler _card.Resize, Sub()
                                         If _detail IsNot Nothing AndAlso _card.Width <> _lastWidth Then Build(_detail)
                                     End Sub
        End Sub

        Private _lastWidth As Integer

        Public Overrides ReadOnly Property Title As String
            Get
                Return "Concern Details"
            End Get
        End Property

        Public Overrides ReadOnly Property NavKey As String
            Get
                Return "concerns"
            End Get
        End Property

        Public Overrides Async Function RefreshDataAsync() As Task
            Dim r = Await RunAsync(Function() Svc.Concerns.GetDetailAsync(Session, _concernId))
            If IsDisposed Then Return
            If Not r.Ok Then
                If TypeOf r.Error Is AuthorizationException Then Shell.GoBack()
                Return
            End If
            _detail = r.Value
            _update.Enabled = _detail.Status <> ConcernStatus.Closed
            Build(_detail)
        End Function

        Private Sub Build(d As ConcernDetail)
            _lastWidth = _card.Width
            _card.SuspendLayout()
            For Each c As Control In _card.Controls.Cast(Of Control)().ToList()
                c.Dispose()
            Next
            _card.Controls.Clear()
            Dim w = Math.Max(400, _card.ClientSize.Width - 40)

            Dim hero As New ThumbnailTile With {.Location = New Point(20, 20), .Size = New Size(260, 190), .AccessibleName = "Open the first picture"}
            If d.Attachments.Count > 0 Then
                hero.SetImageBytes(d.Attachments(0).Thumbnail)
                AddHandler hero.Opened, Sub() OpenAttachments(FindForm(), d.Attachments, 0)
            Else
                hero.Cursor = Cursors.Default
            End If
            _card.Controls.Add(hero)

            Dim info = InfoTable(130, Math.Max(240, w - 290))
            AddInfoRow(info, "Concern ID:", ValueLabel(d.ReportCode, True))
            AddInfoRow(info, "Category:", CategoryPill(d.CategoryName))
            If Not String.IsNullOrEmpty(d.SubcategoryName) Then AddInfoRow(info, "Type:", ValueLabel(d.SubcategoryName))
            AddInfoRow(info, "Resident:", ValueLabel(d.ResidentName & If(String.IsNullOrEmpty(d.ResidentEmail), "", $"  ({d.ResidentEmail})")))
            AddInfoRow(info, "Location:", ValueLabel(d.LocationDisplay))
            AddInfoRow(info, "Date Reported:", ValueLabel(TextFormat.DateTimeText(d.ReportedAt)))
            AddInfoRow(info, "Status:", StatusPill(d.Status))
            info.Location = New Point(300, 20)
            _card.Controls.Add(info)

            Dim y = Math.Max(hero.Bottom, info.Bottom) + 16
            Dim line As New Panel With {.Location = New Point(20, y), .Size = New Size(w, 1), .BackColor = Theme.CardBorder, .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right}
            _card.Controls.Add(line)
            y += 14
            Dim dl = Lbl("Description:", 9.5F, FontStyle.Bold, Theme.Navy)
            dl.Location = New Point(20, y)
            _card.Controls.Add(dl)
            Dim desc = Paragraph(d.Description, w - 10)
            desc.Location = New Point(24, y + 24)
            _card.Controls.Add(desc)
            y = desc.Bottom + 16
            Dim al = Lbl(If(d.Attachments.Count = 1, "Attached Picture:", $"Attached Pictures ({d.Attachments.Count}):"), 9.5F, FontStyle.Bold, Theme.Navy)
            al.Location = New Point(20, y)
            _card.Controls.Add(al)
            Dim strip = AttachmentStrip(Me, d.Attachments, New Size(110, 82), w)
            strip.Location = New Point(20, y + 26)
            _card.Controls.Add(strip)
            y = strip.Bottom + 18

            Dim hl = Lbl("Status History", 9.5F, FontStyle.Bold, Theme.Navy)
            hl.Location = New Point(20, y)
            _card.Controls.Add(hl)
            Dim grid = HistoryGrid(d.History)
            grid.Location = New Point(20, y + 26)
            grid.Size = New Size(w, grid.HeightFor(Math.Max(1, Math.Min(d.History.Count, 8))))
            grid.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
            _card.Controls.Add(grid)
            Dim pad As New Panel With {.Location = New Point(0, grid.Bottom), .Size = New Size(10, 16), .BackColor = Color.Transparent}
            _card.Controls.Add(pad)
            _card.ResumeLayout()
        End Sub

        ''' <summary>Status history table shared with the Update Status page.</summary>
        Public Shared Function HistoryGrid(history As IEnumerable(Of StatusHistoryEntry)) As ThemedGrid
            Dim grid As New ThemedGrid With {.EmptyText = "No status history.", .ScrollBars = ScrollBars.Vertical}
            grid.RowTemplate.Height = 36
            grid.ColumnHeadersHeight = 36
            grid.AddText("date", "Date", 120)
            grid.AddText("status", "Status", 90)
            grid.AddText("remarks", "Remarks", 220)
            grid.AddText("by", "Updated By", 110)
            For Each h In history
                Dim i = grid.Rows.Add(TextFormat.IsoDateTime(h.ChangedAt), DisplayName(h.ToStatus), If(h.Remarks, ""), h.ChangedByDisplay)
                Dim row = grid.Rows(i)
                row.Cells("status").Style.ForeColor = Theme.StatusSolid(h.ToStatus)
                row.Cells("status").Style.SelectionForeColor = Theme.StatusSolid(h.ToStatus)
                row.Cells("status").Style.Font = Theme.F(9, FontStyle.Bold)
                row.Cells("remarks").ToolTipText = h.Remarks
                If h.EffectiveDate.HasValue Then row.Cells("date").ToolTipText = $"Effective date: {h.EffectiveDate.Value:yyyy-MM-dd}"
            Next
            Return grid
        End Function

    End Class

    ''' <summary>
    ''' Update Concern Status (reference admin screen 5). Offers only transitions allowed by the workflow;
    ''' the service re-checks permission, the transition and concurrency inside a transaction.
    ''' </summary>
    Public Class UpdateStatusPage
        Inherits PageBase

        Private ReadOnly _concernId As Integer
        Private _detail As ConcernDetail
        Private ReadOnly _idValue As Label = ValueLabel("", True)
        Private ReadOnly _current As New Pill()
        Private ReadOnly _newStatus As New SelectField()
        Private ReadOnly _remarks As New InputField("Describe the action taken or the reason for this update...", multiline:=True)
        Private ReadOnly _date As New DateField()
        Private ReadOnly _historyHost As New Panel With {.BackColor = Color.White}
        Private ReadOnly _save As New ModernButton("Update Status") With {.Size = New Size(160, 40)}
        Private ReadOnly _cancel As New ModernButton("Cancel", ButtonKind.Secondary) With {.Size = New Size(110, 40)}
        Private ReadOnly _msg As Label = MessageLabel()

        Public Sub New(concernId As Integer)
            _concernId = concernId
            AutoScrollMinSize = New Size(820, 640)
            _remarks.MaxLength = Validation.MaxRemarksLength
            _date.MaxDate = DateTime.Today
            _newStatus.ItemColor = Function(o)
                                       Dim st = TryCast(o, ListOption)?.Value
                                       Return If(st Is Nothing, Theme.Muted, Theme.StatusSolid(CType(st, ConcernStatus)))
                                   End Function

            ' Top info cards: Concern ID | Current Status
            Dim top As New TableLayoutPanel With {.Dock = DockStyle.Top, .Height = 64, .ColumnCount = 2, .RowCount = 1, .BackColor = Color.Transparent}
            top.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
            top.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
            Dim idCard As New CardPanel With {.Dock = DockStyle.Fill, .Margin = New Padding(0, 0, 8, 0)}
            Dim idLbl = Lbl("Concern ID:", 9.25F, FontStyle.Regular, Theme.Muted)
            idLbl.Location = New Point(18, 21)
            _idValue.Location = New Point(130, 20)
            idCard.Controls.Add(idLbl)
            idCard.Controls.Add(_idValue)
            Dim stCard As New CardPanel With {.Dock = DockStyle.Fill, .Margin = New Padding(8, 0, 0, 0)}
            Dim stLbl = Lbl("Current Status:", 9.25F, FontStyle.Regular, Theme.Muted)
            stLbl.Location = New Point(18, 21)
            _current.Location = New Point(140, 18)
            stCard.Controls.Add(stLbl)
            stCard.Controls.Add(_current)
            top.Controls.Add(idCard, 0, 0)
            top.Controls.Add(stCard, 1, 0)

            ' Form card
            Dim form As New CardPanel With {.Dock = DockStyle.Fill}
            Dim grid As New TableLayoutPanel With {.Location = New Point(18, 16), .ColumnCount = 2, .RowCount = 3, .BackColor = Color.Transparent,
                                                   .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right, .Height = 196}
            grid.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 120))
            grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            grid.RowStyles.Add(New RowStyle(SizeType.Absolute, 50))
            grid.RowStyles.Add(New RowStyle(SizeType.Absolute, 96))
            grid.RowStyles.Add(New RowStyle(SizeType.Absolute, 50))
            Dim l1 = Lbl("New Status:", 9.25F, FontStyle.Bold)
            Dim l2 = Lbl("Remarks:", 9.25F, FontStyle.Bold)
            Dim l3 = Lbl("Update Date:", 9.25F, FontStyle.Bold)
            For Each l In {l1, l2, l3} : l.Margin = New Padding(0, 10, 0, 0) : Next
            _newStatus.Width = 260
            _newStatus.Margin = New Padding(0, 0, 0, 12)
            _remarks.Dock = DockStyle.Fill
            _remarks.Margin = New Padding(0, 0, 0, 12)
            _date.Width = 220
            _date.Margin = New Padding(0, 0, 0, 0)
            grid.Controls.Add(l1, 0, 0) : grid.Controls.Add(_newStatus, 1, 0)
            grid.Controls.Add(l2, 0, 1) : grid.Controls.Add(_remarks, 1, 1)
            grid.Controls.Add(l3, 0, 2) : grid.Controls.Add(_date, 1, 2)
            form.Controls.Add(grid)
            _msg.Location = New Point(138, 214)
            _msg.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
            form.Controls.Add(_msg)
            Dim hTitle = Lbl("Status History", 10, FontStyle.Bold, Theme.Navy)
            hTitle.Location = New Point(18, 240)
            form.Controls.Add(hTitle)
            _historyHost.Location = New Point(18, 268)
            _historyHost.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom
            form.Controls.Add(_historyHost)
            _save.Anchor = AnchorStyles.Bottom Or AnchorStyles.Right
            _cancel.Anchor = AnchorStyles.Bottom Or AnchorStyles.Right
            form.Controls.Add(_save)
            form.Controls.Add(_cancel)
            AddHandler form.Resize, Sub()
                                        grid.Width = form.ClientSize.Width - 36
                                        _msg.Width = form.ClientSize.Width - 156
                                        _cancel.Location = New Point(form.ClientSize.Width - 18 - _cancel.Width, form.ClientSize.Height - 16 - _cancel.Height)
                                        _save.Location = New Point(_cancel.Left - 12 - _save.Width, _cancel.Top)
                                        _historyHost.Size = New Size(form.ClientSize.Width - 36, Math.Max(80, _cancel.Top - 16 - _historyHost.Top))
                                    End Sub

            Controls.Add(form)
            AddTopDown(Me, BuildHeader("Update Concern Status"), Gap(6), top, Gap(14))

            AddHandler _save.Click, AddressOf OnSave
            AddHandler _cancel.Click, Sub() Shell.GoBack()
        End Sub

        Public Overrides ReadOnly Property Title As String
            Get
                Return "Update Concern Status"
            End Get
        End Property

        Public Overrides ReadOnly Property NavKey As String
            Get
                Return "concerns"
            End Get
        End Property

        Public Overrides Async Function RefreshDataAsync() As Task
            Dim r = Await RunAsync(Async Function()
                                       Dim d = Await Svc.Concerns.GetDetailAsync(Session, _concernId)
                                       Dim allowed = Await Svc.Concerns.GetAllowedTransitionsAsync(Session, d)
                                       Return (d, allowed)
                                   End Function)
            If IsDisposed Then Return
            If Not r.Ok Then
                If TypeOf r.Error Is AuthorizationException Then Shell.GoBack()
                Return
            End If
            _detail = r.Value.d
            _idValue.Text = _detail.ReportCode
            _current.SetStatus(_detail.Status, solid:=True)
            _newStatus.Items.Clear()
            _newStatus.Items.Add(New ListOption("Select new status", Nothing))
            For Each st In r.Value.allowed
                _newStatus.Items.Add(New ListOption(DisplayName(st), st))
            Next
            _newStatus.SelectedIndex = If(r.Value.allowed.Count = 1, 1, 0)
            _date.MinDate = TextFormat.ToLocal(_detail.ReportedAt).Date
            _date.Value = DateTime.Today
            Dim canUpdate = r.Value.allowed.Count > 0
            _newStatus.Enabled = canUpdate
            _remarks.Enabled = canUpdate
            _save.Enabled = canUpdate
            _msg.ForeColor = Theme.Muted
            _msg.Text = If(canUpdate, "", If(_detail.Status = ConcernStatus.Closed, "This concern is closed; its status can no longer be changed.", "You do not have permission to change this concern's status."))
            For Each c As Control In _historyHost.Controls.Cast(Of Control)().ToList()
                c.Dispose()
            Next
            Dim hg = AdminConcernDetailsPage.HistoryGrid(_detail.History)
            hg.Dock = DockStyle.Fill
            _historyHost.Controls.Add(hg)
        End Function

        Private Async Sub OnSave(sender As Object, e As EventArgs)
            _msg.ForeColor = Theme.Danger
            _msg.Text = ""
            Dim target = SelectedStatus(_newStatus)
            If Not target.HasValue Then
                _msg.Text = "Select the new status."
                _newStatus.HasError = True
                _newStatus.Focus()
                Return
            End If
            Dim req As New StatusUpdateRequest With {.ConcernId = _detail.Id, .ExpectedCurrentStatus = _detail.Status, .NewStatus = target.Value,
                                                    .Remarks = _remarks.Text, .EffectiveDate = _date.Value}
            Dim r = Await RunAsync(Function() Svc.Concerns.UpdateStatusAsync(Session, req), _save, _cancel)
            If IsDisposed Then Return
            If Not r.Ok Then
                If TypeOf r.Error Is ConcurrencyException Then Await RefreshDataAsync()
                _msg.ForeColor = Theme.Danger
                _msg.Text = FriendlyMessage(r.Error)
                Return
            End If
            Dim note = If(r.Value.ResidentNotified, vbLf & vbLf & "The resident was notified by email.",
                          If(r.Value.NotificationError IsNot Nothing, vbLf & vbLf & r.Value.NotificationError, ""))
            Info(FindForm(), $"{_detail.ReportCode} was updated from '{DisplayName(_detail.Status)}' to '{DisplayName(target.Value)}' and recorded in the status history.{note}", "Status updated")
            Shell.GoBack()
        End Sub

    End Class

End Namespace
