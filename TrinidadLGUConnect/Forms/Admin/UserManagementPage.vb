Imports TrinidadLGUConnect.Controls
Imports TrinidadLGUConnect.Helpers
Imports TrinidadLGUConnect.Models
Imports TrinidadLGUConnect.Services

Namespace Forms

    ''' <summary>User Management (reference admin screen 7). Every rule (admin-only roles, no self-demotion, last admin) is enforced by UserService.</summary>
    Public Class UserManagementPage
        Inherits PageBase

        Private ReadOnly _search As New InputField("Search by name, username, email or role...", Glyph.Search)
        Private ReadOnly _filterBtn As New ModernButton("Filter", ButtonKind.Outline, Glyph.Filter)
        Private ReadOnly _role As New SelectField()
        Private ReadOnly _status As New SelectField()
        Private ReadOnly _filterPanel As New TableLayoutPanel With {.Dock = DockStyle.Top, .Height = 64, .ColumnCount = 3, .RowCount = 1, .BackColor = Color.Transparent, .Visible = False}
        Private ReadOnly _grid As New ThemedGrid With {.EmptyText = "No users match your search."}
        Private ReadOnly _pager As New Pager With {.Dock = DockStyle.Bottom, .Height = 52}
        Private ReadOnly _add As New ModernButton("Add New User", ButtonKind.Primary, Glyph.Add) With {.Size = New Size(160, 40)}
        Private ReadOnly _debounce As New Timer With {.Interval = 400}
        Private _page As Integer = 1
        Private _pageSize As Integer = 10
        Private _ready As Boolean
        Private _barangays As New List(Of Barangay)()

        Public Sub New()
            AutoScrollMinSize = New Size(860, 560)
            Fill(_role, {New ListOption("All Roles", Nothing), New ListOption("Admin", UserRole.Administrator), New ListOption("Staff", UserRole.Staff), New ListOption("Resident", UserRole.Resident)})
            Fill(_status, {New ListOption("All Status", Nothing), New ListOption("Active", True), New ListOption("Inactive", False)})
            _filterPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 30))
            _filterPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 30))
            _filterPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 40))
            _filterPanel.Controls.Add(Labeled("Role", _role), 0, 0)
            _filterPanel.Controls.Add(Labeled("Status", _status), 1, 0)

            _grid.AddText("id", "ID", 70)
            _grid.AddText("name", "Name", 140)
            _grid.AddText("username", "Username", 110)
            _grid.AddText("email", "Email", 150)
            _grid.AddText("role", "Role", 80)
            _grid.AddText("barangay", "Barangay", 100)
            _grid.AddText("status", "Status", 80)
            _grid.AddActions("actions", "Actions", {
                New GridAction With {.Key = "view", .GlyphText = Glyph.Eye, .Color = Color.FromArgb(70, 84, 110), .Tooltip = "View profile"},
                New GridAction With {.Key = "edit", .GlyphText = Glyph.Edit, .Tooltip = "Edit user", .IsVisible = Function(o) CanEdit(TryCast(o, UserAccount))},
                New GridAction With {.Key = "toggle", .GlyphText = Glyph.Lock, .Color = Color.FromArgb(196, 120, 0), .Tooltip = "Activate / deactivate", .IsVisible = Function(o) CanRemove(TryCast(o, UserAccount))},
                New GridAction With {.Key = "delete", .GlyphText = Glyph.Delete, .Color = Theme.Danger, .Tooltip = "Delete user", .IsVisible = Function(o) CanRemove(TryCast(o, UserAccount))}})
            AddHandler _grid.CellPainting, AddressOf PaintStatusCell

            Dim card As New CardPanel With {.Dock = DockStyle.Fill, .Padding = New Padding(14, 12, 14, 4)}
            _grid.Dock = DockStyle.Fill
            card.Controls.Add(_grid)
            card.Controls.Add(_pager)
            Controls.Add(card)

            Dim header As New Panel With {.Dock = DockStyle.Top, .Height = 58, .BackColor = Color.Transparent}
            Dim t = Heading("User Management", 15)
            t.Location = New Point(0, 2)
            Dim st = Subtle("Manage system users and accounts.", 9)
            st.Location = New Point(2, 32)
            header.Controls.Add(t)
            header.Controls.Add(st)
            _add.Anchor = AnchorStyles.Top Or AnchorStyles.Right
            header.Controls.Add(_add)
            AddHandler header.Resize, Sub() _add.Location = New Point(header.Width - _add.Width, 8)
            _add.Visible = AppServices.Instance.Session.Has(Permission.UserManage)
            AddTopDown(Me, header, Gap(8), SearchRow(_search, _filterBtn), Gap(10), _filterPanel, Gap(10))

            AddHandler _filterBtn.Click, Sub() _filterPanel.Visible = Not _filterPanel.Visible
            AddHandler _search.TextChanged, Sub()
                                                _debounce.Stop()
                                                _debounce.Start()
                                            End Sub
            AddHandler _debounce.Tick, Sub()
                                           _debounce.Stop()
                                           Reload()
                                       End Sub
            AddHandler _role.SelectionChanged, Sub() Reload()
            AddHandler _status.SelectionChanged, Sub() Reload()
            AddHandler _pager.PageRequested, Async Sub(s, p)
                                                 _page = p
                                                 Await LoadAsync()
                                             End Sub
            AddHandler _add.Click, Sub() OpenEditor(Nothing)
            AddHandler _grid.RowActivated, Sub(s, row) OpenProfile(TryCast(row.Tag, UserAccount))
            AddHandler _grid.ActionClicked, AddressOf OnAction
        End Sub

        Public Overrides ReadOnly Property Title As String
            Get
                Return "User Management"
            End Get
        End Property

        Public Overrides ReadOnly Property NavKey As String
            Get
                Return "users"
            End Get
        End Property

        Public Overrides ReadOnly Property RefreshOnDataChange As Boolean
            Get
                Return False
            End Get
        End Property

        Private Sub Reload()
            If Not _ready Then Return
            _page = 1
            Dim unused = LoadAsync()
        End Sub

        Public Overrides Async Function RefreshDataAsync() As Task
            If Not _ready Then
                Dim r = Await RunAsync(Function() Svc.Settings.GetIntAsync(SettingKeys.ItemsPerPage, 10))
                If r.Ok Then _pageSize = Math.Max(5, r.Value)
                Dim b = Await RunAsync(Function() Svc.Concerns.GetBarangaysAsync())
                If b.Ok Then _barangays = b.Value
                _ready = True
            End If
            Await LoadAsync()
        End Function

        Private Async Function LoadAsync() As Task
            Dim f As New UserFilter With {
                .Search = If(String.IsNullOrWhiteSpace(_search.Text), Nothing, _search.Text.Trim()),
                .Role = If(SelectedValue(_role) Is Nothing, CType(Nothing, UserRole?), CType(SelectedValue(_role), UserRole)),
                .IsActive = If(SelectedValue(_status) Is Nothing, CType(Nothing, Boolean?), CBool(SelectedValue(_status))),
                .Page = _page, .PageSize = _pageSize}
            Dim r = Await RunAsync(Function() Svc.Users.SearchAsync(Session, f))
            If Not r.Ok OrElse IsDisposed Then Return
            _page = r.Value.Page
            _grid.Rows.Clear()
            For Each u In r.Value.Items
                Dim i = _grid.Rows.Add(u.DisplayId, u.FullName, u.Username, u.Email, u.RoleName, If(u.BarangayName, "-"), If(u.IsActive, "Active", "Inactive"))
                _grid.Rows(i).Tag = u
                Dim tags As New List(Of String)()
                If u.Id = Session.UserId Then tags.Add("you")
                If u.IsPrimaryAdmin Then tags.Add("primary")
                If tags.Count > 0 Then _grid.Rows(i).Cells("name").Value = $"{u.FullName} ({String.Join(", ", tags)})"
            Next
            _pager.SetState(r.Value.Page, r.Value.TotalPages, r.Value.TotalCount, r.Value.FirstIndex, r.Value.LastIndex)
        End Function

        Private Sub PaintStatusCell(sender As Object, e As DataGridViewCellPaintingEventArgs)
            If e.RowIndex < 0 OrElse e.ColumnIndex < 0 OrElse _grid.Columns(e.ColumnIndex).Name <> "status" Then Return
            e.PaintBackground(e.CellBounds, True)
            Dim active = CStr(e.Value) = "Active"
            Dim inner = New Rectangle(e.CellBounds.X + 10, e.CellBounds.Y, e.CellBounds.Width - 16, e.CellBounds.Height)
            Theme.DrawPill(e.Graphics, CStr(e.Value), inner, If(active, Color.FromArgb(222, 245, 233), Color.FromArgb(238, 240, 244)),
                           If(active, Color.FromArgb(22, 145, 80), Theme.Muted), center:=False)
            e.Handled = True
        End Sub

        Private Async Sub OnAction(sender As Object, row As DataGridViewRow, key As String)
            Dim u = TryCast(row.Tag, UserAccount)
            If u Is Nothing Then Return
            Select Case key
                Case "view"
                    OpenProfile(u)
                Case "edit"
                    OpenEditor(u)
                Case "toggle"
                    Dim activate = Not u.IsActive
                    Dim verb = If(activate, "activate", "deactivate")
                    Dim extra = If(activate, "", vbLf & vbLf & "The user will be signed out and will not be able to log in until reactivated.")
                    If Not Confirm(FindForm(), $"Do you want to {verb} the account '{u.Username}' ({u.FullName})?{extra}", "Confirm") Then Return
                    If Await RunAsync(Function() Svc.Users.SetActiveAsync(Session, u.Id, activate)) Then
                        Await LoadAsync()
                    End If
                Case "delete"
                    If Not Confirm(FindForm(), $"Permanently delete the account '{u.Username}' ({u.FullName})?" & vbLf & vbLf &
                                   "Accounts that have reports or status updates cannot be deleted; deactivate them instead.", "Delete user") Then Return
                    If Await RunAsync(Function() Svc.Users.DeleteAsync(Session, u.Id)) Then
                        Info(FindForm(), $"The account '{u.Username}' was deleted.")
                        Await LoadAsync()
                    End If
            End Select
        End Sub

        ''' <summary>Your own account and the primary administrator cannot be deactivated or deleted (the service enforces this too).</summary>
        Private Shared Function CanRemove(u As UserAccount) As Boolean
            Return u IsNot Nothing AndAlso u.Id <> AppServices.Instance.Session.UserId AndAlso Not u.IsPrimaryAdmin
        End Function

        ''' <summary>Only its owner may edit the primary administrator account.</summary>
        Private Shared Function CanEdit(u As UserAccount) As Boolean
            Return u IsNot Nothing AndAlso (Not u.IsPrimaryAdmin OrElse u.Id = AppServices.Instance.Session.UserId)
        End Function

        Private Sub OpenProfile(u As UserAccount)
            If u Is Nothing Then Return
            Using dlg As New UserProfileDialog(u.Id, Session.Has(Permission.UserManage) AndAlso CanEdit(u))
                If dlg.ShowDialog(FindForm()) = DialogResult.Yes Then OpenEditor(u)
            End Using
        End Sub

        Private Async Sub OpenEditor(u As UserAccount)
            If Not Session.Has(Permission.UserManage) OrElse (u IsNot Nothing AndAlso Not CanEdit(u)) Then Return
            Using dlg As New UserEditDialog(u, Session.Has(Permission.UserManageAdmins), u IsNot Nothing AndAlso u.Id = Session.UserId, _barangays)
                If dlg.ShowDialog(FindForm()) = DialogResult.OK Then Await LoadAsync()
            End Using
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then _debounce.Dispose()
            MyBase.Dispose(disposing)
        End Sub

    End Class

    ''' <summary>
    ''' Read-only profile of a user for LGU personnel: profile picture, contact details, barangay, address,
    ''' account status and the user's report counts. "Edit User" closes it with DialogResult.Yes.
    ''' </summary>
    Public Class UserProfileDialog
        Inherits DialogBase

        Private ReadOnly _userId As Integer
        Private ReadOnly _canEdit As Boolean
        Private ReadOnly _content As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.White, .AutoScroll = True}

        Public Sub New(userId As Integer, canEdit As Boolean)
            MyBase.New("User Profile", 600, 680)
            _userId = userId
            _canEdit = canEdit
            Dim bar As New Panel With {.Dock = DockStyle.Bottom, .Height = 64, .BackColor = Color.White, .Padding = New Padding(30, 10, 30, 14)}
            Dim close As New ModernButton("Close", ButtonKind.Outline) With {.Dock = DockStyle.Right, .Width = 120}
            bar.Controls.Add(close)
            If canEdit Then
                Dim gap As New Panel With {.Dock = DockStyle.Right, .Width = 12, .BackColor = Color.White}
                Dim edit As New ModernButton("Edit User", ButtonKind.Primary, Glyph.Edit) With {.Dock = DockStyle.Right, .Width = 140}
                bar.Controls.Add(gap)
                bar.Controls.Add(edit)
                AddHandler edit.Click, Sub()
                                           DialogResult = DialogResult.Yes
                                           Me.Close()
                                       End Sub
            End If
            Body.Controls.Add(_content)
            Body.Controls.Add(bar)
            _content.Controls.Add(Subtle("Loading profile...", 9.5F))
            CancelButton = close
            AddHandler close.Click, Sub() Me.Close()
        End Sub

        Protected Overrides Async Sub OnShown(e As EventArgs)
            MyBase.OnShown(e)
            Dim r = Await TryRunAsync(Me, Function() AppServices.Instance.Users.GetUserProfileAsync(AppServices.Instance.Session, _userId))
            If IsDisposed Then Return
            If Not r.Ok Then
                Close()
                Return
            End If
            Build(r.Value)
        End Sub

        Private Sub Build(p As UserService.UserProfileView)
            Dim u = p.User
            _content.SuspendLayout()
            _content.Controls.Clear()
            Text = $"User Profile - {u.DisplayId}"

            Dim avatar As New AvatarCircle With {.Location = New Point(30, 24), .Size = New Size(104, 104), .Image = AttachmentService.TryDecode(p.Photo)}
            _content.Controls.Add(avatar)
            Dim name = Heading(u.FullName, 14)
            name.Location = New Point(150, 30)
            name.MaximumSize = New Size(400, 0)
            _content.Controls.Add(name)
            Dim sub1 = Subtle($"@{u.Username}  ·  {u.DisplayId}", 9)
            sub1.Location = New Point(152, 62)
            _content.Controls.Add(sub1)
            Dim rolePill As New Pill(u.RoleName) With {.Location = New Point(152, 90), .PillBack = Color.FromArgb(226, 236, 252), .PillFore = Theme.Primary}
            rolePill.FitWidth()
            Dim statusPill As New Pill(If(u.IsActive, "Active", "Inactive")) With {
                .PillBack = If(u.IsActive, Color.FromArgb(222, 245, 233), Color.FromArgb(238, 240, 244)),
                .PillFore = If(u.IsActive, Color.FromArgb(22, 145, 80), Theme.Muted)}
            statusPill.FitWidth()
            statusPill.Location = New Point(rolePill.Right + 8, 90)
            _content.Controls.Add(rolePill)
            _content.Controls.Add(statusPill)
            If u.IsPrimaryAdmin Then
                Dim primary As New Pill("Primary Admin · protected") With {.PillBack = Color.FromArgb(255, 243, 219), .PillFore = Color.FromArgb(196, 120, 0)}
                primary.FitWidth()
                primary.Location = New Point(statusPill.Right + 8, 90)
                _content.Controls.Add(primary)
            End If

            Dim info = InfoTable(150, 520)
            AddInfoRow(info, "Email Address:", ValueLabel(u.Email))
            AddInfoRow(info, "Email Verified:", ValueLabel(If(u.EmailVerified, "Yes", "No - not yet verified")))
            AddInfoRow(info, "Contact Number:", ValueLabel(If(String.IsNullOrWhiteSpace(u.ContactNumber), "Not provided", u.ContactNumber)))
            AddInfoRow(info, "Barangay:", ValueLabel(If(String.IsNullOrEmpty(u.BarangayName), "Not set", u.BarangayName)))
            AddInfoRow(info, "Address:", ValueLabel(If(String.IsNullOrWhiteSpace(u.Address), "Not provided", u.Address)))
            AddInfoRow(info, "Email Notifications:", ValueLabel(If(u.NotifyEmail, "On", "Off")))
            AddInfoRow(info, "Member Since:", ValueLabel(TextFormat.DateTimeText(u.CreatedAt)))
            AddInfoRow(info, "Last Login:", ValueLabel(If(u.LastLoginAt.HasValue, TextFormat.DateTimeText(u.LastLoginAt.Value), "Never")))
            If u.LockoutUntil.HasValue AndAlso u.LockoutUntil.Value > DateTime.UtcNow Then
                AddInfoRow(info, "Locked Until:", ValueLabel(TextFormat.DateTimeText(u.LockoutUntil.Value)))
            End If
            info.Location = New Point(30, 146)
            _content.Controls.Add(info)

            Dim y = info.Bottom + 18
            Dim total = p.ReportCounts.Values.Sum()
            If u.Role = UserRole.Resident OrElse total > 0 Then
                Dim rt = Lbl("Reports Submitted", 10, FontStyle.Bold, Theme.Navy)
                rt.Location = New Point(30, y)
                _content.Controls.Add(rt)
                y += 30
                Dim boxes = {("Total", total, Theme.CardBlue), ("Submitted", p.ReportCounts(ConcernStatus.Submitted), Theme.CardAmber),
                             ("In Progress", p.ReportCounts(ConcernStatus.InProgress) + p.ReportCounts(ConcernStatus.UnderReview), Theme.CardGreen),
                             ("Resolved", p.ReportCounts(ConcernStatus.Resolved), Theme.CardPurple), ("Closed", p.ReportCounts(ConcernStatus.Closed), Theme.CardRed)}
                Dim x = 30
                For Each b In boxes
                    Dim card As New CardPanel With {.Location = New Point(x, y), .Size = New Size(100, 62), .Padding = New Padding(0)}
                    Dim v = Lbl(b.Item2.ToString(), 15, FontStyle.Bold, b.Item3)
                    v.Location = New Point(12, 6)
                    Dim l = Subtle(b.Item1, 8.25F)
                    l.Location = New Point(13, 38)
                    card.Controls.Add(v)
                    card.Controls.Add(l)
                    _content.Controls.Add(card)
                    x += 106
                Next
                y += 72
            End If
            _content.Controls.Add(New Panel With {.Location = New Point(0, y), .Size = New Size(10, 10), .BackColor = Color.White})
            _content.ResumeLayout()
        End Sub
    End Class

    ''' <summary>Add / Edit user. Passwords are never displayed; a new one can be set (hashed by the service).</summary>
    Public Class UserEditDialog
        Inherits DialogBase

        Private ReadOnly _existing As UserAccount
        Private ReadOnly _name As New InputField("Full name")
        Private ReadOnly _email As New InputField("Email address", Glyph.Mail)
        Private ReadOnly _username As New InputField("Username")
        Private ReadOnly _contact As New InputField("Contact number (optional)")
        Private ReadOnly _barangay As New SelectField(editable:=True)
        Private ReadOnly _role As New SelectField()
        Private ReadOnly _active As New CheckBox With {.Text = "Account is active (can log in)", .AutoSize = True, .Checked = True, .Font = Theme.F(9.25F), .BackColor = Color.White}
        Private ReadOnly _verified As New CheckBox With {.Text = "Email address verified", .AutoSize = True, .Checked = True, .Font = Theme.F(9.25F), .BackColor = Color.White}
        Private ReadOnly _pass As New InputField("", Glyph.Lock, password:=True)
        Private ReadOnly _confirm As New InputField("Confirm password", Glyph.Lock, password:=True)
        Private ReadOnly _msg As Label = MessageLabel()
        Private ReadOnly _fields As Dictionary(Of String, FieldBase)

        Public Sub New(existing As UserAccount, canManageAdmins As Boolean, isSelf As Boolean, barangays As IEnumerable(Of Barangay))
            MyBase.New(If(existing Is Nothing, "Add New User", $"Edit User - {existing.DisplayId}"), 600, 640)
            _existing = existing
            _fields = New Dictionary(Of String, FieldBase) From {{"FullName", _name}, {"Email", _email}, {"Username", _username}, {"ContactNumber", _contact}, {"Barangay", _barangay}, {"Password", _pass}, {"Role", _role}}
            Fill(_barangay, BarangayOptions(barangays, "Not set"), existing?.BarangayId)
            _name.MaxLength = 120
            _email.MaxLength = 254
            _username.MaxLength = 30
            _contact.MaxLength = 20
            _pass.MaxLength = 128
            _confirm.MaxLength = 128
            Dim roles As New List(Of ListOption)()
            If canManageAdmins OrElse existing?.Role = UserRole.Administrator Then roles.Add(New ListOption("Admin", UserRole.Administrator))
            roles.Add(New ListOption("Staff", UserRole.Staff))
            roles.Add(New ListOption("Resident", UserRole.Resident))
            Fill(_role, roles, If(existing?.Role, UserRole.Staff))
            _pass.Box.PlaceholderText = If(existing Is Nothing, "Initial password", "Leave blank to keep the current password")

            If existing IsNot Nothing Then
                _name.Text = existing.FullName
                _email.Text = existing.Email
                _username.Text = existing.Username
                _contact.Text = If(existing.ContactNumber, "")
                _active.Checked = existing.IsActive
                _verified.Checked = existing.EmailVerified
            End If
            If isSelf Then
                _role.Enabled = False
                _active.Enabled = False
            End If

            Dim s As New VStack(Body, 30, 22)
            s.Add(DialogHeader(If(existing Is Nothing, "Add New User", "Edit User"), If(existing Is Nothing, "Create an account for LGU personnel or a resident.", $"{existing.DisplayId} · created {TextFormat.ShortDate(existing.CreatedAt)}"), Glyph.People), 56, 12)
            s.AddRow(18, 16, 4, CType(FieldLabel("Full Name", True), Control), FieldLabel("Role", True))
            s.AddRow(38, 16, 10, _name, _role)
            s.AddRow(18, 16, 4, CType(FieldLabel("Username", True), Control), FieldLabel("Contact Number"))
            s.AddRow(38, 16, 10, _username, _contact)
            s.AddRow(18, 16, 4, CType(FieldLabel("Email Address", True), Control), FieldLabel("Barangay"))
            s.AddRow(38, 16, 10, _email, _barangay)
            s.AddAuto(_active, 4, 2)
            s.AddAuto(_verified, 10, 2)
            If isSelf Then s.AddAuto(Subtle("You cannot change your own role or deactivate your own account.", 8.25F), 8)
            s.AddRow(18, 16, 4, CType(FieldLabel(If(existing Is Nothing, "Password", "New Password"), existing Is Nothing), Control), FieldLabel("Confirm Password", existing Is Nothing))
            s.AddRow(38, 16, 3, _pass, _confirm)
            Dim hint = Subtle("At least 8 characters with upper- and lower-case letters, a number and a symbol. Setting a new password signs the user out.", 7.75F)
            hint.MaximumSize = New Size(s.Width, 0)
            s.AddAuto(hint, 6)
            _msg.Height = 36
            s.Add(_msg, 36, 4)
            Dim save As New ModernButton(If(existing Is Nothing, "Create User", "Save Changes"), ButtonKind.Primary, Glyph.CheckMark)
            Dim cancel As New ModernButton("Cancel", ButtonKind.Outline)
            s.AddRow(42, 12, 0, save, cancel)
            AcceptButton = save
            CancelButton = cancel
            AddHandler cancel.Click, Sub() Close()
            AddHandler save.Click, Async Sub() Await SaveAsync(save, cancel)
        End Sub

        Private Async Function SaveAsync(ParamArray disable As Control()) As Task
            _msg.Text = ""
            If _pass.Text <> _confirm.Text Then
                _msg.Text = "Passwords do not match."
                _confirm.HasError = True
                Return
            End If
            If _barangay.HasUnmatchedText Then
                _msg.Text = $"'{_barangay.Combo.Text.Trim()}' is not a barangay of Trinidad. Pick one from the list."
                _barangay.HasError = True
                Return
            End If
            Dim role = CType(SelectedValue(_role), UserRole)
            Dim req As New UserEditRequest With {.BarangayId = SelectedInt(_barangay),
                .Id = If(_existing?.Id, 0), .FullName = _name.Text, .Email = _email.Text, .Username = _username.Text, .ContactNumber = _contact.Text,
                .Role = role, .IsActive = _active.Checked, .EmailVerified = _verified.Checked,
                .NewPassword = If(String.IsNullOrEmpty(_pass.Text), Nothing, _pass.Text)}
            Dim changes As New List(Of String)()
            If _existing IsNot Nothing Then
                If _existing.Role <> role Then changes.Add($"change the role from {DisplayName(_existing.Role)} to {DisplayName(role)}")
                If _existing.IsActive AndAlso Not req.IsActive Then changes.Add("deactivate the account")
                If req.NewPassword IsNot Nothing Then changes.Add("set a new password")
            ElseIf role = UserRole.Administrator Then
                changes.Add("create an ADMINISTRATOR account with full access")
            End If
            If changes.Count > 0 AndAlso Not Confirm(Me, $"You are about to {String.Join(", ", changes)}. Continue?", "Confirm changes") Then Return

            Dim r = Await TryRunAsync(Of Boolean)(Me, Async Function()
                                                           If _existing Is Nothing Then
                                                               Await AppServices.Instance.Users.CreateAsync(AppServices.Instance.Session, req)
                                                           Else
                                                               Await AppServices.Instance.Users.UpdateAsync(AppServices.Instance.Session, req)
                                                           End If
                                                           Return True
                                                       End Function, disable, showErrors:=False)
            If Not r.Ok Then
                If TypeOf r.Error Is SessionExpiredException Then
                    ShowError(Me, r.Error)
                    Close()
                    Return
                End If
                _msg.Text = FriendlyMessage(r.Error)
                MarkField(_fields, r.Error)
                Return
            End If
            _pass.Text = ""
            _confirm.Text = ""
            Info(Me, If(_existing Is Nothing, $"The account '{req.Username.Trim()}' was created.", "The changes were saved."))
            DialogResult = DialogResult.OK
            Close()
        End Function

    End Class

End Namespace
