Imports TrinidadLGUConnect.Controls
Imports TrinidadLGUConnect.Helpers
Imports TrinidadLGUConnect.Models
Imports TrinidadLGUConnect.Services

Namespace Forms

    ''' <summary>
    ''' Report a Concern (reference screen 4). Residents open it from the sidebar; administrators open the
    ''' same form with "Add Concern" on the Concerns page. Fields, validation and submission are identical;
    ''' only where the page returns to afterwards differs.
    ''' </summary>
    Public Class ReportConcernPage
        Inherits PageBase

        Private ReadOnly _category As New SelectField()
        Private ReadOnly _subcategory As New SelectField()
        ''' <summary>Type a barangay (auto-completes) or pick one from the list.</summary>
        Private ReadOnly _barangay As New SelectField(editable:=True)
        Private ReadOnly _location As New InputField("Street, purok or landmark (e.g., Purok 3, near the chapel)", Glyph.MapPin)
        Private ReadOnly _description As New InputField("Describe the concern in detail...", multiline:=True)
        Private ReadOnly _counter As Label = Subtle("0 / 2000", 8)
        Private ReadOnly _date As New DateField()
        Private ReadOnly _drop As New DropZone With {.Compact = True}
        Private ReadOnly _thumbs As New FlowLayoutPanel With {.AutoSize = False, .WrapContents = False, .AutoScroll = True, .BackColor = Color.White, .Height = 0}
        Private ReadOnly _submit As New ModernButton("Submit Report")
        Private ReadOnly _cancel As New ModernButton("Cancel", ButtonKind.Outline)
        Private ReadOnly _msg As Label = MessageLabel()
        Private ReadOnly _categoryList As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .FlowDirection = FlowDirection.TopDown, .WrapContents = False, .AutoScroll = True, .BackColor = Color.Transparent}
        Private ReadOnly _attachments As New List(Of PendingAttachment)()
        Private ReadOnly _fields As Dictionary(Of String, FieldBase)
        Private _categories As List(Of Category)
        Private _submissionKey As Guid = Guid.NewGuid()
        Private _maxBytes As Long = 5L * 1024 * 1024
        Private _maxCount As Integer = 5
        Private _loaded As Boolean
        ''' <summary>Opened by LGU personnel from the Concerns page rather than by a resident.</summary>
        Private ReadOnly _personnel As Boolean = If(AppServices.Instance.Session?.Has(Permission.ConcernViewAll), False)

        Public Overrides ReadOnly Property Title As String
            Get
                Return "Report a Concern"
            End Get
        End Property

        Public Overrides ReadOnly Property NavKey As String
            Get
                Return If(_personnel, "concerns", "report")
            End Get
        End Property

        ''' <summary>The form has its own state; it must not be reset by unrelated data refreshes.</summary>
        Public Overrides ReadOnly Property RefreshOnDataChange As Boolean
            Get
                Return False
            End Get
        End Property

        Public Sub New()
            AutoScrollMinSize = New Size(900, 700)
            _fields = New Dictionary(Of String, FieldBase) From {{"Category", _category}, {"Subcategory", _subcategory}, {"Barangay", _barangay}, {"Location", _location}, {"Description", _description}, {"DateReported", _date}}
            _location.MaxLength = Validation.MaxLocationLength
            _description.MaxLength = Validation.MaxDescriptionLength
            _date.MinDate = DateTime.Today.AddDays(-365)
            _date.MaxDate = DateTime.Today

            Dim root As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 1, .BackColor = Color.Transparent}
            root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 66))
            root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 34))
            Controls.Add(root)

            ' ---------------- form card
            Dim formCard As New CardPanel With {.Dock = DockStyle.Fill, .Margin = New Padding(0, 0, 10, 0), .AutoScroll = True}
            root.Controls.Add(formCard, 0, 0)
            Dim s As New VStack(formCard, 24, 20)
            Dim header As New Panel With {.Height = 54, .BackColor = Color.Transparent}
            Dim icon As New Label With {.Text = Glyph.Contact, .Font = Theme.IconFont(18), .ForeColor = Theme.Navy, .BackColor = Color.FromArgb(230, 238, 250), .Size = New Size(46, 46), .TextAlign = ContentAlignment.MiddleCenter}
            Dim t = Heading("Report a Concern", 14)
            t.Location = New Point(58, 2)
            Dim st = Subtle("Fill in the details below to report a community concern.", 9)
            st.Location = New Point(59, 30)
            header.Controls.AddRange({icon, t, st})
            s.Add(header, 54, 14)

            s.AddRow(18, 16, 4, CType(FieldLabel("Category", True), Control), FieldLabel("Subcategory (Optional)"))
            s.AddRow(38, 16, 12, _category, _subcategory)
            s.AddRow(18, 16, 4, CType(FieldLabel("Barangay", True), Control), FieldLabel("Street / Purok / Landmark", True))
            s.AddRow(38, 16, 12, _barangay, _location)
            s.AddAuto(FieldLabel("Description", True), 4)
            s.Add(_description, 84, 2)
            _counter.Anchor = AnchorStyles.Top Or AnchorStyles.Right
            _counter.Location = New Point(formCard.Width - 24 - 70, s.Y)
            formCard.Controls.Add(_counter)
            s.Space(14)
            s.AddAuto(FieldLabel("Date Reported"), 4)
            _date.Width = 200
            _date.Location = New Point(24, s.Y)
            formCard.Controls.Add(_date)
            s.Space(50)
            s.AddAuto(FieldLabel("Attach Photo (Optional)"), 4)
            s.Add(_drop, 92, 6)
            s.Add(_thumbs, 0, 4)
            _msg.Height = 34
            s.Add(_msg, 34, 4)
            s.AddRow(42, 14, 10, _submit, _cancel)
            AddHandler formCard.Resize, Sub() _counter.Left = formCard.ClientSize.Width - 24 - _counter.PreferredWidth

            ' ---------------- categories card
            Dim catCard = TitledCard("Concern Categories", 10.5F)
            catCard.Margin = New Padding(10, 0, 0, 0)
            catCard.Padding = New Padding(16, 50, 10, 12)
            catCard.Controls.Add(_categoryList)
            root.Controls.Add(catCard, 1, 0)

            AddHandler _category.SelectionChanged, Sub() OnCategoryChanged()
            AddHandler _description.TextChanged, Sub() _counter.Text = $"{_description.Text.Length} / {Validation.MaxDescriptionLength}"
            AddHandler _drop.BrowseRequested, Sub() OpenAttachDialog(Nothing)
            AddHandler _drop.FilesDropped, Sub(sender, paths) OpenAttachDialog(paths)
            AddHandler _submit.Click, AddressOf OnSubmit
            AddHandler _cancel.Click, AddressOf OnCancel
        End Sub

        Public Overrides Async Function RefreshDataAsync() As Task
            If _loaded Then Return
            Dim r = Await RunAsync(Async Function()
                                       Dim cats = Await Svc.Concerns.GetCategoriesAsync()
                                       Dim limits = Await Svc.Concerns.GetAttachmentLimitsAsync()
                                       Dim barangays = Await Svc.Concerns.GetBarangaysAsync()
                                       Dim me1 = Await Svc.Users.GetCurrentUserAsync(Session)
                                       Return (cats, limits, barangays, me1.BarangayId)
                                   End Function)
            If Not r.Ok OrElse IsDisposed Then Return
            _loaded = True
            _categories = r.Value.cats
            ' Start with the resident's own barangay (from the profile); they can type or pick another.
            Fill(_barangay, BarangayOptions(r.Value.barangays, Nothing), r.Value.BarangayId)
            If Not r.Value.BarangayId.HasValue Then _barangay.Combo.SelectedIndex = -1
            _maxBytes = r.Value.limits.MaxBytes
            _maxCount = r.Value.limits.MaxCount
            _drop.Hint = $"JPG, PNG (Max size {TextFormat.FileSize(_maxBytes)})"
            _drop.Invalidate()
            _category.Items.Clear()
            _category.Items.Add(New ListOption("Select a category", Nothing))
            For Each c In _categories
                _category.Items.Add(New ListOption(c.Name, c.Id))
            Next
            _category.SelectedIndex = 0
            BuildCategoryList()
        End Function

        Private Sub BuildCategoryList()
            _categoryList.Controls.Clear()
            For Each c In _categories
                Dim cat = c
                Dim row As New CategoryRow(cat) With {.Width = 250}
                AddHandler row.Click, Sub()
                                          SelectCategory(cat.Id)
                                          _location.Focus()
                                      End Sub
                _categoryList.Controls.Add(row)
            Next
            AddHandler _categoryList.Resize, Sub()
                                                 For Each ctl As Control In _categoryList.Controls
                                                     ctl.Width = _categoryList.ClientSize.Width - 4
                                                 Next
                                             End Sub
        End Sub

        Private Sub SelectCategory(id As Integer)
            For Each o As ListOption In _category.Items
                If Equals(o.Value, id) Then _category.SelectedItem = o
            Next
        End Sub

        Private Sub OnCategoryChanged()
            Dim id = SelectedInt(_category)
            _subcategory.Items.Clear()
            _subcategory.Items.Add(New ListOption(If(id.HasValue, "Select a subcategory (optional)", "Select a category first"), Nothing))
            If id.HasValue Then
                Dim cat = _categories.First(Function(c) c.Id = id.Value)
                For Each sc In cat.Subcategories
                    _subcategory.Items.Add(New ListOption(sc.Name, sc.Id))
                Next
            End If
            _subcategory.SelectedIndex = 0
            _subcategory.Enabled = id.HasValue
            For Each row As CategoryRow In _categoryList.Controls.OfType(Of CategoryRow)()
                row.Selected = id.HasValue AndAlso row.CategoryId = id.Value
            Next
        End Sub

        Private Sub OpenAttachDialog(dropped As String())
            Using dlg As New AttachPictureDialog(_attachments, _maxBytes, _maxCount)
                If dropped IsNot Nothing Then
                    AddHandler dlg.Shown, Async Sub() Await dlg.AddFilesFromDropAsync(dropped)
                End If
                If dlg.ShowDialog(FindForm()) = DialogResult.OK Then
                    _attachments.Clear()
                    _attachments.AddRange(dlg.Result)
                    RenderThumbs()
                End If
            End Using
        End Sub

        Private Sub RenderThumbs()
            For Each c As Control In _thumbs.Controls.Cast(Of Control)().ToList()
                c.Dispose()
            Next
            _thumbs.Controls.Clear()
            For i = 0 To _attachments.Count - 1
                Dim idx = i
                Dim t As New ThumbnailTile With {.Size = New Size(96, 72), .ShowRemove = True, .Margin = New Padding(0, 0, 10, 0), .AccessibleName = _attachments(i).FileName}
                t.SetImageBytes(_attachments(i).Thumbnail)
                AddHandler t.Opened, Sub() ImageViewerForm.Open(FindForm(), _attachments.Select(Function(a) ViewerImage.FromPending(a)), idx)
                AddHandler t.RemoveClicked, Sub()
                                                _attachments.RemoveAt(idx)
                                                RenderThumbs()
                                            End Sub
                _thumbs.Controls.Add(t)
            Next
            Dim newH = If(_attachments.Count > 0, 80, 0)
            Dim delta = newH - _thumbs.Height
            _thumbs.Height = newH
            ' Shift the controls below the strip.
            For Each c As Control In _thumbs.Parent.Controls
                If c IsNot _thumbs AndAlso c.Top > _thumbs.Top AndAlso c IsNot _counter Then c.Top += delta
            Next
        End Sub

        Private Function IsDirty() As Boolean
            Return SelectedValue(_category) IsNot Nothing OrElse _location.Text.Trim().Length > 0 OrElse _description.Text.Trim().Length > 0 OrElse _attachments.Count > 0
        End Function

        Private Async Sub OnSubmit(sender As Object, e As EventArgs)
            _msg.ForeColor = Theme.Danger
            _msg.Text = ""
            Dim catId = SelectedInt(_category)
            If Not catId.HasValue Then
                _msg.Text = "Select a category."
                _category.Focus()
                _category.HasError = True
                Return
            End If
            Dim brgyId = SelectedInt(_barangay)
            If Not brgyId.HasValue Then
                _msg.Text = If(_barangay.HasUnmatchedText, $"'{_barangay.Combo.Text.Trim()}' is not a barangay of Trinidad. Pick one from the list.", "Select or type the barangay.")
                _barangay.Focus()
                _barangay.HasError = True
                Return
            End If
            Dim req As New NewConcernRequest With {
                .SubmissionKey = _submissionKey,
                .CategoryId = catId.Value,
                .SubcategoryId = SelectedInt(_subcategory),
                .BarangayId = brgyId,
                .Location = _location.Text,
                .Description = _description.Text,
                .ReportedDate = If(_date.Value, DateTime.Today),
                .Attachments = _attachments.ToList()}
            _msg.ForeColor = Theme.Muted
            _msg.Text = If(_attachments.Count > 0, $"Submitting report and uploading {_attachments.Count} picture(s)...", "Submitting report...")
            Dim r = Await RunAsync(Function() Svc.Concerns.SubmitAsync(Session, req), _submit, _cancel, _drop)
            If IsDisposed Then Return
            If Not r.Ok Then
                _msg.ForeColor = Theme.Danger
                _msg.Text = FriendlyMessage(r.Error)
                MarkField(_fields, r.Error)
                Return
            End If
            _msg.Text = ""
            _submissionKey = Guid.NewGuid()
            If _personnel Then
                Info(FindForm(), $"The concern has been submitted successfully." & vbLf & vbLf & $"Report ID: {r.Value.ReportCode}" & vbLf & vbLf &
                                 "It is now listed under Concerns.", "Report submitted")
                Shell.NavigateReplace(New SubmittedConcernsPage())
                Shell.Navigate(New AdminConcernDetailsPage(r.Value.ConcernId))
                Return
            End If
            Info(FindForm(), $"Your report has been submitted successfully." & vbLf & vbLf & $"Report ID: {r.Value.ReportCode}" & vbLf & vbLf &
                             "You can track its status under My Reports.", "Report submitted")
            Shell.NavigateReplace(New MyReportsPage())
            Shell.Navigate(New ResidentConcernDetailsPage(r.Value.ConcernId))
        End Sub

        Private Sub OnCancel(sender As Object, e As EventArgs)
            If IsDirty() AndAlso Not Confirm(FindForm(), "Discard this report? The information you entered will not be saved.", "Cancel report") Then Return
            If _personnel Then Shell.GoBack() Else Shell.NavigateRoot("dashboard")
        End Sub

        ''' <summary>A category entry in the right-hand "Concern Categories" list.</summary>
        Private Class CategoryRow
            Inherits Control
            Private ReadOnly _cat As Category
            Private _hover As Boolean
            Private _selected As Boolean

            Public Sub New(cat As Category)
                _cat = cat
                SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.UserPaint Or ControlStyles.ResizeRedraw Or ControlStyles.Selectable, True)
                Height = 48
                Margin = New Padding(0, 0, 0, 4)
                Cursor = Cursors.Hand
                TabStop = True
                AccessibleName = cat.Name
                AccessibleRole = AccessibleRole.PushButton
            End Sub

            Public ReadOnly Property CategoryId As Integer
                Get
                    Return _cat.Id
                End Get
            End Property

            Public Property Selected As Boolean
                Get
                    Return _selected
                End Get
                Set(value As Boolean)
                    _selected = value
                    Invalidate()
                End Set
            End Property

            Protected Overrides Sub OnPaint(e As PaintEventArgs)
                Dim g = e.Graphics
                Theme.PaintParent(Me, g)
                Theme.HighQuality(g)
                If _selected OrElse _hover OrElse Focused Then
                    Using p = Theme.RoundedRect(New RectangleF(0, 0, Width - 1, Height - 1), 8), b As New SolidBrush(If(_selected, Color.FromArgb(232, 240, 254), Color.FromArgb(244, 247, 252)))
                        g.FillPath(b, p)
                    End Using
                End If
                Dim catColor = Theme.FromHex(_cat.ColorHex)
                Dim tile As New Rectangle(6, 6, 36, 36)
                Using p = Theme.RoundedRect(tile, 7), b As New SolidBrush(catColor)
                    g.FillPath(b, p)
                End Using
                Theme.DrawGlyph(g, Theme.CategoryGlyph(_cat.Code), tile, Color.White, 12)
                TextRenderer.DrawText(g, _cat.Name, Theme.F(9.25F, If(_selected, FontStyle.Bold, FontStyle.Regular)), New Rectangle(54, 0, Width - 58, Height), Theme.Ink,
                                      TextFormatFlags.NoPrefix Or TextFormatFlags.VerticalCenter Or TextFormatFlags.Left Or TextFormatFlags.EndEllipsis)
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

    End Class

End Namespace
