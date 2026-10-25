Imports TrinidadLGUConnect.Controls
Imports TrinidadLGUConnect.Helpers
Imports TrinidadLGUConnect.Services

Namespace Forms

    ''' <summary>Common look for modal dialogs: white body, title row, fixed size.</summary>
    Public Class DialogBase
        Inherits Form

        Protected ReadOnly Body As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.White}

        Public Sub New(title As String, width As Integer, height As Integer)
            ApplyWindowDefaults(Me)
            Text = title
            BackColor = Color.White
            FormBorderStyle = FormBorderStyle.FixedDialog
            MaximizeBox = False
            MinimizeBox = False
            ShowInTaskbar = False
            StartPosition = FormStartPosition.CenterParent
            ClientSize = New Size(width, height)
            Controls.Add(Body)
        End Sub

        Protected Shared Function DialogHeader(title As String, subtitle As String, glyphText As String) As Control
            Return LoginFlow.CardHeader(title, subtitle, glyphText)
        End Function
    End Class

    ''' <summary>
    ''' Password recovery: (1) email → a single-use code is emailed, (2) enter the code → it is verified,
    ''' (3) choose a new password → it is hashed and saved, the code is consumed and other sessions are signed out.
    ''' Success is shown only after the database update succeeds.
    ''' </summary>
    Public Class ForgotPasswordDialog
        Inherits DialogBase

        Private ReadOnly _steps As New Panel With {.Dock = DockStyle.Fill, .BackColor = Color.White}
        Private ReadOnly _email As New InputField("Enter your registered email address", Glyph.Mail)
        Private ReadOnly _code As New InputField("XXXX-XXXX", Glyph.Key)
        Private ReadOnly _new As New InputField("New password", Glyph.Lock, password:=True)
        Private ReadOnly _confirm As New InputField("Confirm new password", Glyph.Lock, password:=True)
        Private ReadOnly _msg As Label = MessageLabel()
        Private _verifiedCode As String
        Private _resendAvailableAt As DateTime

        Public Sub New()
            MyBase.New("Forgot Password", 500, 470)
            Body.Controls.Add(_steps)
            _email.MaxLength = 254
            _code.MaxLength = 12
            _new.MaxLength = 128
            _confirm.MaxLength = 128
            _code.Box.CharacterCasing = CharacterCasing.Upper
            ShowEmailStep()
        End Sub

        Private Function NewStep(title As String, subtitle As String, glyphText As String) As VStack
            _steps.Controls.Clear()
            Dim s As New VStack(_steps, 32, 28)
            s.Add(DialogHeader(title, subtitle, glyphText), 56, 18)
            Return s
        End Function

        Private Sub ShowEmailStep()
            Dim s = NewStep("Forgot Password", "We will email you a one-time reset code.", Glyph.Lock)
            Dim infoText = Subtle("Enter the email address registered to your account. For your security we never send existing passwords - you will choose a new one.", 9)
            infoText.MaximumSize = New Size(s.Width, 0)
            s.AddAuto(infoText, 14)
            s.AddAuto(FieldLabel("Email Address"), 5)
            s.Add(_email, 40, 8)
            _msg.Height = 44
            s.Add(_msg, 44, 6)
            Dim send As New ModernButton("Send Reset Code", ButtonKind.Primary, Glyph.Mail)
            Dim cancel As New ModernButton("Cancel", ButtonKind.Outline)
            s.AddRow(42, 12, 12, send, cancel)
            Dim haveCode As New LinkText("I already have a code", 8.75F)
            haveCode.Location = New Point(32, s.Y)
            _steps.Controls.Add(haveCode)
            AcceptButton = send
            CancelButton = cancel
            AddHandler cancel.Click, Sub() Close()
            AddHandler haveCode.Click, Sub()
                                           If Not IsValidEmail(_email.Text) Then
                                               _msg.ForeColor = Theme.Danger
                                               _msg.Text = "Enter your registered email address first."
                                               _email.HasError = True
                                               Return
                                           End If
                                           ShowCodeStep()
                                       End Sub
            AddHandler send.Click, Async Sub() Await SendCodeAsync(send, cancel)
            _email.Focus()
        End Sub

        Private Async Function SendCodeAsync(ParamArray disable As Control()) As Task
            _msg.ForeColor = Theme.Danger
            _msg.Text = ""
            Dim email = _email.Text
            Dim r = Await TryRunAsync(Of Boolean)(Me, Async Function()
                                                           Await AppServices.Instance.Auth.RequestPasswordResetAsync(email)
                                                           Return True
                                                       End Function, disable, showErrors:=False)
            If Not r.Ok Then
                _msg.ForeColor = Theme.Danger
                _msg.Text = FriendlyMessage(r.Error)
                If TypeOf r.Error Is ValidationException Then _email.HasError = True
                Return
            End If
            _resendAvailableAt = DateTime.Now.AddSeconds(60)
            ShowCodeStep(sent:=True)
        End Function

        Private Sub ShowCodeStep(Optional sent As Boolean = False)
            Dim s = NewStep("Enter Reset Code", "Check your inbox (and spam folder).", Glyph.Key)
            Dim text = If(sent,
                          $"If an account is registered with {NormalizeEmail(_email.Text)}, a reset code has been sent to it. Enter the 8-character code below. The code expires soon and can be used once.",
                          $"Enter the 8-character code that was emailed to {NormalizeEmail(_email.Text)}.")
            Dim infoText = Subtle(text, 9)
            infoText.MaximumSize = New Size(s.Width, 0)
            s.AddAuto(infoText, 14)
            s.AddAuto(FieldLabel("Reset Code"), 5)
            s.Add(_code, 40, 8)
            _msg.Height = 44
            _msg.Text = ""
            s.Add(_msg, 44, 6)
            Dim verify As New ModernButton("Verify Code", ButtonKind.Primary, Glyph.CheckMark)
            Dim back As New ModernButton("Back", ButtonKind.Outline)
            s.AddRow(42, 12, 12, verify, back)
            Dim resend As New LinkText("Send a new code", 8.75F)
            resend.Location = New Point(32, s.Y)
            _steps.Controls.Add(resend)
            AcceptButton = verify
            AddHandler back.Click, Sub() ShowEmailStep()
            AddHandler resend.Click, Async Sub()
                                         If DateTime.Now < _resendAvailableAt Then
                                             _msg.ForeColor = Theme.Muted
                                             _msg.Text = $"Please wait {CInt((_resendAvailableAt - DateTime.Now).TotalSeconds)} seconds before requesting another code."
                                             Return
                                         End If
                                         Await SendCodeAsync(verify, back)
                                     End Sub
            AddHandler verify.Click, Async Sub()
                                         _msg.ForeColor = Theme.Danger
                                         _msg.Text = ""
                                         Dim email = _email.Text, code = _code.Text
                                         Dim r = Await TryRunAsync(Of Boolean)(Me, Async Function()
                                                                                        Await AppServices.Instance.Auth.VerifyResetCodeAsync(email, code)
                                                                                        Return True
                                                                                    End Function, {verify, back}, showErrors:=False)
                                         If Not r.Ok Then
                                             _msg.Text = FriendlyMessage(r.Error)
                                             _code.HasError = True
                                             Return
                                         End If
                                         _verifiedCode = code
                                         ShowPasswordStep()
                                     End Sub
            _code.Focus()
        End Sub

        Private Sub ShowPasswordStep()
            Dim s = NewStep("Create New Password", "Your code was accepted.", Glyph.Lock)
            s.AddAuto(FieldLabel("New Password"), 5)
            s.Add(_new, 40, 3)
            s.AddAuto(Subtle("At least 8 characters with upper- and lower-case letters, a number and a symbol.", 7.75F), 10)
            s.AddAuto(FieldLabel("Confirm New Password"), 5)
            s.Add(_confirm, 40, 8)
            _msg.Height = 44
            _msg.Text = ""
            s.Add(_msg, 44, 6)
            Dim save As New ModernButton("Reset Password", ButtonKind.Primary, Glyph.CheckMark)
            Dim cancel As New ModernButton("Cancel", ButtonKind.Outline)
            s.AddRow(42, 12, 12, save, cancel)
            AcceptButton = save
            AddHandler cancel.Click, Sub() Close()
            AddHandler save.Click, Async Sub()
                                       _msg.ForeColor = Theme.Danger
                                       _msg.Text = ""
                                       Dim email = _email.Text, code = _verifiedCode, pw = _new.Text, cf = _confirm.Text
                                       Dim r = Await TryRunAsync(Of Boolean)(Me, Async Function()
                                                                                      Await AppServices.Instance.Auth.ResetPasswordAsync(email, code, pw, cf)
                                                                                      Return True
                                                                                  End Function, {save, cancel}, showErrors:=False)
                                       If Not r.Ok Then
                                           _msg.Text = FriendlyMessage(r.Error)
                                           Dim v = TryCast(r.Error, ValidationException)
                                           If v IsNot Nothing AndAlso v.Field = "Code" Then
                                               ShowCodeStep()
                                               _msg.ForeColor = Theme.Danger
                                               _msg.Text = FriendlyMessage(r.Error)
                                           ElseIf v IsNot Nothing AndAlso v.Field = "ConfirmPassword" Then
                                               _confirm.HasError = True
                                           Else
                                               _new.HasError = True
                                           End If
                                           Return
                                       End If
                                       _new.Text = ""
                                       _confirm.Text = ""
                                       ShowDoneStep()
                                   End Sub
            _new.Focus()
        End Sub

        Private Sub ShowDoneStep()
            Dim s = NewStep("Password Reset", "Your password has been changed.", Glyph.CheckMark)
            Dim infoText = Subtle("Your new password has been saved. For your security, any other signed-in sessions were signed out. You can now log in with your new password.", 9.5F)
            infoText.MaximumSize = New Size(s.Width, 0)
            s.AddAuto(infoText, 30)
            Dim done As New ModernButton("Back to Login")
            s.Add(done, 42, 0)
            AcceptButton = done
            AddHandler done.Click, Sub()
                                       DialogResult = DialogResult.OK
                                       Close()
                                   End Sub
            done.Focus()
        End Sub

        Protected Overrides Sub OnShown(e As EventArgs)
            MyBase.OnShown(e)
            _email.Focus()
        End Sub
    End Class

    ''' <summary>Email verification with a single-use emailed code (and a resend option).</summary>
    Public Class VerifyEmailDialog
        Inherits DialogBase

        Private ReadOnly _email As New InputField("Email address", Glyph.Mail)
        Private ReadOnly _code As New InputField("XXXX-XXXX", Glyph.Key)
        Private ReadOnly _msg As Label = MessageLabel()
        Private ReadOnly _verify As New ModernButton("Verify Email", ButtonKind.Primary, Glyph.CheckMark)
        Private ReadOnly _cancel As New ModernButton("Later", ButtonKind.Outline)

        Public Sub New(email As String, Optional justSent As Boolean = False)
            MyBase.New("Verify Email Address", 500, 430)
            _email.Text = email
            _email.ReadOnly = Not String.IsNullOrEmpty(email)
            _code.Box.CharacterCasing = CharacterCasing.Upper
            _code.MaxLength = 12
            Dim s As New VStack(Body, 32, 28)
            s.Add(DialogHeader("Verify Your Email", "Required before your first login.", Glyph.Mail), 56, 16)
            Dim infoText = Subtle(If(justSent, "We emailed an 8-character verification code to the address below. Enter it to activate your account.",
                                 "Your email address has not been verified yet. Enter the code we emailed you, or request a new one."), 9)
            infoText.MaximumSize = New Size(s.Width, 0)
            s.AddAuto(infoText, 12)
            s.Add(_email, 38, 10)
            s.AddAuto(FieldLabel("Verification Code"), 5)
            s.Add(_code, 40, 6)
            _msg.Height = 40
            s.Add(_msg, 40, 6)
            s.AddRow(42, 12, 10, _verify, _cancel)
            Dim resend As New LinkText("Send a new code", 8.75F)
            resend.Location = New Point(32, s.Y)
            Body.Controls.Add(resend)
            AcceptButton = _verify
            CancelButton = _cancel
            AddHandler _cancel.Click, Sub() Close()
            AddHandler _verify.Click, Async Sub()
                                          _msg.ForeColor = Theme.Danger
                                          _msg.Text = ""
                                          Dim em = _email.Text, cd = _code.Text
                                          Dim r = Await TryRunAsync(Of Boolean)(Me, Async Function()
                                                                                         Await AppServices.Instance.Auth.VerifyEmailAsync(em, cd)
                                                                                         Return True
                                                                                     End Function, {_verify, _cancel}, showErrors:=False)
                                          If Not r.Ok Then
                                              _msg.Text = FriendlyMessage(r.Error)
                                              _code.HasError = True
                                              Return
                                          End If
                                          Info(Me, "Your email address has been verified.", "Email verified")
                                          DialogResult = DialogResult.OK
                                          Close()
                                      End Sub
            AddHandler resend.Click, Async Sub()
                                         _msg.ForeColor = Theme.Danger
                                         _msg.Text = ""
                                         Dim em = _email.Text
                                         Dim r = Await TryRunAsync(Of Boolean)(Me, Async Function()
                                                                                        Await AppServices.Instance.Auth.ResendVerificationAsync(em)
                                                                                        Return True
                                                                                    End Function, {_verify, _cancel}, showErrors:=False)
                                         If Not r.Ok Then
                                             _msg.Text = FriendlyMessage(r.Error)
                                             Return
                                         End If
                                         _msg.ForeColor = Theme.Success
                                         _msg.Text = "If this address belongs to an unverified account, a new code has been sent."
                                     End Sub
        End Sub

        Protected Overrides Sub OnShown(e As EventArgs)
            MyBase.OnShown(e)
            _code.Focus()
        End Sub
    End Class

    ''' <summary>
    ''' Creates the first Administrator. Shown only while no administrator exists; the service re-checks this
    ''' inside a locked transaction, so it cannot be repeated to gain administrator access later.
    ''' </summary>
    Public Class FirstRunSetupForm
        Inherits DialogBase

        Private ReadOnly _name As New InputField("Full name")
        Private ReadOnly _email As New InputField("Official email address", Glyph.Mail)
        Private ReadOnly _username As New InputField("Administrator username")
        Private ReadOnly _pass As New InputField("Password (min. 12 characters)", Glyph.Lock, password:=True)
        Private ReadOnly _confirm As New InputField("Confirm password", Glyph.Lock, password:=True)
        Private ReadOnly _msg As Label = MessageLabel()
        Private ReadOnly _fields As Dictionary(Of String, FieldBase)

        Public Sub New()
            MyBase.New("Initial Administrator Setup", 540, 640)
            _fields = New Dictionary(Of String, FieldBase) From {{"FullName", _name}, {"Email", _email}, {"Username", _username}, {"Password", _pass}, {"ConfirmPassword", _confirm}}
            Dim s As New VStack(Body, 32, 24)
            s.Add(DialogHeader("Initial Administrator Setup", "First run: create the system administrator account.", Glyph.Shield), 56, 12)
            Dim infoText = Subtle("No administrator exists yet. The account you create here manages users, settings and reports. This screen is available only once.", 9)
            infoText.MaximumSize = New Size(s.Width, 0)
            s.AddAuto(infoText, 12)
            s.AddAuto(FieldLabel("Full Name", True), 4)
            s.Add(_name, 38, 8)
            s.AddAuto(FieldLabel("Email Address", True), 4)
            s.Add(_email, 38, 8)
            s.AddAuto(FieldLabel("Username", True), 4)
            s.Add(_username, 38, 8)
            s.AddAuto(FieldLabel("Password", True), 4)
            s.Add(_pass, 38, 3)
            s.AddAuto(Subtle("At least 12 characters with upper- and lower-case letters, a number and a symbol.", 7.75F), 8)
            s.AddAuto(FieldLabel("Confirm Password", True), 4)
            s.Add(_confirm, 38, 6)
            _msg.Height = 36
            s.Add(_msg, 36, 4)
            Dim create As New ModernButton("Create Administrator", ButtonKind.Primary, Glyph.CheckMark)
            Dim exitBtn As New ModernButton("Exit", ButtonKind.Outline)
            s.AddRow(42, 12, 0, create, exitBtn)
            AcceptButton = create
            AddHandler exitBtn.Click, Sub() Close()
            AddHandler create.Click, Async Sub()
                                         _msg.Text = ""
                                         Dim n = _name.Text, em = _email.Text, u = _username.Text, p = _pass.Text, c = _confirm.Text
                                         Dim r = Await TryRunAsync(Of Boolean)(Me, Async Function()
                                                                                        Await AppServices.Instance.Setup.CreateInitialAdminAsync(n, em, u, p, c)
                                                                                        Return True
                                                                                    End Function, {create, exitBtn}, showErrors:=False)
                                         If Not r.Ok Then
                                             _msg.Text = FriendlyMessage(r.Error)
                                             MarkField(_fields, r.Error)
                                             Return
                                         End If
                                         _pass.Text = ""
                                         _confirm.Text = ""
                                         Info(Me, $"Administrator account '{u.Trim()}' has been created. Use the Admin / LGU Personnel Login to sign in.", "Setup complete")
                                         DialogResult = DialogResult.OK
                                         Close()
                                     End Sub
        End Sub

        Protected Overrides Sub OnShown(e As EventArgs)
            MyBase.OnShown(e)
            _name.Focus()
        End Sub
    End Class

End Namespace
