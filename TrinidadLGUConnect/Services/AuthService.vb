Imports System.Security.Cryptography
Imports System.Text
Imports MySql.Data.MySqlClient
Imports TrinidadLGUConnect.Data
Imports TrinidadLGUConnect.Helpers
Imports TrinidadLGUConnect.Models
Imports TrinidadLGUConnect.Repositories

Namespace Services

    Public Enum LoginPortal
        Resident
        Personnel
    End Enum

    ''' <summary>
    ''' The single authentication service used by both the resident and the admin/personnel login screens:
    ''' login, logout, registration, email verification, password recovery and password change.
    ''' </summary>
    Public Class AuthService

        Private Const GenericLoginError As String = "Invalid username/email or password."
        Private Const GenericCodeError As String = "The code is invalid or has expired. Request a new code and try again."
        Private Const MaxCodeAttempts As Integer = 5

        Private ReadOnly _db As Db
        Private ReadOnly _settings As SettingsService
        Private ReadOnly _email As EmailService
        Private ReadOnly _authz As AuthorizationService

        Public Sub New(db As Db, settings As SettingsService, email As EmailService, authz As AuthorizationService)
            _db = db
            _settings = settings
            _email = email
            _authz = authz
        End Sub

        Private Shared ReadOnly Property Machine As String
            Get
                Return Environment.MachineName
            End Get
        End Property

        Private Shared Function IdHash(identifier As String) As String
            Return SecureRandom.Sha256Hex("id:" & If(identifier, "").Trim().ToLowerInvariant())
        End Function

        ' =============================================================== login / logout

        Public Async Function LoginAsync(identifier As String, password As String, portal As LoginPortal) As Task(Of LoginResult)
            identifier = If(identifier, "").Trim()
            Require(identifier.Length > 0, "Enter your username or email.", "Username")
            Require(Not String.IsNullOrEmpty(password), "Enter your password.", "Password")
            Require(identifier.Length <= 254 AndAlso password.Length <= MaxPasswordLength, GenericLoginError)

            Return Await _db.RunAsync(
                Async Function(conn)
                    Dim since = DateTime.UtcNow.AddMinutes(-15)
                    Dim idh = IdHash(identifier)
                    If Await AttemptRepository.CountAsync(conn, AttemptType.Login, idh, since).ConfigureAwait(False) >= 10 OrElse
                       Await AttemptRepository.CountByMachineAsync(conn, AttemptType.Login, Machine, since).ConfigureAwait(False) >= 30 Then
                        Throw New RateLimitException("Too many failed login attempts. Please wait 15 minutes and try again.")
                    End If

                    Dim user = Await UserRepository.FindByLoginAsync(conn, identifier).ConfigureAwait(False)
                    If user Is Nothing Then
                        PasswordHasher.VerifyDummy(password)
                        Await AttemptRepository.InsertAsync(conn, AttemptType.Login, idh, False, Machine).ConfigureAwait(False)
                        Return New LoginResult With {.Message = GenericLoginError}
                    End If

                    Dim now = DateTime.UtcNow
                    If user.LockoutUntil.HasValue AndAlso user.LockoutUntil.Value > now Then
                        Dim mins = CInt(Math.Ceiling((user.LockoutUntil.Value - now).TotalMinutes))
                        Await AttemptRepository.InsertAsync(conn, AttemptType.Login, idh, False, Machine).ConfigureAwait(False)
                        Return New LoginResult With {.Message = $"This account is temporarily locked because of repeated failed logins. Try again in {mins} minute(s) or reset your password."}
                    End If

                    If Not PasswordHasher.Verify(password, user.PasswordHash) Then
                        Dim maxFailures = Await _settings.GetIntAsync(SettingKeys.MaxFailedLogins, 5, conn).ConfigureAwait(False)
                        Dim lockMinutes = Await _settings.GetIntAsync(SettingKeys.LockoutMinutes, 15, conn).ConfigureAwait(False)
                        Dim count = Await UserRepository.RecordLoginFailureAsync(conn, user.Id, maxFailures, lockMinutes).ConfigureAwait(False)
                        Await AttemptRepository.InsertAsync(conn, AttemptType.Login, idh, False, Machine).ConfigureAwait(False)
                        Await AuditRepository.InsertAsync(conn, Nothing, user.Id, "auth.login_failed", "user", user.Id.ToString(), $"Failed login ({count} consecutive)").ConfigureAwait(False)
                        If count >= maxFailures Then
                            Await AuditRepository.InsertAsync(conn, Nothing, user.Id, "auth.account_locked", "user", user.Id.ToString(), $"Locked for {lockMinutes} minutes").ConfigureAwait(False)
                        End If
                        Return New LoginResult With {.Message = GenericLoginError}
                    End If

                    ' Password is correct from here on, so account-specific messages do not leak existence.
                    If Not user.IsActive Then
                        Await AuditRepository.InsertAsync(conn, Nothing, user.Id, "auth.login_denied", "user", user.Id.ToString(), "Inactive account").ConfigureAwait(False)
                        Return New LoginResult With {.Message = "This account has been deactivated. Please contact the LGU administrator."}
                    End If

                    Dim isPersonnel = user.Role = UserRole.Administrator OrElse user.Role = UserRole.Staff
                    If portal = LoginPortal.Resident AndAlso isPersonnel Then
                        Return New LoginResult With {.WrongPortal = True, .Message = "This is an LGU personnel account. Please use the Admin / LGU Personnel Login."}
                    End If
                    If portal = LoginPortal.Personnel AndAlso Not isPersonnel Then
                        Return New LoginResult With {.WrongPortal = True, .Message = "This is a resident account. Please use the Resident Login."}
                    End If

                    Dim requireVerify = Await _settings.GetBoolAsync(SettingKeys.RequireEmailVerification, True, conn).ConfigureAwait(False)
                    If requireVerify AndAlso user.Role = UserRole.Resident AndAlso Not user.EmailVerified Then
                        Return New LoginResult With {
                            .RequiresEmailVerification = True, .UnverifiedUserEmail = user.Email,
                            .Message = "Please verify your email address before logging in."}
                    End If

                    If PasswordHasher.NeedsRehash(user.PasswordHash) Then
                        Await UserRepository.RehashAsync(conn, user.Id, PasswordHasher.Hash(password)).ConfigureAwait(False)
                    End If

                    Await UserRepository.RecordLoginSuccessAsync(conn, user.Id).ConfigureAwait(False)
                    Await AttemptRepository.InsertAsync(conn, AttemptType.Login, idh, True, Machine).ConfigureAwait(False)

                    Dim token = SecureRandom.SessionToken()
                    Dim timeout = Await _settings.GetIntAsync(SettingKeys.SessionTimeoutMinutes, 30, conn).ConfigureAwait(False)
                    Dim expires = now.AddMinutes(Math.Max(5, timeout))
                    Dim sessionId = Await SessionRepository.InsertAsync(conn, user.Id, SecureRandom.Sha256Hex(token), Machine, expires).ConfigureAwait(False)
                    Dim perms = Await UserRepository.GetPermissionsAsync(conn, user.Role).ConfigureAwait(False)
                    Await AuditRepository.InsertAsync(conn, Nothing, user.Id, "auth.login", "user", user.Id.ToString(), $"Login via {portal} portal").ConfigureAwait(False)

                    Return New LoginResult With {
                        .Success = True,
                        .Session = New UserSession With {
                            .SessionId = sessionId, .Token = token, .UserId = user.Id, .Username = user.Username,
                            .FullName = user.FullName, .Email = user.Email, .Role = user.Role, .Permissions = perms, .ExpiresAt = expires}}
                End Function).ConfigureAwait(False)
        End Function

        Public Async Function LogoutAsync(session As UserSession) As Task
            If session Is Nothing OrElse String.IsNullOrEmpty(session.Token) Then Return
            Try
                Await _db.RunAsync(
                    Async Function(conn)
                        Await SessionRepository.RevokeAsync(conn, SecureRandom.Sha256Hex(session.Token)).ConfigureAwait(False)
                        Await AuditRepository.InsertAsync(conn, Nothing, session.UserId, "auth.logout", "user", session.UserId.ToString(), Nothing).ConfigureAwait(False)
                        Return True
                    End Function).ConfigureAwait(False)
            Finally
                session.Token = ""
            End Try
        End Function

        ''' <summary>Re-validates the session (used by the shell's idle/expiry checks).</summary>
        Public Async Function ValidateSessionAsync(session As UserSession) As Task(Of Boolean)
            Try
                Await _db.RunAsync(Function(conn) _authz.ResolveAsync(conn, session)).ConfigureAwait(False)
                Return True
            Catch ex As SessionExpiredException
                Return False
            End Try
        End Function

        ' =============================================================== registration

        Public Async Function RegisterAsync(req As RegistrationRequest) As Task(Of RegistrationResult)
            Dim fullName = CleanText(req.FullName, 120)
            Dim email = NormalizeEmail(req.Email)
            Dim username = If(req.Username, "").Trim()
            Require(fullName.Length > 0, "Enter your full name.", "FullName")
            Require(IsValidFullName(fullName), "Enter a valid full name (letters, spaces, periods, apostrophes and hyphens only).", "FullName")
            Require(email.Length > 0, "Enter your email address.", "Email")
            Require(IsValidEmail(email), "Enter a valid email address.", "Email")
            Require(username.Length > 0, "Choose a username.", "Username")
            Require(IsValidUsername(username), "Username must be 4-30 characters: letters, numbers, dots or underscores, starting and ending with a letter or number.", "Username")
            Dim problem = PasswordProblem(req.Password, username, email)
            Require(problem Is Nothing, problem, "Password")
            Require(req.Password = req.ConfirmPassword, "Passwords do not match.", "ConfirmPassword")
            Require(req.BarangayId.HasValue, "Select your barangay.", "Barangay")

            Dim requireVerify = Await _settings.GetBoolAsync(SettingKeys.RequireEmailVerification, True).ConfigureAwait(False)
            Dim minutes = Await _settings.GetIntAsync(SettingKeys.CodeExpiryMinutes, 15).ConfigureAwait(False)
            Dim hash = PasswordHasher.Hash(req.Password)
            Dim code As String = Nothing

            Dim userId = Await _db.InTransactionAsync(
                Async Function(conn, tx)
                    If Await UserRepository.UsernameExistsAsync(conn, username, 0, tx).ConfigureAwait(False) Then Throw New ValidationException("This username is already taken.", "Username")
                    If Await UserRepository.EmailExistsAsync(conn, email, 0, tx).ConfigureAwait(False) Then Throw New ValidationException("This email address is already registered.", "Email")
                    If Not (Await ConcernRepository.GetBarangaysAsync(conn, tx).ConfigureAwait(False)).Any(Function(b) b.Id = req.BarangayId.Value) Then
                        Throw New ValidationException("Select a barangay from the list.", "Barangay")
                    End If
                    Dim u As New UserAccount With {.FullName = fullName, .Email = email, .Username = username, .Role = UserRole.Resident, .IsActive = True,
                                                   .EmailVerified = Not requireVerify, .BarangayId = req.BarangayId}
                    Dim id = Await UserRepository.InsertAsync(conn, tx, u, hash, Nothing).ConfigureAwait(False)
                    If requireVerify Then
                        code = SecureRandom.OneTimeCode()
                        Await TokenRepository.InsertAsync(conn, tx, TokenPurpose.EmailVerification, id, SecureRandom.HashCode(code), DateTime.UtcNow.AddMinutes(minutes)).ConfigureAwait(False)
                    End If
                    Await AuditRepository.InsertAsync(conn, tx, id, "user.registered", "user", id.ToString(), $"Resident self-registration ({username})").ConfigureAwait(False)
                    Return id
                End Function).ConfigureAwait(False)

            Dim result As New RegistrationResult With {.UserId = userId, .RequiresVerification = requireVerify}
            If requireVerify Then
                Try
                    Await _email.SendVerificationCodeAsync(email, fullName, code, minutes).ConfigureAwait(False)
                    result.VerificationEmailSent = True
                Catch ex As EmailDeliveryException
                    result.VerificationEmailSent = False
                    result.EmailError = ex.Message
                End Try
            End If
            Return result
        End Function

        ''' <summary>
        ''' Sends a new verification code if the address belongs to an unverified account. The caller always
        ''' shows the same neutral message, so the response does not reveal whether the address is registered.
        ''' Throws EmailDeliveryException only when SMTP is unavailable or rejects the message.
        ''' </summary>
        Public Async Function ResendVerificationAsync(email As String) As Task
            email = NormalizeEmail(email)
            Require(IsValidEmail(email), "Enter a valid email address.", "Email")
            If Not _email.IsConfigured Then Throw New EmailDeliveryException("Email delivery is not configured. Please ask the LGU office to verify your account.")
            Dim minutes = Await _settings.GetIntAsync(SettingKeys.CodeExpiryMinutes, 15).ConfigureAwait(False)
            Dim idh = IdHash(email)

            Dim toSend = Await _db.InTransactionAsync(
                Async Function(conn, tx)
                    Await EnforceRequestLimitsAsync(conn, AttemptType.VerificationRequest, idh).ConfigureAwait(False)
                    Await AttemptRepository.InsertAsync(conn, AttemptType.VerificationRequest, idh, True, Machine).ConfigureAwait(False)
                    Dim user = Await UserRepository.FindByEmailAsync(conn, email, tx).ConfigureAwait(False)
                    If user Is Nothing OrElse user.EmailVerified OrElse Not user.IsActive Then Return CType(Nothing, Tuple(Of UserAccount, String))
                    Await TokenRepository.RevokeOutstandingAsync(conn, tx, TokenPurpose.EmailVerification, user.Id).ConfigureAwait(False)
                    Dim code = SecureRandom.OneTimeCode()
                    Await TokenRepository.InsertAsync(conn, tx, TokenPurpose.EmailVerification, user.Id, SecureRandom.HashCode(code), DateTime.UtcNow.AddMinutes(minutes)).ConfigureAwait(False)
                    Return Tuple.Create(user, code)
                End Function).ConfigureAwait(False)

            If toSend IsNot Nothing Then
                Dim failure As EmailDeliveryException = Nothing
                Try
                    Await _email.SendVerificationCodeAsync(toSend.Item1.Email, toSend.Item1.FullName, toSend.Item2, minutes).ConfigureAwait(False)
                Catch ex As EmailDeliveryException
                    failure = ex
                End Try
                If failure IsNot Nothing Then
                    Await RevokeQuietlyAsync(TokenPurpose.EmailVerification, toSend.Item1.Id).ConfigureAwait(False)
                    Throw failure
                End If
            Else
                Await Task.Delay(RandomNumberGenerator.GetInt32(300, 900)).ConfigureAwait(False)
            End If
        End Function

        Public Async Function VerifyEmailAsync(email As String, code As String) As Task
            email = NormalizeEmail(email)
            Require(IsValidEmail(email), "Enter a valid email address.", "Email")
            Require(SecureRandom.NormalizeCode(code).Length = 8, "Enter the 8-character code from the email.", "Code")
            Dim ok = Await _db.InTransactionAsync(
                Async Function(conn, tx)
                    Dim user = Await CheckCodeAsync(conn, tx, TokenPurpose.EmailVerification, email, code, consume:=True).ConfigureAwait(False)
                    If user Is Nothing Then Return False
                    Await UserRepository.SetEmailVerifiedAsync(conn, tx, user.Id).ConfigureAwait(False)
                    Await AuditRepository.InsertAsync(conn, tx, user.Id, "user.email_verified", "user", user.Id.ToString(), Nothing).ConfigureAwait(False)
                    Return True
                End Function).ConfigureAwait(False)
            If Not ok Then Throw New ValidationException(GenericCodeError, "Code")
        End Function

        ' =============================================================== password recovery

        ''' <summary>
        ''' Step 1 of recovery. If an active account uses this address a single-use code is emailed.
        ''' Unknown addresses get the same response. Throws EmailDeliveryException if SMTP is not configured
        ''' or the SMTP server refuses the message, so the UI never claims an email was sent when it was not.
        ''' </summary>
        Public Async Function RequestPasswordResetAsync(email As String) As Task
            email = NormalizeEmail(email)
            Require(email.Length > 0, "Enter your registered email address.", "Email")
            Require(IsValidEmail(email), "Enter a valid email address.", "Email")
            If Not _email.IsConfigured Then
                Throw New EmailDeliveryException("Password recovery by email is not available because the email server has not been configured. Please contact the LGU office.")
            End If
            Dim minutes = Await _settings.GetIntAsync(SettingKeys.CodeExpiryMinutes, 15).ConfigureAwait(False)
            Dim idh = IdHash(email)

            Dim toSend = Await _db.InTransactionAsync(
                Async Function(conn, tx)
                    Await EnforceRequestLimitsAsync(conn, AttemptType.ResetRequest, idh).ConfigureAwait(False)
                    Await AttemptRepository.InsertAsync(conn, AttemptType.ResetRequest, idh, True, Machine).ConfigureAwait(False)
                    Dim user = Await UserRepository.FindByEmailAsync(conn, email, tx).ConfigureAwait(False)
                    If user Is Nothing OrElse Not user.IsActive Then Return CType(Nothing, Tuple(Of UserAccount, String))
                    Await TokenRepository.RevokeOutstandingAsync(conn, tx, TokenPurpose.PasswordReset, user.Id).ConfigureAwait(False)
                    Dim code = SecureRandom.OneTimeCode()
                    Await TokenRepository.InsertAsync(conn, tx, TokenPurpose.PasswordReset, user.Id, SecureRandom.HashCode(code), DateTime.UtcNow.AddMinutes(minutes)).ConfigureAwait(False)
                    Await AuditRepository.InsertAsync(conn, tx, user.Id, "auth.password_reset_requested", "user", user.Id.ToString(), Nothing).ConfigureAwait(False)
                    Return Tuple.Create(user, code)
                End Function).ConfigureAwait(False)

            If toSend IsNot Nothing Then
                Dim failure As EmailDeliveryException = Nothing
                Try
                    Await _email.SendPasswordResetCodeAsync(toSend.Item1.Email, toSend.Item1.FullName, toSend.Item2, minutes).ConfigureAwait(False)
                Catch ex As EmailDeliveryException
                    failure = ex
                End Try
                If failure IsNot Nothing Then
                    ' The undelivered code is revoked so it can never be used.
                    Await RevokeQuietlyAsync(TokenPurpose.PasswordReset, toSend.Item1.Id).ConfigureAwait(False)
                    Throw New EmailDeliveryException("We could not send the email right now. Please try again later.", failure)
                End If
            Else
                ' Similar timing for unknown addresses.
                Await Task.Delay(RandomNumberGenerator.GetInt32(600, 1500)).ConfigureAwait(False)
            End If
        End Function

        ''' <summary>Step 2: checks a reset code without consuming it (wrong codes count toward the attempt limit).</summary>
        Public Async Function VerifyResetCodeAsync(email As String, code As String) As Task
            email = NormalizeEmail(email)
            Require(SecureRandom.NormalizeCode(code).Length = 8, "Enter the 8-character code from the email.", "Code")
            Dim ok = Await _db.InTransactionAsync(
                Async Function(conn, tx)
                    Return Await CheckCodeAsync(conn, tx, TokenPurpose.PasswordReset, email, code, consume:=False).ConfigureAwait(False) IsNot Nothing
                End Function).ConfigureAwait(False)
            If Not ok Then Throw New ValidationException(GenericCodeError, "Code")
        End Function

        ''' <summary>Step 3: validates the code again, sets the new password, consumes the code and signs out other sessions.</summary>
        Public Async Function ResetPasswordAsync(email As String, code As String, newPassword As String, confirmPassword As String) As Task
            email = NormalizeEmail(email)
            Require(SecureRandom.NormalizeCode(code).Length = 8, "Enter the 8-character code from the email.", "Code")
            Require(Not String.IsNullOrEmpty(newPassword), "Enter a new password.", "NewPassword")
            Require(newPassword = confirmPassword, "Passwords do not match.", "ConfirmPassword")

            Dim user = Await _db.InTransactionAsync(
                Async Function(conn, tx)
                    Dim u = Await CheckCodeAsync(conn, tx, TokenPurpose.PasswordReset, email, code, consume:=True).ConfigureAwait(False)
                    If u Is Nothing Then Return Nothing
                    Dim problem = PasswordProblem(newPassword, u.Username, u.Email)
                    If problem IsNot Nothing Then Throw New ValidationException(problem, "NewPassword")
                    If PasswordHasher.Verify(newPassword, u.PasswordHash) Then Throw New ValidationException("Choose a password different from your current one.", "NewPassword")
                    Await UserRepository.UpdatePasswordAsync(conn, tx, u.Id, PasswordHasher.Hash(newPassword)).ConfigureAwait(False)
                    Await TokenRepository.RevokeOutstandingAsync(conn, tx, TokenPurpose.PasswordReset, u.Id).ConfigureAwait(False)
                    Await SessionRepository.RevokeAllForUserAsync(conn, tx, u.Id).ConfigureAwait(False)
                    ' Receiving the code by email proves control of the address.
                    If Not u.EmailVerified Then Await UserRepository.SetEmailVerifiedAsync(conn, tx, u.Id).ConfigureAwait(False)
                    Await AuditRepository.InsertAsync(conn, tx, u.Id, "auth.password_reset", "user", u.Id.ToString(), "Password reset with emailed code").ConfigureAwait(False)
                    Return u
                End Function).ConfigureAwait(False)
            If user Is Nothing Then Throw New ValidationException(GenericCodeError, "Code")

            Try
                Await _email.SendPasswordChangedAsync(user.Email, user.FullName).ConfigureAwait(False)
            Catch ex As EmailDeliveryException
                AppLog.Warn("Password-changed notification could not be sent.", ex)
            End Try
        End Function

        Public Async Function ChangePasswordAsync(session As UserSession, currentPassword As String, newPassword As String, confirmPassword As String) As Task
            Require(Not String.IsNullOrEmpty(currentPassword), "Enter your current password.", "CurrentPassword")
            Require(newPassword = confirmPassword, "New passwords do not match.", "ConfirmPassword")
            Dim user = Await _db.InTransactionAsync(
                Async Function(conn, tx)
                    Dim actor = Await _authz.ResolveAsync(conn, session).ConfigureAwait(False)
                    Dim u = Await UserRepository.GetByIdAsync(conn, actor.UserId, tx, forUpdate:=True).ConfigureAwait(False)
                    If Not PasswordHasher.Verify(currentPassword, u.PasswordHash) Then Throw New ValidationException("Your current password is incorrect.", "CurrentPassword")
                    Dim problem = PasswordProblem(newPassword, u.Username, u.Email)
                    If problem IsNot Nothing Then Throw New ValidationException(problem, "NewPassword")
                    If PasswordHasher.Verify(newPassword, u.PasswordHash) Then Throw New ValidationException("Choose a password different from your current one.", "NewPassword")
                    Await UserRepository.UpdatePasswordAsync(conn, tx, u.Id, PasswordHasher.Hash(newPassword)).ConfigureAwait(False)
                    Await SessionRepository.RevokeAllForUserAsync(conn, tx, u.Id, actor.SessionId).ConfigureAwait(False)
                    Await AuditRepository.InsertAsync(conn, tx, u.Id, "auth.password_changed", "user", u.Id.ToString(), Nothing).ConfigureAwait(False)
                    Return u
                End Function).ConfigureAwait(False)
            Try
                Await _email.SendPasswordChangedAsync(user.Email, user.FullName).ConfigureAwait(False)
            Catch ex As EmailDeliveryException
                AppLog.Warn("Password-changed notification could not be sent.", ex)
            End Try
        End Function

        ' =============================================================== helpers

        Private Async Function EnforceRequestLimitsAsync(conn As MySqlConnection, attemptType As String, idh As String) As Task
            Dim now = DateTime.UtcNow
            Dim last = Await AttemptRepository.LastAttemptAsync(conn, attemptType, idh).ConfigureAwait(False)
            If last.HasValue AndAlso now - last.Value < TimeSpan.FromSeconds(60) Then
                Throw New RateLimitException("Please wait a minute before requesting another code.")
            End If
            If Await AttemptRepository.CountAsync(conn, attemptType, idh, now.AddHours(-1), failuresOnly:=False).ConfigureAwait(False) >= 3 Then
                Throw New RateLimitException("Too many code requests for this address. Please try again in an hour.")
            End If
            If Await AttemptRepository.CountByMachineAsync(conn, attemptType, Machine, now.AddHours(-1), failuresOnly:=False).ConfigureAwait(False) >= 10 Then
                Throw New RateLimitException("Too many code requests from this computer. Please try again later.")
            End If
        End Function

        ''' <summary>
        ''' Validates a one-time code for the account with this email. Returns Nothing for a wrong, expired or
        ''' used code; in that case the failure is recorded in the same transaction (so the caller must commit
        ''' and then report the error, rather than throwing and rolling the counter back).
        ''' </summary>
        Private Async Function CheckCodeAsync(conn As MySqlConnection, tx As MySqlTransaction, purpose As TokenPurpose, email As String, code As String, consume As Boolean) As Task(Of UserAccount)
            Dim idh = IdHash(email)
            If Await AttemptRepository.CountAsync(conn, AttemptType.CodeCheck, idh, DateTime.UtcNow.AddMinutes(-15)).ConfigureAwait(False) >= 10 Then
                Throw New RateLimitException("Too many incorrect codes. Please wait 15 minutes and request a new code.")
            End If
            Dim user = Await UserRepository.FindByEmailAsync(conn, email, tx).ConfigureAwait(False)
            Dim token = If(user Is Nothing, Nothing, Await TokenRepository.FindActiveAsync(conn, tx, purpose, user.Id).ConfigureAwait(False))
            Dim ok = False
            If token IsNot Nothing Then
                Dim expected = Encoding.ASCII.GetBytes(token.TokenHash)
                Dim actual = Encoding.ASCII.GetBytes(SecureRandom.HashCode(code))
                ok = CryptographicOperations.FixedTimeEquals(expected, actual)
            End If
            If Not ok Then
                Await AttemptRepository.InsertAsync(conn, AttemptType.CodeCheck, idh, False, Machine).ConfigureAwait(False)
                If token IsNot Nothing Then Await TokenRepository.IncrementFailedAsync(conn, tx, purpose, token.Id, MaxCodeAttempts).ConfigureAwait(False)
                Return Nothing
            End If
            If consume Then Await TokenRepository.MarkUsedAsync(conn, tx, purpose, token.Id).ConfigureAwait(False)
            Return user
        End Function

        Private Async Function RevokeQuietlyAsync(purpose As TokenPurpose, userId As Integer) As Task
            Try
                Await _db.RunAsync(
                    Async Function(c)
                        Await TokenRepository.RevokeOutstandingAsync(c, Nothing, purpose, userId).ConfigureAwait(False)
                        Return True
                    End Function).ConfigureAwait(False)
            Catch ex As Exception
                AppLog.Warn("Could not revoke undelivered code.", ex)
            End Try
        End Function

    End Class

End Namespace
