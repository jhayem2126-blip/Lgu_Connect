Imports System.Security.Cryptography
Imports System.Text

Namespace Services

    ''' <summary>
    ''' PBKDF2-HMAC-SHA256 password hashing (OWASP 2023+ guidance: 600,000 iterations, 128-bit salt).
    ''' Stored format: pbkdf2-sha256$&lt;iterations&gt;$&lt;base64 salt&gt;$&lt;base64 hash&gt;
    ''' Uses only the .NET base class library, so there is no native dependency to deploy.
    ''' </summary>
    Public NotInheritable Class PasswordHasher

        Public Const Algorithm As String = "pbkdf2-sha256"
        Public Const DefaultIterations As Integer = 600000
        Private Const SaltSize As Integer = 16
        Private Const KeySize As Integer = 32

        Private Shared ReadOnly DummyHash As String = Hash("dummy-password-for-timing", 1000)

        Private Sub New()
        End Sub

        Public Shared Function Hash(password As String, Optional iterations As Integer = DefaultIterations) As String
            If password Is Nothing Then Throw New ArgumentNullException(NameOf(password))
            Dim salt = RandomNumberGenerator.GetBytes(SaltSize)
            Dim key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, KeySize)
            Return $"{Algorithm}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}"
        End Function

        Public Shared Function Verify(password As String, stored As String) As Boolean
            If password Is Nothing OrElse String.IsNullOrEmpty(stored) Then Return False
            Dim parts = stored.Split("$"c)
            If parts.Length <> 4 OrElse parts(0) <> Algorithm Then Return False
            Dim iterations As Integer
            If Not Integer.TryParse(parts(1), iterations) OrElse iterations < 1000 Then Return False
            Dim salt, expected As Byte()
            Try
                salt = Convert.FromBase64String(parts(2))
                expected = Convert.FromBase64String(parts(3))
            Catch ex As FormatException
                Return False
            End Try
            Dim actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length)
            Return CryptographicOperations.FixedTimeEquals(actual, expected)
        End Function

        Public Shared Function NeedsRehash(stored As String) As Boolean
            Dim parts = If(stored, "").Split("$"c)
            Dim iterations As Integer
            Return parts.Length <> 4 OrElse parts(0) <> Algorithm OrElse Not Integer.TryParse(parts(1), iterations) OrElse iterations < DefaultIterations
        End Function

        ''' <summary>Performs a hash comparison for unknown accounts so response time does not reveal whether an account exists.</summary>
        Public Shared Sub VerifyDummy(password As String)
            Dim salt = RandomNumberGenerator.GetBytes(SaltSize)
            Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(If(password, "")), salt, DefaultIterations, HashAlgorithmName.SHA256, KeySize)
            Verify(password, DummyHash)
        End Sub

    End Class

End Namespace
