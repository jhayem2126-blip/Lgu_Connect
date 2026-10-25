Imports TrinidadLGUConnect.Models
Imports TrinidadLGUConnect.Services

Namespace Forms

    ''' <summary>
    ''' Owns the application's top-level windows. Exactly one top-level window (landing, a login screen,
    ''' registration or the main shell) is open at a time; switching replaces it, so there are no stray
    ''' duplicate windows. Closing the current window exits the application.
    ''' </summary>
    Public Class AppController
        Inherits ApplicationContext

        Public Shared Property Current As AppController
        Private _active As Form
        Private _lastPortal As LoginPortal = LoginPortal.Resident
        Private _loggingOut As Boolean

        Public Sub New()
            Current = Me
            ShowForm(New LandingForm())
        End Sub

        Public ReadOnly Property ActiveForm As Form
            Get
                Return _active
            End Get
        End Property

        Public Sub ShowForm(f As Form)
            Dim old = _active
            _active = f
            MainForm = f
            If old IsNot Nothing AndAlso Not old.IsDisposed Then
                f.StartPosition = FormStartPosition.Manual
                Dim bounds = Screen.FromControl(old).WorkingArea
                f.Location = New Point(bounds.X + Math.Max(0, (bounds.Width - f.Width) \ 2), bounds.Y + Math.Max(0, (bounds.Height - f.Height) \ 2))
            End If
            f.Show()
            f.Activate()
            If old IsNot Nothing AndAlso Not old.IsDisposed Then old.Close()
        End Sub

        Public Sub ShowLanding()
            ShowForm(New LandingForm(skipBootstrap:=True))
        End Sub

        Public Sub ShowResidentLogin(Optional message As String = Nothing)
            _lastPortal = LoginPortal.Resident
            ShowForm(New ResidentLoginForm(message))
        End Sub

        Public Sub ShowAdminLogin(Optional message As String = Nothing)
            _lastPortal = LoginPortal.Personnel
            ShowForm(New AdminLoginForm(message))
        End Sub

        Public Sub ShowRegistration()
            ShowForm(New RegistrationForm())
        End Sub

        Public Sub OpenShell(session As UserSession)
            AppServices.Instance.Session = session
            _lastPortal = If(session.Role = UserRole.Resident, LoginPortal.Resident, LoginPortal.Personnel)
            ShowForm(New MainShellForm(session))
        End Sub

        ''' <summary>Revokes the session in the database and returns to the login screen of the same portal.</summary>
        Public Async Function LogoutAsync(Optional message As String = Nothing) As Task
            If _loggingOut Then Return
            _loggingOut = True
            Try
                Dim s = AppServices.Instance.Session
                AppServices.Instance.Session = Nothing
                If s IsNot Nothing Then
                    Try
                        Await Task.Run(Function() AppServices.Instance.Auth.LogoutAsync(s))
                    Catch ex As Exception
                        ' The local session is discarded regardless; the server record expires on its own.
                        AppLog.Warn("Logout could not reach the database.", ex)
                    End Try
                End If
                If _lastPortal = LoginPortal.Resident Then ShowResidentLogin(message) Else ShowAdminLogin(message)
            Finally
                _loggingOut = False
            End Try
        End Function

        ''' <summary>Used when the server reports the session is no longer valid (expired, revoked, deactivated).</summary>
        Public Sub ForceLogout(message As String)
            If _active IsNot Nothing AndAlso _active.InvokeRequired Then
                _active.BeginInvoke(Sub() ForceLogout(message))
                Return
            End If
            Dim unused = LogoutAsync(message)
        End Sub

    End Class

End Namespace
