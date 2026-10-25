Imports TrinidadLGUConnect.Configuration
Imports TrinidadLGUConnect.Controls
Imports TrinidadLGUConnect.Data

Namespace Forms

    ''' <summary>
    ''' Database connection wizard shown at startup when MySQL is unreachable or the schema is missing.
    ''' Tests the connection, can create the database and apply the (non-destructive) schema script,
    ''' and saves the settings with the password encrypted by Windows DPAPI.
    ''' </summary>
    Public Class DatabaseSetupForm
        Inherits DialogBase

        Private ReadOnly _server As New InputField("localhost")
        Private ReadOnly _port As New InputField("3306")
        Private ReadOnly _database As New InputField("trinidad_lgu_connect")
        Private ReadOnly _user As New InputField("MySQL user name")
        Private ReadOnly _pass As New InputField("MySQL password", password:=True)
        Private ReadOnly _ssl As New SelectField()
        Private ReadOnly _status As New Label With {.AutoSize = False, .Font = Theme.F(9), .ForeColor = Theme.Ink, .BackColor = Color.FromArgb(245, 247, 251), .Padding = New Padding(10, 8, 10, 8)}
        Private ReadOnly _test As New ModernButton("Test Connection", ButtonKind.Outline, Glyph.Refresh)
        Private ReadOnly _init As New ModernButton("Create / Update Schema", ButtonKind.Secondary, Glyph.Database)
        Private ReadOnly _save As New ModernButton("Save & Continue", ButtonKind.Primary, Glyph.CheckMark)
        Private _lastState As SchemaState = SchemaState.ServerUnreachable

        Public Sub New(current As DatabaseOptions, check As SchemaCheckResult)
            MyBase.New("Database Setup", 600, 640)
            _server.Text = current.Server
            _port.Text = current.Port.ToString()
            _database.Text = current.Database
            _user.Text = current.UserId
            _pass.Text = current.Password
            For Each m In {"Preferred", "Required", "None"}
                _ssl.Items.Add(m)
            Next
            _ssl.SelectedItem = If({"Preferred", "Required", "None"}.Contains(current.SslMode), current.SslMode, "Preferred")

            Dim s As New VStack(Body, 32, 24)
            s.Add(DialogHeader("Database Connection", "Connect Trinidad LGU Connect to its MySQL database.", Glyph.Database), 56, 14)
            If Not String.IsNullOrWhiteSpace(current.ConnectionStringOverride) Then
                Dim envNote = Subtle("Note: the TLC_DB_CONNECTION environment variable is set and overrides the values saved here.", 8.5F)
                envNote.ForeColor = Theme.Danger
                envNote.MaximumSize = New Size(s.Width, 0)
                s.AddAuto(envNote, 8)
            End If
            s.AddAuto(FieldLabel("Server"), 4)
            s.Add(_server, 38, 8)
            Dim portLbl = FieldLabel("Port")
            Dim sslLbl = FieldLabel("SSL Mode")
            s.AddRow(18, 16, 4, portLbl, sslLbl)
            s.AddRow(38, 16, 8, _port, _ssl)
            s.AddAuto(FieldLabel("Database Name"), 4)
            s.Add(_database, 38, 8)
            s.AddAuto(FieldLabel("User Name"), 4)
            s.Add(_user, 38, 8)
            s.AddAuto(FieldLabel("Password"), 4)
            s.Add(_pass, 38, 12)
            s.Add(_status, 78, 12)
            s.AddRow(40, 10, 10, _test, _init)
            Dim exitBtn As New ModernButton("Exit", ButtonKind.Outline)
            s.AddRow(42, 10, 0, _save, exitBtn)
            ShowCheck(check)

            AddHandler _test.Click, Async Sub() Await TestAsync()
            AddHandler _init.Click, Async Sub() Await InitializeAsync()
            AddHandler _save.Click, AddressOf OnSave
            AddHandler exitBtn.Click, Sub() Close()
        End Sub

        Private Function ReadOptions() As DatabaseOptions
            Dim port As Integer
            If Not Integer.TryParse(_port.Text.Trim(), port) OrElse port < 1 OrElse port > 65535 Then
                Throw New Helpers.ValidationException("Enter a valid port number (1-65535).")
            End If
            If String.IsNullOrWhiteSpace(_server.Text) OrElse String.IsNullOrWhiteSpace(_user.Text) Then
                Throw New Helpers.ValidationException("Enter the server and the MySQL user name.")
            End If
            Return New DatabaseOptions With {
                .Server = _server.Text.Trim(), .Port = port, .Database = DatabaseInitializer.EnsureSafeName(_database.Text.Trim()),
                .UserId = _user.Text.Trim(), .Password = _pass.Text, .SslMode = CStr(_ssl.SelectedItem)}
        End Function

        Private Sub ShowCheck(check As SchemaCheckResult)
            _lastState = check.State
            Dim ok = check.State = SchemaState.Ready
            _status.ForeColor = If(ok, Theme.Success, If(check.State = SchemaState.ServerUnreachable, Theme.Danger, Color.FromArgb(180, 100, 0)))
            Dim version = If(String.IsNullOrEmpty(check.ServerVersion), "", $"MySQL server {check.ServerVersion}. ")
            Dim hint = ""
            Select Case check.State
                Case SchemaState.DatabaseMissing, SchemaState.SchemaMissing, SchemaState.SchemaOutdated
                    hint = " Click 'Create / Update Schema' to set it up (existing data is never deleted)."
                Case SchemaState.Ready
                    hint = " Click 'Save & Continue'."
            End Select
            _status.Text = version & check.Message & hint
            _save.Enabled = ok
            _init.Enabled = check.State <> SchemaState.ServerUnreachable AndAlso Not ok
        End Sub

        Private Async Function TestAsync() As Task
            Dim opts As DatabaseOptions
            Try
                opts = ReadOptions()
            Catch ex As Helpers.ValidationException
                _status.ForeColor = Theme.Danger
                _status.Text = ex.Message
                Return
            End Try
            _status.ForeColor = Theme.Muted
            _status.Text = "Testing connection..."
            Dim r = Await TryRunAsync(Me, Function() DatabaseInitializer.CheckAsync(opts), {_test, _init, _save})
            If r.Ok Then ShowCheck(r.Value)
        End Function

        Private Async Function InitializeAsync() As Task
            Dim opts As DatabaseOptions
            Try
                opts = ReadOptions()
            Catch ex As Helpers.ValidationException
                _status.ForeColor = Theme.Danger
                _status.Text = ex.Message
                Return
            End Try
            If Not Confirm(Me, $"Create the database '{opts.Database}' if it does not exist and create any missing tables and reference data?" & vbLf & vbLf &
                           "Existing tables and data are never dropped or overwritten. The MySQL user needs CREATE privileges for this step.", "Create / Update Schema") Then Return
            _status.ForeColor = Theme.Muted
            _status.Text = "Creating schema..."
            Dim ok = Await TryRunAsync(Me, Function() DatabaseInitializer.InitializeAsync(opts), {_test, _init, _save})
            Dim r = Await TryRunAsync(Me, Function() DatabaseInitializer.CheckAsync(opts), {_test, _init, _save})
            If r.Ok Then ShowCheck(r.Value)
        End Function

        Private Sub OnSave(sender As Object, e As EventArgs)
            If _lastState <> SchemaState.Ready Then Return
            Try
                AppConfig.SaveDatabase(ReadOptions())
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException OrElse TypeOf ex Is Helpers.ValidationException
                ShowError(Me, New Helpers.ValidationException("The settings could not be saved: " & ex.Message))
                Return
            End Try
            DialogResult = DialogResult.OK
            Close()
        End Sub

    End Class

End Namespace
