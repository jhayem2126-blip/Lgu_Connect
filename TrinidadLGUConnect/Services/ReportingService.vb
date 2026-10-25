Imports System.Globalization
Imports TrinidadLGUConnect.Data
Imports TrinidadLGUConnect.Helpers
Imports TrinidadLGUConnect.Models
Imports TrinidadLGUConnect.Repositories

Namespace Services

    Public Class ReportingService

        Private ReadOnly _db As Db
        Private ReadOnly _authz As AuthorizationService

        Public Sub New(db As Db, authz As AuthorizationService)
            _db = db
            _authz = authz
        End Sub

        Public Shared Function PresetText(p As DateRangePreset) As String
            Select Case p
                Case DateRangePreset.Today : Return "Today"
                Case DateRangePreset.Last7Days : Return "Last 7 Days"
                Case DateRangePreset.Last30Days : Return "Last 30 Days"
                Case DateRangePreset.ThisMonth : Return "This Month"
                Case DateRangePreset.LastMonth : Return "Last Month"
                Case DateRangePreset.ThisYear : Return "This Year"
                Case DateRangePreset.AllTime : Return "All Time"
                Case Else : Return "Custom Range"
            End Select
        End Function

        Public Shared Function TypeText(t As ReportType) As String
            Select Case t
                Case ReportType.ConcernsByStatus : Return "Concerns by Status"
                Case ReportType.ConcernsByCategory : Return "Concerns by Category"
                Case ReportType.DateRangeSummary : Return "Date Range Summary"
                Case Else : Return "Concern Summary"
            End Select
        End Function

        ''' <summary>Inclusive local date range for a preset.</summary>
        Public Shared Function ResolveRange(p As DateRangePreset, today As DateTime) As (FromDate As DateTime, ToDate As DateTime)
            today = today.Date
            Select Case p
                Case DateRangePreset.Today : Return (today, today)
                Case DateRangePreset.Last7Days : Return (today.AddDays(-6), today)
                Case DateRangePreset.Last30Days : Return (today.AddDays(-29), today)
                Case DateRangePreset.ThisMonth : Return (New DateTime(today.Year, today.Month, 1), today)
                Case DateRangePreset.LastMonth
                    Dim first = New DateTime(today.Year, today.Month, 1).AddMonths(-1)
                    Return (first, first.AddMonths(1).AddDays(-1))
                Case DateRangePreset.ThisYear : Return (New DateTime(today.Year, 1, 1), today)
                Case DateRangePreset.AllTime : Return (New DateTime(2000, 1, 1), today)
                Case Else : Return (today.AddDays(-29), today)
            End Select
        End Function

        Public Async Function GenerateAsync(session As UserSession, req As ReportRequest) As Task(Of ReportResult)
            Require(req.FromDate.Date <= req.ToDate.Date, "The start date must be on or before the end date.", "DateRange")
            Require(req.FromDate.Year >= 2000, "The start date is too far in the past.", "DateRange")
            Require(req.ToDate.Date <= DateTime.Today.AddYears(1), "The end date is too far in the future.", "DateRange")
            req.FromDate = req.FromDate.Date
            req.ToDate = req.ToDate.Date
            Dim fromUtc = req.FromDate.ToUniversalTime()
            Dim toUtc = req.ToDate.AddDays(1).ToUniversalTime()

            Return Await _db.RunAsync(
                Async Function(conn)
                    Dim actor = Await _authz.DemandAsync(conn, session, Permission.ReportGenerate).ConfigureAwait(False)
                    Dim result As New ReportResult With {.Request = req, .GeneratedAt = DateTime.Now, .GeneratedBy = actor.User.FullName}
                    Dim counts = Await ConcernRepository.StatusCountsAsync(conn, Nothing, fromUtc, toUtc).ConfigureAwait(False)
                    result.Submitted = counts(ConcernStatus.Submitted)
                    result.UnderReview = counts(ConcernStatus.UnderReview)
                    result.InProgress = counts(ConcernStatus.InProgress)
                    result.Resolved = counts(ConcernStatus.Resolved)
                    result.Closed = counts(ConcernStatus.Closed)
                    result.Total = counts.Values.Sum()
                    result.ByStatus = AllStatuses.Select(Function(s) New StatusReportRow With {
                        .Status = s, .Count = counts(s), .Percent = TextFormat.Percent(counts(s), result.Total)}).ToList()
                    result.ByCategory = Await ConcernRepository.CategoryBreakdownAsync(conn, fromUtc, toUtc).ConfigureAwait(False)
                    result.Concerns = Await ConcernRepository.ListInRangeAsync(conn, fromUtc, toUtc).ConfigureAwait(False)
                    result.ByDay = BuildPeriods(result.Concerns, req.FromDate, req.ToDate)
                    Await AuditRepository.InsertAsync(conn, Nothing, actor.UserId, "report.generated", "report", TypeText(req.Type),
                        $"{req.FromDate:yyyy-MM-dd} to {req.ToDate:yyyy-MM-dd}; {result.Total} concern(s)").ConfigureAwait(False)
                    Return result
                End Function).ConfigureAwait(False)
        End Function

        ''' <summary>Daily rows for ranges up to 62 days, monthly rows for longer ranges. Empty periods are included.</summary>
        Public Shared Function BuildPeriods(concerns As IEnumerable(Of ConcernSummary), fromDate As DateTime, toDate As DateTime) As List(Of DailyReportRow)
            Dim rows As New List(Of DailyReportRow)()
            Dim monthly = (toDate - fromDate).TotalDays > 62
            Dim culture = CultureInfo.GetCultureInfo("en-US")
            Dim keyOf = Function(d As DateTime) If(monthly, New DateTime(d.Year, d.Month, 1), d.Date)
            Dim cursor = keyOf(fromDate)
            ' For "All Time" begin at the first period that has data.
            If concerns.Any() AndAlso fromDate.Year <= 2000 Then cursor = keyOf(concerns.Min(Function(c) TextFormat.ToLocal(c.ReportedAt)))
            Dim last = keyOf(toDate)
            Dim groups = concerns.GroupBy(Function(c) keyOf(TextFormat.ToLocal(c.ReportedAt))).ToDictionary(Function(g) g.Key, Function(g) g.ToList())
            While cursor <= last
                Dim items As List(Of ConcernSummary) = Nothing
                groups.TryGetValue(cursor, items)
                items = If(items, New List(Of ConcernSummary)())
                rows.Add(New DailyReportRow With {
                    .Day = cursor,
                    .Label = If(monthly, cursor.ToString("MMM yyyy", culture), cursor.ToString("MMM dd, yyyy", culture)),
                    .Total = items.Count,
                    .Resolved = items.Where(Function(c) c.Status = ConcernStatus.Resolved).Count(),
                    .Closed = items.Where(Function(c) c.Status = ConcernStatus.Closed).Count()})
                cursor = If(monthly, cursor.AddMonths(1), cursor.AddDays(1))
            End While
            Return rows
        End Function

        Public Async Function AuditExportAsync(session As UserSession, action As String, report As ReportResult, detail As String) As Task
            Await _db.RunAsync(
                Async Function(conn)
                    Dim actor = Await _authz.DemandAsync(conn, session, Permission.ReportExport).ConfigureAwait(False)
                    Await AuditRepository.InsertAsync(conn, Nothing, actor.UserId, action, "report", TypeText(report.Request.Type),
                        $"{report.Request.FromDate:yyyy-MM-dd} to {report.Request.ToDate:yyyy-MM-dd}; {detail}").ConfigureAwait(False)
                    Return True
                End Function).ConfigureAwait(False)
        End Function

        ''' <summary>Throws AuthorizationException unless the session may export/print.</summary>
        Public Async Function DemandExportAsync(session As UserSession) As Task
            Await _db.RunAsync(Function(conn) _authz.DemandAsync(conn, session, Permission.ReportExport)).ConfigureAwait(False)
        End Function

    End Class

End Namespace
