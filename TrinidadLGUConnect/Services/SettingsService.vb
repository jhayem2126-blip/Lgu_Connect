Imports System.Globalization
Imports MySql.Data.MySqlClient
Imports TrinidadLGUConnect.Data
Imports TrinidadLGUConnect.Helpers
Imports TrinidadLGUConnect.Models
Imports TrinidadLGUConnect.Repositories

Namespace Services

    Public NotInheritable Class SettingKeys
        Private Sub New()
        End Sub
        Public Const Municipality As String = "app.municipality"
        Public Const ItemsPerPage As String = "app.items_per_page"
        Public Const RequireEmailVerification As String = "security.require_email_verification"
        Public Const SessionTimeoutMinutes As String = "security.session_timeout_minutes"
        Public Const MaxFailedLogins As String = "security.max_failed_logins"
        Public Const LockoutMinutes As String = "security.lockout_minutes"
        Public Const CodeExpiryMinutes As String = "security.code_expiry_minutes"
        Public Const MaxAttachmentMb As String = "attachments.max_size_mb"
        Public Const MaxAttachmentCount As String = "attachments.max_count"
        Public Const EmailOnStatusChange As String = "notifications.email_on_status_change"
        Public Const EmailOnSubmission As String = "notifications.email_on_submission"
        Public Const SetupCompleted As String = "setup.completed"
        ''' <summary>Id of the account created by first-run setup (protected; not editable in Settings).</summary>
        Public Const PrimaryAdminUserId As String = "setup.primary_admin_user_id"
    End Class

    ''' <summary>Definition and validation rule of an editable system setting.</summary>
    Public Class SettingDefinition
        Public Property Key As String = ""
        Public Property Label As String = ""
        Public Property IsBoolean As Boolean
        Public Property Min As Integer
        Public Property Max As Integer
        Public Property MaxLength As Integer = 100
        Public Property IsText As Boolean
    End Class

    ''' <summary>System-wide settings persisted in app_settings (shared by every workstation).</summary>
    Public Class SettingsService

        Private ReadOnly _db As Db
        Private _cache As Dictionary(Of String, String)
        Private _cachedAt As DateTime
        Private ReadOnly _gate As New Object()
        Private Shared ReadOnly CacheLifetime As TimeSpan = TimeSpan.FromSeconds(30)

        Public Shared ReadOnly Definitions As SettingDefinition() = {
            New SettingDefinition With {.Key = SettingKeys.Municipality, .Label = "Municipality / location label", .IsText = True, .MaxLength = 80},
            New SettingDefinition With {.Key = SettingKeys.ItemsPerPage, .Label = "Rows per page", .Min = 5, .Max = 100},
            New SettingDefinition With {.Key = SettingKeys.RequireEmailVerification, .Label = "Require email verification for new residents", .IsBoolean = True},
            New SettingDefinition With {.Key = SettingKeys.SessionTimeoutMinutes, .Label = "Session timeout (minutes of inactivity)", .Min = 5, .Max = 480},
            New SettingDefinition With {.Key = SettingKeys.MaxFailedLogins, .Label = "Failed logins before lockout", .Min = 3, .Max = 20},
            New SettingDefinition With {.Key = SettingKeys.LockoutMinutes, .Label = "Lockout duration (minutes)", .Min = 1, .Max = 1440},
            New SettingDefinition With {.Key = SettingKeys.CodeExpiryMinutes, .Label = "Verification / reset code lifetime (minutes)", .Min = 5, .Max = 60},
            New SettingDefinition With {.Key = SettingKeys.MaxAttachmentMb, .Label = "Maximum picture size (MB)", .Min = 1, .Max = 20},
            New SettingDefinition With {.Key = SettingKeys.MaxAttachmentCount, .Label = "Maximum pictures per report", .Min = 1, .Max = 10},
            New SettingDefinition With {.Key = SettingKeys.EmailOnStatusChange, .Label = "Email residents when a status changes", .IsBoolean = True},
            New SettingDefinition With {.Key = SettingKeys.EmailOnSubmission, .Label = "Email residents a receipt when they submit", .IsBoolean = True}
        }

        Public Sub New(db As Db)
            _db = db
        End Sub

        Public Sub Invalidate()
            SyncLock _gate
                _cache = Nothing
            End SyncLock
        End Sub

        Public Async Function GetAllAsync(Optional conn As MySqlConnection = Nothing) As Task(Of Dictionary(Of String, String))
            SyncLock _gate
                If _cache IsNot Nothing AndAlso DateTime.UtcNow - _cachedAt < CacheLifetime Then
                    Return New Dictionary(Of String, String)(_cache, StringComparer.OrdinalIgnoreCase)
                End If
            End SyncLock
            Dim d As Dictionary(Of String, String)
            If conn IsNot Nothing Then
                d = Await SettingsRepository.GetAllAsync(conn).ConfigureAwait(False)
            Else
                d = Await _db.RunAsync(Function(c) SettingsRepository.GetAllAsync(c)).ConfigureAwait(False)
            End If
            SyncLock _gate
                _cache = d
                _cachedAt = DateTime.UtcNow
            End SyncLock
            Return New Dictionary(Of String, String)(d, StringComparer.OrdinalIgnoreCase)
        End Function

        Public Async Function GetStringAsync(key As String, fallback As String, Optional conn As MySqlConnection = Nothing) As Task(Of String)
            Dim d = Await GetAllAsync(conn).ConfigureAwait(False)
            Dim v As String = Nothing
            Return If(d.TryGetValue(key, v) AndAlso Not String.IsNullOrWhiteSpace(v), v, fallback)
        End Function

        Public Async Function GetIntAsync(key As String, fallback As Integer, Optional conn As MySqlConnection = Nothing) As Task(Of Integer)
            Dim s = Await GetStringAsync(key, Nothing, conn).ConfigureAwait(False)
            Dim n As Integer
            Return If(Integer.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, n), n, fallback)
        End Function

        Public Async Function GetBoolAsync(key As String, fallback As Boolean, Optional conn As MySqlConnection = Nothing) As Task(Of Boolean)
            Dim s = Await GetStringAsync(key, Nothing, conn).ConfigureAwait(False)
            If s Is Nothing Then Return fallback
            Return s.Equals("true", StringComparison.OrdinalIgnoreCase) OrElse s = "1"
        End Function

        ''' <summary>Validates and saves system settings. Requires settings.manage.</summary>
        Public Async Function SaveAsync(session As UserSession, values As Dictionary(Of String, String)) As Task
            Dim clean As New Dictionary(Of String, String)()
            For Each kv In values
                Dim def = Definitions.FirstOrDefault(Function(d) d.Key = kv.Key)
                If def Is Nothing Then Throw New ValidationException($"Unknown setting '{kv.Key}'.")
                Dim v = If(kv.Value, "").Trim()
                If def.IsBoolean Then
                    Require(v = "true" OrElse v = "false", $"{def.Label}: choose yes or no.")
                ElseIf def.IsText Then
                    Require(v.Length > 0 AndAlso v.Length <= def.MaxLength, $"{def.Label}: enter 1 to {def.MaxLength} characters.")
                    v = CleanText(v, def.MaxLength)
                Else
                    Dim n As Integer
                    Require(Integer.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, n) AndAlso n >= def.Min AndAlso n <= def.Max,
                            $"{def.Label}: enter a whole number from {def.Min} to {def.Max}.")
                    v = n.ToString(CultureInfo.InvariantCulture)
                End If
                clean(def.Key) = v
            Next
            Await AppServices.Instance.Db.InTransactionAsync(
                Async Function(conn, tx)
                    Dim actor = Await AppServices.Instance.Authorization.DemandAsync(conn, session, Permission.SettingsManage).ConfigureAwait(False)
                    Dim before = Await SettingsRepository.GetAllAsync(conn).ConfigureAwait(False)
                    Dim changed As New List(Of String)()
                    For Each kv In clean
                        Dim old As String = Nothing
                        before.TryGetValue(kv.Key, old)
                        If old <> kv.Value Then
                            Await SettingsRepository.UpsertAsync(conn, tx, kv.Key, kv.Value, actor.UserId).ConfigureAwait(False)
                            changed.Add($"{kv.Key}: {old} -> {kv.Value}")
                        End If
                    Next
                    If changed.Count > 0 Then
                        Await AuditRepository.InsertAsync(conn, tx, actor.UserId, "settings.updated", "settings", Nothing, String.Join("; ", changed)).ConfigureAwait(False)
                    End If
                    Return True
                End Function).ConfigureAwait(False)
            Invalidate()
        End Function

    End Class

End Namespace
