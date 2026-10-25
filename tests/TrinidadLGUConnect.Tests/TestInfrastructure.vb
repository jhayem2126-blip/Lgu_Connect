Imports System.Collections.Concurrent
Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports MimeKit
Imports MySql.Data.MySqlClient
Imports TrinidadLGUConnect.Configuration
Imports TrinidadLGUConnect.Data
Imports TrinidadLGUConnect.Services

''' <summary>
''' Integration tests run only when TLC_TEST_DB is set to a MySQL connection string (without a database name)
''' for an account that may CREATE and DROP databases. Each run creates a uniquely named tlc_test_* database
''' and drops only that database afterwards.
''' </summary>
Public Class DbFactAttribute
    Inherits FactAttribute
    Public Sub New()
        If String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TLC_TEST_DB")) Then
            Skip = "Set TLC_TEST_DB (MySQL connection string with CREATE/DROP privileges) to run database integration tests."
        End If
    End Sub
End Class

''' <summary>Minimal in-process SMTP server that records messages, so the real MailKit code path can be tested.</summary>
Public NotInheritable Class FakeSmtpServer
    Implements IDisposable

    Private ReadOnly _listener As New TcpListener(IPAddress.Loopback, 0)
    Private ReadOnly _cts As New CancellationTokenSource()
    Public ReadOnly Messages As New ConcurrentQueue(Of MimeMessage)()
    Public Property RejectAll As Boolean

    Public Sub New()
        _listener.Start()
        Task.Run(AddressOf AcceptLoop)
    End Sub

    Public ReadOnly Property Port As Integer
        Get
            Return DirectCast(_listener.LocalEndpoint, IPEndPoint).Port
        End Get
    End Property

    Private Async Function AcceptLoop() As Task
        While Not _cts.IsCancellationRequested
            Dim client As TcpClient
            Try
                client = Await _listener.AcceptTcpClientAsync(_cts.Token)
            Catch ex As Exception
                Return
            End Try
            Dim unused = Task.Run(Function() Handle(client))
        End While
    End Function

    Private Async Function Handle(client As TcpClient) As Task
        Using client
            Using stream = client.GetStream(), reader As New StreamReader(stream, Encoding.ASCII), writer As New StreamWriter(stream, Encoding.ASCII) With {.NewLine = vbCrLf, .AutoFlush = True}
                Await writer.WriteLineAsync("220 fake.smtp ESMTP")
                While True
                    Dim line = Await reader.ReadLineAsync()
                    If line Is Nothing Then Return
                    Dim cmd = line.ToUpperInvariant()
                    If cmd.StartsWith("EHLO") Then
                        Await writer.WriteLineAsync("250-fake.smtp")
                        Await writer.WriteLineAsync("250 8BITMIME")
                    ElseIf cmd.StartsWith("HELO") OrElse cmd.StartsWith("RSET") OrElse cmd.StartsWith("NOOP") Then
                        Await writer.WriteLineAsync("250 OK")
                    ElseIf cmd.StartsWith("MAIL FROM") Then
                        Await writer.WriteLineAsync(If(RejectAll, "550 Rejected", "250 OK"))
                    ElseIf cmd.StartsWith("RCPT TO") Then
                        Await writer.WriteLineAsync("250 OK")
                    ElseIf cmd = "DATA" Then
                        Await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>")
                        Dim sb As New StringBuilder()
                        While True
                            Dim l = Await reader.ReadLineAsync()
                            If l Is Nothing OrElse l = "." Then Exit While
                            If l.StartsWith("..") Then l = l.Substring(1)
                            sb.Append(l).Append(vbCrLf)
                        End While
                        Using ms As New MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()))
                            Messages.Enqueue(MimeMessage.Load(ms))
                        End Using
                        Await writer.WriteLineAsync("250 Queued")
                    ElseIf cmd = "QUIT" Then
                        Await writer.WriteLineAsync("221 Bye")
                        Return
                    Else
                        Await writer.WriteLineAsync("250 OK")
                    End If
                End While
            End Using
        End Using
    End Function

    Public Function TakeAll() As List(Of MimeMessage)
        Dim list As New List(Of MimeMessage)()
        Dim m As MimeMessage = Nothing
        While Messages.TryDequeue(m)
            list.Add(m)
        End While
        Return list
    End Function

    ''' <summary>Returns the one-time code in the latest message to <paramref name="toAddress"/>.</summary>
    Public Function LatestCodeFor(toAddress As String) As String
        Dim msgs = TakeAll().Where(Function(x) x.To.Mailboxes.Any(Function(mb) String.Equals(mb.Address, toAddress, StringComparison.OrdinalIgnoreCase))).ToList()
        If msgs.Count = 0 Then Return Nothing
        Dim m = Regex.Match(msgs.Last().TextBody, "\b[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}\b")
        Return If(m.Success, m.Value, Nothing)
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        _cts.Cancel()
        _listener.Stop()
    End Sub
End Class

''' <summary>Creates a fresh tlc_test_* database with the schema, wires AppServices to it, and drops it afterwards.</summary>
Public Class DatabaseFixture
    Implements IDisposable

    Public ReadOnly Property Enabled As Boolean
    Public ReadOnly Property DatabaseName As String
    Public ReadOnly Property Smtp As FakeSmtpServer
    Public ReadOnly Property AdminConnection As String
    Public Const AdminPassword As String = "Admin#Trinidad2025"

    Public Sub New()
        Dim baseCs = Environment.GetEnvironmentVariable("TLC_TEST_DB")
        If String.IsNullOrWhiteSpace(baseCs) Then Return
        Enabled = True
        AdminConnection = baseCs
        DatabaseName = "tlc_test_" & Guid.NewGuid().ToString("N").Substring(0, 10)
        Smtp = New FakeSmtpServer()

        Dim b As New MySqlConnectionStringBuilder(baseCs) With {.Database = DatabaseName}
        Environment.SetEnvironmentVariable("TLC_DB_CONNECTION", b.ConnectionString)
        Environment.SetEnvironmentVariable("TLC_SMTP_HOST", "127.0.0.1")
        Environment.SetEnvironmentVariable("TLC_SMTP_PORT", Smtp.Port.ToString())
        Environment.SetEnvironmentVariable("TLC_SMTP_SECURITY", "None")
        Environment.SetEnvironmentVariable("TLC_SMTP_FROM", "noreply@trinidad.test")
        Environment.SetEnvironmentVariable("TLC_SMTP_USER", Nothing)
        ' Never use the real database/SMTP settings saved on this computer.
        AppConfig.IgnoreLocalConfigFiles = True
        AppConfig.Reload()

        DatabaseInitializer.InitializeAsync(AppConfig.Current.Database).GetAwaiter().GetResult()
        AppServices.Initialize(AppConfig.Current)
        AppServices.Instance.Setup.CreateInitialAdminAsync("Ana Lopez", "ana.admin@trinidad.test", "ana_admin", AdminPassword, AdminPassword).GetAwaiter().GetResult()
    End Sub

    ''' <summary>Runs SQL directly against the test database (for arranging edge cases such as rate-limit windows).</summary>
    Public Sub Exec(sql As String, ParamArray prms As (String, Object)())
        Dim b As New MySqlConnectionStringBuilder(AdminConnection) With {.Database = DatabaseName}
        Using conn As New MySqlConnection(b.ConnectionString)
            conn.Open()
            Using cmd As New MySqlCommand(sql, conn)
                For Each prm In prms : cmd.Parameters.AddWithValue(prm.Item1, prm.Item2) : Next
                cmd.ExecuteNonQuery()
            End Using
        End Using
    End Sub

    Public Function Scalar(sql As String, ParamArray prms As (String, Object)()) As Object
        Dim b As New MySqlConnectionStringBuilder(AdminConnection) With {.Database = DatabaseName}
        Using conn As New MySqlConnection(b.ConnectionString)
            conn.Open()
            Using cmd As New MySqlCommand(sql, conn)
                For Each prm In prms : cmd.Parameters.AddWithValue(prm.Item1, prm.Item2) : Next
                Return cmd.ExecuteScalar()
            End Using
        End Using
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        If Not Enabled Then Return
        Smtp?.Dispose()
        ' Only ever drop the database this fixture created.
        If DatabaseName IsNot Nothing AndAlso DatabaseName.StartsWith("tlc_test_", StringComparison.Ordinal) AndAlso
           Environment.GetEnvironmentVariable("TLC_KEEP_TEST_DB") <> "1" Then
            Using conn As New MySqlConnection(AdminConnection)
                conn.Open()
                Using cmd As New MySqlCommand($"DROP DATABASE IF EXISTS `{DatabaseName}`", conn)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        End If
        Environment.SetEnvironmentVariable("TLC_DB_CONNECTION", Nothing)
    End Sub
End Class

<CollectionDefinition("db")>
Public Class DbCollection
    Implements ICollectionFixture(Of DatabaseFixture)
End Class
