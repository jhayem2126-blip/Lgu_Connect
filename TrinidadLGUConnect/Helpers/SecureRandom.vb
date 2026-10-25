Imports System.Security.Cryptography
Imports System.Text

Namespace Helpers

    Public Module SecureRandom

        ' Unambiguous alphabet (no 0/O, 1/I/L) so codes are easy to type from an email.
        Private Const CodeAlphabet As String = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"

        ''' <summary>Generates a one-time code such as "K7QM-3XRA" (8 symbols, ~40 bits of entropy).</summary>
        Public Function OneTimeCode() As String
            Dim sb As New StringBuilder(9)
            For i = 0 To 7
                If i = 4 Then sb.Append("-"c)
                sb.Append(CodeAlphabet(RandomNumberGenerator.GetInt32(CodeAlphabet.Length)))
            Next
            Return sb.ToString()
        End Function

        ''' <summary>Normalises a code the user typed: upper-case, no spaces or dashes.</summary>
        Public Function NormalizeCode(code As String) As String
            Return New String(If(code, "").ToUpperInvariant().Where(Function(c) Char.IsLetterOrDigit(c)).ToArray())
        End Function

        Public Function SessionToken() As String
            Return Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
        End Function

        Public Function Sha256Hex(value As String) As String
            Return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()
        End Function

        Public Function Sha256Hex(data As Byte()) As String
            Return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant()
        End Function

        ''' <summary>Hash of a one-time code for storage. Only this hash is written to the database.</summary>
        Public Function HashCode(code As String) As String
            Return Sha256Hex("tlc-otc:" & NormalizeCode(code))
        End Function

    End Module

End Namespace
