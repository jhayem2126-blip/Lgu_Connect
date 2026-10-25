Imports MySql.Data.MySqlClient
Imports TrinidadLGUConnect.Data
Imports TrinidadLGUConnect.Helpers
Imports TrinidadLGUConnect.Models
Imports TrinidadLGUConnect.Repositories

Namespace Services

    ''' <summary>Pure workflow rules, driven by the concern_status_transitions table.</summary>
    Public NotInheritable Class StatusWorkflow
        Public Const StaffScope As String = "staff"
        Public Const ResidentScope As String = "resident"

        Private Sub New()
        End Sub

        Public Shared Function Allowed(transitions As IEnumerable(Of StatusTransition), current As ConcernStatus, scope As String) As List(Of ConcernStatus)
            Return transitions.Where(Function(t) t.FromStatus = current AndAlso t.ActorScope = scope).
                Select(Function(t) t.ToStatus).Distinct().OrderBy(Function(s) CByte(s)).ToList()
        End Function

        Public Shared Function IsAllowed(transitions As IEnumerable(Of StatusTransition), current As ConcernStatus, target As ConcernStatus, scope As String) As Boolean
            Return transitions.Any(Function(t) t.FromStatus = current AndAlso t.ToStatus = target AndAlso t.ActorScope = scope)
        End Function
    End Class

    Public Class SubmitResult
        Public Property ConcernId As Integer
        Public Property ReportCode As String = ""
        Public Property WasDuplicate As Boolean
    End Class

    Public Class StatusUpdateResult
        Public Property NewStatus As ConcernStatus
        Public Property ResidentNotified As Boolean
        Public Property NotificationError As String
    End Class

    Public Class ConcernService

        Private ReadOnly _db As Db
        Private ReadOnly _authz As AuthorizationService
        Private ReadOnly _settings As SettingsService
        Private ReadOnly _email As EmailService
        Private _categories As List(Of Category)
        Private _barangays As List(Of Barangay)

        Public Sub New(db As Db, authz As AuthorizationService, settings As SettingsService, email As EmailService)
            _db = db
            _authz = authz
            _settings = settings
            _email = email
        End Sub

        ''' <summary>Raised after any successful submission or status change so open screens can refresh.</summary>
        Public Shared Event DataChanged As EventHandler

        Public Shared Sub RaiseDataChanged()
            RaiseEvent DataChanged(Nothing, EventArgs.Empty)
        End Sub

        Public Async Function GetCategoriesAsync() As Task(Of List(Of Category))
            If _categories Is Nothing Then
                _categories = Await _db.RunAsync(Function(c) ConcernRepository.GetCategoriesAsync(c)).ConfigureAwait(False)
            End If
            Return _categories
        End Function

        Public Async Function GetBarangaysAsync() As Task(Of List(Of Barangay))
            If _barangays Is Nothing Then
                _barangays = Await _db.RunAsync(Function(c) ConcernRepository.GetBarangaysAsync(c)).ConfigureAwait(False)
            End If
            Return _barangays
        End Function

        Public Async Function GetAttachmentLimitsAsync() As Task(Of (MaxBytes As Long, MaxCount As Integer))
            Dim mb = Await _settings.GetIntAsync(SettingKeys.MaxAttachmentMb, 5).ConfigureAwait(False)
            Dim count = Await _settings.GetIntAsync(SettingKeys.MaxAttachmentCount, 5).ConfigureAwait(False)
            Return (CLng(mb) * 1024L * 1024L, count)
        End Function

        ' ================================================================ submit

        Public Async Function SubmitAsync(session As UserSession, req As NewConcernRequest) As Task(Of SubmitResult)
            Dim cats = Await GetCategoriesAsync().ConfigureAwait(False)
            Dim cat = cats.FirstOrDefault(Function(c) c.Id = req.CategoryId)
            Require(cat IsNot Nothing, "Select a category.", "Category")
            If req.SubcategoryId.HasValue Then
                Require(cat.Subcategories.Any(Function(s) s.Id = req.SubcategoryId.Value), "The selected subcategory does not belong to this category.", "Subcategory")
            End If
            Dim barangays = Await GetBarangaysAsync().ConfigureAwait(False)
            Dim brgy = If(req.BarangayId.HasValue, barangays.FirstOrDefault(Function(b) b.Id = req.BarangayId.Value), Nothing)
            Require(brgy IsNot Nothing, "Select the barangay where the concern is located.", "Barangay")
            req.Location = CleanText(req.Location, MaxLocationLength)
            Require(req.Location.Length >= 3, "Enter the street, purok or landmark (at least 3 characters).", "Location")
            req.Description = CleanText(req.Description, MaxDescriptionLength, multiline:=True)
            Require(req.Description.Length >= MinDescriptionLength, $"Describe the concern in at least {MinDescriptionLength} characters.", "Description")
            Dim reportedDate = req.ReportedDate.Date
            Require(reportedDate <= DateTime.Today, "The date reported cannot be in the future.", "DateReported")
            Require(reportedDate >= DateTime.Today.AddDays(-365), "The date reported cannot be more than one year ago.", "DateReported")

            Dim limits = Await GetAttachmentLimitsAsync().ConfigureAwait(False)
            Require(req.Attachments.Count <= limits.MaxCount, $"You can attach up to {limits.MaxCount} pictures.", "Attachments")
            For Each a In req.Attachments
                Require(a.Data IsNot Nothing AndAlso a.Data.Length > 0 AndAlso a.Thumbnail IsNot Nothing, "One of the pictures is invalid. Remove it and attach it again.", "Attachments")
                Require(a.ContentType = "image/jpeg" OrElse a.ContentType = "image/png", "Only JPG and PNG pictures are allowed.", "Attachments")
                Require(a.SizeBytes <= limits.MaxBytes * 2, $"'{a.FileName}' is too large.", "Attachments")
            Next

            ' Today's report uses the current time; a back-dated report uses noon of that day.
            Dim reportedLocal = If(reportedDate = DateTime.Today, DateTime.Now, reportedDate.AddHours(12))
            Dim reportedUtc = reportedLocal.ToUniversalTime()

            Dim result = Await _db.InTransactionAsync(
                Async Function(conn, tx)
                    Dim actor = Await _authz.DemandAsync(conn, session, Permission.ConcernCreate).ConfigureAwait(False)
                    ' Idempotency: a repeated submit with the same key returns the original report.
                    Dim existing = Await ConcernRepository.FindBySubmissionKeyAsync(conn, tx, req.SubmissionKey).ConfigureAwait(False)
                    If existing IsNot Nothing Then
                        If existing.ResidentId <> actor.UserId Then Throw New AuthorizationException()
                        Return New SubmitResult With {.ConcernId = existing.Id, .ReportCode = existing.ReportCode, .WasDuplicate = True}
                    End If
                    Dim id = Await ConcernRepository.InsertAsync(conn, tx, actor.UserId, req, reportedUtc).ConfigureAwait(False)
                    Dim order = 0
                    For Each a In req.Attachments
                        Await ConcernRepository.InsertAttachmentAsync(conn, tx, id, a, order, actor.UserId).ConfigureAwait(False)
                        order += 1
                    Next
                    Await ConcernRepository.InsertHistoryAsync(conn, tx, id, Nothing, ConcernStatus.Submitted, "New report submitted", reportedDate, actor.UserId, DateTime.UtcNow).ConfigureAwait(False)
                    Dim code = ReportCode(id)
                    Await AuditRepository.InsertAsync(conn, tx, actor.UserId, "concern.submitted", "concern", code,
                        $"{cat.Name} at {req.Location}, {brgy.Name}; {req.Attachments.Count} picture(s)").ConfigureAwait(False)
                    Return New SubmitResult With {.ConcernId = id, .ReportCode = code}
                End Function).ConfigureAwait(False)

            If Not result.WasDuplicate Then
                RaiseDataChanged()
                If Await _settings.GetBoolAsync(SettingKeys.EmailOnSubmission, True).ConfigureAwait(False) AndAlso _email.IsConfigured Then
                    Try
                        Await _email.SendSubmissionReceiptAsync(session.Email, session.FullName, result.ReportCode, cat.Name, $"{req.Location}, {brgy.Name}").ConfigureAwait(False)
                    Catch ex As EmailDeliveryException
                        AppLog.Warn($"Submission receipt for {result.ReportCode} not sent.", ex)
                    End Try
                End If
            End If
            Return result
        End Function

        ' ================================================================ read

        ''' <summary>Residents only ever receive their own concerns; the filter is applied in SQL.</summary>
        Public Async Function SearchAsync(session As UserSession, filter As ConcernFilter) As Task(Of PagedResult(Of ConcernSummary))
            If filter.DateFrom.HasValue AndAlso filter.DateTo.HasValue AndAlso filter.DateFrom.Value.Date > filter.DateTo.Value.Date Then
                Throw New ValidationException("'Date From' must be on or before 'Date To'.", "DateRange")
            End If
            If filter.Search IsNot Nothing AndAlso filter.Search.Length > 100 Then filter.Search = filter.Search.Substring(0, 100)
            Return Await _db.RunAsync(
                Async Function(conn)
                    Dim actor = Await _authz.DemandAsync(conn, session, Permission.ConcernViewAll, Permission.ConcernViewOwn).ConfigureAwait(False)
                    Dim scope As Integer? = If(actor.Has(Permission.ConcernViewAll), CType(Nothing, Integer?), actor.UserId)
                    Return Await ConcernRepository.SearchAsync(conn, filter, scope).ConfigureAwait(False)
                End Function).ConfigureAwait(False)
        End Function

        Public Async Function GetDetailAsync(session As UserSession, concernId As Integer) As Task(Of ConcernDetail)
            Return Await _db.RunAsync(
                Async Function(conn)
                    Dim actor = Await _authz.DemandAsync(conn, session, Permission.ConcernViewAll, Permission.ConcernViewOwn).ConfigureAwait(False)
                    Dim d = Await ConcernRepository.GetDetailAsync(conn, concernId).ConfigureAwait(False)
                    ' Same message for "missing" and "not yours" so IDs of other residents' reports cannot be probed.
                    If d Is Nothing OrElse (Not actor.Has(Permission.ConcernViewAll) AndAlso d.ResidentId <> actor.UserId) Then
                        Throw New AuthorizationException("The report was not found or you do not have access to it.")
                    End If
                    Dim isStaff = actor.Has(Permission.ConcernViewAll)
                    For Each h In d.History
                        If h.ChangedById = d.ResidentId Then
                            h.ChangedByDisplay = If(isStaff, h.ChangedByName, "Resident")
                        ElseIf isStaff Then
                            h.ChangedByDisplay = h.ChangedByName
                        Else
                            h.ChangedByDisplay = "LGU Staff"
                        End If
                    Next
                    If Not isStaff Then d.ResidentEmail = ""
                    Return d
                End Function).ConfigureAwait(False)
        End Function

        Public Async Function GetAttachmentAsync(session As UserSession, attachmentId As Integer) As Task(Of AttachmentData)
            Return Await _db.RunAsync(
                Async Function(conn)
                    Dim actor = Await _authz.DemandAsync(conn, session, Permission.ConcernViewAll, Permission.ConcernViewOwn).ConfigureAwait(False)
                    Dim a = Await ConcernRepository.GetAttachmentDataAsync(conn, attachmentId).ConfigureAwait(False)
                    If a Is Nothing OrElse (Not actor.Has(Permission.ConcernViewAll) AndAlso a.ResidentId <> actor.UserId) Then
                        Throw New AuthorizationException("The picture was not found or you do not have access to it.")
                    End If
                    Return a
                End Function).ConfigureAwait(False)
        End Function

        ''' <summary>Statuses the current user may move this concern to (empty when none).</summary>
        Public Async Function GetAllowedTransitionsAsync(session As UserSession, concern As ConcernSummary) As Task(Of List(Of ConcernStatus))
            Return Await _db.RunAsync(
                Async Function(conn)
                    Dim actor = Await _authz.ResolveAsync(conn, session).ConfigureAwait(False)
                    Dim transitions = Await ConcernRepository.GetTransitionsAsync(conn).ConfigureAwait(False)
                    If actor.Has(Permission.ConcernUpdateStatus) Then
                        Return StatusWorkflow.Allowed(transitions, concern.Status, StatusWorkflow.StaffScope)
                    End If
                    If actor.Has(Permission.ConcernResidentUpdate) AndAlso concern.ResidentId = actor.UserId Then
                        Return StatusWorkflow.Allowed(transitions, concern.Status, StatusWorkflow.ResidentScope)
                    End If
                    Return New List(Of ConcernStatus)()
                End Function).ConfigureAwait(False)
        End Function

        ' ================================================================ status update

        Public Async Function UpdateStatusAsync(session As UserSession, req As StatusUpdateRequest) As Task(Of StatusUpdateResult)
            Require(req.ConcernId > 0, "Select a report first.")
            Require([Enum].IsDefined(GetType(ConcernStatus), req.NewStatus), "Select a valid new status.", "NewStatus")
            Dim remarks = CleanText(req.Remarks, MaxRemarksLength, multiline:=True)
            If req.EffectiveDate.HasValue Then
                Require(req.EffectiveDate.Value.Date <= DateTime.Today, "The update date cannot be in the future.", "UpdateDate")
            End If

            Dim info = Await _db.InTransactionAsync(
                Async Function(conn, tx)
                    Dim actor = Await _authz.DemandAsync(conn, session, Permission.ConcernUpdateStatus, Permission.ConcernResidentUpdate).ConfigureAwait(False)
                    Dim locked = Await ConcernRepository.LockAsync(conn, tx, req.ConcernId).ConfigureAwait(False)
                    If locked Is Nothing Then Throw New ValidationException("The report no longer exists.")

                    Dim scope As String
                    If actor.Has(Permission.ConcernUpdateStatus) Then
                        scope = StatusWorkflow.StaffScope
                    ElseIf actor.Has(Permission.ConcernResidentUpdate) AndAlso locked.ResidentId = actor.UserId Then
                        scope = StatusWorkflow.ResidentScope
                    Else
                        Throw New AuthorizationException("The report was not found or you do not have access to it.")
                    End If

                    If locked.Status <> req.ExpectedCurrentStatus Then
                        Throw New ConcurrencyException($"This report was updated by someone else and is now '{DisplayName(locked.Status)}'. The screen has been refreshed; review it and try again.")
                    End If
                    If locked.Status = req.NewStatus Then Throw New ValidationException("Choose a status different from the current status.", "NewStatus")
                    Dim transitions = Await ConcernRepository.GetTransitionsAsync(conn, tx).ConfigureAwait(False)
                    If Not StatusWorkflow.IsAllowed(transitions, locked.Status, req.NewStatus, scope) Then
                        Throw New ValidationException($"A report cannot move from '{DisplayName(locked.Status)}' to '{DisplayName(req.NewStatus)}'.", "NewStatus")
                    End If
                    If req.EffectiveDate.HasValue AndAlso req.EffectiveDate.Value.Date < TextFormat.ToLocal(locked.ReportedAt).Date Then
                        Throw New ValidationException("The update date cannot be earlier than the date the concern was reported.", "UpdateDate")
                    End If

                    Dim finalRemarks = remarks
                    If scope = StatusWorkflow.ResidentScope AndAlso String.IsNullOrWhiteSpace(finalRemarks) Then
                        finalRemarks = If(locked.Status = ConcernStatus.Resolved, "Resident confirmed the resolution", "Withdrawn by resident")
                    End If

                    Dim now = DateTime.UtcNow
                    Await ConcernRepository.UpdateStatusAsync(conn, tx, locked.Id, req.NewStatus, now).ConfigureAwait(False)
                    Await ConcernRepository.InsertHistoryAsync(conn, tx, locked.Id, locked.Status, req.NewStatus, finalRemarks,
                                                               If(req.EffectiveDate, DateTime.Today), actor.UserId, now).ConfigureAwait(False)
                    Await AuditRepository.InsertAsync(conn, tx, actor.UserId, "concern.status_changed", "concern", locked.ReportCode,
                        $"{DisplayName(locked.Status)} -> {DisplayName(req.NewStatus)}" & If(String.IsNullOrEmpty(finalRemarks), "", $" ({finalRemarks})")).ConfigureAwait(False)

                    Dim resident = Await UserRepository.GetByIdAsync(conn, locked.ResidentId, tx).ConfigureAwait(False)
                    Return New With {.Resident = resident, .Code = locked.ReportCode, .Remarks = finalRemarks, .ByStaff = scope = StatusWorkflow.StaffScope}
                End Function).ConfigureAwait(False)

            RaiseDataChanged()
            Dim result As New StatusUpdateResult With {.NewStatus = req.NewStatus}
            Dim wantEmail = info.ByStaff AndAlso info.Resident IsNot Nothing AndAlso info.Resident.NotifyEmail AndAlso info.Resident.EmailVerified AndAlso
                            Await _settings.GetBoolAsync(SettingKeys.EmailOnStatusChange, True).ConfigureAwait(False)
            If wantEmail Then
                If Not _email.IsConfigured Then
                    result.NotificationError = "Email is not configured, so the resident was not emailed."
                Else
                    Try
                        Await _email.SendStatusChangedAsync(info.Resident.Email, info.Resident.FullName, info.Code, DisplayName(req.NewStatus), info.Remarks).ConfigureAwait(False)
                        result.ResidentNotified = True
                    Catch ex As EmailDeliveryException
                        result.NotificationError = "The resident could not be emailed: " & ex.Message
                    End Try
                End If
            End If
            Return result
        End Function

        ' ================================================================ dashboards

        Public Async Function GetResidentDashboardAsync(session As UserSession) As Task(Of DashboardStats)
            Return Await _db.RunAsync(
                Async Function(conn)
                    Dim actor = Await _authz.DemandAsync(conn, session, Permission.ConcernViewOwn).ConfigureAwait(False)
                    Dim stats As New DashboardStats()
                    stats.ByStatus = Await ConcernRepository.StatusCountsAsync(conn, actor.UserId).ConfigureAwait(False)
                    stats.Total = stats.ByStatus.Values.Sum()
                    Dim recent = Await ConcernRepository.SearchAsync(conn, New ConcernFilter With {.Page = 1, .PageSize = 5}, actor.UserId).ConfigureAwait(False)
                    stats.Recent = recent.Items
                    Return stats
                End Function).ConfigureAwait(False)
        End Function

        Public Async Function GetAdminDashboardAsync(session As UserSession) As Task(Of DashboardStats)
            Return Await _db.RunAsync(
                Async Function(conn)
                    Await _authz.DemandAsync(conn, session, Permission.DashboardAdmin).ConfigureAwait(False)
                    Dim stats As New DashboardStats()
                    stats.ByStatus = Await ConcernRepository.StatusCountsAsync(conn, Nothing).ConfigureAwait(False)
                    stats.Total = stats.ByStatus.Values.Sum()
                    Dim cats = Await ConcernRepository.CategoryBreakdownAsync(conn).ConfigureAwait(False)
                    stats.ByCategory = cats.Select(Function(c) New CountItem With {.Label = c.Category, .Value = c.Total, .ColorHex = c.ColorHex, .Key = c.CategoryId}).ToList()
                    Return stats
                End Function).ConfigureAwait(False)
        End Function

        Public Class NotificationList
            Public Property Items As New List(Of StatusHistoryEntry)()
            Public Property UnreadCount As Integer
        End Class

        Public Async Function GetResidentNotificationsAsync(session As UserSession) As Task(Of NotificationList)
            Return Await _db.RunAsync(
                Async Function(conn)
                    Dim actor = Await _authz.DemandAsync(conn, session, Permission.ConcernViewOwn).ConfigureAwait(False)
                    Dim list As New NotificationList()
                    list.Items = Await ConcernRepository.ResidentNotificationsAsync(conn, actor.UserId, 10).ConfigureAwait(False)
                    Dim seen = Await UserRepository.GetNotificationsSeenAsync(conn, actor.UserId).ConfigureAwait(False)
                    list.UnreadCount = list.Items.Where(Function(i) Not seen.HasValue OrElse i.ChangedAt > seen.Value).Count()
                    Return list
                End Function).ConfigureAwait(False)
        End Function

        Public Async Function MarkNotificationsSeenAsync(session As UserSession) As Task
            Await _db.RunAsync(
                Async Function(conn)
                    Dim actor = Await _authz.DemandAsync(conn, session, Permission.ConcernViewOwn).ConfigureAwait(False)
                    Await UserRepository.SetNotificationsSeenAsync(conn, actor.UserId).ConfigureAwait(False)
                    Return True
                End Function).ConfigureAwait(False)
        End Function

    End Class

End Namespace
