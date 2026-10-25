Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports System.Text.Json.Nodes
Imports MySql.Data.MySqlClient

Namespace Configuration

    Public Class DatabaseOptions
        Public Property Server As String = "localhost"
        Public Property Port As Integer = 3306
        Public Property Database As String = "trinidad_lgu_connect"
        Public Property UserId As String = ""
        Public Property Password As String = ""
        Public Property SslMode As String = "Preferred"

        ''' <summary>Full connection string override (environment variable TLC_DB_CONNECTION).</summary>
        Public Property ConnectionStringOverride As String = ""

        Public ReadOnly Property IsConfigured As Boolean
            Get
                Return Not String.IsNullOrWhiteSpace(ConnectionStringOverride) OrElse
                    (Not String.IsNullOrWhiteSpace(Server) AndAlso Not String.IsNullOrWhiteSpace(UserId) AndAlso Not String.IsNullOrWhiteSpace(Database))
            End Get
        End Property

        Public Function BuildConnectionString(Optional includeDatabase As Boolean = True) As String
            Dim b As MySqlConnectionStringBuilder
            If Not String.IsNullOrWhiteSpace(ConnectionStringOverride) Then
                b = New MySqlConnectionStringBuilder(ConnectionStringOverride)
            Else
                b = New MySqlConnectionStringBuilder With {
                    .Server = Server,
                    .Port = CUInt(Port),
                    .UserID = UserId,
                    .Password = Password,
                    .Database = Database
                }
                Dim mode As MySqlSslMode
                If [Enum].TryParse(SslMode, True, mode) Then b.SslMode = mode
            End If
            If Not includeDatabase Then b.Database = ""
            b.CharacterSet = "utf8mb4"
            b.AllowUserVariables = False
            b.ConnectionTimeout = 10
            b.DefaultCommandTimeout = 60
            b.Pooling = True
            Return b.ConnectionString
        End Function

        Public ReadOnly Property DatabaseName As String
            Get
                If Not String.IsNullOrWhiteSpace(ConnectionStringOverride) Then
                    Return New MySqlConnectionStringBuilder(ConnectionStringOverride).Database
                End If
                Return Database
            End Get
        End Property

        Public Function Clone() As DatabaseOptions
            Return DirectCast(MemberwiseClone(), DatabaseOptions)
        End Function
    End Class

    Public Class SmtpOptions
        Public Property Host As String = ""
        Public Property Port As Integer = 587
        ''' <summary>None, StartTls, SslOnConnect or Auto.</summary>
        Public Property Security As String = "StartTls"
        Public Property UserName As String = ""
        Public Property Password As String = ""
        Public Property FromAddress As String = ""
        Public Property FromName As String = "Trinidad LGU Connect"
        Public Property TimeoutSeconds As Integer = 30

        Public ReadOnly Property IsConfigured As Boolean
            Get
                Return Not String.IsNullOrWhiteSpace(Host) AndAlso Port > 0 AndAlso Not String.IsNullOrWhiteSpace(FromAddress)
            End Get
        End Property

        Public Function Clone() As SmtpOptions
            Return DirectCast(MemberwiseClone(), SmtpOptions)
        End Function
    End Class

    ''' <summary>
    ''' Loads configuration in this order (later sources override earlier ones):
    '''   1. appsettings.json next to the executable (no secrets, committed)
    '''   2. appsettings.local.json next to the executable (developer machine, git-ignored)
    '''   3. %ProgramData%\TrinidadLGUConnect\appsettings.local.json (written by the setup wizard)
    '''   4. Environment variables TLC_DB_CONNECTION, TLC_SMTP_HOST, TLC_SMTP_PORT, TLC_SMTP_USER,
    '''      TLC_SMTP_PASSWORD, TLC_SMTP_FROM, TLC_SMTP_SECURITY
    ''' Passwords written by the application are encrypted with Windows DPAPI (machine scope) and stored
    ''' as "PasswordProtected"; plain "Password" values are accepted for manual configuration.
    ''' </summary>
    Public NotInheritable Class AppConfig

        Private Shared ReadOnly Entropy As Byte() = Encoding.UTF8.GetBytes("TrinidadLGUConnect.v1")
        Private Shared _current As AppConfig

        Public Property Database As New DatabaseOptions()
        Public Property Smtp As New SmtpOptions()
        Public Property ImagesDirectory As String = Path.Combine(AppContext.BaseDirectory, "Resources", "Images")

        Public Shared ReadOnly Property MachineConfigDirectory As String
            Get
                Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TrinidadLGUConnect")
            End Get
        End Property

        Public Shared ReadOnly Property MachineConfigPath As String
            Get
                Return Path.Combine(MachineConfigDirectory, "appsettings.local.json")
            End Get
        End Property

        Public Shared ReadOnly Property Current As AppConfig
            Get
                If _current Is Nothing Then _current = Load()
                Return _current
            End Get
        End Property

        Public Shared Sub Reload()
            _current = Load()
        End Sub

        ''' <summary>
        ''' Set by the automated tests so they never read (or send email through) the real configuration
        ''' saved on this computer. The application itself never sets it.
        ''' </summary>
        Friend Shared Property IgnoreLocalConfigFiles As Boolean

        Public Shared Function Load() As AppConfig
            Dim cfg As New AppConfig()
            Dim baseDir = AppContext.BaseDirectory
            ApplyFile(cfg, Path.Combine(baseDir, "appsettings.json"))
            If Not IgnoreLocalConfigFiles Then
                ApplyFile(cfg, Path.Combine(baseDir, "appsettings.local.json"))
                ApplyFile(cfg, MachineConfigPath)
            End If
            ApplyEnvironment(cfg)
            Return cfg
        End Function

        Private Shared Sub ApplyFile(cfg As AppConfig, filePath As String)
            If Not File.Exists(filePath) Then Return
            Dim root As JsonNode
            Try
                root = JsonNode.Parse(File.ReadAllText(filePath), Nothing, New JsonDocumentOptions With {.CommentHandling = JsonCommentHandling.Skip, .AllowTrailingCommas = True})
            Catch ex As JsonException
                Throw New InvalidOperationException($"The configuration file '{filePath}' is not valid JSON: {ex.Message}")
            End Try
            If root Is Nothing Then Return

            Dim db = root("Database")
            If db IsNot Nothing Then
                cfg.Database.Server = Str(db, "Server", cfg.Database.Server)
                cfg.Database.Port = Int(db, "Port", cfg.Database.Port)
                cfg.Database.Database = Str(db, "Database", cfg.Database.Database)
                cfg.Database.UserId = Str(db, "UserId", cfg.Database.UserId)
                cfg.Database.SslMode = Str(db, "SslMode", cfg.Database.SslMode)
                cfg.Database.Password = Secret(db, cfg.Database.Password)
            End If

            Dim smtp = root("Smtp")
            If smtp IsNot Nothing Then
                cfg.Smtp.Host = Str(smtp, "Host", cfg.Smtp.Host)
                cfg.Smtp.Port = Int(smtp, "Port", cfg.Smtp.Port)
                cfg.Smtp.Security = Str(smtp, "Security", cfg.Smtp.Security)
                cfg.Smtp.UserName = Str(smtp, "UserName", cfg.Smtp.UserName)
                cfg.Smtp.FromAddress = Str(smtp, "FromAddress", cfg.Smtp.FromAddress)
                cfg.Smtp.FromName = Str(smtp, "FromName", cfg.Smtp.FromName)
                cfg.Smtp.TimeoutSeconds = Int(smtp, "TimeoutSeconds", cfg.Smtp.TimeoutSeconds)
                cfg.Smtp.Password = Secret(smtp, cfg.Smtp.Password)
            End If
        End Sub

        Private Shared Sub ApplyEnvironment(cfg As AppConfig)
            Dim env = Function(name As String) Environment.GetEnvironmentVariable(name)
            If Not String.IsNullOrWhiteSpace(env("TLC_DB_CONNECTION")) Then cfg.Database.ConnectionStringOverride = env("TLC_DB_CONNECTION")
            If Not String.IsNullOrWhiteSpace(env("TLC_SMTP_HOST")) Then cfg.Smtp.Host = env("TLC_SMTP_HOST")
            Dim port As Integer
            If Integer.TryParse(env("TLC_SMTP_PORT"), port) Then cfg.Smtp.Port = port
            If Not String.IsNullOrWhiteSpace(env("TLC_SMTP_USER")) Then cfg.Smtp.UserName = env("TLC_SMTP_USER")
            If Not String.IsNullOrWhiteSpace(env("TLC_SMTP_PASSWORD")) Then cfg.Smtp.Password = env("TLC_SMTP_PASSWORD")
            If Not String.IsNullOrWhiteSpace(env("TLC_SMTP_FROM")) Then cfg.Smtp.FromAddress = env("TLC_SMTP_FROM")
            If Not String.IsNullOrWhiteSpace(env("TLC_SMTP_SECURITY")) Then cfg.Smtp.Security = env("TLC_SMTP_SECURITY")
        End Sub

        Private Shared Function Str(node As JsonNode, key As String, fallback As String) As String
            Dim v = node(key)
            If v Is Nothing Then Return fallback
            Return v.GetValue(Of String)()
        End Function

        Private Shared Function Int(node As JsonNode, key As String, fallback As Integer) As Integer
            Dim v = node(key)
            If v Is Nothing Then Return fallback
            Dim n As Integer
            If v.GetValueKind() = JsonValueKind.Number Then Return v.GetValue(Of Integer)()
            If Integer.TryParse(v.ToString(), n) Then Return n
            Return fallback
        End Function

        Private Shared Function Secret(node As JsonNode, fallback As String) As String
            Dim prot = node("PasswordProtected")
            If prot IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(prot.GetValue(Of String)()) Then
                Return Unprotect(prot.GetValue(Of String)())
            End If
            Return Str(node, "Password", fallback)
        End Function

        Public Shared Function Protect(plain As String) As String
            If String.IsNullOrEmpty(plain) Then Return ""
            Dim bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.LocalMachine)
            Return Convert.ToBase64String(bytes)
        End Function

        Public Shared Function Unprotect(cipher As String) As String
            Try
                Dim bytes = ProtectedData.Unprotect(Convert.FromBase64String(cipher), Entropy, DataProtectionScope.LocalMachine)
                Return Encoding.UTF8.GetString(bytes)
            Catch ex As Exception When TypeOf ex Is CryptographicException OrElse TypeOf ex Is FormatException
                Throw New InvalidOperationException("A protected password in the local configuration could not be decrypted on this computer. Re-enter it in the setup or settings screen.", ex)
            End Try
        End Function

        ''' <summary>Saves database settings to the machine configuration file (password encrypted with DPAPI).</summary>
        Public Shared Sub SaveDatabase(options As DatabaseOptions)
            Update(Sub(root)
                       Dim db As New JsonObject From {
                           {"Server", options.Server},
                           {"Port", options.Port},
                           {"Database", options.Database},
                           {"UserId", options.UserId},
                           {"SslMode", options.SslMode},
                           {"PasswordProtected", Protect(options.Password)}
                       }
                       root("Database") = db
                   End Sub)
        End Sub

        ''' <summary>Saves SMTP settings. When <paramref name="newPassword"/> is Nothing the stored password is kept.</summary>
        Public Shared Sub SaveSmtp(options As SmtpOptions, newPassword As String)
            Update(Sub(root)
                       Dim existing = TryCast(root("Smtp"), JsonObject)
                       Dim keptProtected As String = Nothing
                       If existing IsNot Nothing AndAlso existing("PasswordProtected") IsNot Nothing Then keptProtected = existing("PasswordProtected").GetValue(Of String)()
                       Dim smtp As New JsonObject From {
                           {"Host", options.Host},
                           {"Port", options.Port},
                           {"Security", options.Security},
                           {"UserName", options.UserName},
                           {"FromAddress", options.FromAddress},
                           {"FromName", options.FromName},
                           {"TimeoutSeconds", options.TimeoutSeconds}
                       }
                       If newPassword IsNot Nothing Then
                           smtp("PasswordProtected") = Protect(newPassword)
                       ElseIf keptProtected IsNot Nothing Then
                           smtp("PasswordProtected") = keptProtected
                       End If
                       root("Smtp") = smtp
                   End Sub)
        End Sub

        Private Shared Sub Update(mutate As Action(Of JsonObject))
            Directory.CreateDirectory(MachineConfigDirectory)
            Dim root As JsonObject = Nothing
            If File.Exists(MachineConfigPath) Then
                root = TryCast(JsonNode.Parse(File.ReadAllText(MachineConfigPath)), JsonObject)
            End If
            If root Is Nothing Then root = New JsonObject()
            mutate(root)
            Dim tmp = MachineConfigPath & ".tmp"
            File.WriteAllText(tmp, root.ToJsonString(New JsonSerializerOptions With {.WriteIndented = True}))
            File.Move(tmp, MachineConfigPath, True)
            Reload()
        End Sub

    End Class

End Namespace
