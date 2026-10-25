Imports TrinidadLGUConnect.Controls
Imports TrinidadLGUConnect.Models
Imports TrinidadLGUConnect.Services

Namespace Forms

    ''' <summary>
    ''' Main window after login: sidebar + content area. Pages are UserControls; opening a detail screen
    ''' pushes the current page on a back stack, and Close/Cancel pops it and reloads its data.
    ''' </summary>
    Public Class MainShellForm
        Inherits Form
        Implements IMessageFilter

        Private ReadOnly _session As UserSession
        Private ReadOnly _sidebar As New Sidebar()
        Private ReadOnly _content As New Panel With {.Dock = DockStyle.Fill, .BackColor = Theme.AppBack}
        Private ReadOnly _host As New Panel With {.Dock = DockStyle.Fill, .BackColor = Theme.AppBack}
        Private ReadOnly _busy As New BusyBar()
        Private ReadOnly _stack As New Stack(Of PageBase)()
        Private _current As PageBase
        Private ReadOnly _timer As New Timer With {.Interval = 30000}
        Private _lastInput As DateTime = DateTime.Now
        Private _timeoutMinutes As Integer = 30
        Private _ticks As Integer
        Private _closingHandled As Boolean
        Private _dataChangedPending As Boolean

        Public Sub New(session As UserSession)
            _session = session
            ApplyWindowDefaults(Me)
            Text = AppTitle
            StartPosition = FormStartPosition.CenterScreen
            FitToScreen(Me, New Size(1280, 800), New Size(1024, 660))
            ' Resident and admin windows open maximized; the size above is what "Restore Down" returns to.
            WindowState = FormWindowState.Maximized

            _content.Controls.Add(_host)
            _content.Controls.Add(_busy)
            Controls.Add(_content)
            Controls.Add(_sidebar)

            If session.Role = UserRole.Resident Then
                _sidebar.Title = session.FullName
                _sidebar.Subtitle = "Resident"
                _sidebar.AddItem("dashboard", "Dashboard", Glyph.Home)
                _sidebar.AddItem("report", "Report a Concern", Glyph.Edit)
                _sidebar.AddItem("myreports", "My Reports", Glyph.Document)
                _sidebar.AddItem("profile", "Profile", Glyph.Contact)
                _sidebar.AddItem("settings", "Settings", Glyph.Settings)
            Else
                _sidebar.Title = "Trinidad LGU Connect"
                _sidebar.Subtitle = If(session.Role = UserRole.Administrator, "Admin Panel", "Staff Panel")
                _sidebar.AvatarImage = Assets.Seal
                If session.Has(Permission.DashboardAdmin) Then _sidebar.AddItem("dashboard", "Dashboard", Glyph.Home)
                If session.Has(Permission.ConcernViewAll) Then _sidebar.AddItem("concerns", "Concerns", Glyph.Clipboard)
                If session.Has(Permission.ReportGenerate) Then _sidebar.AddItem("reports", "Reports", Glyph.ReportDoc)
                If session.Has(Permission.UserView) Then _sidebar.AddItem("users", "User Management", Glyph.People)
                _sidebar.AddItem("profile", "My Profile", Glyph.Contact)
                _sidebar.AddItem("settings", "Settings", Glyph.Settings)
            End If

            AddHandler _sidebar.ItemSelected, Sub(s, key) NavigateRoot(key)
            AddHandler _sidebar.LogoutClicked, AddressOf OnLogoutClicked
            AddHandler _timer.Tick, AddressOf OnTimerTick
            AddHandler ConcernService.DataChanged, AddressOf OnDataChanged
            Application.AddMessageFilter(Me)
        End Sub

        Public ReadOnly Property Session As UserSession
            Get
                Return _session
            End Get
        End Property

        Public ReadOnly Property IsResident As Boolean
            Get
                Return _session.Role = UserRole.Resident
            End Get
        End Property

        Protected Overrides Async Sub OnShown(e As EventArgs)
            MyBase.OnShown(e)
            NavigateRoot("dashboard")
            _timer.Start()
            Await RefreshIdentityAsync()
            Try
                _timeoutMinutes = Await Task.Run(Function() AppServices.Instance.Settings.GetIntAsync(SettingKeys.SessionTimeoutMinutes, 30))
            Catch ex As Exception
                AppLog.Warn("Could not read the session timeout setting.", ex)
            End Try
        End Sub

        Friend Sub SetBusy(busy As Boolean)
            _busy.Visible = busy
        End Sub

        ' ---------------------------------------------------------------- navigation

        Public Function CreateRootPage(key As String) As PageBase
            If IsResident Then
                Select Case key
                    Case "report" : Return New ReportConcernPage()
                    Case "myreports" : Return New MyReportsPage()
                    Case "profile" : Return New ProfilePage()
                    Case "settings" : Return New ResidentSettingsPage()
                    Case Else : Return New ResidentDashboardPage()
                End Select
            End If
            Select Case key
                Case "concerns" : Return New SubmittedConcernsPage()
                Case "reports" : Return New ReportsPage()
                Case "users" : Return New UserManagementPage()
                Case "settings" : Return New AdminSettingsPage()
                Case "profile" : Return New ProfilePage()
                Case Else
                    If _session.Has(Permission.DashboardAdmin) Then Return New AdminDashboardPage()
                    Return New AdminSettingsPage()
            End Select
        End Function

        ''' <summary>Opens a sidebar destination, discarding the back stack.</summary>
        Public Sub NavigateRoot(key As String)
            ' Always a fresh page, so a sidebar click also clears any filter applied from a dashboard card.
            While _stack.Count > 0
                _stack.Pop().Dispose()
            End While
            ShowPage(CreateRootPage(key), push:=False)
        End Sub

        ''' <summary>Opens a page on top of the current one (Back returns to the current page).</summary>
        Public Sub Navigate(page As PageBase)
            ShowPage(page, push:=True)
        End Sub

        ''' <summary>Opens a root page with a pre-applied filter etc. (e.g. a dashboard card opening a filtered list).</summary>
        Public Sub NavigateReplace(page As PageBase)
            While _stack.Count > 0
                _stack.Pop().Dispose()
            End While
            ShowPage(page, push:=False)
        End Sub

        Public Sub GoBack()
            If _stack.Count = 0 Then
                NavigateRoot("dashboard")
                Return
            End If
            Dim prev = _stack.Pop()
            Dim old = _current
            _current = prev
            prev.Visible = True
            prev.BringToFront()
            UpdateChrome()
            If old IsNot Nothing Then
                _host.Controls.Remove(old)
                old.Dispose()
            End If
            Dim unused = prev.RefreshDataAsync()
        End Sub

        Private Sub ShowPage(page As PageBase, push As Boolean)
            page.Shell = Me
            Dim old = _current
            _host.SuspendLayout()
            _host.Controls.Add(page)
            page.BringToFront()
            _host.ResumeLayout()
            _current = page
            If old IsNot Nothing Then
                If push Then
                    old.Visible = False
                    _stack.Push(old)
                Else
                    _host.Controls.Remove(old)
                    old.Dispose()
                End If
            End If
            UpdateChrome()
            Dim unused = page.RefreshDataAsync()
        End Sub

        Private Sub UpdateChrome()
            Text = If(String.IsNullOrEmpty(_current?.Title), AppTitle, $"{AppTitle} - {_current.Title}")
            _sidebar.SelectedKey = _current?.NavKey
        End Sub

        Public ReadOnly Property CurrentPage As PageBase
            Get
                Return _current
            End Get
        End Property

        Private _photo As Image

        ''' <summary>Reloads the signed-in user's name and profile photo (sidebar for residents; the LGU seal stays for personnel).</summary>
        Public Async Function RefreshIdentityAsync() As Task
            If IsResident Then _sidebar.Title = _session.FullName
            Dim r = Await TryRunAsync(Me, Function() AppServices.Instance.Users.GetMyPhotoAsync(_session), showErrors:=False)
            If IsDisposed Then Return
            If r.Ok AndAlso IsResident Then
                Dim old = _photo
                _photo = AttachmentService.TryDecode(r.Value)
                _sidebar.AvatarImage = _photo
                old?.Dispose()
            End If
            _sidebar.Invalidate()
        End Function

        ' ---------------------------------------------------------------- data change / session checks

        Private Sub OnDataChanged(sender As Object, e As EventArgs)
            If IsDisposed OrElse Not IsHandleCreated Then Return
            If InvokeRequired Then
                BeginInvoke(Sub() OnDataChanged(sender, e))
                Return
            End If
            ' Coalesce bursts into one refresh.
            If _dataChangedPending Then Return
            _dataChangedPending = True
            BeginInvoke(Sub()
                            _dataChangedPending = False
                            If _current IsNot Nothing AndAlso _current.RefreshOnDataChange Then
                                Dim unused = _current.RefreshDataAsync()
                            End If
                        End Sub)
        End Sub

        Public Function PreFilterMessage(ByRef m As Message) As Boolean Implements IMessageFilter.PreFilterMessage
            Select Case m.Msg
                Case &H100, &H201, &H204, &H20A, &H104
                    _lastInput = DateTime.Now
            End Select
            Return False
        End Function

        Private Async Sub OnTimerTick(sender As Object, e As EventArgs)
            If AppServices.Instance.Session Is Nothing Then Return
            If (DateTime.Now - _lastInput).TotalMinutes >= _timeoutMinutes Then
                _timer.Stop()
                AppController.Current.ForceLogout($"You were logged out after {_timeoutMinutes} minutes of inactivity.")
                Return
            End If
            _ticks += 1
            If _ticks Mod 4 = 0 Then
                Dim valid As Boolean
                Try
                    valid = Await Task.Run(Function() AppServices.Instance.Auth.ValidateSessionAsync(_session))
                Catch ex As Exception
                    ' Database temporarily unreachable: keep the session; the next operation will report it.
                    AppLog.Warn("Session check failed.", ex)
                    Return
                End Try
                If Not valid AndAlso AppServices.Instance.Session IsNot Nothing Then
                    _timer.Stop()
                    AppController.Current.ForceLogout("Your session has ended (it expired, your password was changed, or your account was changed by an administrator). Please log in again.")
                End If
            End If
        End Sub

        Private Async Sub OnLogoutClicked(sender As Object, e As EventArgs)
            If Not Confirm(Me, "Are you sure you want to log out?", "Logout") Then Return
            _closingHandled = True
            Await AppController.Current.LogoutAsync()
        End Sub

        Protected Overrides Async Sub OnFormClosing(e As FormClosingEventArgs)
            MyBase.OnFormClosing(e)
            ' Closing the window with X: revoke the server session before exiting.
            If Not _closingHandled AndAlso AppServices.Instance.Session IsNot Nothing AndAlso e.CloseReason = CloseReason.UserClosing Then
                e.Cancel = True
                _closingHandled = True
                Dim s = AppServices.Instance.Session
                AppServices.Instance.Session = Nothing
                Try
                    Await Task.Run(Function() AppServices.Instance.Auth.LogoutAsync(s))
                Catch ex As Exception
                    AppLog.Warn("Logout on close could not reach the database.", ex)
                End Try
                Close()
            End If
        End Sub

        Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
            _timer.Stop()
            Application.RemoveMessageFilter(Me)
            RemoveHandler ConcernService.DataChanged, AddressOf OnDataChanged
            MyBase.OnFormClosed(e)
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then
                _timer.Dispose()
                While _stack.Count > 0
                    _stack.Pop().Dispose()
                End While
            End If
            MyBase.Dispose(disposing)
        End Sub

    End Class

End Namespace
