Namespace Services

    ''' <summary>
    ''' Minimal local diagnostic log (%LocalAppData%\TrinidadLGUConnect\logs). Used for technical
    ''' details that must not be shown to ordinary users. Never pass passwords, codes or tokens here.
    ''' Security-relevant business events go to the audit_logs table through AuditService.
    ''' </summary>
    Public Module AppLog

        Private ReadOnly Gate As New Object()

        Public ReadOnly Property LogDirectory As String
            Get
                Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrinidadLGUConnect", "logs")
            End Get
        End Property

        Public Sub Info(message As String)
            Write("INFO", message, Nothing)
        End Sub

        Public Sub Warn(message As String, Optional ex As Exception = Nothing)
            Write("WARN", message, ex)
        End Sub

        Public Sub [Error](message As String, Optional ex As Exception = Nothing)
            Write("ERROR", message, ex)
        End Sub

        Private Sub Write(level As String, message As String, ex As Exception)
            Try
                SyncLock Gate
                    Directory.CreateDirectory(LogDirectory)
                    Dim file = Path.Combine(LogDirectory, $"app-{DateTime.Now:yyyyMMdd}.log")
                    Dim line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}"
                    If ex IsNot Nothing Then line &= Environment.NewLine & ex.ToString()
                    IO.File.AppendAllText(file, line & Environment.NewLine)
                End SyncLock
            Catch
                ' Logging must never crash the application.
            End Try
        End Sub

    End Module

End Namespace
