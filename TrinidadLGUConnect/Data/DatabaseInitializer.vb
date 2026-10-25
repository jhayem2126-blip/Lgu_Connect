Imports System.Reflection
Imports System.Text
Imports System.Text.RegularExpressions
Imports MySql.Data.MySqlClient
Imports TrinidadLGUConnect.Configuration

Namespace Data

    Public Enum SchemaState
        ServerUnreachable
        DatabaseMissing
        SchemaMissing
        SchemaOutdated
        Ready
    End Enum

    Public Class SchemaCheckResult
        Public Property State As SchemaState
        Public Property Message As String = ""
        Public Property ServerVersion As String = ""
        Public Property SchemaVersion As Integer
    End Class

    ''' <summary>
    ''' Creates the database (if missing) and applies the embedded, idempotent schema script.
    ''' It never drops, truncates or deletes anything, so it is safe to run against an existing database.
    ''' </summary>
    Public NotInheritable Class DatabaseInitializer

        Public Const CurrentSchemaVersion As Integer = 3

        Private Shared ReadOnly RequiredTables As String() = {
            "roles", "permissions", "role_permissions", "barangays", "users", "user_sessions", "password_reset_tokens",
            "email_verification_tokens", "auth_attempts", "concern_categories", "concern_subcategories",
            "concern_statuses", "concern_status_transitions", "concerns", "concern_attachments",
            "concern_attachment_data", "concern_status_history", "app_settings", "audit_logs", "schema_migrations"}

        Private Shared ReadOnly DbNamePattern As New Regex("^[A-Za-z0-9_]{1,64}$")

        Private Sub New()
        End Sub

        Public Shared Async Function CheckAsync(options As DatabaseOptions) As Task(Of SchemaCheckResult)
            Dim result As New SchemaCheckResult()
            If Not options.IsConfigured Then
                result.State = SchemaState.ServerUnreachable
                result.Message = "The database connection has not been configured."
                Return result
            End If
            Dim dbName = options.DatabaseName
            Try
                Using conn As New MySqlConnection(options.BuildConnectionString(includeDatabase:=False))
                    Await conn.OpenAsync().ConfigureAwait(False)
                    result.ServerVersion = conn.ServerVersion
                    Using cmd = Db.Cmd(conn, "SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name = @db").P("@db", dbName)
                        If Await cmd.ScalarIntAsync().ConfigureAwait(False) = 0 Then
                            result.State = SchemaState.DatabaseMissing
                            result.Message = $"The database '{dbName}' does not exist yet."
                            Return result
                        End If
                    End Using
                    Dim sql = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = @db AND table_name IN (" &
                              String.Join(",", RequiredTables.Select(Function(t, i) "@t" & i)) & ")"
                    Using cmd = Db.Cmd(conn, sql).P("@db", dbName)
                        For i = 0 To RequiredTables.Length - 1
                            cmd.P("@t" & i, RequiredTables(i))
                        Next
                        Dim found = Await cmd.ScalarIntAsync().ConfigureAwait(False)
                        If found < RequiredTables.Length Then
                            result.State = SchemaState.SchemaMissing
                            result.Message = $"The database exists but {RequiredTables.Length - found} required table(s) are missing."
                            Return result
                        End If
                    End Using
                    Using cmd = Db.Cmd(conn, $"SELECT COALESCE(MAX(version), 0) FROM `{EnsureSafeName(dbName)}`.schema_migrations")
                        result.SchemaVersion = Await cmd.ScalarIntAsync().ConfigureAwait(False)
                    End Using
                    If result.SchemaVersion < CurrentSchemaVersion Then
                        result.State = SchemaState.SchemaOutdated
                        result.Message = $"The database schema (version {result.SchemaVersion}) needs to be upgraded to version {CurrentSchemaVersion}."
                    Else
                        result.State = SchemaState.Ready
                        result.Message = "Connected."
                    End If
                End Using
            Catch ex As MySqlException
                result.State = SchemaState.ServerUnreachable
                result.Message = Db.Translate(ex).Message
            End Try
            Return result
        End Function

        ''' <summary>Creates the database if needed and applies the schema. Requires CREATE privileges.</summary>
        Public Shared Async Function InitializeAsync(options As DatabaseOptions) As Task
            Dim dbName = EnsureSafeName(options.DatabaseName)
            Try
                Using conn As New MySqlConnection(options.BuildConnectionString(includeDatabase:=False))
                    Await conn.OpenAsync().ConfigureAwait(False)
                    Using cmd = Db.Cmd(conn, $"CREATE DATABASE IF NOT EXISTS `{dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci")
                        Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
                    End Using
                    conn.ChangeDatabase(dbName)
                    For Each statement In SplitStatements(LoadSchemaScript())
                        Using cmd = Db.Cmd(conn, statement)
                            Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
                        End Using
                    Next
                    Await ApplyUpgradesAsync(conn, dbName).ConfigureAwait(False)
                End Using
                Services.AppLog.Info($"Database schema applied to '{dbName}'.")
            Catch ex As MySqlException
                Throw Db.Translate(ex)
            End Try
        End Function

        ''' <summary>
        ''' Brings a database created by an older version up to date. CREATE TABLE IF NOT EXISTS does not
        ''' change tables that already exist, so new columns are added here - only when they are missing.
        ''' </summary>
        Private Shared Async Function ApplyUpgradesAsync(conn As MySqlConnection, dbName As String) As Task
            ' Version 2: barangays on users and concerns, profile photos.
            Await AddColumnIfMissingAsync(conn, dbName, "users", "barangay_id",
                "ALTER TABLE users ADD COLUMN barangay_id TINYINT UNSIGNED NULL AFTER address, ADD KEY ix_users_barangay (barangay_id), " &
                "ADD CONSTRAINT fk_users_barangay FOREIGN KEY (barangay_id) REFERENCES barangays (id)").ConfigureAwait(False)
            Await AddColumnIfMissingAsync(conn, dbName, "users", "profile_photo",
                "ALTER TABLE users ADD COLUMN profile_photo MEDIUMBLOB NULL AFTER barangay_id").ConfigureAwait(False)
            Await AddColumnIfMissingAsync(conn, dbName, "users", "profile_photo_updated_at",
                "ALTER TABLE users ADD COLUMN profile_photo_updated_at DATETIME NULL AFTER profile_photo").ConfigureAwait(False)
            Await AddColumnIfMissingAsync(conn, dbName, "concerns", "barangay_id",
                "ALTER TABLE concerns ADD COLUMN barangay_id TINYINT UNSIGNED NULL AFTER subcategory_id, ADD KEY ix_concerns_barangay (barangay_id), " &
                "ADD CONSTRAINT fk_concerns_barangay FOREIGN KEY (barangay_id) REFERENCES barangays (id)").ConfigureAwait(False)
            ' Record which account the first-run setup created (older databases only have it in the audit log).
            Using cmd = Db.Cmd(conn, "INSERT IGNORE INTO app_settings (setting_key, setting_value) " &
                                     "SELECT 'setup.primary_admin_user_id', CAST(user_id AS CHAR) FROM audit_logs " &
                                     "WHERE action = 'setup.initial_admin_created' AND user_id IS NOT NULL ORDER BY id LIMIT 1")
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
            End Using
            Using cmd = Db.Cmd(conn, "INSERT IGNORE INTO schema_migrations (version, description) VALUES (2, 'Barangays and profile photos')")
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
            End Using
            ' Version 3: administrators may file a concern with the same form residents use
            ' (the role_permissions row itself comes from the seed data in schema.sql).
            Using cmd = Db.Cmd(conn, "INSERT IGNORE INTO schema_migrations (version, description) VALUES (3, 'Administrators can submit concerns')")
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
            End Using
        End Function

        Private Shared Async Function AddColumnIfMissingAsync(conn As MySqlConnection, dbName As String, table As String, column As String, alterSql As String) As Task
            Using check = Db.Cmd(conn, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = @db AND table_name = @t AND column_name = @c").
                    P("@db", dbName).P("@t", table).P("@c", column)
                If Await check.ScalarIntAsync().ConfigureAwait(False) > 0 Then Return
            End Using
            Using cmd = Db.Cmd(conn, alterSql)
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
            End Using
            Services.AppLog.Info($"Schema upgrade: added {table}.{column}.")
        End Function

        Public Shared Function EnsureSafeName(name As String) As String
            If String.IsNullOrWhiteSpace(name) OrElse Not DbNamePattern.IsMatch(name) Then
                Throw New Helpers.ValidationException("The database name may contain only letters, numbers and underscores (max 64 characters).")
            End If
            Return name
        End Function

        Public Shared Function LoadSchemaScript() As String
            Using s = Assembly.GetExecutingAssembly().GetManifestResourceStream("TrinidadLGUConnect.schema.sql")
                If s Is Nothing Then Throw New InvalidOperationException("The embedded schema script is missing from the build.")
                Using r As New StreamReader(s, Encoding.UTF8)
                    Return r.ReadToEnd()
                End Using
            End Using
        End Function

        ''' <summary>Splits the schema script into statements. Full-line "--" comments are removed first.</summary>
        Public Shared Function SplitStatements(script As String) As List(Of String)
            Dim lines = script.Replace(vbCrLf, vbLf).Split(ControlChars.Lf).
                Where(Function(l) Not l.TrimStart().StartsWith("--", StringComparison.Ordinal))
            Dim body = String.Join(vbLf, lines)
            Return body.Split(";"c).
                Select(Function(s) s.Trim()).
                Where(Function(s) s.Length > 0).
                ToList()
        End Function

    End Class

End Namespace
