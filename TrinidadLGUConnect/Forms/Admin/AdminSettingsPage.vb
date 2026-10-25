Imports System.Globalization
Imports TrinidadLGUConnect.Configuration
Imports TrinidadLGUConnect.Controls
Imports TrinidadLGUConnect.Data
Imports TrinidadLGUConnect.Helpers
Imports TrinidadLGUConnect.Models
Imports TrinidadLGUConnect.Services

Namespace Forms

    ''' <summary>
    ''' Settings for LGU personnel. Administrators (settings.manage) edit the shared system settings stored in
    ''' MySQL, configure SMTP and see database status; anyone with audit.view sees the audit log; every user can
    ''' manage their own password. Sections a user cannot use are not shown.
    ''' </summary>
    Public Class AdminSettingsPage
        Inherits PageBase

        Private ReadOnly _flow As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .AutoScroll = True, .WrapContents = True, .BackColor = Color.Transparent, .Padding = New Padding(0, 0, 0, 20)}
        Private ReadOnly _inputs As New Dictionary(Of String, Control)()
        Private ReadOnly _saveSettings As New ModernButton("Save Settings", ButtonKind.Primary, Glyph.CheckMark) With {.Size = New Size(170, 40)}
        Private ReadOnly _settingsMsg As Label = MessageLabel()
        Private ReadOnly _smtpStatus As Label = Lbl("", 9.25F)
        Private ReadOnly _dbStatus As Label = Lbl("", 9)
        Private ReadOnly _audit As New ThemedGrid With {.EmptyText = "No audit entries."}
        Private ReadOnly _canManage As Boolean
        Private ReadOnly _canAudit As Boolean
        Private ReadOnly _cards As New List(Of CardPanel)()

        Public Sub New()
            _canManage = AppServices.Instance.Session.Has(Permission.SettingsManage)
            _canAudit = AppServices.Instance.Session.Has(Permission.AuditView)
            Controls.Add(_flow)
            AddTopDown(Me, BuildHeader("Settings", "System configuration, email, security and your account.", Glyph.Settings), Gap(10))
            If _canManage Then
                BuildSystemCard()
                BuildSmtpCard()
                BuildDatabaseCard()
            End If
            BuildAccountCard()
            If _canAudit Then BuildAuditCard()
            AddHandler _flow.Resize, Sub() LayoutCards()
        End Sub

        Public Overrides ReadOnly Property Title As String
            Get
                Return "Settings"
            End Get
        End Property

        Public Overrides ReadOnly Property NavKey As String
            Get
                Return "settings"
            End Get
        End Property

        Public Overrides ReadOnly Property RefreshOnDataChange As Boolean
            Get
                Return False
            End Get
        End Property

        Private Function NewCard(title As String, height As Integer, Optional fullWidth As Boolean = False) As CardPanel
            Dim c = TitledCard(title, 10.5F)
            c.Dock = DockStyle.None
            c.Height = height
            c.Margin = New Padding(0, 0, 16, 16)
            c.Tag = fullWidth
            _flow.Controls.Add(c)
            _cards.Add(c)
            Return c
        End Function

        Private Sub LayoutCards()
            Dim avail = _flow.ClientSize.Width - 24
            Dim twoCol = avail >= 900
            For Each c In _cards
                Dim full = CBool(c.Tag) OrElse Not twoCol
                c.Width = If(full, avail, (avail - 16) \ 2)
            Next
        End Sub

        ' ------------------------------------------------------------------ system settings

        Private Sub BuildSystemCard()
            Const rowH As Integer = 46
            Dim rowCount = CInt(Math.Ceiling(SettingsService.Definitions.Length / 2.0))
            Dim card = NewCard("System, Security & Notifications", 52 + rowCount * rowH + 84, fullWidth:=True)
            Dim grid As New TableLayoutPanel With {.Location = New Point(18, 52), .ColumnCount = 4, .RowCount = rowCount, .BackColor = Color.Transparent,
                                                   .Height = rowCount * rowH, .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right}
            For i = 1 To rowCount
                grid.RowStyles.Add(New RowStyle(SizeType.Absolute, rowH))
            Next
            grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 32))
            grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 18))
            grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 32))
            grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 18))
            Dim col = 0
            Dim row = 0
            For Each def In SettingsService.Definitions
                Dim input As Control
                If def.IsBoolean Then
                    input = New CheckBox With {.Text = "Enabled", .AutoSize = True, .Font = Theme.F(9.25F), .BackColor = Color.White, .Margin = New Padding(0, 10, 0, 0)}
                Else
                    Dim f As New InputField(If(def.IsText, "", $"{def.Min} - {def.Max}")) With {.Height = 36, .Width = 150, .Margin = New Padding(0, 4, 12, 4)}
                    f.MaxLength = If(def.IsText, def.MaxLength, 5)
                    If def.IsText Then f.Dock = DockStyle.Fill
                    input = f
                End If
                _inputs(def.Key) = input
                Dim l = Lbl(def.Label, 9, FontStyle.Regular, Theme.Ink)
                l.MaximumSize = New Size(280, 0)
                l.Margin = New Padding(0, 12, 8, 0)
                grid.Controls.Add(l, col * 2, row)
                grid.Controls.Add(input, col * 2 + 1, row)
                col += 1
                If col = 2 Then
                    col = 0
                    row += 1
                End If
            Next
            card.Controls.Add(grid)
            AddHandler card.Resize, Sub() grid.Width = card.ClientSize.Width - 36
            _settingsMsg.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
            _saveSettings.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left
            card.Controls.Add(_settingsMsg)
            card.Controls.Add(_saveSettings)
            AddHandler card.Resize, Sub()
                                        _saveSettings.Location = New Point(18, card.ClientSize.Height - 58)
                                        _settingsMsg.SetBounds(200, card.ClientSize.Height - 50, card.ClientSize.Width - 220, 24)
                                    End Sub
            AddHandler _saveSettings.Click, AddressOf OnSaveSettings
        End Sub

        Private Async Sub OnSaveSettings(sender As Object, e As EventArgs)
            _settingsMsg.Text = ""
            Dim values As New Dictionary(Of String, String)()
            For Each kv In _inputs
                Dim cb = TryCast(kv.Value, CheckBox)
                values(kv.Key) = If(cb IsNot Nothing, If(cb.Checked, "true", "false"), DirectCast(kv.Value, InputField).Text)
            Next
            If Await RunAsync(Function() Svc.Settings.SaveAsync(Session, values), _saveSettings) Then
                _settingsMsg.ForeColor = Theme.Success
                _settingsMsg.Text = $"Settings saved at {DateTime.Now.ToString("hh:mm tt", CultureInfo.GetCultureInfo("en-US"))}. They apply to every workstation."
            Else
                _settingsMsg.ForeColor = Theme.Danger
                _settingsMsg.Text = "The settings were not saved. Correct the highlighted value and try again."
            End If
        End Sub

        ' ------------------------------------------------------------------ SMTP

        Private Sub BuildSmtpCard()
            Dim card = NewCard("Email (SMTP)", 250)
            _smtpStatus.Location = New Point(18, 52)
            _smtpStatus.MaximumSize = New Size(460, 0)
            card.Controls.Add(_smtpStatus)
            Dim configure As New ModernButton("Configure SMTP", ButtonKind.Outline, Glyph.Settings) With {.Size = New Size(170, 38), .Anchor = AnchorStyles.Bottom Or AnchorStyles.Left}
            Dim test As New ModernButton("Send Test Email", ButtonKind.Secondary, Glyph.Mail) With {.Size = New Size(170, 38), .Anchor = AnchorStyles.Bottom Or AnchorStyles.Left}
            card.Controls.Add(configure)
            card.Controls.Add(test)
            AddHandler card.Resize, Sub()
                                        configure.Location = New Point(18, card.ClientSize.Height - 56)
                                        test.Location = New Point(200, card.ClientSize.Height - 56)
                                        _smtpStatus.MaximumSize = New Size(card.ClientSize.Width - 36, 0)
                                    End Sub
            AddHandler configure.Click, Sub()
                                            Using dlg As New SmtpSettingsDialog()
                                                If dlg.ShowDialog(FindForm()) = DialogResult.OK Then ShowSmtpStatus()
                                            End Using
                                        End Sub
            AddHandler test.Click, Async Sub()
                                       Dim target = Session.Email
                                       If Await RunAsync(Function() Svc.Email.SendTestAsync(target), test, configure) Then
                                           Info(FindForm(), $"The SMTP server accepted the test email to {target}. Check that inbox (and its spam folder) to confirm delivery.")
                                       End If
                                   End Sub
        End Sub

        Private Sub ShowSmtpStatus()
            Dim o = AppConfig.Current.Smtp
            If o.IsConfigured Then
                _smtpStatus.ForeColor = Theme.Ink
                _smtpStatus.Text = $"Status: Configured" & vbLf & $"Server: {o.Host}:{o.Port} ({o.Security})" & vbLf &
                                   $"Sender: {o.FromName} <{o.FromAddress}>" & vbLf &
                                   $"Login: {If(String.IsNullOrEmpty(o.UserName), "(none)", o.UserName)}  ·  Password: {If(String.IsNullOrEmpty(o.Password), "not set", "stored (hidden)")}"
            Else
                _smtpStatus.ForeColor = Theme.Danger
                _smtpStatus.Text = "Status: Not configured." & vbLf & "Password recovery, email verification and status notifications cannot send email until SMTP is configured on this workstation."
            End If
        End Sub

        ' ------------------------------------------------------------------ database

        Private Sub BuildDatabaseCard()
            Dim card = NewCard("Database", 250)
            _dbStatus.Location = New Point(18, 52)
            card.Controls.Add(_dbStatus)
            Dim test As New ModernButton("Test Connection", ButtonKind.Outline, Glyph.Refresh) With {.Size = New Size(170, 38), .Anchor = AnchorStyles.Bottom Or AnchorStyles.Left}
            card.Controls.Add(test)
            AddHandler card.Resize, Sub()
                                        test.Location = New Point(18, card.ClientSize.Height - 56)
                                        _dbStatus.MaximumSize = New Size(card.ClientSize.Width - 36, 0)
                                    End Sub
            AddHandler test.Click, Async Sub() Await CheckDatabaseAsync(test)
        End Sub

        Private Async Function CheckDatabaseAsync(Optional btn As Control = Nothing) As Task
            Dim opts = AppConfig.Current.Database
            _dbStatus.ForeColor = Theme.Muted
            _dbStatus.Text = "Checking..."
            Dim r = Await RunAsync(Function() DatabaseInitializer.CheckAsync(opts), btn)
            If Not r.Ok OrElse IsDisposed Then Return
            Dim b As New MySql.Data.MySqlClient.MySqlConnectionStringBuilder(opts.BuildConnectionString())
            _dbStatus.ForeColor = If(r.Value.State = SchemaState.Ready, Theme.Success, Theme.Danger)
            _dbStatus.Text = $"Status: {If(r.Value.State = SchemaState.Ready, "Connected", r.Value.State.ToString())} - {r.Value.Message}" & vbLf &
                             $"Server: {b.Server}:{b.Port}  ·  MySQL {r.Value.ServerVersion}" & vbLf &
                             $"Database: {b.Database}  ·  Schema version {r.Value.SchemaVersion}" & vbLf &
                             $"User: {b.UserID}  ·  SSL: {b.SslMode}" & vbLf &
                             $"Checked at {DateTime.Now.ToString("hh:mm:ss tt", CultureInfo.GetCultureInfo("en-US"))}"
        End Function

        ' ------------------------------------------------------------------ account

        Private Sub BuildAccountCard()
            Dim card = NewCard("My Account", 250)
            Dim info = Lbl($"{Session.FullName}" & vbLf & $"Username: {Session.Username}" & vbLf & $"Email: {Session.Email}" & vbLf & $"Role: {Session.RoleTitle}", 9.25F)
            info.Location = New Point(18, 52)
            card.Controls.Add(info)
            Dim change As New ModernButton("Change Password", ButtonKind.Outline, Glyph.Lock) With {.Size = New Size(180, 38), .Anchor = AnchorStyles.Bottom Or AnchorStyles.Left}
            card.Controls.Add(change)
            Dim edit As New ModernButton("Edit My Profile", ButtonKind.Primary, Glyph.Contact) With {.Size = New Size(170, 38), .Anchor = AnchorStyles.Bottom Or AnchorStyles.Left}
            card.Controls.Add(edit)
            AddHandler edit.Click, Sub() Shell.NavigateRoot("profile")
            AddHandler card.Resize, Sub()
                                        edit.Location = New Point(18, card.ClientSize.Height - 56)
                                        change.Location = New Point(200, card.ClientSize.Height - 56)
                                    End Sub
            AddHandler change.Click, Sub()
                                         Using dlg As New ChangePasswordDialog()
                                             dlg.ShowDialog(FindForm())
                                         End Using
                                     End Sub
        End Sub

        ' ------------------------------------------------------------------ audit

        Private Sub BuildAuditCard()
            Dim card = NewCard("Audit Log (latest 200 events)", 420, fullWidth:=True)
            _audit.AddText("time", "Date / Time", 110)
            _audit.AddText("user", "User", 80)
            _audit.AddText("action", "Action", 120)
            _audit.AddText("entity", "Record", 80)
            _audit.AddText("details", "Details", 260)
            _audit.AddText("machine", "Workstation", 90)
            _audit.RowTemplate.Height = 32
            _audit.ColumnHeadersHeight = 34
            _audit.Location = New Point(18, 52)
            _audit.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Bottom
            card.Controls.Add(_audit)
            Dim refresh As New ModernButton("", ButtonKind.Secondary, Glyph.Refresh) With {.Size = New Size(36, 32), .Anchor = AnchorStyles.Top Or AnchorStyles.Right, .AccessibleName = "Refresh audit log"}
            card.Controls.Add(refresh)
            AddHandler card.Resize, Sub()
                                        _audit.Size = New Size(card.ClientSize.Width - 36, card.ClientSize.Height - 70)
                                        refresh.Location = New Point(card.ClientSize.Width - 18 - refresh.Width, 12)
                                    End Sub
            AddHandler refresh.Click, Async Sub() Await LoadAuditAsync()
        End Sub

        Private Async Function LoadAuditAsync() As Task
            Dim r = Await RunAsync(Function() Svc.Users.GetAuditLogAsync(Session, 200))
            If Not r.Ok OrElse IsDisposed Then Return
            _audit.Rows.Clear()
            For Each a In r.Value
                Dim i = _audit.Rows.Add(TextFormat.IsoDateTime(a.CreatedAt), If(a.UserName, "-"), a.Action, a.EntityId, a.Details, a.MachineName)
                _audit.Rows(i).Cells("details").ToolTipText = a.Details
            Next
        End Function

        ' ------------------------------------------------------------------ load

        Public Overrides Async Function RefreshDataAsync() As Task
            LayoutCards()
            If _canManage Then
                Dim r = Await RunAsync(Function() Svc.Settings.GetAllAsync())
                If r.Ok AndAlso Not IsDisposed Then
                    For Each kv In _inputs
                        Dim v As String = Nothing
                        r.Value.TryGetValue(kv.Key, v)
                        Dim cb = TryCast(kv.Value, CheckBox)
                        If cb IsNot Nothing Then
                            cb.Checked = String.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
                        Else
                            DirectCast(kv.Value, InputField).Text = If(v, "")
                        End If
                    Next
                End If
                ShowSmtpStatus()
                Await CheckDatabaseAsync()
            End If
            If _canAudit Then Await LoadAuditAsync()
        End Function

    End Class

    ''' <summary>
    ''' SMTP configuration for this workstation. The password is write-only: it is never displayed, and it is
    ''' stored encrypted with Windows DPAPI in %ProgramData%\TrinidadLGUConnect\appsettings.local.json.
    ''' </summary>
    Public Class SmtpSettingsDialog
        Inherits DialogBase

        Private ReadOnly _host As New InputField("smtp.example.com")
        Private ReadOnly _port As New InputField("587")
        Private ReadOnly _security As New SelectField()
        Private ReadOnly _user As New InputField("SMTP user name (often the email address)")
        Private ReadOnly _pass As New InputField("Leave blank to keep the stored password", password:=True)
        Private ReadOnly _from As New InputField("noreply@trinidad.gov.ph", Glyph.Mail)
        Private ReadOnly _fromName As New InputField("Trinidad LGU Connect")
        Private ReadOnly _msg As Label = MessageLabel()

        Public Sub New()
            MyBase.New("Configure SMTP", 600, 640)
            Dim o = AppConfig.Current.Smtp
            _host.Text = o.Host
            _port.Text = o.Port.ToString(CultureInfo.InvariantCulture)
            For Each s In {"StartTls", "SslOnConnect", "Auto", "None"} : _security.Items.Add(s) : Next
            _security.SelectedItem = If({"StartTls", "SslOnConnect", "Auto", "None"}.Contains(o.Security), o.Security, "StartTls")
            _user.Text = o.UserName
            _from.Text = o.FromAddress
            _fromName.Text = o.FromName

            Dim st As New VStack(Body, 30, 22)
            st.Add(DialogHeader("Email (SMTP) Settings", "Used for verification codes, password resets and notifications.", Glyph.Mail), 56, 12)
            st.AddAuto(FieldLabel("SMTP Server", True), 4)
            st.Add(_host, 38, 10)
            st.AddRow(18, 16, 4, CType(FieldLabel("Port", True), Control), FieldLabel("Security"))
            st.AddRow(38, 16, 10, _port, _security)
            st.AddAuto(FieldLabel("User Name"), 4)
            st.Add(_user, 38, 10)
            st.AddAuto(FieldLabel("Password"), 4)
            st.Add(_pass, 38, 10)
            st.AddRow(18, 16, 4, CType(FieldLabel("Sender Email", True), Control), FieldLabel("Sender Name"))
            st.AddRow(38, 16, 6, _from, _fromName)
            Dim note = Subtle("Settings are saved for this computer only. The password is encrypted with Windows DPAPI and is never shown again. Environment variables (TLC_SMTP_*) override these values.", 8)
            note.MaximumSize = New Size(st.Width, 0)
            st.AddAuto(note, 6)
            _msg.Height = 34
            st.Add(_msg, 34, 4)
            Dim save As New ModernButton("Save", ButtonKind.Primary, Glyph.CheckMark)
            Dim test As New ModernButton("Save & Send Test", ButtonKind.Outline, Glyph.Mail)
            Dim cancel As New ModernButton("Cancel", ButtonKind.Secondary)
            st.AddRow(42, 10, 0, save, test, cancel)
            AcceptButton = save
            CancelButton = cancel
            AddHandler cancel.Click, Sub() Close()
            AddHandler save.Click, Sub()
                                       If SaveSettings() Then
                                           DialogResult = DialogResult.OK
                                           Close()
                                       End If
                                   End Sub
            AddHandler test.Click, Async Sub()
                                       If Not SaveSettings() Then Return
                                       Dim target = AppServices.Instance.Session.Email
                                       Dim r = Await TryRunAsync(Of Boolean)(Me, Async Function()
                                                                                      Await AppServices.Instance.Email.SendTestAsync(target)
                                                                                      Return True
                                                                                  End Function, {save, test, cancel}, showErrors:=False)
                                       If r.Ok Then
                                           _msg.ForeColor = Theme.Success
                                           _msg.Text = $"Saved. The server accepted a test email to {target}."
                                           DialogResult = DialogResult.OK
                                       Else
                                           _msg.ForeColor = Theme.Danger
                                           _msg.Text = "Saved, but the test failed: " & FriendlyMessage(r.Error)
                                       End If
                                   End Sub
        End Sub

        Private Function SaveSettings() As Boolean
            _msg.ForeColor = Theme.Danger
            _msg.Text = ""
            Dim port As Integer
            If String.IsNullOrWhiteSpace(_host.Text) Then
                _msg.Text = "Enter the SMTP server."
                _host.HasError = True
                Return False
            End If
            If Not Integer.TryParse(_port.Text.Trim(), port) OrElse port < 1 OrElse port > 65535 Then
                _msg.Text = "Enter a valid port (for example 587 for STARTTLS or 465 for SSL)."
                _port.HasError = True
                Return False
            End If
            If Not IsValidEmail(_from.Text) Then
                _msg.Text = "Enter a valid sender email address."
                _from.HasError = True
                Return False
            End If
            Dim o As New SmtpOptions With {
                .Host = _host.Text.Trim(), .Port = port, .Security = CStr(_security.SelectedItem), .UserName = _user.Text.Trim(),
                .FromAddress = _from.Text.Trim(), .FromName = If(String.IsNullOrWhiteSpace(_fromName.Text), "Trinidad LGU Connect", _fromName.Text.Trim()), .TimeoutSeconds = 30}
            Try
                AppConfig.SaveSmtp(o, If(_pass.Text.Length > 0, _pass.Text, Nothing))
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is Security.Cryptography.CryptographicException
                _msg.Text = "The settings could not be saved: " & ex.Message
                Return False
            End Try
            _pass.Text = ""
            Try
                Dim session = AppServices.Instance.Session
                Dim unused = Task.Run(Function() AppServices.Instance.Db.RunAsync(
                    Async Function(conn)
                        Dim actor = Await AppServices.Instance.Authorization.DemandAsync(conn, session, Permission.SettingsManage)
                        Await Repositories.AuditRepository.InsertAsync(conn, Nothing, actor.UserId, "settings.smtp_updated", "settings", Environment.MachineName, $"{o.Host}:{o.Port} from {o.FromAddress}")
                        Return True
                    End Function))
            Catch ex As Exception
                AppLog.Warn("SMTP audit entry failed.", ex)
            End Try
            Return True
        End Function

    End Class

End Namespace
