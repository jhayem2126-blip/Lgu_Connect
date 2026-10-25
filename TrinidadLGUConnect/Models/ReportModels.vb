Namespace Models

    Public Enum ReportType
        ConcernSummary = 0
        ConcernsByStatus = 1
        ConcernsByCategory = 2
        DateRangeSummary = 3
    End Enum

    Public Enum DateRangePreset
        Today = 0
        Last7Days = 1
        Last30Days = 2
        ThisMonth = 3
        LastMonth = 4
        ThisYear = 5
        AllTime = 6
        Custom = 7
    End Enum

    Public Class ReportRequest
        Public Property Type As ReportType = ReportType.ConcernSummary
        ''' <summary>Inclusive local date.</summary>
        Public Property FromDate As DateTime
        ''' <summary>Inclusive local date.</summary>
        Public Property ToDate As DateTime
    End Class

    Public Class CategoryReportRow
        Public Property CategoryId As Integer
        Public Property Category As String = ""
        Public Property ColorHex As String = ""
        Public Property Total As Integer
        Public Property Submitted As Integer
        Public Property UnderReview As Integer
        Public Property InProgress As Integer
        Public Property Resolved As Integer
        Public Property Closed As Integer
    End Class

    Public Class StatusReportRow
        Public Property Status As ConcernStatus
        Public Property Count As Integer
        Public Property Percent As Double

        Public ReadOnly Property StatusName As String
            Get
                Return DisplayName(Status)
            End Get
        End Property
    End Class

    ''' <summary>One period (a day, or a month for long ranges) of the date-range report.</summary>
    Public Class DailyReportRow
        Public Property Day As DateTime
        Public Property Label As String = ""
        Public Property Total As Integer
        Public Property Resolved As Integer
        Public Property Closed As Integer
    End Class

    Public Class ReportResult
        Public Property Request As ReportRequest
        Public Property GeneratedAt As DateTime
        Public Property GeneratedBy As String = ""
        Public Property Total As Integer
        Public Property Submitted As Integer
        Public Property UnderReview As Integer
        Public Property InProgress As Integer
        Public Property Resolved As Integer
        Public Property Closed As Integer
        Public Property ByStatus As New List(Of StatusReportRow)()
        Public Property ByCategory As New List(Of CategoryReportRow)()
        Public Property ByDay As New List(Of DailyReportRow)()
        Public Property Concerns As New List(Of ConcernSummary)()

        Public ReadOnly Property Title As String
            Get
                Select Case Request.Type
                    Case ReportType.ConcernsByStatus : Return "CONCERN STATUS REPORT"
                    Case ReportType.ConcernsByCategory : Return "CONCERN CATEGORY REPORT"
                    Case ReportType.DateRangeSummary : Return "DATE RANGE REPORT"
                    Case Else : Return "CONCERN SUMMARY REPORT"
                End Select
            End Get
        End Property

        Public ReadOnly Property RangeText As String
            Get
                Return $"{Request.FromDate:MMMM dd, yyyy} - {Request.ToDate:MMMM dd, yyyy}"
            End Get
        End Property
    End Class

End Namespace
