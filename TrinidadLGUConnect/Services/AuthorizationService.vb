Imports MySql.Data.MySqlClient
Imports TrinidadLGUConnect.Data
Imports TrinidadLGUConnect.Helpers
Imports TrinidadLGUConnect.Models
Imports TrinidadLGUConnect.Repositories

Namespace Services

    ''' <summary>The verified identity of the caller, re-read from MySQL for the current operation.</summary>
    Public Class Actor
        Public Property User As UserAccount
        Public Property SessionId As Long
        Public Property Permissions As HashSet(Of String)

        Public ReadOnly Property UserId As Integer
            Get
                Return User.Id
            End Get
        End Property

        Public Function Has(permissionCode As String) As Boolean
            Return Permissions.Contains(permissionCode)
        End Function
    End Class

    ''' <summary>
    ''' Server-side style authorization for a two-tier desktop app. The client-side <see cref="UserSession"/>
    ''' is treated as an untrusted handle: every check looks the session token hash up in user_sessions,
    ''' confirms it is not revoked or expired, re-reads the user's active flag and role, and loads the
    ''' role's permissions from role_permissions. Editing in-memory values in the UI therefore cannot
    ''' elevate privileges.
    ''' </summary>
    Public Class AuthorizationService

        Private ReadOnly _settings As SettingsService

        Public Sub New(settings As SettingsService)
            _settings = settings
        End Sub

        Public Async Function ResolveAsync(conn As MySqlConnection, session As UserSession) As Task(Of Actor)
            If session Is Nothing OrElse String.IsNullOrEmpty(session.Token) Then Throw New SessionExpiredException()
            Dim rec = Await SessionRepository.FindAsync(conn, SecureRandom.Sha256Hex(session.Token)).ConfigureAwait(False)
            Dim now = DateTime.UtcNow
            If rec Is Nothing OrElse rec.RevokedAt.HasValue OrElse rec.ExpiresAt <= now Then
                Throw New SessionExpiredException()
            End If
            If Not rec.User.IsActive Then
                Throw New SessionExpiredException("Your account has been deactivated. Please contact the LGU administrator.")
            End If
            Dim perms = Await UserRepository.GetPermissionsAsync(conn, rec.User.Role).ConfigureAwait(False)
            ' Sliding expiration.
            Dim timeout = Await _settings.GetIntAsync(SettingKeys.SessionTimeoutMinutes, 30, conn).ConfigureAwait(False)
            Await SessionRepository.TouchAsync(conn, rec.SessionId, now.AddMinutes(Math.Max(5, timeout))).ConfigureAwait(False)
            Return New Actor With {.User = rec.User, .SessionId = rec.SessionId, .Permissions = perms}
        End Function

        ''' <summary>Resolves the caller and requires at least one of the given permissions.</summary>
        Public Async Function DemandAsync(conn As MySqlConnection, session As UserSession, ParamArray anyOf As String()) As Task(Of Actor)
            Dim actor = Await ResolveAsync(conn, session).ConfigureAwait(False)
            If anyOf IsNot Nothing AndAlso anyOf.Length > 0 AndAlso Not anyOf.Any(Function(p) actor.Has(p)) Then
                AppLog.Warn($"Authorization denied for user {actor.UserId}: requires {String.Join(" or ", anyOf)}")
                Throw New AuthorizationException()
            End If
            Return actor
        End Function

    End Class

End Namespace
