Imports TrinidadLGUConnect.Configuration
Imports TrinidadLGUConnect.Data
Imports TrinidadLGUConnect.Models

Namespace Services

    ''' <summary>Composition root: creates the services once the database connection is known.</summary>
    Public Class AppServices

        Private Shared _instance As AppServices

        Public ReadOnly Property Config As AppConfig
        Public ReadOnly Property Db As Db
        Public ReadOnly Property Settings As SettingsService
        Public ReadOnly Property Authorization As AuthorizationService
        Public ReadOnly Property Email As EmailService
        Public ReadOnly Property Auth As AuthService
        Public ReadOnly Property Concerns As ConcernService
        Public ReadOnly Property Users As UserService
        Public ReadOnly Property Reports As ReportingService
        Public ReadOnly Property Setup As SetupService

        ''' <summary>The signed-in user's session handle (Nothing when signed out).</summary>
        Public Property Session As UserSession

        Public Sub New(config As AppConfig)
            Me.Config = config
            Db = New Db(config.Database.BuildConnectionString())
            Settings = New SettingsService(Db)
            Authorization = New AuthorizationService(Settings)
            Email = New EmailService(Function() AppConfig.Current.Smtp)
            Auth = New AuthService(Db, Settings, Email, Authorization)
            Concerns = New ConcernService(Db, Authorization, Settings, Email)
            Users = New UserService(Db, Authorization)
            Reports = New ReportingService(Db, Authorization)
            Setup = New SetupService(Db)
        End Sub

        Public Shared ReadOnly Property Instance As AppServices
            Get
                If _instance Is Nothing Then Throw New InvalidOperationException("Services have not been initialised.")
                Return _instance
            End Get
        End Property

        Public Shared ReadOnly Property IsInitialized As Boolean
            Get
                Return _instance IsNot Nothing
            End Get
        End Property

        Public Shared Sub Initialize(config As AppConfig)
            _instance = New AppServices(config)
        End Sub

    End Class

End Namespace
