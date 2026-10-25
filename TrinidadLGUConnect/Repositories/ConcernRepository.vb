Imports System.Data.Common
Imports MySql.Data.MySqlClient
Imports TrinidadLGUConnect.Data
Imports TrinidadLGUConnect.Models

Namespace Repositories

    Public Class ConcernLock
        Public Property Id As Integer
        Public Property ResidentId As Integer
        Public Property Status As ConcernStatus
        Public Property ReportCode As String = ""
        Public Property ReportedAt As DateTime
    End Class

    Public Class AttachmentData
        Public Property AttachmentId As Integer
        Public Property ConcernId As Integer
        Public Property ResidentId As Integer
        Public Property FileName As String = ""
        Public Property ContentType As String = ""
        Public Property Data As Byte()
    End Class

    Public Class ReportRow
        Public Property CategoryId As Integer
        Public Property Status As ConcernStatus
        Public Property ReportedAt As DateTime
    End Class

    Public NotInheritable Class ConcernRepository

        Private Const SummarySelect As String =
            "SELECT c.id, c.report_code, c.resident_id, u.full_name AS resident_name, u.email AS resident_email, c.category_id, " &
            "cat.name AS category_name, sc.name AS subcategory_name, c.location, c.barangay_id, bg.name AS barangay_name, c.status_id, c.reported_at, c.created_at, c.updated_at, " &
            "(SELECT COUNT(*) FROM concern_attachments a WHERE a.concern_id = c.id) AS attachment_count "

        Private Const SummaryFrom As String =
            "FROM concerns c JOIN users u ON u.id = c.resident_id JOIN concern_categories cat ON cat.id = c.category_id " &
            "LEFT JOIN concern_subcategories sc ON sc.id = c.subcategory_id LEFT JOIN barangays bg ON bg.id = c.barangay_id "

        Private Sub New()
        End Sub

        Private Shared Sub MapSummary(r As DbDataReader, c As ConcernSummary)
            c.Id = r.Int("id")
            c.ReportCode = If(r.Str("report_code"), ReportCode(c.Id))
            c.ResidentId = r.Int("resident_id")
            c.ResidentName = r.Str("resident_name")
            c.CategoryId = r.Int("category_id")
            c.CategoryName = r.Str("category_name")
            c.SubcategoryName = r.Str("subcategory_name")
            c.Location = r.Str("location")
            c.BarangayId = r.IntN("barangay_id")
            c.BarangayName = r.Str("barangay_name")
            c.Status = CType(r.Int("status_id"), ConcernStatus)
            c.ReportedAt = r.Utc("reported_at")
            c.CreatedAt = r.Utc("created_at")
            c.UpdatedAt = r.Utc("updated_at")
            c.AttachmentCount = r.Int("attachment_count")
        End Sub

        ' ---------------------------------------------------------------- reference data

        Public Shared Async Function GetCategoriesAsync(conn As MySqlConnection) As Task(Of List(Of Category))
            Dim cats As New List(Of Category)()
            Using cmd = Db.Cmd(conn, "SELECT id, code, name, short_name, color_hex, sort_order FROM concern_categories WHERE is_active = 1 ORDER BY sort_order")
                Using r = Await cmd.ExecuteReaderAsync().ConfigureAwait(False)
                    While Await r.ReadAsync().ConfigureAwait(False)
                        cats.Add(New Category With {.Id = r.Int("id"), .Code = r.Str("code"), .Name = r.Str("name"), .ShortName = r.Str("short_name"), .ColorHex = r.Str("color_hex"), .SortOrder = r.Int("sort_order")})
                    End While
                End Using
            End Using
            Using cmd = Db.Cmd(conn, "SELECT id, category_id, name FROM concern_subcategories WHERE is_active = 1 ORDER BY category_id, sort_order")
                Using r = Await cmd.ExecuteReaderAsync().ConfigureAwait(False)
                    While Await r.ReadAsync().ConfigureAwait(False)
                        Dim catId = r.Int("category_id")
                        Dim cat = cats.FirstOrDefault(Function(c) c.Id = catId)
                        cat?.Subcategories.Add(New Subcategory With {.Id = r.Int("id"), .CategoryId = catId, .Name = r.Str("name")})
                    End While
                End Using
            End Using
            Return cats
        End Function

        Public Shared Async Function GetBarangaysAsync(conn As MySqlConnection, Optional tx As MySqlTransaction = Nothing) As Task(Of List(Of Barangay))
            Dim list As New List(Of Barangay)()
            Using cmd = Db.Cmd(conn, "SELECT id, name FROM barangays WHERE is_active = 1 ORDER BY sort_order, name", tx)
                Using r = Await cmd.ExecuteReaderAsync().ConfigureAwait(False)
                    While Await r.ReadAsync().ConfigureAwait(False)
                        list.Add(New Barangay With {.Id = r.Int("id"), .Name = r.Str("name")})
                    End While
                End Using
            End Using
            Return list
        End Function

        Public Shared Async Function GetTransitionsAsync(conn As MySqlConnection, Optional tx As MySqlTransaction = Nothing) As Task(Of List(Of StatusTransition))
            Dim list As New List(Of StatusTransition)()
            Using cmd = Db.Cmd(conn, "SELECT from_status_id, to_status_id, actor_scope FROM concern_status_transitions", tx)
                Using r = Await cmd.ExecuteReaderAsync().ConfigureAwait(False)
                    While Await r.ReadAsync().ConfigureAwait(False)
                        list.Add(New StatusTransition With {.FromStatus = CType(r.Int("from_status_id"), ConcernStatus), .ToStatus = CType(r.Int("to_status_id"), ConcernStatus), .ActorScope = r.Str("actor_scope")})
                    End While
                End Using
            End Using
            Return list
        End Function

        ' ---------------------------------------------------------------- writes

        Public Shared Async Function FindBySubmissionKeyAsync(conn As MySqlConnection, tx As MySqlTransaction, key As Guid) As Task(Of ConcernLock)
            Using cmd = Db.Cmd(conn, "SELECT id, resident_id, status_id, report_code, reported_at FROM concerns WHERE submission_key = @k", tx).P("@k", key.ToString())
                Return Await ReadLockAsync(cmd).ConfigureAwait(False)
            End Using
        End Function

        Private Shared Async Function ReadLockAsync(cmd As MySqlCommand) As Task(Of ConcernLock)
            Using r = Await cmd.ExecuteReaderAsync().ConfigureAwait(False)
                If Not Await r.ReadAsync().ConfigureAwait(False) Then Return Nothing
                Return New ConcernLock With {.Id = r.Int("id"), .ResidentId = r.Int("resident_id"), .Status = CType(r.Int("status_id"), ConcernStatus), .ReportCode = r.Str("report_code"), .ReportedAt = r.Utc("reported_at")}
            End Using
        End Function

        ''' <summary>Inserts the concern and assigns its report code (CON-001, CON-002, ...). Returns the new id.</summary>
        Public Shared Async Function InsertAsync(conn As MySqlConnection, tx As MySqlTransaction, residentId As Integer, req As NewConcernRequest, reportedAtUtc As DateTime) As Task(Of Integer)
            Const sql As String =
                "INSERT INTO concerns (submission_key, resident_id, category_id, subcategory_id, barangay_id, location, description, reported_at, status_id, created_at, updated_at) " &
                "VALUES (@key, @res, @cat, @sub, @bgy, @loc, @desc, @rep, 1, @now, @now)"
            Dim now = DateTime.UtcNow
            Dim id As Integer
            Using cmd = Db.Cmd(conn, sql, tx).
                    P("@key", req.SubmissionKey.ToString()).P("@res", residentId).P("@cat", req.CategoryId).P("@sub", req.SubcategoryId).P("@bgy", req.BarangayId).
                    P("@loc", req.Location).P("@desc", req.Description).P("@rep", reportedAtUtc).P("@now", now)
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
                id = CInt(cmd.LastInsertedId)
            End Using
            Using cmd = Db.Cmd(conn, "UPDATE concerns SET report_code = @code WHERE id = @id", tx).P("@code", ReportCode(id)).P("@id", id)
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
            End Using
            Return id
        End Function

        Public Shared Async Function InsertAttachmentAsync(conn As MySqlConnection, tx As MySqlTransaction, concernId As Integer, a As PendingAttachment, sortOrder As Integer, userId As Integer) As Task(Of Integer)
            Const sql As String =
                "INSERT INTO concern_attachments (concern_id, original_file_name, content_type, file_size_bytes, width, height, sha256, thumbnail, sort_order, uploaded_by, created_at) " &
                "VALUES (@c, @n, @t, @s, @w, @h, @sha, @thumb, @o, @u, @now)"
            Dim attId As Integer
            Using cmd = Db.Cmd(conn, sql, tx).
                    P("@c", concernId).P("@n", a.FileName).P("@t", a.ContentType).P("@s", a.SizeBytes).P("@w", a.Width).P("@h", a.Height).
                    P("@sha", a.Sha256).Blob("@thumb", a.Thumbnail).P("@o", sortOrder).P("@u", userId).P("@now", DateTime.UtcNow)
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
                attId = CInt(cmd.LastInsertedId)
            End Using
            Using cmd = Db.Cmd(conn, "INSERT INTO concern_attachment_data (attachment_id, data) VALUES (@id, @d)", tx).P("@id", attId).Blob("@d", a.Data)
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
            End Using
            Return attId
        End Function

        Public Shared Async Function InsertHistoryAsync(conn As MySqlConnection, tx As MySqlTransaction, concernId As Integer, fromStatus As ConcernStatus?, toStatus As ConcernStatus,
                                                       remarks As String, effectiveDate As DateTime?, changedBy As Integer, changedAt As DateTime) As Task
            Const sql As String =
                "INSERT INTO concern_status_history (concern_id, from_status_id, to_status_id, remarks, effective_date, changed_by, changed_at) " &
                "VALUES (@c, @f, @t, @r, @e, @u, @at)"
            Dim fromValue As Object = If(fromStatus.HasValue, CObj(CByte(fromStatus.Value)), Nothing)
            Dim effValue As Object = If(effectiveDate.HasValue, CObj(effectiveDate.Value.Date), Nothing)
            Using cmd = Db.Cmd(conn, sql, tx).
                    P("@c", concernId).P("@f", fromValue).P("@t", CByte(toStatus)).P("@r", If(String.IsNullOrWhiteSpace(remarks), Nothing, remarks)).
                    P("@e", effValue).P("@u", changedBy).P("@at", changedAt)
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
            End Using
        End Function

        Public Shared Async Function LockAsync(conn As MySqlConnection, tx As MySqlTransaction, concernId As Integer) As Task(Of ConcernLock)
            Using cmd = Db.Cmd(conn, "SELECT id, resident_id, status_id, report_code, reported_at FROM concerns WHERE id = @id FOR UPDATE", tx).P("@id", concernId)
                Return Await ReadLockAsync(cmd).ConfigureAwait(False)
            End Using
        End Function

        Public Shared Async Function UpdateStatusAsync(conn As MySqlConnection, tx As MySqlTransaction, concernId As Integer, newStatus As ConcernStatus, now As DateTime) As Task
            Const sql As String =
                "UPDATE concerns SET status_id = @s, updated_at = @now, " &
                "resolved_at = CASE WHEN @s = 4 THEN @now WHEN @s IN (1, 2, 3) THEN NULL ELSE resolved_at END, " &
                "closed_at = CASE WHEN @s = 5 THEN @now ELSE NULL END WHERE id = @id"
            Using cmd = Db.Cmd(conn, sql, tx).P("@s", CByte(newStatus)).P("@now", now).P("@id", concernId)
                Await cmd.ExecuteNonQueryAsync().ConfigureAwait(False)
            End Using
        End Function

        ' ---------------------------------------------------------------- reads

        Private Shared Function BuildWhere(f As ConcernFilter, residentId As Integer?, prms As List(Of KeyValuePair(Of String, Object))) As String
            Dim where As New List(Of String) From {"1 = 1"}
            If residentId.HasValue Then
                where.Add("c.resident_id = @rid")
                prms.Add(New KeyValuePair(Of String, Object)("@rid", residentId.Value))
            End If
            If Not String.IsNullOrWhiteSpace(f.Search) Then
                Dim cols = "c.report_code LIKE @q OR cat.name LIKE @q OR sc.name LIKE @q OR c.location LIKE @q OR bg.name LIKE @q OR c.description LIKE @q"
                If Not residentId.HasValue Then cols &= " OR u.full_name LIKE @q"
                where.Add("(" & cols & ")")
                prms.Add(New KeyValuePair(Of String, Object)("@q", LikeContains(f.Search.Trim())))
            End If
            If f.CategoryId.HasValue Then
                where.Add("c.category_id = @cat")
                prms.Add(New KeyValuePair(Of String, Object)("@cat", f.CategoryId.Value))
            End If
            If f.Status.HasValue Then
                where.Add("c.status_id = @st")
                prms.Add(New KeyValuePair(Of String, Object)("@st", CByte(f.Status.Value)))
            End If
            If f.BarangayId.HasValue Then
                where.Add("c.barangay_id = @bgy")
                prms.Add(New KeyValuePair(Of String, Object)("@bgy", f.BarangayId.Value))
            End If
            If Not String.IsNullOrWhiteSpace(f.Location) Then
                where.Add("c.location LIKE @loc")
                prms.Add(New KeyValuePair(Of String, Object)("@loc", LikeContains(f.Location.Trim())))
            End If
            If f.DateFrom.HasValue Then
                where.Add("c.reported_at >= @from")
                prms.Add(New KeyValuePair(Of String, Object)("@from", f.DateFrom.Value.Date.ToUniversalTime()))
            End If
            If f.DateTo.HasValue Then
                where.Add("c.reported_at < @to")
                prms.Add(New KeyValuePair(Of String, Object)("@to", f.DateTo.Value.Date.AddDays(1).ToUniversalTime()))
            End If
            Return " WHERE " & String.Join(" AND ", where)
        End Function

        ''' <summary>Searches concerns. When <paramref name="residentId"/> is set, only that resident's concerns are returned.</summary>
        Public Shared Async Function SearchAsync(conn As MySqlConnection, f As ConcernFilter, residentId As Integer?) As Task(Of PagedResult(Of ConcernSummary))
            Dim prms As New List(Of KeyValuePair(Of String, Object))()
            Dim whereSql = BuildWhere(f, residentId, prms)
            Dim result As New PagedResult(Of ConcernSummary) With {.PageSize = Math.Max(1, f.PageSize)}
            Using cmd = Db.Cmd(conn, "SELECT COUNT(*) " & SummaryFrom & whereSql)
                For Each kv In prms : cmd.P(kv.Key, kv.Value) : Next
                result.TotalCount = Await cmd.ScalarIntAsync().ConfigureAwait(False)
            End Using
            result.Page = Math.Min(Math.Max(1, f.Page), result.TotalPages)
            Using cmd = Db.Cmd(conn, SummarySelect & SummaryFrom & whereSql & " ORDER BY c.reported_at DESC, c.id DESC LIMIT @limit OFFSET @offset")
                For Each kv In prms : cmd.P(kv.Key, kv.Value) : Next
                cmd.P("@limit", result.PageSize).P("@offset", (result.Page - 1) * result.PageSize)
                Using r = Await cmd.ExecuteReaderAsync().ConfigureAwait(False)
                    While Await r.ReadAsync().ConfigureAwait(False)
                        Dim c As New ConcernSummary()
                        MapSummary(r, c)
                        result.Items.Add(c)
                    End While
                End Using
            End Using
            Return result
        End Function

        ''' <summary>All concerns reported in a range (used by reports and exports).</summary>
        Public Shared Async Function ListInRangeAsync(conn As MySqlConnection, fromUtc As DateTime, toUtcExclusive As DateTime) As Task(Of List(Of ConcernSummary))
            Dim list As New List(Of ConcernSummary)()
            Using cmd = Db.Cmd(conn, SummarySelect & SummaryFrom & "WHERE c.reported_at >= @f AND c.reported_at < @t ORDER BY c.reported_at, c.id").P("@f", fromUtc).P("@t", toUtcExclusive)
                Using r = Await cmd.ExecuteReaderAsync().ConfigureAwait(False)
                    While Await r.ReadAsync().ConfigureAwait(False)
                        Dim c As New ConcernSummary()
                        MapSummary(r, c)
                        list.Add(c)
                    End While
                End Using
            End Using
            Return list
        End Function

        Public Shared Async Function GetDetailAsync(conn As MySqlConnection, concernId As Integer) As Task(Of ConcernDetail)
            Dim d As ConcernDetail = Nothing
            Using cmd = Db.Cmd(conn, SummarySelect & ", c.description " & SummaryFrom & "WHERE c.id = @id").P("@id", concernId)
                Using r = Await cmd.ExecuteReaderAsync().ConfigureAwait(False)
                    If Await r.ReadAsync().ConfigureAwait(False) Then
                        d = New ConcernDetail()
                        MapSummary(r, d)
                        d.Description = r.Str("description")
                        d.ResidentEmail = r.Str("resident_email")
                    End If
                End Using
            End Using
            If d Is Nothing Then Return Nothing

            Const attSql As String =
                "SELECT id, concern_id, original_file_name, content_type, file_size_bytes, width, height, thumbnail " &
                "FROM concern_attachments WHERE concern_id = @id ORDER BY sort_order, id"
            Using cmd = Db.Cmd(conn, attSql).P("@id", concernId)
                Using r = Await cmd.ExecuteReaderAsync().ConfigureAwait(False)
                    While Await r.ReadAsync().ConfigureAwait(False)
                        d.Attachments.Add(New AttachmentInfo With {
                            .Id = r.Int("id"), .ConcernId = r.Int("concern_id"), .FileName = r.Str("original_file_name"),
                            .ContentType = r.Str("content_type"), .SizeBytes = r.Lng("file_size_bytes"),
                            .Width = r.Int("width"), .Height = r.Int("height"), .Thumbnail = r.Bytes("thumbnail")})
                    End While
                End Using
            End Using

            d.History.AddRange(Await GetHistoryAsync(conn, concernId).ConfigureAwait(False))
            Return d
        End Function

        Public Shared Async Function GetHistoryAsync(conn As MySqlConnection, concernId As Integer) As Task(Of List(Of StatusHistoryEntry))
            Dim list As New List(Of StatusHistoryEntry)()
            Const sql As String =
                "SELECT h.id, h.concern_id, h.from_status_id, h.to_status_id, h.remarks, h.effective_date, h.changed_by, h.changed_at, " &
                "u.full_name, u.role_id FROM concern_status_history h JOIN users u ON u.id = h.changed_by " &
                "WHERE h.concern_id = @id ORDER BY h.changed_at, h.id"
            Using cmd = Db.Cmd(conn, sql).P("@id", concernId)
                Using r = Await cmd.ExecuteReaderAsync().ConfigureAwait(False)
                    While Await r.ReadAsync().ConfigureAwait(False)
                        Dim fromId = r.IntN("from_status_id")
                        Dim eff = r.UtcN("effective_date")
                        list.Add(New StatusHistoryEntry With {
                            .Id = r.Lng("id"), .ConcernId = r.Int("concern_id"),
                            .FromStatus = If(fromId.HasValue, CType(fromId.Value, ConcernStatus), CType(Nothing, ConcernStatus?)),
                            .ToStatus = CType(r.Int("to_status_id"), ConcernStatus),
                            .Remarks = r.Str("remarks"),
                            .EffectiveDate = If(eff.HasValue, DateTime.SpecifyKind(eff.Value, DateTimeKind.Unspecified), CType(Nothing, DateTime?)),
                            .ChangedById = r.Int("changed_by"), .ChangedByName = r.Str("full_name"),
                            .ChangedByRole = CType(r.Int("role_id"), UserRole), .ChangedAt = r.Utc("changed_at")})
                    End While
                End Using
            End Using
            Return list
        End Function

        Public Shared Async Function GetAttachmentDataAsync(conn As MySqlConnection, attachmentId As Integer) As Task(Of AttachmentData)
            Const sql As String =
                "SELECT a.id, a.concern_id, c.resident_id, a.original_file_name, a.content_type, d.data FROM concern_attachments a " &
                "JOIN concerns c ON c.id = a.concern_id JOIN concern_attachment_data d ON d.attachment_id = a.id WHERE a.id = @id"
            Using cmd = Db.Cmd(conn, sql).P("@id", attachmentId)
                Using r = Await cmd.ExecuteReaderAsync().ConfigureAwait(False)
                    If Not Await r.ReadAsync().ConfigureAwait(False) Then Return Nothing
                    Return New AttachmentData With {
                        .AttachmentId = r.Int("id"), .ConcernId = r.Int("concern_id"), .ResidentId = r.Int("resident_id"),
                        .FileName = r.Str("original_file_name"), .ContentType = r.Str("content_type"), .Data = r.Bytes("data")}
                End Using
            End Using
        End Function

        ' ---------------------------------------------------------------- statistics

        Public Shared Async Function StatusCountsAsync(conn As MySqlConnection, residentId As Integer?, Optional fromUtc As DateTime? = Nothing, Optional toUtcExclusive As DateTime? = Nothing) As Task(Of Dictionary(Of ConcernStatus, Integer))
            Dim d As New Dictionary(Of ConcernStatus, Integer)()
            For Each s In AllStatuses : d(s) = 0 : Next
            Dim sql = "SELECT status_id, COUNT(*) AS n FROM concerns WHERE 1 = 1" &
                      If(residentId.HasValue, " AND resident_id = @rid", "") &
                      If(fromUtc.HasValue, " AND reported_at >= @f", "") &
                      If(toUtcExclusive.HasValue, " AND reported_at < @t", "") &
                      " GROUP BY status_id"
            Using cmd = Db.Cmd(conn, sql)
                If residentId.HasValue Then cmd.P("@rid", residentId.Value)
                If fromUtc.HasValue Then cmd.P("@f", fromUtc.Value)
                If toUtcExclusive.HasValue Then cmd.P("@t", toUtcExclusive.Value)
                Using r = Await cmd.ExecuteReaderAsync().ConfigureAwait(False)
                    While Await r.ReadAsync().ConfigureAwait(False)
                        d(CType(r.Int("status_id"), ConcernStatus)) = r.Int("n")
                    End While
                End Using
            End Using
            Return d
        End Function

        ''' <summary>Per-category totals with a status breakdown. Categories with no concerns are included with zeros.</summary>
        Public Shared Async Function CategoryBreakdownAsync(conn As MySqlConnection, Optional fromUtc As DateTime? = Nothing, Optional toUtcExclusive As DateTime? = Nothing) As Task(Of List(Of CategoryReportRow))
            Dim joinFilter = If(fromUtc.HasValue, " AND c.reported_at >= @f", "") & If(toUtcExclusive.HasValue, " AND c.reported_at < @t", "")
            Dim sql =
                "SELECT cat.id, cat.short_name, cat.color_hex, COUNT(c.id) AS total, " &
                "COALESCE(SUM(c.status_id = 1), 0) AS s1, COALESCE(SUM(c.status_id = 2), 0) AS s2, COALESCE(SUM(c.status_id = 3), 0) AS s3, " &
                "COALESCE(SUM(c.status_id = 4), 0) AS s4, COALESCE(SUM(c.status_id = 5), 0) AS s5 " &
                "FROM concern_categories cat LEFT JOIN concerns c ON c.category_id = cat.id" & joinFilter & " " &
                "WHERE cat.is_active = 1 GROUP BY cat.id, cat.short_name, cat.color_hex, cat.sort_order ORDER BY cat.sort_order"
            Dim list As New List(Of CategoryReportRow)()
            Using cmd = Db.Cmd(conn, sql)
                If fromUtc.HasValue Then cmd.P("@f", fromUtc.Value)
                If toUtcExclusive.HasValue Then cmd.P("@t", toUtcExclusive.Value)
                Using r = Await cmd.ExecuteReaderAsync().ConfigureAwait(False)
                    While Await r.ReadAsync().ConfigureAwait(False)
                        list.Add(New CategoryReportRow With {
                            .CategoryId = r.Int("id"), .Category = r.Str("short_name"), .ColorHex = r.Str("color_hex"), .Total = r.Int("total"),
                            .Submitted = r.Int("s1"), .UnderReview = r.Int("s2"), .InProgress = r.Int("s3"), .Resolved = r.Int("s4"), .Closed = r.Int("s5")})
                    End While
                End Using
            End Using
            Return list
        End Function

        ''' <summary>Recent status changes on a resident's concerns made by someone other than the resident.</summary>
        Public Shared Async Function ResidentNotificationsAsync(conn As MySqlConnection, residentId As Integer, limit As Integer) As Task(Of List(Of StatusHistoryEntry))
            Const sql As String =
                "SELECT h.id, h.concern_id, h.to_status_id, h.remarks, h.changed_at, c.report_code " &
                "FROM concern_status_history h JOIN concerns c ON c.id = h.concern_id " &
                "WHERE c.resident_id = @rid AND h.changed_by <> @rid ORDER BY h.changed_at DESC, h.id DESC LIMIT @n"
            Dim list As New List(Of StatusHistoryEntry)()
            Using cmd = Db.Cmd(conn, sql).P("@rid", residentId).P("@n", limit)
                Using r = Await cmd.ExecuteReaderAsync().ConfigureAwait(False)
                    While Await r.ReadAsync().ConfigureAwait(False)
                        list.Add(New StatusHistoryEntry With {
                            .Id = r.Lng("id"), .ConcernId = r.Int("concern_id"), .ToStatus = CType(r.Int("to_status_id"), ConcernStatus),
                            .Remarks = r.Str("remarks"), .ChangedAt = r.Utc("changed_at"), .ChangedByDisplay = r.Str("report_code")})
                    End While
                End Using
            End Using
            Return list
        End Function

    End Class

End Namespace
