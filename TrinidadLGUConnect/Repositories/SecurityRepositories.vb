Imports MySql.Data.MySqlClient
Imports TrinidadLGUConnect.Data
Imports TrinidadLGUConnect.Models

Namespace Repositories

    Public Class SessionRecord
        Public Property SessionId As Long
        Public Property ExpiresAt As DateTime
        Public Property RevokedAt As DateTime?
        Public Property User As UserAccount
    End Class

    Public NotInheritable Class SessionRepository
        Private Sub New()
        End Sub

        Public Shared Async Function InsertAsync(conn As MySqlConnection, userId As Integer, tokenHash As String, machine As String, expiresAt As DateTime) As Task(Of Long)
            Using cmd = Db.Cmd(conn, "INSERT INTO user_sessions (user_id, token_hash, machine_name, created_at, last_seen_at, expires_at) VALUES (@u, @h, @m, @now, @now, @exp)").
                    P("@u", userId).P("@h", tokenHash).P("@m", machine).P("@now", DateTime.UtcNow).P("@exp", expiresAt)
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
                Return cmd.LastInsertedId
            End Using
        End Function

        Public Shared Async Function FindAsync(conn As MySqlConnection, tokenHash As String) As Task(Of SessionRecord)
            Const sql As String =
                "SELECT s.id AS session_id, s.expires_at, s.revoked_at, u.id, u.full_name, u.email, u.username, u.password_hash, u.role_id, " &
                "u.contact_number, u.address, u.is_active, u.email_verified, u.notify_email, u.failed_login_count, u.lockout_until, u.last_login_at, u.created_at, " &
                "u.barangay_id, bgy.name AS barangay_name, (u.profile_photo IS NOT NULL) AS has_photo " &
                "FROM user_sessions s JOIN users u ON u.id = s.user_id LEFT JOIN barangays bgy ON bgy.id = u.barangay_id WHERE s.token_hash = @h"
            Using cmd = Db.Cmd(conn, sql).P("@h", tokenHash)
                Using r = Await cmd.ExecuteReaderAsync().ConfigureAwait(False)
                    If Not Await r.ReadAsync().ConfigureAwait(False) Then Return Nothing
                    Return New SessionRecord With {
                        .SessionId = r.Lng("session_id"),
                        .ExpiresAt = r.Utc("expires_at"),
                        .RevokedAt = r.UtcN("revoked_at"),
                        .User = UserRepository.Map(r)
                    }
                End Using
            End Using
        End Function

        Public Shared Async Function TouchAsync(conn As MySqlConnection, sessionId As Long, newExpiry As DateTime) As Task
            Using cmd = Db.Cmd(conn, "UPDATE user_sessions SET last_seen_at = @now, expires_at = @exp WHERE id = @id AND revoked_at IS NULL").
                    P("@now", DateTime.UtcNow).P("@exp", newExpiry).P("@id", sessionId)
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
            End Using
        End Function

        Public Shared Async Function RevokeAsync(conn As MySqlConnection, tokenHash As String) As Task
            Using cmd = Db.Cmd(conn, "UPDATE user_sessions SET revoked_at = @now WHERE token_hash = @h AND revoked_at IS NULL").P("@now", DateTime.UtcNow).P("@h", tokenHash)
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
            End Using
        End Function

        ''' <summary>Revokes every session of a user, optionally keeping one (the caller's own).</summary>
        Public Shared Async Function RevokeAllForUserAsync(conn As MySqlConnection, tx As MySqlTransaction, userId As Integer, Optional exceptSessionId As Long = 0) As Task
            Using cmd = Db.Cmd(conn, "UPDATE user_sessions SET revoked_at = @now WHERE user_id = @u AND revoked_at IS NULL AND id <> @keep", tx).
                    P("@now", DateTime.UtcNow).P("@u", userId).P("@keep", exceptSessionId)
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
            End Using
        End Function
    End Class

    Public Enum TokenPurpose
        PasswordReset
        EmailVerification
    End Enum

    Public Class TokenRecord
        Public Property Id As Long
        Public Property UserId As Integer
        Public Property TokenHash As String = ""
        Public Property ExpiresAt As DateTime
        Public Property FailedAttempts As Integer
    End Class

    ''' <summary>Single-use, expiring one-time codes. Only SHA-256 hashes are stored.</summary>
    Public NotInheritable Class TokenRepository
        Private Sub New()
        End Sub

        ' Table names come from this fixed map only, never from input.
        Private Shared Function Table(purpose As TokenPurpose) As String
            Return If(purpose = TokenPurpose.PasswordReset, "password_reset_tokens", "email_verification_tokens")
        End Function

        Public Shared Async Function InsertAsync(conn As MySqlConnection, tx As MySqlTransaction, purpose As TokenPurpose, userId As Integer, tokenHash As String, expiresAt As DateTime) As Task(Of Long)
            Using cmd = Db.Cmd(conn, $"INSERT INTO {Table(purpose)} (user_id, token_hash, expires_at, created_at) VALUES (@u, @h, @exp, @now)", tx).
                    P("@u", userId).P("@h", tokenHash).P("@exp", expiresAt).P("@now", DateTime.UtcNow)
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
                Return cmd.LastInsertedId
            End Using
        End Function

        Public Shared Async Function RevokeOutstandingAsync(conn As MySqlConnection, tx As MySqlTransaction, purpose As TokenPurpose, userId As Integer) As Task
            Using cmd = Db.Cmd(conn, $"UPDATE {Table(purpose)} SET revoked_at = @now WHERE user_id = @u AND used_at IS NULL AND revoked_at IS NULL", tx).
                    P("@now", DateTime.UtcNow).P("@u", userId)
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
            End Using
        End Function

        Public Shared Async Function RevokeAsync(conn As MySqlConnection, tx As MySqlTransaction, purpose As TokenPurpose, tokenId As Long) As Task
            Using cmd = Db.Cmd(conn, $"UPDATE {Table(purpose)} SET revoked_at = @now WHERE id = @id AND revoked_at IS NULL", tx).P("@now", DateTime.UtcNow).P("@id", tokenId)
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
            End Using
        End Function

        ''' <summary>The newest usable token for the user, locked for update.</summary>
        Public Shared Async Function FindActiveAsync(conn As MySqlConnection, tx As MySqlTransaction, purpose As TokenPurpose, userId As Integer) As Task(Of TokenRecord)
            Dim sql = $"SELECT id, user_id, token_hash, expires_at, failed_attempts FROM {Table(purpose)} " &
                      "WHERE user_id = @u AND used_at IS NULL AND revoked_at IS NULL AND expires_at > @now ORDER BY id DESC LIMIT 1 FOR UPDATE"
            Using cmd = Db.Cmd(conn, sql, tx).P("@u", userId).P("@now", DateTime.UtcNow)
                Using r = Await cmd.ExecuteReaderAsync().ConfigureAwait(False)
                    If Not Await r.ReadAsync().ConfigureAwait(False) Then Return Nothing
                    Return New TokenRecord With {
                        .Id = r.Lng("id"), .UserId = r.Int("user_id"), .TokenHash = r.Str("token_hash"),
                        .ExpiresAt = r.Utc("expires_at"), .FailedAttempts = r.Int("failed_attempts")}
                End Using
            End Using
        End Function

        Public Shared Async Function IncrementFailedAsync(conn As MySqlConnection, tx As MySqlTransaction, purpose As TokenPurpose, tokenId As Long, maxAttempts As Integer) As Task
            ' MySQL evaluates single-table SET assignments left to right, so failed_attempts is already incremented here.
            Dim sql = $"UPDATE {Table(purpose)} SET failed_attempts = failed_attempts + 1, " &
                      "revoked_at = CASE WHEN failed_attempts >= @max THEN @now ELSE revoked_at END WHERE id = @id"
            Using cmd = Db.Cmd(conn, sql, tx).P("@max", maxAttempts).P("@now", DateTime.UtcNow).P("@id", tokenId)
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
            End Using
        End Function

        Public Shared Async Function MarkUsedAsync(conn As MySqlConnection, tx As MySqlTransaction, purpose As TokenPurpose, tokenId As Long) As Task
            Using cmd = Db.Cmd(conn, $"UPDATE {Table(purpose)} SET used_at = @now WHERE id = @id AND used_at IS NULL", tx).P("@now", DateTime.UtcNow).P("@id", tokenId)
                If Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False) <> 1 Then
                    Throw New Helpers.ValidationException("This code has already been used.")
                End If
            End Using
        End Function

        Public Shared Async Function CountCreatedSinceAsync(conn As MySqlConnection, purpose As TokenPurpose, userId As Integer, since As DateTime) As Task(Of Integer)
            Using cmd = Db.Cmd(conn, $"SELECT COUNT(*) FROM {Table(purpose)} WHERE user_id = @u AND created_at >= @since").P("@u", userId).P("@since", since)
                Return Await cmd.ScalarIntAsync().ConfigureAwait(False)
            End Using
        End Function
    End Class

    Public NotInheritable Class AttemptType
        Private Sub New()
        End Sub
        Public Const Login As String = "login"
        Public Const ResetRequest As String = "reset_request"
        Public Const VerificationRequest As String = "verify_request"
        Public Const CodeCheck As String = "code_check"
    End Class

    ''' <summary>Records authentication attempts for rate limiting.</summary>
    Public NotInheritable Class AttemptRepository
        Private Sub New()
        End Sub

        Public Shared Async Function InsertAsync(conn As MySqlConnection, attemptType As String, identifierHash As String, succeeded As Boolean, machine As String) As Task
            Using cmd = Db.Cmd(conn, "INSERT INTO auth_attempts (attempt_type, identifier_hash, succeeded, machine_name, created_at) VALUES (@t, @h, @s, @m, @now)").
                    P("@t", attemptType).P("@h", identifierHash).P("@s", succeeded).P("@m", machine).P("@now", DateTime.UtcNow)
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
            End Using
        End Function

        Public Shared Async Function CountAsync(conn As MySqlConnection, attemptType As String, identifierHash As String, since As DateTime, Optional failuresOnly As Boolean = True) As Task(Of Integer)
            Dim sql = "SELECT COUNT(*) FROM auth_attempts WHERE attempt_type = @t AND identifier_hash = @h AND created_at >= @since" & If(failuresOnly, " AND succeeded = 0", "")
            Using cmd = Db.Cmd(conn, sql).P("@t", attemptType).P("@h", identifierHash).P("@since", since)
                Return Await cmd.ScalarIntAsync().ConfigureAwait(False)
            End Using
        End Function

        Public Shared Async Function CountByMachineAsync(conn As MySqlConnection, attemptType As String, machine As String, since As DateTime, Optional failuresOnly As Boolean = True) As Task(Of Integer)
            Dim sql = "SELECT COUNT(*) FROM auth_attempts WHERE attempt_type = @t AND machine_name = @m AND created_at >= @since" & If(failuresOnly, " AND succeeded = 0", "")
            Using cmd = Db.Cmd(conn, sql).P("@t", attemptType).P("@m", machine).P("@since", since)
                Return Await cmd.ScalarIntAsync().ConfigureAwait(False)
            End Using
        End Function

        Public Shared Async Function LastAttemptAsync(conn As MySqlConnection, attemptType As String, identifierHash As String) As Task(Of DateTime?)
            Using cmd = Db.Cmd(conn, "SELECT MAX(created_at) FROM auth_attempts WHERE attempt_type = @t AND identifier_hash = @h").P("@t", attemptType).P("@h", identifierHash)
                Dim v = Await cmd.ExecuteScalarAsync().ConfigureAwait(False)
                If v Is Nothing OrElse v Is DBNull.Value Then Return Nothing
                Return DateTime.SpecifyKind(CDate(v), DateTimeKind.Utc)
            End Using
        End Function
    End Class

    Public NotInheritable Class SettingsRepository
        Private Sub New()
        End Sub

        Public Shared Async Function GetAllAsync(conn As MySqlConnection) As Task(Of Dictionary(Of String, String))
            Dim d As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            Using cmd = Db.Cmd(conn, "SELECT setting_key, setting_value FROM app_settings")
                Using r = Await cmd.ExecuteReaderAsync().ConfigureAwait(False)
                    While Await r.ReadAsync().ConfigureAwait(False)
                        d(r.GetString(0)) = r.GetString(1)
                    End While
                End Using
            End Using
            Return d
        End Function

        Public Shared Async Function GetForUpdateAsync(conn As MySqlConnection, tx As MySqlTransaction, key As String) As Task(Of String)
            Using cmd = Db.Cmd(conn, "SELECT setting_value FROM app_settings WHERE setting_key = @k FOR UPDATE", tx).P("@k", key)
                Dim v = Await cmd.ExecuteScalarAsync().ConfigureAwait(False)
                Return If(v Is Nothing OrElse v Is DBNull.Value, Nothing, CStr(v))
            End Using
        End Function

        Public Shared Async Function UpsertAsync(conn As MySqlConnection, tx As MySqlTransaction, key As String, value As String, userId As Integer?) As Task
            Const sql As String =
                "INSERT INTO app_settings (setting_key, setting_value, updated_at, updated_by) VALUES (@k, @v, @now, @u) " &
                "ON DUPLICATE KEY UPDATE setting_value = VALUES(setting_value), updated_at = VALUES(updated_at), updated_by = VALUES(updated_by)"
            Using cmd = Db.Cmd(conn, sql, tx).P("@k", key).P("@v", value).P("@now", DateTime.UtcNow).P("@u", userId)
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
            End Using
        End Function
    End Class

    Public Class AuditEntry
        Public Property Id As Long
        Public Property UserName As String
        Public Property Action As String = ""
        Public Property EntityType As String
        Public Property EntityId As String
        Public Property Details As String
        Public Property MachineName As String
        Public Property CreatedAt As DateTime
    End Class

    Public NotInheritable Class AuditRepository
        Private Sub New()
        End Sub

        Public Shared Async Function InsertAsync(conn As MySqlConnection, tx As MySqlTransaction, userId As Integer?, action As String, entityType As String, entityId As String, details As String) As Task
            Dim d = If(details IsNot Nothing AndAlso details.Length > 1000, details.Substring(0, 1000), details)
            Using cmd = Db.Cmd(conn, "INSERT INTO audit_logs (user_id, action, entity_type, entity_id, details, machine_name, created_at) VALUES (@u, @a, @t, @e, @d, @m, @now)", tx).
                    P("@u", userId).P("@a", action).P("@t", entityType).P("@e", entityId).P("@d", d).P("@m", Environment.MachineName).P("@now", DateTime.UtcNow)
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
            End Using
        End Function

        Public Shared Async Function RecentAsync(conn As MySqlConnection, limit As Integer) As Task(Of List(Of AuditEntry))
            Dim list As New List(Of AuditEntry)()
            Const sql As String =
                "SELECT a.id, u.username, a.action, a.entity_type, a.entity_id, a.details, a.machine_name, a.created_at " &
                "FROM audit_logs a LEFT JOIN users u ON u.id = a.user_id ORDER BY a.id DESC LIMIT @n"
            Using cmd = Db.Cmd(conn, sql).P("@n", limit)
                Using r = Await cmd.ExecuteReaderAsync().ConfigureAwait(False)
                    While Await r.ReadAsync().ConfigureAwait(False)
                        list.Add(New AuditEntry With {
                            .Id = r.Lng("id"), .UserName = r.Str("username"), .Action = r.Str("action"),
                            .EntityType = r.Str("entity_type"), .EntityId = r.Str("entity_id"), .Details = r.Str("details"),
                            .MachineName = r.Str("machine_name"), .CreatedAt = r.Utc("created_at")})
                    End While
                End Using
            End Using
            Return list
        End Function
    End Class

End Namespace
