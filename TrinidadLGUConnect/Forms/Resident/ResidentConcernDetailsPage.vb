Imports TrinidadLGUConnect.Controls
Imports TrinidadLGUConnect.Helpers
Imports TrinidadLGUConnect.Models
Imports TrinidadLGUConnect.Services

Namespace Forms

    ''' <summary>
    ''' Concern Details / Status (reference screen 7). Residents can view their own report, its pictures and the
    ''' status timeline. The Update Status panel only offers what the workflow allows a resident: withdraw a
    ''' report that is still Submitted, or confirm (close) a Resolved report. The service enforces the same rules.
    ''' </summary>
    Public Class ResidentConcernDetailsPage
        Inherits PageBase

        Private ReadOnly _concernId As Integer
        Private _detail As ConcernDetail
        Private _lastInfoWidth As Integer
        Private ReadOnly _infoCard As New CardPanel With {.Dock = DockStyle.Fill, .Margin = New Padding(0, 0, 8, 0), .AutoScroll = True}
        Private ReadOnly _timelineCard As New CardPanel With {.Dock = DockStyle.Fill, .Margin = New Padding(8, 0, 8, 0), .AutoScroll = True}
        Private ReadOnly _updateCard As New CardPanel With {.Dock = DockStyle.Fill, .Margin = New Padding(8, 0, 0, 0)}
        Private ReadOnly _timeline As New StatusTimeline()
        Private ReadOnly _historyLink As New LinkText("View all updates", 8.75F)
        Private ReadOnly _newStatus As New SelectField()
        Private ReadOnly _remarks As New InputField("Enter remarks...", multiline:=True)
        Private ReadOnly _update As New ModernButton("Update Status")
        Private ReadOnly _updateInfo As Label = Subtle("", 8.75F)

        Public Sub New(concernId As Integer)
            _concernId = concernId
            AutoScrollMinSize = New Size(980, 560)
            Dim root As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 3, .RowCount = 1, .BackColor = Color.Transparent}
            root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 47))
            root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 27))
            root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 26))
            root.Controls.Add(_infoCard, 0, 0)
            root.Controls.Add(_timelineCard, 1, 0)
            root.Controls.Add(_updateCard, 2, 0)
            Controls.Add(root)
            Dim back As New LinkText("‹ Back", 9.5F)
            Dim topBar As New Panel With {.Dock = DockStyle.Top, .Height = 30, .BackColor = Color.Transparent}
            back.Location = New Point(0, 2)
            topBar.Controls.Add(back)
            AddTopDown(Me, topBar)
            AddHandler back.Click, Sub() Shell.GoBack()

            ' Status updates card
            Dim t1 = Heading("Status Updates", 11)
            t1.Location = New Point(18, 14)
            _timelineCard.Controls.Add(t1)
            _timeline.Location = New Point(18, 52)
            _timeline.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
            _timelineCard.Controls.Add(_timeline)
            _timelineCard.Controls.Add(_historyLink)
            AddHandler _timelineCard.Resize, Sub() LayoutTimeline()
            AddHandler _infoCard.Resize, Sub()
                                             If _detail IsNot Nothing AndAlso _infoCard.Width <> _lastInfoWidth Then BuildInfo(_detail)
                                         End Sub
            AddHandler _historyLink.Click, Sub() ShowHistoryDialog()

            ' Update status card
            Dim s As New VStack(_updateCard, 18, 14)
            s.AddAuto(Heading("Update Status", 11), 14)
            s.Add(_newStatus, 38, 14)
            Dim remarksLabel As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .WrapContents = False, .BackColor = Color.Transparent}
            remarksLabel.Controls.Add(Lbl("Remarks", 9, FontStyle.Bold))
            remarksLabel.Controls.Add(Subtle("(Optional)", 8.5F))
            s.AddAuto(remarksLabel, 4)
            _remarks.MaxLength = Validation.MaxRemarksLength
            s.Add(_remarks, 96, 14)
            s.Add(_update, 40, 12)
            _updateInfo.MaximumSize = New Size(240, 0)
            s.AddAuto(_updateInfo, 0)
            AddHandler _updateCard.Resize, Sub() _updateInfo.MaximumSize = New Size(Math.Max(120, _updateCard.Width - 36), 0)
            _newStatus.ItemColor = Function(o)
                                       Dim st = TryCast(o, ListOption)?.Value
                                       Return If(st Is Nothing, Theme.Muted, Theme.StatusSolid(CType(st, ConcernStatus)))
                                   End Function
            AddHandler _update.Click, AddressOf OnUpdate
        End Sub

        Public Overrides ReadOnly Property Title As String
            Get
                Return "Concern Details"
            End Get
        End Property

        Public Overrides ReadOnly Property NavKey As String
            Get
                Return "myreports"
            End Get
        End Property

        Private Sub LayoutTimeline()
            _timeline.Width = Math.Max(120, _timelineCard.ClientSize.Width - 36)
            _historyLink.Location = New Point(18, _timeline.Bottom + 8)
        End Sub

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
            BuildInfo(_detail)
            _timeline.SetSteps(StatusTimeline.BuildSteps(_detail))
            _historyLink.Text = $"View all {_detail.History.Count} update(s)"
            LayoutTimeline()
            BuildUpdateOptions(r.Value.allowed)
        End Function

        Private Sub BuildInfo(d As ConcernDetail)
            _lastInfoWidth = _infoCard.Width
            _infoCard.SuspendLayout()
            For Each c As Control In _infoCard.Controls.Cast(Of Control)().ToList()
                c.Dispose()
            Next
            _infoCard.Controls.Clear()
            Dim w = Math.Max(300, _infoCard.ClientSize.Width - 36)
            Dim t = Heading("Concern Information", 11)
            t.Location = New Point(18, 14)
            _infoCard.Controls.Add(t)

            Dim hero As New ThumbnailTile With {.Location = New Point(18, 50), .Size = New Size(160, 120), .AccessibleName = "Open the first picture"}
            If d.Attachments.Count > 0 Then
                hero.SetImageBytes(d.Attachments(0).Thumbnail)
                AddHandler hero.Opened, Sub() OpenAttachments(FindForm(), d.Attachments, 0)
            Else
                hero.Cursor = Cursors.Default
            End If
            _infoCard.Controls.Add(hero)

            Dim info = InfoTable(104, Math.Max(200, w - 174))
            AddInfoRow(info, "ID:", ValueLabel(d.ReportCode, True))
            AddInfoRow(info, "Category:", CategoryPill(d.CategoryName))
            If Not String.IsNullOrEmpty(d.SubcategoryName) Then AddInfoRow(info, "Type:", ValueLabel(d.SubcategoryName))
            AddInfoRow(info, "Location:", ValueLabel(d.LocationDisplay))
            AddInfoRow(info, "Date Reported:", ValueLabel(TextFormat.DateTimeText(d.ReportedAt)))
            AddInfoRow(info, "Status:", StatusPill(d.Status))
            info.Location = New Point(192, 48)
            _infoCard.Controls.Add(info)

            Dim y = Math.Max(hero.Bottom, info.Bottom) + 14
            Dim dl = Lbl("Description:", 9.25F, FontStyle.Bold)
            dl.Location = New Point(18, y)
            _infoCard.Controls.Add(dl)
            Dim desc = Paragraph(d.Description, w)
            desc.Location = New Point(18, y + 22)
            _infoCard.Controls.Add(desc)
            y = desc.Bottom + 14
            Dim al = Lbl(If(d.Attachments.Count = 1, "Attached Picture:", $"Attached Pictures ({d.Attachments.Count}):"), 9.25F, FontStyle.Bold)
            al.Location = New Point(18, y)
            _infoCard.Controls.Add(al)
            Dim strip = AttachmentStrip(Me, d.Attachments, New Size(110, 82), w)
            strip.Location = New Point(18, y + 24)
            _infoCard.Controls.Add(strip)
            _infoCard.ResumeLayout()
        End Sub

        Private Sub BuildUpdateOptions(allowed As List(Of ConcernStatus))
            _newStatus.Items.Clear()
            _newStatus.Items.Add(New ListOption("Select new status", Nothing))
            For Each st In allowed
                Dim label = DisplayName(st)
                If st = ConcernStatus.Closed AndAlso _detail.Status = ConcernStatus.Submitted Then label = "Closed - withdraw this report"
                If st = ConcernStatus.Closed AndAlso _detail.Status = ConcernStatus.Resolved Then label = "Closed - confirm the issue is fixed"
                _newStatus.Items.Add(New ListOption(label, st))
            Next
            _newStatus.SelectedIndex = 0
            Dim canUpdate = allowed.Count > 0
            _newStatus.Enabled = canUpdate
            _remarks.Enabled = canUpdate
            _update.Enabled = canUpdate
            _remarks.Text = ""
            If _detail.Status = ConcernStatus.Closed Then
                _updateInfo.Text = "This report is closed. No further updates are possible."
            ElseIf canUpdate Then
                _updateInfo.Text = If(_detail.Status = ConcernStatus.Submitted,
                                      "You may withdraw this report while it has not been reviewed yet. LGU personnel handle all other status changes.",
                                      "The LGU marked this concern as resolved. If the problem is fixed, you can confirm and close the report.")
            Else
                _updateInfo.Text = "LGU personnel are handling this report and will update its status. You will see every update here."
            End If
        End Sub

        Private Async Sub OnUpdate(sender As Object, e As EventArgs)
            Dim target = SelectedStatus(_newStatus)
            If Not target.HasValue Then
                _newStatus.HasError = True
                _newStatus.Focus()
                Return
            End If
            If Not Confirm(FindForm(), $"Change the status of {_detail.ReportCode} to '{DisplayName(target.Value)}'? This cannot be undone.", "Update Status") Then Return
            Dim req As New StatusUpdateRequest With {.ConcernId = _detail.Id, .ExpectedCurrentStatus = _detail.Status, .NewStatus = target.Value, .Remarks = _remarks.Text}
            Dim r = Await RunAsync(Function() Svc.Concerns.UpdateStatusAsync(Session, req), _update, _newStatus)
            If Not r.Ok Then
                If TypeOf r.Error Is ConcurrencyException Then Await RefreshDataAsync()
                Return
            End If
            Info(FindForm(), $"{_detail.ReportCode} is now '{DisplayName(target.Value)}'. The update was saved to the status history.", "Status updated")
            ' The shell refreshes this page through ConcernService.DataChanged.
        End Sub

        Private Sub ShowHistoryDialog()
            If _detail Is Nothing Then Return
            Using dlg As New StatusHistoryDialog(_detail)
                dlg.ShowDialog(FindForm())
            End Using
        End Sub

    End Class

    ''' <summary>Full chronological status history (every saved record) of one report.</summary>
    Public Class StatusHistoryDialog
        Inherits DialogBase

        Public Sub New(d As ConcernDetail)
            MyBase.New($"Status History - {d.ReportCode}", 760, 460)
            Dim grid As New ThemedGrid With {.Dock = DockStyle.Fill, .EmptyText = "No status history."}
            grid.AddText("date", "Date", 120)
            grid.AddText("from", "From", 80)
            grid.AddStatus("status", "Status", 90)
            grid.AddText("remarks", "Remarks", 200)
            grid.AddText("by", "Updated By", 100)
            For Each h In d.History
                Dim i = grid.Rows.Add(TextFormat.IsoDateTime(h.ChangedAt), If(h.FromStatus.HasValue, DisplayName(h.FromStatus.Value), "-"), h.ToStatus, If(h.Remarks, ""), h.ChangedByDisplay)
                grid.Rows(i).Cells("remarks").ToolTipText = h.Remarks
            Next
            Dim close As New ModernButton("Close", ButtonKind.Primary) With {.Dock = DockStyle.Right, .Width = 110}
            Dim bar As New Panel With {.Dock = DockStyle.Bottom, .Height = 56, .Padding = New Padding(16, 8, 16, 8), .BackColor = Color.White}
            bar.Controls.Add(close)
            Body.Padding = New Padding(16, 16, 16, 0)
            Body.Controls.Add(grid)
            Body.Controls.Add(bar)
            AcceptButton = close
            CancelButton = close
            AddHandler close.Click, Sub() Me.Close()
        End Sub
    End Class

End Namespace
