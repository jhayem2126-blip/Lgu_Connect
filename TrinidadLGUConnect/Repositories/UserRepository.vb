Imports System.Data.Common
Imports MySql.Data.MySqlClient
Imports TrinidadLGUConnect.Data
Imports TrinidadLGUConnect.Helpers
Imports TrinidadLGUConnect.Models

Namespace Repositories

    Public NotInheritable Class UserRepository

        Private Const SelectColumns As String =
            "SELECT u.id, u.full_name, u.email, u.username, u.password_hash, u.role_id, u.contact_number, u.address, " &
            "u.is_active, u.email_verified, u.notify_email, u.failed_login_count, u.lockout_until, u.last_login_at, u.created_at, " &
            "u.barangay_id, bgy.name AS barangay_name, (u.profile_photo IS NOT NULL) AS has_photo " &
            "FROM users u LEFT JOIN barangays bgy ON bgy.id = u.barangay_id "

        Private Sub New()
        End Sub

        Friend Shared Function Map(r As DbDataReader) As UserAccount
            Return New UserAccount With {
                .Id = r.Int("id"),
                .FullName = r.Str("full_name"),
                .Email = r.Str("email"),
                .Username = r.Str("username"),
                .PasswordHash = r.Str("password_hash"),
                .Role = CType(r.Int("role_id"), UserRole),
                .ContactNumber = r.Str("contact_number"),
                .Address = r.Str("address"),
                .IsActive = r.Bool("is_active"),
                .EmailVerified = r.Bool("email_verified"),
                .NotifyEmail = r.Bool("notify_email"),
                .FailedLoginCount = r.Int("failed_login_count"),
                .LockoutUntil = r.UtcN("lockout_until"),
                .LastLoginAt = r.UtcN("last_login_at"),
                .CreatedAt = r.Utc("created_at"),
                .BarangayId = r.IntN("barangay_id"),
                .BarangayName = r.Str("barangay_name"),
                .HasPhoto = r.Bool("has_photo")
            }
        End Function

        Private Shared Async Function SingleAsync(cmd As MySqlCommand) As Task(Of UserAccount)
            Using r = Await cmd.ExecuteReaderAsync().ConfigureAwait(False)
                If Await r.ReadAsync().ConfigureAwait(False) Then Return Map(r)
            End Using
            Return Nothing
        End Function

        Public Shared Async Function GetByIdAsync(conn As MySqlConnection, id As Integer, Optional tx As MySqlTransaction = Nothing, Optional forUpdate As Boolean = False) As Task(Of UserAccount)
            Using cmd = Db.Cmd(conn, SelectColumns & "WHERE u.id = @id" & If(forUpdate, " FOR UPDATE", ""), tx).P("@id", id)
                Return Await SingleAsync(cmd).ConfigureAwait(False)
            End Using
        End Function

        ''' <summary>Finds a user by username or email (both are case-insensitive in the database collation).</summary>
        Public Shared Async Function FindByLoginAsync(conn As MySqlConnection, identifier As String) As Task(Of UserAccount)
            Using cmd = Db.Cmd(conn, SelectColumns & "WHERE u.username = @id OR u.email = @email LIMIT 1").
                    P("@id", identifier.Trim()).P("@email", NormalizeEmail(identifier))
                Return Await SingleAsync(cmd).ConfigureAwait(False)
            End Using
        End Function

        Public Shared Async Function FindByEmailAsync(conn As MySqlConnection, email As String, Optional tx As MySqlTransaction = Nothing) As Task(Of UserAccount)
            Using cmd = Db.Cmd(conn, SelectColumns & "WHERE u.email = @email LIMIT 1", tx).P("@email", NormalizeEmail(email))
                Return Await SingleAsync(cmd).ConfigureAwait(False)
            End Using
        End Function

        Public Shared Async Function UsernameExistsAsync(conn As MySqlConnection, username As String, Optional excludeId As Integer = 0, Optional tx As MySqlTransaction = Nothing) As Task(Of Boolean)
            Using cmd = Db.Cmd(conn, "SELECT COUNT(*) FROM users WHERE username = @u AND id <> @x", tx).P("@u", username.Trim()).P("@x", excludeId)
                Return Await cmd.ScalarIntAsync().ConfigureAwait(False) > 0
            End Using
        End Function

        Public Shared Async Function EmailExistsAsync(conn As MySqlConnection, email As String, Optional excludeId As Integer = 0, Optional tx As MySqlTransaction = Nothing) As Task(Of Boolean)
            Using cmd = Db.Cmd(conn, "SELECT COUNT(*) FROM users WHERE email = @e AND id <> @x", tx).P("@e", NormalizeEmail(email)).P("@x", excludeId)
                Return Await cmd.ScalarIntAsync().ConfigureAwait(False) > 0
            End Using
        End Function

        Public Shared Async Function InsertAsync(conn As MySqlConnection, tx As MySqlTransaction, user As UserAccount, passwordHash As String, createdBy As Integer?) As Task(Of Integer)
            Const sql As String =
                "INSERT INTO users (full_name, email, username, password_hash, role_id, contact_number, barangay_id, is_active, email_verified, notify_email, " &
                "password_changed_at, created_by, created_at, updated_at) VALUES " &
                "(@name, @email, @username, @hash, @role, @contact, @bgy, @active, @verified, 1, @now, @by, @now, @now)"
            Dim now = DateTime.UtcNow
            Using cmd = Db.Cmd(conn, sql, tx).
                    P("@name", user.FullName).P("@email", NormalizeEmail(user.Email)).P("@username", user.Username.Trim()).
                    P("@hash", passwordHash).P("@role", CByte(user.Role)).P("@contact", user.ContactNumber).P("@bgy", user.BarangayId).
                    P("@active", user.IsActive).P("@verified", user.EmailVerified).P("@now", now).P("@by", createdBy)
                Try
                    Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
                Catch ex As MySqlException When ex.Number = 1062
                    Throw DuplicateError(ex)
                End Try
                Return CInt(cmd.LastInsertedId)
            End Using
        End Function

        Friend Shared Function DuplicateError(ex As MySqlException) As ValidationException
            If ex.Message.Contains("uq_users_email") Then Return New ValidationException("This email address is already registered.", "Email")
            If ex.Message.Contains("uq_users_username") Then Return New ValidationException("This username is already taken.", "Username")
            Return New ValidationException("An account with these details already exists.")
        End Function

        Public Shared Async Function UpdateAsync(conn As MySqlConnection, tx As MySqlTransaction, req As UserEditRequest) As Task
            Const sql As String =
                "UPDATE users SET full_name = @name, email = @email, username = @username, role_id = @role, is_active = @active, " &
                "email_verified = @verified, contact_number = @contact, barangay_id = @bgy, updated_at = @now WHERE id = @id"
            Using cmd = Db.Cmd(conn, sql, tx).
                    P("@name", req.FullName).P("@email", NormalizeEmail(req.Email)).P("@username", req.Username.Trim()).
                    P("@role", CByte(req.Role)).P("@active", req.IsActive).P("@verified", req.EmailVerified).
                    P("@contact", req.ContactNumber).P("@bgy", req.BarangayId).P("@now", DateTime.UtcNow).P("@id", req.Id)
                Try
                    Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
                Catch ex As MySqlException When ex.Number = 1062
                    Throw DuplicateError(ex)
                End Try
            End Using
        End Function

        Public Shared Async Function UpdatePasswordAsync(conn As MySqlConnection, tx As MySqlTransaction, userId As Integer, passwordHash As String) As Task
            Using cmd = Db.Cmd(conn, "UPDATE users SET password_hash = @h, password_changed_at = @now, failed_login_count = 0, lockout_until = NULL, updated_at = @now WHERE id = @id", tx).
                    P("@h", passwordHash).P("@now", DateTime.UtcNow).P("@id", userId)
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
            End Using
        End Function

        ''' <summary>Silently upgrades a hash created with older parameters.</summary>
        Public Shared Async Function RehashAsync(conn As MySqlConnection, userId As Integer, passwordHash As String) As Task
            Using cmd = Db.Cmd(conn, "UPDATE users SET password_hash = @h WHERE id = @id").P("@h", passwordHash).P("@id", userId)
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
            End Using
        End Function

        Public Shared Async Function RecordLoginSuccessAsync(conn As MySqlConnection, userId As Integer) As Task
            Using cmd = Db.Cmd(conn, "UPDATE users SET failed_login_count = 0, lockout_until = NULL, last_login_at = @now WHERE id = @id").
                    P("@now", DateTime.UtcNow).P("@id", userId)
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
            End Using
        End Function

        ''' <summary>Increments the failure counter and locks the account when the threshold is reached. Returns the new count.</summary>
        Public Shared Async Function RecordLoginFailureAsync(conn As MySqlConnection, userId As Integer, maxFailures As Integer, lockoutMinutes As Integer) As Task(Of Integer)
            ' MySQL evaluates single-table SET assignments left to right, so failed_login_count is already incremented here.
            Const sql As String =
                "UPDATE users SET failed_login_count = failed_login_count + 1, " &
                "lockout_until = CASE WHEN failed_login_count >= @max THEN @until ELSE lockout_until END WHERE id = @id"
            Using cmd = Db.Cmd(conn, sql).P("@max", maxFailures).P("@until", DateTime.UtcNow.AddMinutes(lockoutMinutes)).P("@id", userId)
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
            End Using
            Using cmd = Db.Cmd(conn, "SELECT failed_login_count FROM users WHERE id = @id").P("@id", userId)
                Dim count = Await cmd.ScalarIntAsync().ConfigureAwait(False)
                If count >= maxFailures Then
                    ' Start a fresh count after the lockout so the next window is not locked immediately.
                    Using reset = Db.Cmd(conn, "UPDATE users SET failed_login_count = 0 WHERE id = @id").P("@id", userId)
                        Await reset.ExecuteNonQueryAsync().ConfigureAwait(False)
                    End Using
                End If
                Return count
            End Using
        End Function

        Public Shared Async Function SetEmailVerifiedAsync(conn As MySqlConnection, tx As MySqlTransaction, userId As Integer) As Task
            Using cmd = Db.Cmd(conn, "UPDATE users SET email_verified = 1, updated_at = @now WHERE id = @id", tx).P("@now", DateTime.UtcNow).P("@id", userId)
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
            End Using
        End Function

        Public Shared Async Function SetActiveAsync(conn As MySqlConnection, tx As MySqlTransaction, userId As Integer, active As Boolean) As Task
            Using cmd = Db.Cmd(conn, "UPDATE users SET is_active = @a, failed_login_count = 0, lockout_until = NULL, updated_at = @now WHERE id = @id", tx).
                    P("@a", active).P("@now", DateTime.UtcNow).P("@id", userId)
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
            End Using
        End Function

        Public Shared Async Function UpdateProfileAsync(conn As MySqlConnection, tx As MySqlTransaction, userId As Integer, p As ProfileUpdate) As Task
            Using cmd = Db.Cmd(conn, "UPDATE users SET full_name = @n, contact_number = @c, address = @a, barangay_id = @b, notify_email = @ne, updated_at = @now WHERE id = @id", tx).
                    P("@n", p.FullName).P("@c", p.ContactNumber).P("@a", p.Address).P("@b", p.BarangayId).P("@ne", p.NotifyEmail).P("@now", DateTime.UtcNow).P("@id", userId)
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
            End Using
        End Function

        Public Shared Async Function CountActiveAdminsAsync(conn As MySqlConnection, Optional tx As MySqlTransaction = Nothing) As Task(Of Integer)
            Using cmd = Db.Cmd(conn, "SELECT COUNT(*) FROM users WHERE role_id = 1 AND is_active = 1", tx)
                Return Await cmd.ScalarIntAsync().ConfigureAwait(False)
            End Using
        End Function

        Public Shared Async Function CountAdminsAsync(conn As MySqlConnection, Optional tx As MySqlTransaction = Nothing) As Task(Of Integer)
            Using cmd = Db.Cmd(conn, "SELECT COUNT(*) FROM users WHERE role_id = 1", tx)
                Return Await cmd.ScalarIntAsync().ConfigureAwait(False)
            End Using
        End Function

        Public Shared Async Function GetPermissionsAsync(conn As MySqlConnection, role As UserRole) As Task(Of HashSet(Of String))
            Dim set1 As New HashSet(Of String)(StringComparer.Ordinal)
            Using cmd = Db.Cmd(conn, "SELECT p.code FROM role_permissions rp JOIN permissions p ON p.id = rp.permission_id WHERE rp.role_id = @r").P("@r", CByte(role))
                Using r = Await cmd.ExecuteReaderAsync().ConfigureAwait(False)
                    While Await r.ReadAsync().ConfigureAwait(False)
                        set1.Add(r.GetString(0))
                    End While
                End Using
            End Using
            Return set1
        End Function

        ''' <summary>True when the user is referenced by concerns, history or attachments (and therefore cannot be deleted).</summary>
        Public Shared Async Function HasRecordsAsync(conn As MySqlConnection, tx As MySqlTransaction, userId As Integer) As Task(Of Boolean)
            Const sql As String =
                "SELECT (SELECT COUNT(*) FROM concerns WHERE resident_id = @id) + " &
                "(SELECT COUNT(*) FROM concern_status_history WHERE changed_by = @id) + " &
                "(SELECT COUNT(*) FROM concern_attachments WHERE uploaded_by = @id)"
            Using cmd = Db.Cmd(conn, sql, tx).P("@id", userId)
                Return Await cmd.ScalarIntAsync().ConfigureAwait(False) > 0
            End Using
        End Function

        Public Shared Async Function DeleteAsync(conn As MySqlConnection, tx As MySqlTransaction, userId As Integer) As Task
            Using cmd = Db.Cmd(conn, "DELETE FROM users WHERE id = @id", tx).P("@id", userId)
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
            End Using
        End Function

        Public Shared Async Function SearchAsync(conn As MySqlConnection, f As UserFilter) As Task(Of PagedResult(Of UserAccount))
            Dim where As New List(Of String) From {"1 = 1"}
            Dim prms As New List(Of KeyValuePair(Of String, Object))()
            If Not String.IsNullOrWhiteSpace(f.Search) Then
                Dim q = f.Search.Trim()
                Dim idPart As Integer
                Dim digits = q.ToUpperInvariant().Replace("USR-", "")
                If Integer.TryParse(digits, idPart) Then
                    where.Add("(u.full_name LIKE @q OR u.username LIKE @q OR u.email LIKE @q OR r.name LIKE @q OR u.id = @qid)")
                    prms.Add(New KeyValuePair(Of String, Object)("@qid", idPart))
                Else
                    where.Add("(u.full_name LIKE @q OR u.username LIKE @q OR u.email LIKE @q OR r.name LIKE @q)")
                End If
                prms.Add(New KeyValuePair(Of String, Object)("@q", LikeContains(q)))
            End If
            If f.Role.HasValue Then
                where.Add("u.role_id = @role")
                prms.Add(New KeyValuePair(Of String, Object)("@role", CByte(f.Role.Value)))
            End If
            If f.IsActive.HasValue Then
                where.Add("u.is_active = @active")
                prms.Add(New KeyValuePair(Of String, Object)("@active", f.IsActive.Value))
            End If
            Dim whereSql = " WHERE " & String.Join(" AND ", where)
            Dim result As New PagedResult(Of UserAccount) With {.PageSize = Math.Max(1, f.PageSize)}

            Using cmd = Db.Cmd(conn, "SELECT COUNT(*) FROM users u JOIN roles r ON r.id = u.role_id" & whereSql)
                For Each kv In prms : cmd.P(kv.Key, kv.Value) : Next
                result.TotalCount = Await cmd.ScalarIntAsync().ConfigureAwait(False)
            End Using
            result.Page = Math.Min(Math.Max(1, f.Page), result.TotalPages)

            Dim sql = SelectColumns & "JOIN roles r ON r.id = u.role_id" & whereSql & " ORDER BY u.id LIMIT @limit OFFSET @offset"
            Using cmd = Db.Cmd(conn, sql)
                For Each kv In prms : cmd.P(kv.Key, kv.Value) : Next
                cmd.P("@limit", result.PageSize).P("@offset", (result.Page - 1) * result.PageSize)
                Using r = Await cmd.ExecuteReaderAsync().ConfigureAwait(False)
                    While Await r.ReadAsync().ConfigureAwait(False)
                        result.Items.Add(Map(r))
                    End While
                End Using
            End Using
            Return result
        End Function

        Public Shared Async Function GetPhotoAsync(conn As MySqlConnection, userId As Integer) As Task(Of Byte())
            Using cmd = Db.Cmd(conn, "SELECT profile_photo FROM users WHERE id = @id").P("@id", userId)
                Dim v = Await cmd.ExecuteScalarAsync().ConfigureAwait(False)
                Return If(v Is Nothing OrElse v Is DBNull.Value, Nothing, DirectCast(v, Byte()))
            End Using
        End Function

        ''' <summary>Stores (or with Nothing, removes) the user's profile photo.</summary>
        Public Shared Async Function SetPhotoAsync(conn As MySqlConnection, tx As MySqlTransaction, userId As Integer, photo As Byte()) As Task
            Using cmd = Db.Cmd(conn, "UPDATE users SET profile_photo = @p, profile_photo_updated_at = @now, updated_at = @now WHERE id = @id", tx).
                    Blob("@p", photo).P("@now", DateTime.UtcNow).P("@id", userId)
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
            End Using
        End Function

        Public Shared Async Function GetNotificationsSeenAsync(conn As MySqlConnection, userId As Integer) As Task(Of DateTime?)
            Using cmd = Db.Cmd(conn, "SELECT notifications_seen_at FROM users WHERE id = @id").P("@id", userId)
                Dim v = Await cmd.ExecuteScalarAsync().ConfigureAwait(False)
                If v Is Nothing OrElse v Is DBNull.Value Then Return Nothing
                Return DateTime.SpecifyKind(CDate(v), DateTimeKind.Utc)
            End Using
        End Function

        Public Shared Async Function SetNotificationsSeenAsync(conn As MySqlConnection, userId As Integer) As Task
            Using cmd = Db.Cmd(conn, "UPDATE users SET notifications_seen_at = @now WHERE id = @id").P("@now", DateTime.UtcNow).P("@id", userId)
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
            End Using
        End Function

    End Class

End Namespace
