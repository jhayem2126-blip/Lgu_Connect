Imports System.Globalization

Namespace Helpers

    ''' <summary>Display formatting. Values are stored in UTC and shown in local time.</summary>
    Public Module TextFormat

        Private ReadOnly Culture As CultureInfo = CultureInfo.GetCultureInfo("en-US")

        Public Function FileSize(bytes As Long) As String
            If bytes < 1024 Then Return $"{bytes} B"
            If bytes < 1024L * 1024L Then Return (bytes / 1024.0).ToString("0.#", Culture) & " KB"
            Return (bytes / (1024.0 * 1024.0)).ToString("0.#", Culture) & " MB"
        End Function

        Public Function ToLocal(utc As DateTime) As DateTime
            Return DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime()
        End Function

        ''' <summary>Aug 20, 2025</summary>
        Public Function ShortDate(utc As DateTime) As String
            Return ToLocal(utc).ToString("MMM dd, yyyy", Culture)
        End Function

        ''' <summary>2025-08-20</summary>
        Public Function IsoDate(utc As DateTime) As String
            Return ToLocal(utc).ToString("yyyy-MM-dd", Culture)
        End Function

        ''' <summary>Aug 20, 2025 08:45 AM</summary>
        Public Function DateTimeText(utc As DateTime) As String
            Return ToLocal(utc).ToString("MMM dd, yyyy hh:mm tt", Culture)
        End Function

        ''' <summary>2025-08-18 10:24 AM</summary>
        Public Function IsoDateTime(utc As DateTime) As String
            Return ToLocal(utc).ToString("yyyy-MM-dd hh:mm tt", Culture)
        End Function

        Public Function Percent(part As Integer, total As Integer) As Double
            If total <= 0 Then Return 0
            Return part * 100.0 / total
        End Function

        Public Function Plural(count As Integer, singular As String, Optional pluralForm As String = Nothing) As String
            Return $"{count} {If(count = 1, singular, If(pluralForm, singular & "s"))}"
        End Function

    End Module

End Namespace
