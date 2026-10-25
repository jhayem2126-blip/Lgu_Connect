Imports TrinidadLGUConnect.Configuration
Imports TrinidadLGUConnect.Controls
Imports TrinidadLGUConnect.Data
Imports TrinidadLGUConnect.Services

Namespace Forms

    ''' <summary>
    ''' Start screen (the branded splash from the reference) and "Main" screen that the login pages return to.
    ''' On first show it checks the database connection and schema, runs the database setup wizard when
    ''' needed, and runs the first-administrator setup if no administrator exists yet.
    ''' </summary>
    Public Class LandingForm
        Inherits Form

        Private ReadOnly _brand As New BrandPanel(BrandStyle.Splash) With {.Dock = DockStyle.Fill}
        Private ReadOnly _status As New Label With {.AutoSize = False, .ForeColor = Color.White, .BackColor = Color.Transparent, .TextAlign = ContentAlignment.MiddleCenter, .Font = Theme.F(9.5F)}
        Private ReadOnly _resident As New ModernButton("Resident Login", ButtonKind.Primary, Glyph.Contact) With {.Size = New Size(230, 44), .Visible = False}
        Private ReadOnly _admin As New ModernButton("Admin / LGU Personnel Login", ButtonKind.Outline, Glyph.Shield) With {.Size = New Size(260, 44), .Visible = False}
        Private ReadOnly _register As New LinkText("New resident? Create an account", 9.5F) With {.ForeColor = Color.White, .Visible = False}
        Private ReadOnly _skipBootstrap As Boolean

        Public Sub New(Optional skipBootstrap As Boolean = False)
            _skipBootstrap = skipBootstrap
            ApplyWindowDefaults(Me)
            Text = AppTitle
            ClientSize = New Size(1000, 640)
            MinimumSize = New Size(820, 560)
            StartPosition = FormStartPosition.CenterScreen
            Controls.Add(_brand)
            _brand.Controls.Add(_status)
            _brand.Controls.Add(_resident)
            _brand.Controls.Add(_admin)
            _brand.Controls.Add(_register)
            AddHandler _resident.Click, Sub() AppController.Current.ShowResidentLogin()
            AddHandler _admin.Click, Sub() AppController.Current.ShowAdminLogin()
            AddHandler _register.Click, Sub() AppController.Current.ShowRegistration()
            AddHandler _brand.Resize, Sub() LayoutButtons()
            LayoutButtons()
        End Sub

        Private Sub LayoutButtons()
            Dim w = _brand.ClientSize.Width, h = _brand.ClientSize.Height
            Dim y = CInt(h * 0.69)
            Dim total = _resident.Width + 16 + _admin.Width
            _resident.Location = New Point((w - total) \ 2, y)
            _admin.Location = New Point(_resident.Right + 16, y)
            _register.Location = New Point((w - _register.PreferredWidth) \ 2, y + 54)
            _status.SetBounds(0, y, w, 44)
        End Sub

        Protected Overrides Async Sub OnShown(e As EventArgs)
            MyBase.OnShown(e)
            If _skipBootstrap AndAlso AppServices.IsInitialized Then
                ShowPortals()
                Return
            End If
            If Await BootstrapAsync() Then
                ShowPortals()
            Else
                Close()
            End If
        End Sub

        Private Sub ShowPortals()
            _status.Visible = False
            _resident.Visible = True
            _admin.Visible = True
            _register.Visible = True
            _resident.Focus()
        End Sub

        ''' <summary>Returns False when the user chose to exit instead of completing setup.</summary>
        Private Async Function BootstrapAsync() As Task(Of Boolean)
            Do
                _status.Text = "Connecting to the database..."
                Dim cfg As AppConfig = Nothing
                Dim configError As String = Nothing
                Try
                    AppConfig.Reload()
                    cfg = AppConfig.Current
                Catch ex As InvalidOperationException
                    configError = ex.Message
                    cfg = New AppConfig()
                End Try
                Dim check As SchemaCheckResult
                If configError IsNot Nothing Then
                    check = New SchemaCheckResult With {.State = SchemaState.ServerUnreachable, .Message = configError}
                Else
                    check = Await Task.Run(Function() DatabaseInitializer.CheckAsync(cfg.Database))
                End If
                If check.State = SchemaState.Ready Then Exit Do
                Using dlg As New DatabaseSetupForm(cfg.Database, check)
                    If dlg.ShowDialog(Me) <> DialogResult.OK Then Return False
                End Using
            Loop

            AppServices.Initialize(AppConfig.Current)
            _status.Text = "Checking system setup..."
            Dim r = Await TryRunAsync(Me, Function() AppServices.Instance.Setup.IsSetupRequiredAsync())
            If Not r.Ok Then Return False
            If r.Value Then
                Using dlg As New FirstRunSetupForm()
                    If dlg.ShowDialog(Me) <> DialogResult.OK Then Return False
                End Using
            End If
            Return True
        End Function

    End Class

End Namespace
