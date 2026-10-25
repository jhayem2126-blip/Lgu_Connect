Imports TrinidadLGUConnect.Controls
Imports TrinidadLGUConnect.Models
Imports TrinidadLGUConnect.Services

Namespace Forms

    ''' <summary>
    ''' Shared login behaviour. Both portals call the same <see cref="AuthService.LoginAsync"/>; the only
    ''' difference is the portal argument, which the service uses to reject the wrong account type.
    ''' </summary>
    Friend Module LoginFlow

        Public Async Function LoginAsync(owner As Form, portal As LoginPortal, identifier As InputField, password As InputField, message As Label, ParamArray disable As Control()) As Task
            message.ForeColor = Theme.Danger
            message.Text = ""
            identifier.HasError = False
            password.HasError = False
            If String.IsNullOrWhiteSpace(identifier.Text) Then
                identifier.HasError = True
                message.Text = If(portal = LoginPortal.Resident, "Enter your username or email.", "Enter your username.")
                identifier.Focus()
                Return
            End If
            If String.IsNullOrEmpty(password.Text) Then
                password.HasError = True
                message.Text = "Enter your password."
                password.Focus()
                Return
            End If
            Dim id = identifier.Text
            Dim pw = password.Text
            message.ForeColor = Theme.Muted
            message.Text = "Signing in..."
            Dim r = Await TryRunAsync(owner, Function() AppServices.Instance.Auth.LoginAsync(id, pw, portal), disable, showErrors:=False)
            message.ForeColor = Theme.Danger
            If Not r.Ok Then
                message.Text = FriendlyMessage(r.Error)
                Return
            End If
            Dim result = r.Value
            If result.Success Then
                password.Text = ""
                AppController.Current.OpenShell(result.Session)
                Return
            End If
            message.Text = result.Message
            If result.RequiresEmailVerification Then
                Using dlg As New VerifyEmailDialog(result.UnverifiedUserEmail)
                    If dlg.ShowDialog(owner) = DialogResult.OK Then
                        ' Verified: sign in with the credentials just entered.
                        Await LoginAsync(owner, portal, identifier, password, message, disable)
                    End If
                End Using
            ElseIf result.WrongPortal Then
                password.Text = ""
            Else
                password.HasError = True
                password.Text = ""
                password.Focus()
            End If
        End Function

        Public Function CardHeader(title As String, subtitle As String, glyphText As String) As Control
            Dim p As New Panel With {.Height = 56, .BackColor = Color.Transparent}
            Dim icon As New Label With {.Text = glyphText, .Font = Theme.IconFont(19), .ForeColor = Theme.Navy, .BackColor = Color.FromArgb(230, 238, 250),
                                        .Size = New Size(48, 48), .TextAlign = ContentAlignment.MiddleCenter, .Location = New Point(0, 2)}
            p.Controls.Add(icon)
            Dim t = Heading(title, 14)
            t.Location = New Point(60, 4)
            p.Controls.Add(t)
            Dim s = Subtle(subtitle, 8.5F)
            s.Location = New Point(61, 31)
            p.Controls.Add(s)
            Return p
        End Function

    End Module

    ''' <summary>Resident Login (reference screen 1).</summary>
    Public Class ResidentLoginForm
        Inherits Form

        Private ReadOnly _brand As New BrandPanel(BrandStyle.SidePanel) With {.Dock = DockStyle.Left, .Width = 340}
        Private ReadOnly _right As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.FromArgb(238, 243, 250)}
        Private ReadOnly _card As New CardPanel With {.Size = New Size(430, 452)}
        Private ReadOnly _user As New InputField("Enter your username or email", Glyph.Mail)
        Private ReadOnly _pass As New InputField("Enter your password", Glyph.Lock, password:=True)
        Private ReadOnly _login As New ModernButton("Login")
        Private ReadOnly _msg As Label = MessageLabel()

        Public Sub New(Optional message As String = Nothing)
            ApplyWindowDefaults(Me)
            Text = $"{AppTitle} - Resident Login"
            ClientSize = New Size(960, 600)
            MinimumSize = New Size(860, 600)
            StartPosition = FormStartPosition.CenterScreen
            Controls.Add(_right)
            Controls.Add(_brand)
            _right.Controls.Add(_card)
            AddHandler _right.Resize, Sub() CenterCard()

            Dim s As New VStack(_card, 34, 30)
            s.Add(CardHeader("Resident Login", "Welcome back! Please login to your account.", Glyph.Contact), 56, 22)
            s.AddAuto(FieldLabel("Username or Email"), 6)
            s.Add(_user, 40, 14)
            s.AddAuto(FieldLabel("Password"), 6)
            s.Add(_pass, 40, 4)
            Dim forgot As New LinkText("Forgot password?", 8.5F)
            forgot.Location = New Point(_card.Width - 34 - forgot.PreferredWidth, s.Y)
            forgot.Anchor = AnchorStyles.Top Or AnchorStyles.Right
            _card.Controls.Add(forgot)
            s.Space(22)
            _msg.Height = 34
            s.Add(_msg, 34, 2)
            s.Add(_login, 42, 18)

            Dim reg As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .WrapContents = False, .BackColor = Color.Transparent}
            reg.Controls.Add(Subtle("Don't have an account?", 9))
            Dim regLink As New LinkText("Register here")
            reg.Controls.Add(regLink)
            reg.Location = New Point((_card.Width - reg.PreferredSize.Width) \ 2, s.Y)
            reg.Anchor = AnchorStyles.Top
            _card.Controls.Add(reg)
            Dim back As New LinkText("‹ Back to Main", 8.5F, bold:=False)
            back.Location = New Point((_card.Width - back.PreferredWidth) \ 2, s.Y + 30)
            back.Anchor = AnchorStyles.Top
            _card.Controls.Add(back)

            If Not String.IsNullOrEmpty(message) Then
                _msg.ForeColor = Theme.Muted
                _msg.Text = message
            End If

            AcceptButton = _login
            AddHandler _login.Click, Async Sub() Await LoginAsync(Me, LoginPortal.Resident, _user, _pass, _msg, _login)
            AddHandler regLink.Click, Sub() AppController.Current.ShowRegistration()
            AddHandler back.Click, Sub() AppController.Current.ShowLanding()
            AddHandler forgot.Click, Sub()
                                         Using dlg As New ForgotPasswordDialog()
                                             If dlg.ShowDialog(Me) = DialogResult.OK Then
                                                 _msg.ForeColor = Theme.Success
                                                 _msg.Text = "Password updated. Log in with your new password."
                                                 _pass.Text = ""
                                                 _pass.Focus()
                                             End If
                                         End Using
                                     End Sub
        End Sub

        Private Sub CenterCard()
            _card.Location = New Point(Math.Max(10, (_right.ClientSize.Width - _card.Width) \ 2), Math.Max(10, (_right.ClientSize.Height - _card.Height) \ 2))
        End Sub

        Protected Overrides Sub OnLoad(e As EventArgs)
            MyBase.OnLoad(e)
            CenterCard()
        End Sub

        Protected Overrides Sub OnShown(e As EventArgs)
            MyBase.OnShown(e)
            _user.Focus()
        End Sub
    End Class

    ''' <summary>Admin / LGU Personnel Login (reference admin screen 1).</summary>
    Public Class AdminLoginForm
        Inherits Form

        Private ReadOnly _brand As New BrandPanel(BrandStyle.FullBackground) With {.Dock = DockStyle.Fill}
        Private ReadOnly _card As New CardPanel With {.Size = New Size(400, 470), .FillColor = Color.FromArgb(250, 252, 255), .BackColor = Color.FromArgb(250, 252, 255)}
        Private ReadOnly _user As New InputField("Enter your username")
        Private ReadOnly _pass As New InputField("Enter your password", password:=True)
        Private ReadOnly _login As New ModernButton("Login")
        Private ReadOnly _back As New ModernButton("Back to Main", ButtonKind.Secondary)
        Private ReadOnly _msg As Label = MessageLabel()

        Public Sub New(Optional message As String = Nothing)
            ApplyWindowDefaults(Me)
            Text = $"{AppTitle} - Login"
            ClientSize = New Size(1000, 620)
            MinimumSize = New Size(900, 620)
            StartPosition = FormStartPosition.CenterScreen
            Controls.Add(_brand)
            _brand.Controls.Add(_card)
            AddHandler _brand.Resize, AddressOf PlaceCard

            Dim title = Heading("Admin / LGU Personnel Login", 12.5F)
            title.AutoSize = False
            title.TextAlign = ContentAlignment.MiddleCenter
            Dim sub1 = Subtle("Enter your credentials to access the system.", 8.75F)
            sub1.AutoSize = False
            sub1.TextAlign = ContentAlignment.MiddleCenter
            Dim s As New VStack(_card, 30, 30)
            s.Add(title, 28, 4)
            s.Add(sub1, 20, 24)

            ' Icon + label/field rows as in the reference.
            For Each item In {(Glyph.Contact, "Username", CType(_user, Control)), (Glyph.Lock, "Password", CType(_pass, Control))}
                Dim row As New Panel With {.Height = 66, .BackColor = Color.Transparent}
                Dim icon As New Label With {.Text = item.Item1, .Font = Theme.IconFont(17), .ForeColor = Theme.Navy, .Size = New Size(34, 40), .Location = New Point(0, 22), .TextAlign = ContentAlignment.MiddleCenter, .BackColor = Color.Transparent}
                Dim lbl = FieldLabel(item.Item2)
                lbl.Location = New Point(44, 0)
                item.Item3.Location = New Point(44, 22)
                item.Item3.Height = 40
                item.Item3.Width = 296
                item.Item3.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
                row.Controls.Add(icon)
                row.Controls.Add(lbl)
                row.Controls.Add(item.Item3)
                s.Add(row, 66, 12)
                item.Item3.Width = row.Width - 44
            Next
            Dim forgot As New LinkText("Forgot password?", 8.5F)
            forgot.Location = New Point(_card.Width - 30 - forgot.PreferredWidth, s.Y - 8)
            forgot.Anchor = AnchorStyles.Top Or AnchorStyles.Right
            _card.Controls.Add(forgot)
            s.Space(10)
            _msg.Height = 34
            s.Add(_msg, 34, 2)
            s.Add(_login, 42, 10)
            Dim orLine As New OrDivider()
            s.Add(orLine, 20, 10)
            s.Add(_back, 40, 0)

            If Not String.IsNullOrEmpty(message) Then
                _msg.ForeColor = Theme.Muted
                _msg.Text = message
            End If

            AcceptButton = _login
            AddHandler _login.Click, Async Sub() Await LoginAsync(Me, LoginPortal.Personnel, _user, _pass, _msg, _login, _back)
            AddHandler _back.Click, Sub() AppController.Current.ShowLanding()
            AddHandler forgot.Click, Sub()
                                         Using dlg As New ForgotPasswordDialog()
                                             If dlg.ShowDialog(Me) = DialogResult.OK Then
                                                 _msg.ForeColor = Theme.Success
                                                 _msg.Text = "Password updated. Log in with your new password."
                                                 _pass.Text = ""
                                             End If
                                         End Using
                                     End Sub
            PlaceCard(Nothing, EventArgs.Empty)
        End Sub

        Private Sub PlaceCard(sender As Object, e As EventArgs)
            Dim w = _brand.ClientSize.Width
            _brand.TextAreaWidth = CInt(w * 0.5)
            _card.Location = New Point(Math.Max(w \ 2 + 20, w - _card.Width - CInt(w * 0.06)), Math.Max(12, (_brand.ClientSize.Height - _card.Height) \ 2))
            _brand.Invalidate()
        End Sub

        Protected Overrides Sub OnShown(e As EventArgs)
            MyBase.OnShown(e)
            _user.Focus()
        End Sub

        ''' <summary>"——— OR ———" separator.</summary>
        Private Class OrDivider
            Inherits Control
            Public Sub New()
                SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.UserPaint Or ControlStyles.ResizeRedraw, True)
            End Sub
            Protected Overrides Sub OnPaint(e As PaintEventArgs)
                Theme.PaintParent(Me, e.Graphics)
                Dim mid = Height \ 2
                Using pen As New Pen(Theme.CardBorder)
                    e.Graphics.DrawLine(pen, 0, mid, Width \ 2 - 22, mid)
                    e.Graphics.DrawLine(pen, Width \ 2 + 22, mid, Width, mid)
                End Using
                TextRenderer.DrawText(e.Graphics, "OR", Theme.F(8.5F), ClientRectangle, Theme.Muted, TextFormatFlags.NoPrefix Or TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
            End Sub
        End Class
    End Class

    ''' <summary>Resident Registration (reference screen 2).</summary>
    Public Class RegistrationForm
        Inherits Form

        Private ReadOnly _brand As New BrandPanel(BrandStyle.SidePanel) With {.Dock = DockStyle.Left, .Width = 330, .Photo = Assets.CoastPhoto}
        Private ReadOnly _right As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.FromArgb(238, 243, 250), .AutoScroll = True}
        Private ReadOnly _card As New CardPanel With {.Size = New Size(540, 600)}
        Private ReadOnly _name As New InputField("Enter your full name")
        Private ReadOnly _email As New InputField("Enter your email address", Glyph.Mail)
        Private ReadOnly _username As New InputField("Choose a username")
        Private ReadOnly _barangay As New SelectField(editable:=True)
        Private ReadOnly _pass As New InputField("Create a password", Glyph.Lock, password:=True)
        Private ReadOnly _confirm As New InputField("Confirm your password", Glyph.Lock, password:=True)
        Private ReadOnly _register As New ModernButton("Register")
        Private ReadOnly _cancel As New ModernButton("Cancel", ButtonKind.Outline)
        Private ReadOnly _msg As Label = MessageLabel()
        Private ReadOnly _fields As Dictionary(Of String, FieldBase)

        Public Sub New()
            ApplyWindowDefaults(Me)
            Text = $"{AppTitle} - Resident Registration"
            ClientSize = New Size(1000, 700)
            MinimumSize = New Size(900, 600)
            StartPosition = FormStartPosition.CenterScreen
            Controls.Add(_right)
            Controls.Add(_brand)
            _right.Controls.Add(_card)
            AddHandler _right.Resize, Sub() CenterCard()
            _fields = New Dictionary(Of String, FieldBase) From {
                {"FullName", _name}, {"Email", _email}, {"Username", _username}, {"Barangay", _barangay}, {"Password", _pass}, {"ConfirmPassword", _confirm}}
            _name.MaxLength = 120
            _email.MaxLength = 254
            _username.MaxLength = 30
            _pass.MaxLength = 128
            _confirm.MaxLength = 128

            Dim s As New VStack(_card, 30, 24)
            s.Add(CardHeader("Create Your Account", "Join the community and help make Trinidad better!", Glyph.Contact), 56, 14)
            s.AddAuto(FieldLabel("Full Name"), 5)
            s.Add(_name, 38, 10)
            s.AddAuto(FieldLabel("Email Address"), 5)
            s.Add(_email, 38, 10)
            s.AddRow(18, 16, 5, CType(FieldLabel("Username"), Control), FieldLabel("Barangay"))
            s.AddRow(38, 16, 10, _username, _barangay)
            s.AddAuto(FieldLabel("Password"), 5)
            s.Add(_pass, 38, 3)
            s.AddAuto(Subtle("At least 8 characters with upper- and lower-case letters, a number and a symbol.", 7.75F), 8)
            s.AddAuto(FieldLabel("Confirm Password"), 5)
            s.Add(_confirm, 38, 6)
            _msg.Height = 32
            s.Add(_msg, 32, 4)
            s.AddRow(42, 14, 14, _register, _cancel)
            Dim login As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .WrapContents = False, .BackColor = Color.Transparent}
            login.Controls.Add(Subtle("Already have an account?", 9))
            Dim loginLink As New LinkText("Login here")
            login.Controls.Add(loginLink)
            login.Location = New Point((_card.Width - login.PreferredSize.Width) \ 2, s.Y)
            _card.Controls.Add(login)
            _card.Height = s.Y + 44

            AcceptButton = _register
            AddHandler _register.Click, AddressOf OnRegister
            AddHandler _cancel.Click, Sub() AppController.Current.ShowResidentLogin()
            AddHandler loginLink.Click, Sub() AppController.Current.ShowResidentLogin()
        End Sub

        Private Async Sub OnRegister(sender As Object, e As EventArgs)
            _msg.ForeColor = Theme.Danger
            _msg.Text = ""
            If _barangay.HasUnmatchedText Then
                _msg.Text = $"'{_barangay.Combo.Text.Trim()}' is not a barangay of Trinidad. Pick one from the list."
                _barangay.HasError = True
                Return
            End If
            Dim req As New RegistrationRequest With {.FullName = _name.Text, .Email = _email.Text, .Username = _username.Text, .Password = _pass.Text,
                                                     .ConfirmPassword = _confirm.Text, .BarangayId = SelectedInt(_barangay)}
            Dim r = Await TryRunAsync(Me, Function() AppServices.Instance.Auth.RegisterAsync(req), {_register, _cancel}, showErrors:=False)
            If Not r.Ok Then
                _msg.Text = FriendlyMessage(r.Error)
                MarkField(_fields, r.Error)
                Return
            End If
            Dim email = req.Email.Trim()
            If Not r.Value.RequiresVerification Then
                Info(Me, "Your account has been created. You can now log in.", "Registration complete")
                AppController.Current.ShowResidentLogin("Account created. Please log in.")
                Return
            End If
            If Not r.Value.VerificationEmailSent Then
                Warn(Me, "Your account has been created, but the verification email could not be sent:" & vbLf & vbLf & r.Value.EmailError &
                         vbLf & vbLf & "You can request a new code when you log in, or ask the LGU office to verify your account.")
                AppController.Current.ShowResidentLogin("Account created. Verify your email to log in.")
                Return
            End If
            Using dlg As New VerifyEmailDialog(email, justSent:=True)
                If dlg.ShowDialog(Me) = DialogResult.OK Then
                    AppController.Current.ShowResidentLogin("Email verified. You can now log in.")
                Else
                    AppController.Current.ShowResidentLogin("Account created. Verify your email to log in.")
                End If
            End Using
        End Sub

        Private Sub CenterCard()
            _card.Location = New Point(Math.Max(10, (_right.ClientSize.Width - _card.Width) \ 2), Math.Max(10, (_right.ClientSize.Height - _card.Height) \ 2))
        End Sub

        Protected Overrides Sub OnLoad(e As EventArgs)
            MyBase.OnLoad(e)
            CenterCard()
        End Sub

        Protected Overrides Async Sub OnShown(e As EventArgs)
            MyBase.OnShown(e)
            _name.Focus()
            Dim r = Await TryRunAsync(Me, Function() AppServices.Instance.Concerns.GetBarangaysAsync())
            If r.Ok AndAlso Not IsDisposed Then
                Fill(_barangay, BarangayOptions(r.Value, Nothing), Nothing)
                _barangay.Combo.SelectedIndex = -1
            End If
        End Sub
    End Class

End Namespace
