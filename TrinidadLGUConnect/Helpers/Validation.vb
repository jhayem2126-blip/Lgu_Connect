Imports System.Net.Mail
Imports System.Text.RegularExpressions

Namespace Helpers

    ''' <summary>Thrown for user-correctable input problems. The message is safe to show to users.</summary>
    Public Class ValidationException
        Inherits Exception
        Public ReadOnly Property Field As String

        Public Sub New(message As String, Optional field As String = Nothing)
            MyBase.New(message)
            Me.Field = field
        End Sub
    End Class

    ''' <summary>Thrown when the current session lacks the required permission or ownership.</summary>
    Public Class AuthorizationException
        Inherits Exception
        Public Sub New(Optional message As String = "You are not authorized to perform this action.")
            MyBase.New(message)
        End Sub
    End Class

    ''' <summary>Thrown when the session has expired, been revoked or the account was deactivated.</summary>
    Public Class SessionExpiredException
        Inherits Exception
        Public Sub New(Optional message As String = "Your session has ended. Please log in again.")
            MyBase.New(message)
        End Sub
    End Class

    ''' <summary>Thrown when an operation is refused because too many attempts were made.</summary>
    Public Class RateLimitException
        Inherits Exception
        Public Sub New(message As String)
            MyBase.New(message)
        End Sub
    End Class

    ''' <summary>A record changed between loading and saving (optimistic concurrency).</summary>
    Public Class ConcurrencyException
        Inherits Exception
        Public Sub New(message As String)
            MyBase.New(message)
        End Sub
    End Class

    Public Module Validation

        Public Const MinPasswordLength As Integer = 8
        Public Const MaxPasswordLength As Integer = 128
        Public Const MaxDescriptionLength As Integer = 2000
        Public Const MinDescriptionLength As Integer = 10
        Public Const MaxLocationLength As Integer = 200
        Public Const MaxRemarksLength As Integer = 1000

        Private ReadOnly UsernamePattern As New Regex("^[A-Za-z0-9](?:[A-Za-z0-9._]{2,28})[A-Za-z0-9]$", RegexOptions.CultureInvariant)
        Private ReadOnly NamePattern As New Regex("^[\p{L}\p{M}][\p{L}\p{M} .,'\-ñÑ]{1,118}$", RegexOptions.CultureInvariant)
        Private ReadOnly PhonePattern As New Regex("^[0-9+()\- ]{7,20}$", RegexOptions.CultureInvariant)

        Public Function NormalizeEmail(email As String) As String
            Return If(email, "").Trim().ToLowerInvariant()
        End Function

        Public Function IsValidEmail(email As String) As Boolean
            Dim e = If(email, "").Trim()
            If e.Length < 6 OrElse e.Length > 254 OrElse e.Contains(" "c) Then Return False
            Try
                Dim addr As New MailAddress(e)
                If Not String.Equals(addr.Address, e, StringComparison.OrdinalIgnoreCase) Then Return False
                Dim host = addr.Host
                Return host.Contains("."c) AndAlso Not host.StartsWith("."c) AndAlso Not host.EndsWith("."c)
            Catch ex As FormatException
                Return False
            End Try
        End Function

        Public Function IsValidUsername(username As String) As Boolean
            Return UsernamePattern.IsMatch(If(username, ""))
        End Function

        Public Function IsValidFullName(name As String) As Boolean
            Return NamePattern.IsMatch(If(name, "").Trim())
        End Function

        Public Function IsValidPhone(phone As String) As Boolean
            Return String.IsNullOrWhiteSpace(phone) OrElse PhonePattern.IsMatch(phone.Trim())
        End Function

        ''' <summary>Returns Nothing when the password is acceptable, otherwise a user-facing reason.</summary>
        Public Function PasswordProblem(password As String, Optional username As String = Nothing, Optional email As String = Nothing) As String
            Dim p = If(password, "")
            If p.Length < MinPasswordLength Then Return $"Password must be at least {MinPasswordLength} characters long."
            If p.Length > MaxPasswordLength Then Return $"Password must be at most {MaxPasswordLength} characters long."
            If Not p.Any(AddressOf Char.IsUpper) Then Return "Password must contain at least one uppercase letter."
            If Not p.Any(AddressOf Char.IsLower) Then Return "Password must contain at least one lowercase letter."
            If Not p.Any(AddressOf Char.IsDigit) Then Return "Password must contain at least one number."
            If Not p.Any(Function(c) Not Char.IsLetterOrDigit(c)) Then Return "Password must contain at least one symbol (for example ! @ # $)."
            If Not String.IsNullOrWhiteSpace(username) AndAlso p.IndexOf(username.Trim(), StringComparison.OrdinalIgnoreCase) >= 0 Then Return "Password must not contain your username."
            If Not String.IsNullOrWhiteSpace(email) Then
                Dim local = email.Split("@"c)(0)
                If local.Length >= 4 AndAlso p.IndexOf(local, StringComparison.OrdinalIgnoreCase) >= 0 Then Return "Password must not contain your email address."
            End If
            Return Nothing
        End Function

        ''' <summary>Trims, collapses control characters and enforces a maximum length for free text.</summary>
        Public Function CleanText(value As String, maxLength As Integer, Optional multiline As Boolean = False) As String
            If value Is Nothing Then Return ""
            Dim chars = value.Where(Function(c) Not Char.IsControl(c) OrElse (multiline AndAlso (c = ControlChars.Lf OrElse c = ControlChars.Cr OrElse c = ControlChars.Tab))).ToArray()
            Dim s = New String(chars).Trim()
            If multiline Then s = s.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)
            If s.Length > maxLength Then Throw New ValidationException($"The text is too long (maximum {maxLength} characters).")
            Return s
        End Function

        Public Sub Require(condition As Boolean, message As String, Optional field As String = Nothing)
            If Not condition Then Throw New ValidationException(message, field)
        End Sub

    End Module

End Namespace
