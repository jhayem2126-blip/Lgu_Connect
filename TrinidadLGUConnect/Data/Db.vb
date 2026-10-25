Imports MySql.Data.MySqlClient

Namespace Data

    ''' <summary>Raised for database failures. The message is safe to show; details go to the local log.</summary>
    Public Class DataAccessException
        Inherits Exception
        Public Sub New(message As String, inner As Exception)
            MyBase.New(message, inner)
        End Sub
    End Class

    ''' <summary>
    ''' Connection factory and small helpers. All SQL in the application lives in the repositories and
    ''' uses parameters for every value; identifiers are never built from user input.
    ''' </summary>
    Public Class Db

        Private ReadOnly _connectionString As String

        Public Sub New(connectionString As String)
            _connectionString = connectionString
        End Sub

        Public ReadOnly Property ConnectionString As String
            Get
                Return _connectionString
            End Get
        End Property

        Public Async Function OpenAsync() As Task(Of MySqlConnection)
            Dim conn As New MySqlConnection(_connectionString)
            Try
                Await conn.OpenAsync().ConfigureAwait(False)
                Return conn
            Catch ex As MySqlException
                conn.Dispose()
                Throw Translate(ex)
            End Try
        End Function

        ''' <summary>Runs <paramref name="work"/> on a fresh connection.</summary>
        Public Async Function RunAsync(Of T)(work As Func(Of MySqlConnection, Task(Of T))) As Task(Of T)
            Using conn = Await OpenAsync().ConfigureAwait(False)
                Try
                    Return Await work(conn).ConfigureAwait(False)
                Catch ex As MySqlException
                    Throw Translate(ex)
                End Try
            End Using
        End Function

        ''' <summary>Runs <paramref name="work"/> inside a transaction; commits on success, rolls back on any exception.</summary>
        Public Async Function InTransactionAsync(Of T)(work As Func(Of MySqlConnection, MySqlTransaction, Task(Of T))) As Task(Of T)
            Using conn = Await OpenAsync().ConfigureAwait(False)
                Using tx = Await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted).ConfigureAwait(False)
                    Try
                        Dim result = Await work(conn, tx).ConfigureAwait(False)
                        Await tx.CommitAsync().ConfigureAwait(False)
                        Return result
                    Catch ex As Exception
                        Try
                            tx.Rollback()
                        Catch rollbackEx As Exception
                            Services.AppLog.Error("Transaction rollback failed", rollbackEx)
                        End Try
                        If TypeOf ex Is MySqlException Then Throw Translate(DirectCast(ex, MySqlException))
                        Throw
                    End Try
                End Using
            End Using
        End Function

        Public Shared Function Translate(ex As MySqlException) As Exception
            Services.AppLog.Error($"MySQL error {ex.Number}", ex)
            Select Case ex.Number
                Case 1042, 2002, 2003, 2005, 2013, 2006
                    Return New DataAccessException("Cannot connect to the database server. Check that MySQL is running and the connection settings are correct.", ex)
                Case 1045
                    Return New DataAccessException("The database rejected the configured user name or password.", ex)
                Case 1049
                    Return New DataAccessException("The configured database does not exist. Run the database setup.", ex)
                Case 1146
                    Return New DataAccessException("The database schema is incomplete. Run the database setup to initialise it.", ex)
                Case 1142, 1143, 1044
                    Return New DataAccessException("The database account does not have permission for this operation.", ex)
                Case 1205, 1213
                    Return New DataAccessException("The database is busy. Please try again.", ex)
                Case 1153
                    Return New DataAccessException("The file is larger than the database server allows (max_allowed_packet).", ex)
                Case Else
                    Return New DataAccessException("A database error occurred. Please try again or contact the system administrator.", ex)
            End Select
        End Function

        Public Shared Function Cmd(conn As MySqlConnection, sql As String, Optional tx As MySqlTransaction = Nothing) As MySqlCommand
            Return New MySqlCommand(sql, conn, tx)
        End Function

    End Class

    Public Module DbExtensions

        <Runtime.CompilerServices.Extension>
        Public Function P(cmd As MySqlCommand, name As String, value As Object) As MySqlCommand
            cmd.Parameters.AddWithValue(name, If(value, DBNull.Value))
            Return cmd
        End Function

        <Runtime.CompilerServices.Extension>
        Public Function Blob(cmd As MySqlCommand, name As String, value As Byte()) As MySqlCommand
            Dim prm = cmd.Parameters.Add(name, MySqlDbType.LongBlob)
            prm.Value = If(value Is Nothing, CObj(DBNull.Value), value)
            Return cmd
        End Function

        <Runtime.CompilerServices.Extension>
        Public Function Str(r As Common.DbDataReader, name As String) As String
            Dim i = r.GetOrdinal(name)
            Return If(r.IsDBNull(i), Nothing, Convert.ToString(r.GetValue(i), Globalization.CultureInfo.InvariantCulture))
        End Function

        <Runtime.CompilerServices.Extension>
        Public Function Int(r As Common.DbDataReader, name As String) As Integer
            Dim i = r.GetOrdinal(name)
            Return If(r.IsDBNull(i), 0, Convert.ToInt32(r.GetValue(i), Globalization.CultureInfo.InvariantCulture))
        End Function

        <Runtime.CompilerServices.Extension>
        Public Function IntN(r As Common.DbDataReader, name As String) As Integer?
            Dim i = r.GetOrdinal(name)
            If r.IsDBNull(i) Then Return Nothing
            Return Convert.ToInt32(r.GetValue(i), Globalization.CultureInfo.InvariantCulture)
        End Function

        <Runtime.CompilerServices.Extension>
        Public Function Lng(r As Common.DbDataReader, name As String) As Long
            Dim i = r.GetOrdinal(name)
            Return If(r.IsDBNull(i), 0L, Convert.ToInt64(r.GetValue(i), Globalization.CultureInfo.InvariantCulture))
        End Function

        <Runtime.CompilerServices.Extension>
        Public Function Bool(r As Common.DbDataReader, name As String) As Boolean
            Dim i = r.GetOrdinal(name)
            Return Not r.IsDBNull(i) AndAlso Convert.ToInt32(r.GetValue(i), Globalization.CultureInfo.InvariantCulture) <> 0
        End Function

        ''' <summary>Reads a DATETIME column that holds UTC.</summary>
        <Runtime.CompilerServices.Extension>
        Public Function Utc(r As Common.DbDataReader, name As String) As DateTime
            Dim i = r.GetOrdinal(name)
            If r.IsDBNull(i) Then Return DateTime.MinValue
            Return DateTime.SpecifyKind(r.GetDateTime(i), DateTimeKind.Utc)
        End Function

        <Runtime.CompilerServices.Extension>
        Public Function UtcN(r As Common.DbDataReader, name As String) As DateTime?
            Dim i = r.GetOrdinal(name)
            If r.IsDBNull(i) Then Return Nothing
            Return DateTime.SpecifyKind(r.GetDateTime(i), DateTimeKind.Utc)
        End Function

        <Runtime.CompilerServices.Extension>
        Public Function Bytes(r As Common.DbDataReader, name As String) As Byte()
            Dim i = r.GetOrdinal(name)
            If r.IsDBNull(i) Then Return Nothing
            Return DirectCast(r.GetValue(i), Byte())
        End Function

        <Runtime.CompilerServices.Extension>
        Public Async Function ScalarIntAsync(cmd As MySqlCommand) As Task(Of Integer)
            Dim v = Await cmd.ExecuteScalarAsync().ConfigureAwait(False)
            If v Is Nothing OrElse v Is DBNull.Value Then Return 0
            Return Convert.ToInt32(v, Globalization.CultureInfo.InvariantCulture)
        End Function

        <Runtime.CompilerServices.Extension>
        Public Async Function ScalarLongAsync(cmd As MySqlCommand) As Task(Of Long)
            Dim v = Await cmd.ExecuteScalarAsync().ConfigureAwait(False)
            If v Is Nothing OrElse v Is DBNull.Value Then Return 0
            Return Convert.ToInt64(v, Globalization.CultureInfo.InvariantCulture)
        End Function

        ''' <summary>Escapes LIKE wildcards in user input so they are matched literally.</summary>
        Public Function LikeContains(value As String) As String
            Dim s = value.Replace("\", "\\").Replace("%", "\%").Replace("_", "\_")
            Return "%" & s & "%"
        End Function

    End Module

End Namespace
