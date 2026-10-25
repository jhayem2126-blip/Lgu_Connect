Imports System.Drawing.Imaging
Imports TrinidadLGUConnect.Data
Imports TrinidadLGUConnect.Helpers
Imports TrinidadLGUConnect.Models
Imports TrinidadLGUConnect.Services

<Collection("db")>
Public Class IntegrationTests

    Private ReadOnly _fx As DatabaseFixture
    Private Shared _seq As Integer

    Public Sub New(fx As DatabaseFixture)
        _fx = fx
    End Sub

    Private Shared ReadOnly Property Svc As AppServices
        Get
            Return AppServices.Instance
        End Get
    End Property

    Private Shared Function Unique(prefix As String) As String
        Return $"{prefix}{Threading.Interlocked.Increment(_seq)}{Guid.NewGuid().ToString("N").Substring(0, 4)}"
    End Function

    Private Shared Async Function LoginAsync(id As String, pw As String, Optional portal As LoginPortal = LoginPortal.Resident) As Task(Of UserSession)
        Dim r = Await Svc.Auth.LoginAsync(id, pw, portal)
        Assert.True(r.Success, $"Login failed for {id}: {r.Message}")
        Return r.Session
    End Function

    Private Shared Function AdminAsync() As Task(Of UserSession)
        Return LoginAsync("ana_admin", DatabaseFixture.AdminPassword, LoginPortal.Personnel)
    End Function

    ''' <summary>Registers a resident and verifies the email using the code captured by the fake SMTP server.</summary>
    Private Async Function NewResidentAsync() As Task(Of (Username As String, Email As String, Password As String, Session As UserSession))
        Dim u = Unique("res")
        Dim email = $"{u}@example.com"
        Dim pw = "Resident#2025x"
        Dim reg = Await Svc.Auth.RegisterAsync(New RegistrationRequest With {.BarangayId = 14, .FullName = "Juan Dela Cruz", .Email = email, .Username = u, .Password = pw, .ConfirmPassword = pw})
        Assert.True(reg.RequiresVerification)
        Assert.True(reg.VerificationEmailSent, reg.EmailError)
        Dim code = _fx.Smtp.LatestCodeFor(email)
        Assert.NotNull(code)
        Await Svc.Auth.VerifyEmailAsync(email, code)
        Return (u, email, pw, Await LoginAsync(u, pw))
    End Function

    Private Shared Function Picture(w As Integer, h As Integer, color As Color) As PendingAttachment
        Using bmp As New Bitmap(w, h)
            Using g = Graphics.FromImage(bmp)
                g.Clear(color)
            End Using
            Using ms As New MemoryStream()
                bmp.Save(ms, ImageFormat.Jpeg)
                Return AttachmentService.Prepare(ms.ToArray(), $"pic-{color.Name}.jpg", 5 * 1024 * 1024)
            End Using
        End Using
    End Function

    <DbFact>
    Public Async Function SchemaIsReadyAndInitializerIsIdempotent() As Task
        Await DatabaseInitializer.InitializeAsync(Configuration.AppConfig.Current.Database)
        Dim check = Await DatabaseInitializer.CheckAsync(Configuration.AppConfig.Current.Database)
        Assert.Equal(SchemaState.Ready, check.State)
        Assert.Equal(8L, Convert.ToInt64(_fx.Scalar("SELECT COUNT(*) FROM concern_categories")))
        Assert.True(Convert.ToInt64(_fx.Scalar("SELECT COUNT(*) FROM users WHERE role_id = 1")) >= 1)
    End Function

    <DbFact>
    Public Async Function InitialAdminSetupCannotBeRepeated() As Task
        Assert.False(Await Svc.Setup.IsSetupRequiredAsync())
        Await Assert.ThrowsAsync(Of AuthorizationException)(Function() Svc.Setup.CreateInitialAdminAsync("Mallory", "m@example.com", "mallory1", "Mallory#123456", "Mallory#123456"))
    End Function

    <DbFact>
    Public Async Function SharedAuthenticationEnforcesPortalsAndVerification() As Task
        Dim admin = Await AdminAsync()
        Assert.Equal(UserRole.Administrator, admin.Role)
        Assert.True(admin.Has(Permission.UserManageAdmins))
        Dim wrong = Await Svc.Auth.LoginAsync("ana_admin", DatabaseFixture.AdminPassword, LoginPortal.Resident)
        Assert.False(wrong.Success)
        Assert.True(wrong.WrongPortal)

        ' Registration requires verification before login.
        Dim u = Unique("ver")
        Dim email = $"{u}@example.com"
        Await Svc.Auth.RegisterAsync(New RegistrationRequest With {.BarangayId = 14, .FullName = "Maria Santos", .Email = email, .Username = u, .Password = "Maria#2025ok", .ConfirmPassword = "Maria#2025ok"})
        Dim code = _fx.Smtp.LatestCodeFor(email)
        Assert.NotNull(code)
        Dim before = Await Svc.Auth.LoginAsync(u, "Maria#2025ok", LoginPortal.Resident)
        Assert.False(before.Success)
        Assert.True(before.RequiresEmailVerification)
        Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Auth.VerifyEmailAsync(email, "AAAA-BBBB"))
        Await Svc.Auth.VerifyEmailAsync(email, code)
        Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Auth.VerifyEmailAsync(email, code)) ' single use
        Dim s = Await LoginAsync(email.ToUpperInvariant(), "Maria#2025ok") ' email login is case-insensitive
        Assert.Equal(UserRole.Resident, s.Role)
        Assert.False(s.Has(Permission.ConcernViewAll))

        ' Duplicates are rejected.
        Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Auth.RegisterAsync(New RegistrationRequest With {.BarangayId = 14, .FullName = "Other", .Email = email, .Username = Unique("dup"), .Password = "Other#2025ok", .ConfirmPassword = "Other#2025ok"}))
        Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Auth.RegisterAsync(New RegistrationRequest With {.BarangayId = 14, .FullName = "Other", .Email = Unique("x") & "@example.com", .Username = u.ToUpperInvariant(), .Password = "Other#2025ok", .ConfirmPassword = "Other#2025ok"}))

        ' Passwords are stored only as PBKDF2 hashes.
        Dim stored = CStr(_fx.Scalar("SELECT password_hash FROM users WHERE username = @u", ("@u", u)))
        Assert.StartsWith("pbkdf2-sha256$", stored)
        Assert.DoesNotContain("Maria#2025ok", stored)
    End Function

    <DbFact>
    Public Async Function EndToEndResidentAdminResidentWorkflow() As Task
        Dim res = Await NewResidentAsync()
        Dim admin = Await AdminAsync()
        Dim cats = Await Svc.Concerns.GetCategoriesAsync()
        Dim roads = cats.First(Function(c) c.Code = "ROADS")

        ' Resident submits a report with two pictures.
        Dim req As New NewConcernRequest With {.BarangayId = 14, 
            .CategoryId = roads.Id, .SubcategoryId = roads.Subcategories.First().Id, .Location = "Poblacion, Trinidad",
            .Description = "Large pothole in front of the public market, dangerous at night.", .ReportedDate = DateTime.Today}
        req.Attachments.Add(Picture(640, 480, Color.Gray))
        req.Attachments.Add(Picture(480, 640, Color.DarkGreen))
        Dim submitted = Await Svc.Concerns.SubmitAsync(res.Session, req)
        Assert.Matches("^CON-\d{3,}$", submitted.ReportCode)
        Assert.False(submitted.WasDuplicate)
        ' A double click re-sends the same submission key: no duplicate report.
        Dim again = Await Svc.Concerns.SubmitAsync(res.Session, req)
        Assert.True(again.WasDuplicate)
        Assert.Equal(submitted.ConcernId, again.ConcernId)
        Assert.Equal(1L, Convert.ToInt64(_fx.Scalar("SELECT COUNT(*) FROM concerns WHERE resident_id = @r", ("@r", res.Session.UserId))))
        Assert.Contains(_fx.Smtp.TakeAll(), Function(m) m.Subject.Contains(submitted.ReportCode))

        ' Resident sees it in My Reports, with pictures and the initial history entry.
        Dim mine = Await Svc.Concerns.SearchAsync(res.Session, New ConcernFilter With {.PageSize = 10})
        Assert.Equal(1, mine.TotalCount)
        Dim detail = Await Svc.Concerns.GetDetailAsync(res.Session, submitted.ConcernId)
        Assert.Equal(2, detail.Attachments.Count)
        Assert.Single(detail.History)
        Assert.Equal(ConcernStatus.Submitted, detail.Status)
        Dim pic1 = Await Svc.Concerns.GetAttachmentAsync(res.Session, detail.Attachments(0).Id)
        Dim pic2 = Await Svc.Concerns.GetAttachmentAsync(res.Session, detail.Attachments(1).Id)
        Assert.NotEqual(Convert.ToBase64String(pic1.Data), Convert.ToBase64String(pic2.Data))
        Using img = AttachmentService.TryDecode(pic2.Data)
            Assert.Equal(480, img.Width) ' the actual second picture, not a placeholder
        End Using

        ' Search and filters are applied in the query.
        Assert.Equal(1, (Await Svc.Concerns.SearchAsync(res.Session, New ConcernFilter With {.Search = "pothole"})).TotalCount)
        Assert.Equal(0, (Await Svc.Concerns.SearchAsync(res.Session, New ConcernFilter With {.Search = "streetlight"})).TotalCount)
        Assert.Equal(1, (Await Svc.Concerns.SearchAsync(res.Session, New ConcernFilter With {.CategoryId = roads.Id, .Status = ConcernStatus.Submitted})).TotalCount)
        Assert.Equal(0, (Await Svc.Concerns.SearchAsync(res.Session, New ConcernFilter With {.DateFrom = DateTime.Today.AddDays(1)})).TotalCount)
        Assert.Equal(1, (Await Svc.Concerns.SearchAsync(res.Session, New ConcernFilter With {.Location = "poblacion", .DateFrom = DateTime.Today, .DateTo = DateTime.Today})).TotalCount)
        ' LIKE wildcards typed by the user are matched literally ("%" must not match everything).
        Assert.Equal(0, (Await Svc.Concerns.SearchAsync(res.Session, New ConcernFilter With {.Search = "%"})).TotalCount)
        Assert.Equal(0, (Await Svc.Concerns.SearchAsync(res.Session, New ConcernFilter With {.Search = "_"})).TotalCount)

        ' Administrator sees the report and moves it through the workflow.
        Dim all = Await Svc.Concerns.SearchAsync(admin, New ConcernFilter With {.Search = submitted.ReportCode})
        Assert.Equal(1, all.TotalCount)
        Dim allowed = Await Svc.Concerns.GetAllowedTransitionsAsync(admin, detail)
        Assert.Equal({ConcernStatus.UnderReview, ConcernStatus.Closed}, allowed)
        Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Concerns.UpdateStatusAsync(admin, New StatusUpdateRequest With {
            .ConcernId = submitted.ConcernId, .ExpectedCurrentStatus = ConcernStatus.Submitted, .NewStatus = ConcernStatus.Resolved}))
        Dim steps = {(ConcernStatus.Submitted, ConcernStatus.UnderReview, "For assessment"),
                     (ConcernStatus.UnderReview, ConcernStatus.InProgress, "Road repair scheduled"),
                     (ConcernStatus.InProgress, ConcernStatus.Resolved, "Road repair completed. Area is now safe.")}
        For Each st In steps
            Dim r = Await Svc.Concerns.UpdateStatusAsync(admin, New StatusUpdateRequest With {
                .ConcernId = submitted.ConcernId, .ExpectedCurrentStatus = st.Item1, .NewStatus = st.Item2, .Remarks = st.Item3, .EffectiveDate = DateTime.Today})
            Assert.True(r.ResidentNotified, r.NotificationError)
        Next
        ' Stale screen: the expected status no longer matches.
        Await Assert.ThrowsAsync(Of ConcurrencyException)(Function() Svc.Concerns.UpdateStatusAsync(admin, New StatusUpdateRequest With {
            .ConcernId = submitted.ConcernId, .ExpectedCurrentStatus = ConcernStatus.InProgress, .NewStatus = ConcernStatus.Resolved}))
        Dim notices = _fx.Smtp.TakeAll().Where(Function(m) m.To.Mailboxes.Any(Function(mb) mb.Address = res.Email)).ToList()
        Assert.Equal(3, notices.Count)
        Assert.Contains("Resolved", notices.Last().Subject)

        ' Resident sees the new status and the complete history; staff names are not exposed to residents.
        Dim after = Await Svc.Concerns.GetDetailAsync(res.Session, submitted.ConcernId)
        Assert.Equal(ConcernStatus.Resolved, after.Status)
        Assert.Equal(4, after.History.Count)
        Assert.Equal("Road repair completed. Area is now safe.", after.History.Last().Remarks)
        Assert.Equal("LGU Staff", after.History.Last().ChangedByDisplay)
        Assert.Equal("Resident", after.History.First().ChangedByDisplay)
        Dim adminView = Await Svc.Concerns.GetDetailAsync(admin, submitted.ConcernId)
        Assert.Equal("Ana Lopez", adminView.History.Last().ChangedByDisplay)

        ' Residents cannot perform administrative transitions, but can confirm a resolved report.
        Dim residentAllowed = Await Svc.Concerns.GetAllowedTransitionsAsync(res.Session, after)
        Assert.Equal({ConcernStatus.Closed}, residentAllowed)
        Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Concerns.UpdateStatusAsync(res.Session, New StatusUpdateRequest With {
            .ConcernId = submitted.ConcernId, .ExpectedCurrentStatus = ConcernStatus.Resolved, .NewStatus = ConcernStatus.InProgress}))
        Await Svc.Concerns.UpdateStatusAsync(res.Session, New StatusUpdateRequest With {
            .ConcernId = submitted.ConcernId, .ExpectedCurrentStatus = ConcernStatus.Resolved, .NewStatus = ConcernStatus.Closed})
        Dim closed = Await Svc.Concerns.GetDetailAsync(res.Session, submitted.ConcernId)
        Assert.Equal(ConcernStatus.Closed, closed.Status)
        Assert.Equal(5, closed.History.Count)
        Assert.Equal("Resident confirmed the resolution", closed.History.Last().Remarks)

        ' Dashboards reflect the data.
        Dim rd = Await Svc.Concerns.GetResidentDashboardAsync(res.Session)
        Assert.Equal(1, rd.Total)
        Assert.Equal(1, rd.Count(ConcernStatus.Closed))
        Dim ad = Await Svc.Concerns.GetAdminDashboardAsync(admin)
        Assert.True(ad.Total >= 1)
        Assert.Equal(ad.Total, ad.ByCategory.Sum(Function(c) c.Value))
        Assert.Equal(8, ad.ByCategory.Count)

        ' Reports use the same data.
        Dim report = Await Svc.Reports.GenerateAsync(admin, New ReportRequest With {.Type = ReportType.ConcernsByCategory, .FromDate = DateTime.Today, .ToDate = DateTime.Today})
        Dim todayCount = Convert.ToInt32(_fx.Scalar("SELECT COUNT(*) FROM concerns WHERE reported_at >= @f AND reported_at < @t",
                                                    ("@f", DateTime.Today.ToUniversalTime()), ("@t", DateTime.Today.AddDays(1).ToUniversalTime())))
        Assert.Equal(todayCount, report.Total)
        Assert.Equal(report.Total, report.ByCategory.Sum(Function(c) c.Total))
        Assert.Equal(report.Total, report.ByStatus.Sum(Function(s) s.Count))
        Assert.Contains(report.Concerns, Function(c) c.ReportCode = submitted.ReportCode)
        Assert.True(report.ByCategory.First(Function(c) c.CategoryId = roads.Id).Closed >= 1)
        Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Reports.GenerateAsync(admin, New ReportRequest With {.FromDate = DateTime.Today, .ToDate = DateTime.Today.AddDays(-1)}))

        ' Audit log recorded the important actions.
        Assert.True(Convert.ToInt64(_fx.Scalar("SELECT COUNT(*) FROM audit_logs WHERE action = 'concern.status_changed' AND entity_id = @c", ("@c", submitted.ReportCode))) = 4)
    End Function

    <DbFact>
    Public Async Function ResidentsCannotAccessOtherResidentsReports() As Task
        Dim a = Await NewResidentAsync()
        Dim b = Await NewResidentAsync()
        Dim cats = Await Svc.Concerns.GetCategoriesAsync()
        Dim req As New NewConcernRequest With {.BarangayId = 14, .CategoryId = cats(0).Id, .Location = "Brgy. San Isidro, Trinidad", .Description = "Garbage not collected for two weeks."}
        req.Attachments.Add(Picture(100, 80, Color.Brown))
        Dim c = Await Svc.Concerns.SubmitAsync(a.Session, req)
        Dim d = Await Svc.Concerns.GetDetailAsync(a.Session, c.ConcernId)

        Assert.Equal(0, (Await Svc.Concerns.SearchAsync(b.Session, New ConcernFilter With {.Search = c.ReportCode})).TotalCount)
        Await Assert.ThrowsAsync(Of AuthorizationException)(Function() Svc.Concerns.GetDetailAsync(b.Session, c.ConcernId))
        Await Assert.ThrowsAsync(Of AuthorizationException)(Function() Svc.Concerns.GetAttachmentAsync(b.Session, d.Attachments(0).Id))
        Await Assert.ThrowsAsync(Of AuthorizationException)(Function() Svc.Concerns.UpdateStatusAsync(b.Session, New StatusUpdateRequest With {
            .ConcernId = c.ConcernId, .ExpectedCurrentStatus = ConcernStatus.Submitted, .NewStatus = ConcernStatus.Closed}))
        Await Assert.ThrowsAsync(Of AuthorizationException)(Function() Svc.Concerns.GetAdminDashboardAsync(b.Session))
        Await Assert.ThrowsAsync(Of AuthorizationException)(Function() Svc.Users.SearchAsync(b.Session, New UserFilter()))
        Await Assert.ThrowsAsync(Of AuthorizationException)(Function() Svc.Reports.GenerateAsync(b.Session, New ReportRequest With {.FromDate = DateTime.Today, .ToDate = DateTime.Today}))
        ' A resident can withdraw their own report while it is still Submitted.
        Await Svc.Concerns.UpdateStatusAsync(a.Session, New StatusUpdateRequest With {.ConcernId = c.ConcernId, .ExpectedCurrentStatus = ConcernStatus.Submitted, .NewStatus = ConcernStatus.Closed})
    End Function

    <DbFact>
    Public Async Function TamperingWithTheClientSessionDoesNotGrantPrivileges() As Task
        Dim r = Await NewResidentAsync()
        ' Simulate a user editing in-memory values (role and permission set) to look like an administrator.
        r.Session.Role = UserRole.Administrator
        r.Session.Permissions.Add(Permission.UserManage)
        r.Session.Permissions.Add(Permission.UserView)
        r.Session.Permissions.Add(Permission.ConcernViewAll)
        r.Session.UserId = 1
        Await Assert.ThrowsAsync(Of AuthorizationException)(Function() Svc.Users.SearchAsync(r.Session, New UserFilter()))
        Await Assert.ThrowsAsync(Of AuthorizationException)(Function() Svc.Users.CreateAsync(r.Session, New UserEditRequest With {
            .FullName = "Evil Admin", .Email = Unique("evil") & "@example.com", .Username = Unique("evil"), .Role = UserRole.Administrator, .NewPassword = "Evil#Pass2025"}))
        Dim mine = Await Svc.Concerns.SearchAsync(r.Session, New ConcernFilter())
        Assert.Equal(0, mine.TotalCount) ' still scoped to the real user from the server-side session
        ' A forged token is rejected outright.
        Dim forged As New UserSession With {.Token = SecureRandom.SessionToken(), .Role = UserRole.Administrator}
        Await Assert.ThrowsAsync(Of SessionExpiredException)(Function() Svc.Concerns.GetAdminDashboardAsync(forged))
    End Function

    <DbFact>
    Public Async Function PasswordRecoveryWorkflow() As Task
        Dim r = Await NewResidentAsync()
        Dim oldSession = r.Session
        _fx.Smtp.TakeAll()

        ' Unknown address: same response, nothing is sent.
        Await Svc.Auth.RequestPasswordResetAsync("nobody-" & Guid.NewGuid().ToString("N") & "@example.com")
        Assert.Empty(_fx.Smtp.TakeAll())

        Await Svc.Auth.RequestPasswordResetAsync(r.Email)
        Dim msgs = _fx.Smtp.TakeAll()
        Assert.Single(msgs)
        Assert.DoesNotContain(r.Password, msgs(0).TextBody) ' the existing password is never emailed
        Dim code = Text.RegularExpressions.Regex.Match(msgs(0).TextBody, "[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}").Value
        Assert.NotEmpty(code)
        ' Only a hash of the code is stored.
        Assert.Equal(0L, Convert.ToInt64(_fx.Scalar("SELECT COUNT(*) FROM password_reset_tokens WHERE token_hash LIKE @c", ("@c", "%" & code & "%"))))

        Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Auth.VerifyResetCodeAsync(r.Email, "ZZZZ-ZZZZ"))
        Assert.Equal(1L, Convert.ToInt64(_fx.Scalar("SELECT failed_attempts FROM password_reset_tokens t JOIN users u ON u.id = t.user_id WHERE u.email = @e ORDER BY t.id DESC LIMIT 1", ("@e", r.Email))))
        Await Svc.Auth.VerifyResetCodeAsync(r.Email, code)
        Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Auth.ResetPasswordAsync(r.Email, code, "weak", "weak"))
        Await Svc.Auth.ResetPasswordAsync(r.Email, code, "Brand#New2025", "Brand#New2025")
        Assert.Contains(_fx.Smtp.TakeAll(), Function(m) m.Subject.Contains("password was changed"))

        ' Code is single use; old password no longer works; new one does; old sessions are signed out.
        Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Auth.ResetPasswordAsync(r.Email, code, "Another#2025x", "Another#2025x"))
        Assert.False((Await Svc.Auth.LoginAsync(r.Username, r.Password, LoginPortal.Resident)).Success)
        Await LoginAsync(r.Username, "Brand#New2025")
        Assert.False(Await Svc.Auth.ValidateSessionAsync(oldSession))

        ' Rate limiting: a second request within 60 seconds is refused...
        Await Assert.ThrowsAsync(Of RateLimitException)(Function() Svc.Auth.RequestPasswordResetAsync(r.Email))
        ' ...and at most 3 requests per address per hour are accepted.
        _fx.Exec("UPDATE auth_attempts SET created_at = created_at - INTERVAL 2 MINUTE WHERE attempt_type = 'reset_request'")
        Await Svc.Auth.RequestPasswordResetAsync(r.Email)
        _fx.Exec("UPDATE auth_attempts SET created_at = created_at - INTERVAL 2 MINUTE WHERE attempt_type = 'reset_request'")
        Await Svc.Auth.RequestPasswordResetAsync(r.Email)
        _fx.Exec("UPDATE auth_attempts SET created_at = created_at - INTERVAL 2 MINUTE WHERE attempt_type = 'reset_request'")
        Dim ex = Await Assert.ThrowsAsync(Of RateLimitException)(Function() Svc.Auth.RequestPasswordResetAsync(r.Email))
        Assert.Contains("hour", ex.Message)
        ' Each new request revoked the previous code, so only the newest one is usable.
        Assert.Equal(1L, Convert.ToInt64(_fx.Scalar("SELECT COUNT(*) FROM password_reset_tokens t JOIN users u ON u.id = t.user_id WHERE u.email = @e AND t.revoked_at IS NULL AND t.used_at IS NULL", ("@e", r.Email))))
    End Function

    <DbFact>
    Public Async Function ExpiredResetCodesAreRejected() As Task
        Dim r = Await NewResidentAsync()
        _fx.Smtp.TakeAll()
        Await Svc.Auth.RequestPasswordResetAsync(r.Email)
        Dim code = _fx.Smtp.LatestCodeFor(r.Email)
        _fx.Exec("UPDATE password_reset_tokens t JOIN users u ON u.id = t.user_id SET t.expires_at = UTC_TIMESTAMP() - INTERVAL 1 MINUTE WHERE u.email = @e", ("@e", r.Email))
        Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Auth.VerifyResetCodeAsync(r.Email, code))
    End Function

    <DbFact>
    Public Async Function SmtpFailureIsReportedAndCodeIsRevoked() As Task
        Dim r = Await NewResidentAsync()
        _fx.Smtp.RejectAll = True
        Try
            Await Assert.ThrowsAsync(Of EmailDeliveryException)(Function() Svc.Auth.RequestPasswordResetAsync(r.Email))
        Finally
            _fx.Smtp.RejectAll = False
        End Try
        Assert.Equal(0L, Convert.ToInt64(_fx.Scalar("SELECT COUNT(*) FROM password_reset_tokens t JOIN users u ON u.id = t.user_id WHERE u.email = @e AND t.revoked_at IS NULL AND t.used_at IS NULL", ("@e", r.Email))))
    End Function

    <DbFact>
    Public Async Function RepeatedFailedLoginsLockTheAccount() As Task
        Dim r = Await NewResidentAsync()
        For i = 1 To 5
            Assert.False((Await Svc.Auth.LoginAsync(r.Username, "Wrong#Pass" & i, LoginPortal.Resident)).Success)
        Next
        Dim locked = Await Svc.Auth.LoginAsync(r.Username, r.Password, LoginPortal.Resident)
        Assert.False(locked.Success)
        Assert.Contains("locked", locked.Message)
        Assert.True(Convert.ToInt64(_fx.Scalar("SELECT COUNT(*) FROM audit_logs WHERE action = 'auth.account_locked'")) >= 1)
    End Function

    <DbFact>
    Public Async Function UserManagementRules() As Task
        Dim admin = Await AdminAsync()
        Dim staffName = Unique("staff")
        Dim staffId = Await Svc.Users.CreateAsync(admin, New UserEditRequest With {
            .FullName = "Carlo Razon", .Email = staffName & "@trinidad.test", .Username = staffName, .Role = UserRole.Staff, .NewPassword = "Staff#Pass2025"})
        Dim staff = Await LoginAsync(staffName, "Staff#Pass2025", LoginPortal.Personnel)
        Assert.True(staff.Has(Permission.ConcernUpdateStatus))
        Assert.False(staff.Has(Permission.UserManage))
        Await Assert.ThrowsAsync(Of AuthorizationException)(Function() Svc.Users.SearchAsync(staff, New UserFilter()))
        Await Assert.ThrowsAsync(Of AuthorizationException)(Function() Svc.Settings.SaveAsync(staff, New Dictionary(Of String, String) From {{SettingKeys.ItemsPerPage, "20"}}))

        ' The administrator cannot demote or deactivate themselves (the only admin).
        Dim self = (Await Svc.Users.SearchAsync(admin, New UserFilter With {.Search = "ana_admin"})).Items.Single()
        Await Assert.ThrowsAsync(Of AuthorizationException)(Function() Svc.Users.UpdateAsync(admin, New UserEditRequest With {
            .Id = self.Id, .FullName = self.FullName, .Email = self.Email, .Username = self.Username, .Role = UserRole.Staff, .IsActive = True, .EmailVerified = True}))
        Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Users.SetActiveAsync(admin, self.Id, False))

        ' Deactivation signs the user out immediately.
        Await Svc.Users.SetActiveAsync(admin, staffId, False)
        Await Assert.ThrowsAsync(Of SessionExpiredException)(Function() Svc.Concerns.SearchAsync(staff, New ConcernFilter()))
        Dim denied = Await Svc.Auth.LoginAsync(staffName, "Staff#Pass2025", LoginPortal.Personnel)
        Assert.False(denied.Success)
        Assert.Contains("deactivated", denied.Message)
        Await Svc.Users.SetActiveAsync(admin, staffId, True)

        ' Role change by an administrator takes effect at the next request.
        Dim staff2 = Await LoginAsync(staffName, "Staff#Pass2025", LoginPortal.Personnel)
        Await Svc.Users.UpdateAsync(admin, New UserEditRequest With {.Id = staffId, .FullName = "Carlo Razon", .Email = staffName & "@trinidad.test", .Username = staffName, .Role = UserRole.Resident, .IsActive = True, .EmailVerified = True})
        Await Assert.ThrowsAsync(Of SessionExpiredException)(Function() Svc.Concerns.SearchAsync(staff2, New ConcernFilter()))

        ' Duplicate username on edit is rejected; unused accounts can be deleted, used ones cannot.
        Dim other = Unique("tmp")
        Dim otherId = Await Svc.Users.CreateAsync(admin, New UserEditRequest With {.FullName = "Temp User", .Email = other & "@trinidad.test", .Username = other, .Role = UserRole.Resident, .NewPassword = "Temp#Pass2025"})
        Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Users.UpdateAsync(admin, New UserEditRequest With {.Id = otherId, .FullName = "Temp User", .Email = other & "@trinidad.test", .Username = staffName, .Role = UserRole.Resident, .IsActive = True}))
        Await Svc.Users.DeleteAsync(admin, otherId)
        Dim withData = Await NewResidentAsync()
        Dim cats = Await Svc.Concerns.GetCategoriesAsync()
        Await Svc.Concerns.SubmitAsync(withData.Session, New NewConcernRequest With {.BarangayId = 14, .CategoryId = cats(3).Id, .Location = "San Jose, Trinidad", .Description = "Streetlight not working for a week."})
        Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Users.DeleteAsync(admin, withData.Session.UserId))
        Assert.True(Convert.ToInt64(_fx.Scalar("SELECT COUNT(*) FROM audit_logs WHERE action = 'user.role_changed' AND entity_id = @e", ("@e", UserCode(staffId)))) = 1)
    End Function

    <DbFact>
    Public Async Function SettingsPersistAndAreValidated() As Task
        Dim admin = Await AdminAsync()
        Await Svc.Settings.SaveAsync(admin, New Dictionary(Of String, String) From {{SettingKeys.ItemsPerPage, "15"}, {SettingKeys.Municipality, "Trinidad, Bohol"}})
        Svc.Settings.Invalidate()
        Assert.Equal(15, Await Svc.Settings.GetIntAsync(SettingKeys.ItemsPerPage, 10))
        Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Settings.SaveAsync(admin, New Dictionary(Of String, String) From {{SettingKeys.ItemsPerPage, "9999"}}))
        Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Settings.SaveAsync(admin, New Dictionary(Of String, String) From {{"setup.completed", "false"}}))
        Await Svc.Settings.SaveAsync(admin, New Dictionary(Of String, String) From {{SettingKeys.ItemsPerPage, "10"}})
    End Function

    <DbFact>
    Public Async Function LogoutRevokesTheSession() As Task
        Dim r = Await NewResidentAsync()
        Dim token = r.Session.Token
        Await Svc.Auth.LogoutAsync(r.Session)
        Dim replay As New UserSession With {.Token = token}
        Await Assert.ThrowsAsync(Of SessionExpiredException)(Function() Svc.Concerns.SearchAsync(replay, New ConcernFilter()))
    End Function

    <DbFact>
    Public Async Function SubmissionValidation() As Task
        Dim r = Await NewResidentAsync()
        Dim cats = Await Svc.Concerns.GetCategoriesAsync()
        Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Concerns.SubmitAsync(r.Session, New NewConcernRequest With {.BarangayId = 14, .CategoryId = 99, .Location = "Poblacion", .Description = "Something happened here."}))
        Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Concerns.SubmitAsync(r.Session, New NewConcernRequest With {.BarangayId = 14, .CategoryId = cats(0).Id, .Location = "x", .Description = "Something happened here."}))
        Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Concerns.SubmitAsync(r.Session, New NewConcernRequest With {.BarangayId = 14, .CategoryId = cats(0).Id, .Location = "Poblacion", .Description = "short"}))
        Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Concerns.SubmitAsync(r.Session, New NewConcernRequest With {.BarangayId = 14, .CategoryId = cats(0).Id, .Location = "Poblacion", .Description = "Something happened here.", .ReportedDate = DateTime.Today.AddDays(1)}))
        Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Concerns.SubmitAsync(r.Session, New NewConcernRequest With {.BarangayId = 14, .CategoryId = cats(0).Id, .SubcategoryId = 801, .Location = "Poblacion", .Description = "Something happened here."}))
        Dim tooMany As New NewConcernRequest With {.BarangayId = 14, .CategoryId = cats(0).Id, .Location = "Poblacion", .Description = "Something happened here."}
        For i = 1 To 6 : tooMany.Attachments.Add(Picture(40, 40, Color.Red)) : Next
        Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Concerns.SubmitAsync(r.Session, tooMany))
        Assert.Equal(0, (Await Svc.Concerns.SearchAsync(r.Session, New ConcernFilter())).TotalCount)
    End Function

    <DbFact>
    Public Async Function AdministratorFilesConcernWithTheResidentForm() As Task
        Dim admin = Await AdminAsync()
        Dim resident = Await NewResidentAsync()
        Dim cats = Await Svc.Concerns.GetCategoriesAsync()
        Dim soom = (Await Svc.Concerns.GetBarangaysAsync()).First(Function(b) b.Name = "Soom")
        Assert.True(admin.Has(Permission.ConcernCreate))

        ' Exactly the same validation rules as a resident submission.
        Dim invalid = {
            New NewConcernRequest With {.BarangayId = 14, .CategoryId = 99, .Location = "Poblacion", .Description = "Something happened here."},
            New NewConcernRequest With {.CategoryId = cats(0).Id, .Location = "Poblacion", .Description = "Something happened here."},
            New NewConcernRequest With {.BarangayId = 14, .CategoryId = cats(0).Id, .Location = "x", .Description = "Something happened here."},
            New NewConcernRequest With {.BarangayId = 14, .CategoryId = cats(0).Id, .Location = "Poblacion", .Description = "short"},
            New NewConcernRequest With {.BarangayId = 14, .CategoryId = cats(0).Id, .Location = "Poblacion", .Description = "Something happened here.", .ReportedDate = DateTime.Today.AddDays(1)},
            New NewConcernRequest With {.BarangayId = 14, .CategoryId = cats(0).Id, .SubcategoryId = 801, .Location = "Poblacion", .Description = "Something happened here."}}
        For Each req In invalid
            Dim fromAdmin = Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Concerns.SubmitAsync(admin, req))
            Dim fromResident = Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Concerns.SubmitAsync(resident.Session, req))
            Assert.Equal(fromResident.Field, fromAdmin.Field)
            Assert.Equal(fromResident.Message, fromAdmin.Message)
        Next

        Dim before = Await Svc.Concerns.GetAdminDashboardAsync(admin)
        Dim ok As New NewConcernRequest With {.BarangayId = soom.Id, .CategoryId = cats(1).Id, .SubcategoryId = cats(1).Subcategories.First().Id,
                                              .Location = "Purok 4, beside the health center", .Description = "Canal is clogged and water overflows onto the road.", .ReportedDate = DateTime.Today}
        ok.Attachments.Add(Picture(320, 240, Color.SteelBlue))
        Dim submitted = Await Svc.Concerns.SubmitAsync(admin, ok)

        ' Stored in the same concerns table, recorded as filed by the administrator, with photo and location.
        Dim d = Await Svc.Concerns.GetDetailAsync(admin, submitted.ConcernId)
        Assert.Equal(admin.UserId, d.ResidentId)
        Assert.Equal(ConcernStatus.Submitted, d.Status)
        Assert.Equal("Purok 4, beside the health center, Brgy. Soom", d.LocationDisplay)
        Assert.Single(d.Attachments)
        Assert.Single(d.History)
        Assert.Equal("Ana Lopez", d.History(0).ChangedByDisplay)

        ' Listed under Concerns, counted on the dashboard and in reports, and workable like any other report.
        Assert.Equal(1, (Await Svc.Concerns.SearchAsync(admin, New ConcernFilter With {.Search = submitted.ReportCode})).TotalCount)
        Dim after = Await Svc.Concerns.GetAdminDashboardAsync(admin)
        Assert.Equal(before.Total + 1, after.Total)
        Assert.Equal(before.Count(ConcernStatus.Submitted) + 1, after.Count(ConcernStatus.Submitted))
        Dim report = Await Svc.Reports.GenerateAsync(admin, New ReportRequest With {.FromDate = DateTime.Today, .ToDate = DateTime.Today})
        Assert.Contains(report.Concerns, Function(c) c.ReportCode = submitted.ReportCode)
        Assert.Equal({ConcernStatus.UnderReview, ConcernStatus.Closed}, Await Svc.Concerns.GetAllowedTransitionsAsync(admin, d))

        ' Residents still see only their own reports.
        Assert.Equal(0, (Await Svc.Concerns.SearchAsync(resident.Session, New ConcernFilter With {.Search = submitted.ReportCode})).TotalCount)
        Await Assert.ThrowsAsync(Of AuthorizationException)(Function() Svc.Concerns.GetDetailAsync(resident.Session, submitted.ConcernId))

        ' Staff accounts were not given this permission.
        Dim staffName = Unique("stf")
        Await Svc.Users.CreateAsync(admin, New UserEditRequest With {.FullName = "Carlo Razon", .Email = staffName & "@trinidad.test", .Username = staffName, .Role = UserRole.Staff, .NewPassword = "Staff#Pass2025"})
        Dim staff = Await LoginAsync(staffName, "Staff#Pass2025", LoginPortal.Personnel)
        Assert.False(staff.Has(Permission.ConcernCreate))
        Await Assert.ThrowsAsync(Of AuthorizationException)(Function() Svc.Concerns.SubmitAsync(staff, ok))
    End Function

    <DbFact>
    Public Async Function BarangaysAreRequiredStoredAndFilterable() As Task
        Dim barangays = Await Svc.Concerns.GetBarangaysAsync()
        Assert.Equal(20, barangays.Count)
        Assert.Equal({"Banlasan", "Bongbong", "Catoogan", "Guinobatan", "Hinlayagan Ilaud", "Hinlayagan Ilaya", "Kauswagan", "Kinan-oan",
                      "La Union", "La Victoria", "Mabuhay Cabigohan", "Mahagbu", "Manuel M. Roxas", "Poblacion", "San Isidro", "San Vicente",
                      "Santo Tomas", "Soom", "Tagum Norte", "Tagum Sur"}, barangays.Select(Function(b) b.Name))
        Dim soom = barangays.First(Function(b) b.Name = "Soom")
        Dim r = Await NewResidentAsync()
        Dim cats = Await Svc.Concerns.GetCategoriesAsync()

        ' Missing or unknown barangay is rejected.
        Dim noBrgy As New NewConcernRequest With {.CategoryId = cats(0).Id, .Location = "Purok 2", .Description = "Garbage not collected this week."}
        Dim ex = Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Concerns.SubmitAsync(r.Session, noBrgy))
        Assert.Equal("Barangay", ex.Field)
        Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Concerns.SubmitAsync(r.Session, New NewConcernRequest With {
            .BarangayId = 99, .CategoryId = cats(0).Id, .Location = "Purok 2", .Description = "Garbage not collected this week."}))

        Dim ok = Await Svc.Concerns.SubmitAsync(r.Session, New NewConcernRequest With {
            .BarangayId = soom.Id, .CategoryId = cats(0).Id, .Location = "Purok 2, near the chapel", .Description = "Garbage not collected this week."})
        Dim d = Await Svc.Concerns.GetDetailAsync(r.Session, ok.ConcernId)
        Assert.Equal("Soom", d.BarangayName)
        Assert.Equal("Purok 2, near the chapel, Brgy. Soom", d.LocationDisplay)

        ' Filter by barangay and search by barangay name.
        Assert.Equal(1, (Await Svc.Concerns.SearchAsync(r.Session, New ConcernFilter With {.BarangayId = soom.Id})).TotalCount)
        Assert.Equal(0, (Await Svc.Concerns.SearchAsync(r.Session, New ConcernFilter With {.BarangayId = barangays.First(Function(b) b.Name = "Bongbong").Id})).TotalCount)
        Assert.Equal(1, (Await Svc.Concerns.SearchAsync(r.Session, New ConcernFilter With {.Search = "soom"})).TotalCount)

        ' Registration requires a barangay and stores it on the account.
        Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Auth.RegisterAsync(New RegistrationRequest With {
            .FullName = "No Barangay", .Email = Unique("nb") & "@example.com", .Username = Unique("nb"), .Password = "NoBrgy#2025x", .ConfirmPassword = "NoBrgy#2025x"}))
        Dim me1 = Await Svc.Users.GetCurrentUserAsync(r.Session)
        Assert.Equal("Poblacion", me1.BarangayName)

        ' Exports carry the barangay.
        Dim admin = Await AdminAsync()
        Dim report = Await Svc.Reports.GenerateAsync(admin, New ReportRequest With {.FromDate = DateTime.Today, .ToDate = DateTime.Today})
        Assert.Contains(",Soom,", ExportService.BuildCsv(report))
    End Function

    <DbFact>
    Public Async Function EveryRoleCanEditOwnProfileAndPhoto() As Task
        Dim admin = Await AdminAsync()
        Dim staffName = Unique("pstaff")
        Await Svc.Users.CreateAsync(admin, New UserEditRequest With {.FullName = "Luis Garcia", .Email = staffName & "@trinidad.test", .Username = staffName, .Role = UserRole.Staff, .NewPassword = "Staff#Pass2025"})
        Dim staff = Await LoginAsync(staffName, "Staff#Pass2025", LoginPortal.Personnel)
        Dim resident = (Await NewResidentAsync()).Session
        Dim barangays = Await Svc.Concerns.GetBarangaysAsync()

        For Each s In {admin, staff, resident}
            Dim before = Await Svc.Users.GetCurrentUserAsync(s)
            Dim target = barangays(DateTime.Now.Millisecond Mod barangays.Count)
            Await Svc.Users.UpdateProfileAsync(s, New ProfileUpdate With {
                .FullName = before.FullName & " Jr.", .ContactNumber = "0912 345 6789", .Address = "Purok 1", .BarangayId = target.Id, .NotifyEmail = True})
            Dim after = Await Svc.Users.GetCurrentUserAsync(s)
            Assert.Equal(before.FullName & " Jr.", after.FullName)
            Assert.Equal(target.Name, after.BarangayName)
            Assert.Equal("Purok 1", after.Address)
            Assert.Equal(before.FullName & " Jr.", s.FullName) ' the open session shows the new name too
            ' An unknown barangay is rejected; Nothing ("Not set") is allowed.
            Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Users.UpdateProfileAsync(s, New ProfileUpdate With {.FullName = after.FullName, .BarangayId = 250}))
            Await Svc.Users.UpdateProfileAsync(s, New ProfileUpdate With {.FullName = after.FullName, .BarangayId = Nothing})
            Assert.Null((Await Svc.Users.GetCurrentUserAsync(s)).BarangayId)

            ' Profile photo: validated, cropped to a square avatar, stored, and removable.
            Assert.Null(Await Svc.Users.GetMyPhotoAsync(s))
            Dim photo As Byte()
            Using bmp As New Bitmap(640, 360)
                Using g = Graphics.FromImage(bmp)
                    g.Clear(Color.SteelBlue)
                End Using
                Using ms As New MemoryStream()
                    bmp.Save(ms, ImageFormat.Png)
                    photo = ms.ToArray()
                End Using
            End Using
            Await Svc.Users.SetMyPhotoAsync(s, photo, "me.png", 5 * 1024 * 1024)
            Dim stored = Await Svc.Users.GetMyPhotoAsync(s)
            Using img = AttachmentService.TryDecode(stored)
                Assert.Equal(AttachmentService.AvatarSize, img.Width)
                Assert.Equal(AttachmentService.AvatarSize, img.Height)
            End Using
            Assert.True((Await Svc.Users.GetCurrentUserAsync(s)).HasPhoto)
            Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Users.SetMyPhotoAsync(s, Text.Encoding.UTF8.GetBytes("not a picture"), "x.jpg", 5 * 1024 * 1024))
            Await Svc.Users.RemoveMyPhotoAsync(s)
            Assert.Null(Await Svc.Users.GetMyPhotoAsync(s))
            ' Restore, so tests that check names stay independent of run order.
            Await Svc.Users.UpdateProfileAsync(s, New ProfileUpdate With {.FullName = before.FullName, .ContactNumber = before.ContactNumber,
                                                                         .Address = before.Address, .BarangayId = before.BarangayId, .NotifyEmail = before.NotifyEmail})
        Next

        ' Administrators can set a user's barangay in User Management.
        Dim u = (Await Svc.Users.SearchAsync(admin, New UserFilter With {.Search = staffName})).Items.Single()
        Dim tagum = barangays.First(Function(b) b.Name = "Tagum Sur")
        Await Svc.Users.UpdateAsync(admin, New UserEditRequest With {.Id = u.Id, .FullName = u.FullName, .Email = u.Email, .Username = u.Username, .Role = u.Role,
                                                                    .IsActive = True, .EmailVerified = True, .BarangayId = tagum.Id})
        Assert.Equal("Tagum Sur", (Await Svc.Users.SearchAsync(admin, New UserFilter With {.Search = staffName})).Items.Single().BarangayName)
        Assert.True(Convert.ToInt64(_fx.Scalar("SELECT COUNT(*) FROM audit_logs WHERE action = 'user.photo_updated'")) >= 3)
    End Function

    <DbFact>
    Public Async Function PersonnelCanViewAUsersProfileButResidentsCannot() As Task
        Dim admin = Await AdminAsync()
        Dim r = Await NewResidentAsync()
        Dim cats = Await Svc.Concerns.GetCategoriesAsync()
        Await Svc.Concerns.SubmitAsync(r.Session, New NewConcernRequest With {.BarangayId = 14, .CategoryId = cats(0).Id, .Location = "Purok 5", .Description = "Garbage pile beside the road."})
        Using bmp As New Bitmap(300, 300)
            Using ms As New MemoryStream()
                bmp.Save(ms, ImageFormat.Png)
                Await Svc.Users.SetMyPhotoAsync(r.Session, ms.ToArray(), "me.png", 5 * 1024 * 1024)
            End Using
        End Using
        Await Svc.Users.UpdateProfileAsync(r.Session, New ProfileUpdate With {.FullName = "Juan Dela Cruz", .ContactNumber = "0917 000 1111", .Address = "Purok 5", .BarangayId = 14, .NotifyEmail = True})

        Dim view = Await Svc.Users.GetUserProfileAsync(admin, r.Session.UserId)
        Assert.Equal(r.Username, view.User.Username)
        Assert.Equal("0917 000 1111", view.User.ContactNumber)
        Assert.Equal("Poblacion", view.User.BarangayName)
        Assert.Equal("Purok 5", view.User.Address)
        Assert.NotNull(view.Photo)
        Assert.Equal(1, view.ReportCounts(ConcernStatus.Submitted))

        ' Residents (no user.view) cannot look at anyone's profile through this call.
        Dim other = Await NewResidentAsync()
        Await Assert.ThrowsAsync(Of AuthorizationException)(Function() Svc.Users.GetUserProfileAsync(other.Session, r.Session.UserId))
    End Function

    <DbFact>
    Public Async Function PrimaryAdministratorIsProtectedFromEveryone() As Task
        Dim primary = Await AdminAsync() ' ana_admin was created by first-run setup in the fixture
        Dim list = Await Svc.Users.SearchAsync(primary, New UserFilter With {.Search = "ana_admin"})
        Dim ana = list.Items.Single()
        Assert.True(ana.IsPrimaryAdmin)

        ' A second, fully privileged administrator...
        Dim name2 = Unique("admin")
        Dim id2 = Await Svc.Users.CreateAsync(primary, New UserEditRequest With {.FullName = "Second Admin", .Email = name2 & "@trinidad.test", .Username = name2, .Role = UserRole.Administrator, .NewPassword = "Second#Admin2025"})
        Dim second = Await LoginAsync(name2, "Second#Admin2025", LoginPortal.Personnel)
        Assert.False((Await Svc.Users.SearchAsync(second, New UserFilter With {.Search = name2})).Items.Single().IsPrimaryAdmin)

        ' ...cannot deactivate, delete, demote or edit the primary administrator.
        Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Users.SetActiveAsync(second, ana.Id, False))
        Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Users.DeleteAsync(second, ana.Id))
        Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Users.UpdateAsync(second, New UserEditRequest With {
            .Id = ana.Id, .FullName = ana.FullName, .Email = ana.Email, .Username = ana.Username, .Role = UserRole.Staff, .IsActive = True, .EmailVerified = True}))
        Await Assert.ThrowsAsync(Of AuthorizationException)(Function() Svc.Users.UpdateAsync(second, New UserEditRequest With {
            .Id = ana.Id, .FullName = ana.FullName, .Email = "takeover@evil.test", .Username = ana.Username, .Role = UserRole.Administrator, .IsActive = True, .EmailVerified = True,
            .NewPassword = "Different#2025x"}))
        ' Nothing changed.
        Await LoginAsync("ana_admin", DatabaseFixture.AdminPassword, LoginPortal.Personnel)
        Assert.Equal("ana.admin@trinidad.test", (Await Svc.Users.GetUserProfileAsync(primary, ana.Id)).User.Email)

        ' The primary administrator cannot remove themselves either, but may edit their own details.
        Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Users.SetActiveAsync(primary, ana.Id, False))
        Await Assert.ThrowsAsync(Of ValidationException)(Function() Svc.Users.DeleteAsync(primary, ana.Id))
        Await Svc.Users.UpdateAsync(primary, New UserEditRequest With {.Id = ana.Id, .FullName = ana.FullName, .Email = ana.Email, .Username = ana.Username,
                                                                      .Role = UserRole.Administrator, .IsActive = True, .EmailVerified = True, .ContactNumber = "0918 111 2222"})
        ' Other administrators remain manageable.
        Await Svc.Users.SetActiveAsync(primary, id2, False)
        Await Svc.Users.SetActiveAsync(primary, id2, True)
    End Function

    <DbFact>
    Public Async Function VersionOneDatabaseIsUpgradedWithoutLosingData() As Task
        ' Build a separate database, then turn it back into a version-1 layout (no barangay or photo columns).
        Dim b As New MySql.Data.MySqlClient.MySqlConnectionStringBuilder(_fx.AdminConnection) With {.Database = "tlc_test_upg_" & Guid.NewGuid().ToString("N").Substring(0, 8)}
        Dim opts As New Configuration.DatabaseOptions With {.ConnectionStringOverride = b.ConnectionString}
        Try
            Await DatabaseInitializer.InitializeAsync(opts)
            Using conn As New MySql.Data.MySqlClient.MySqlConnection(b.ConnectionString)
                conn.Open()
                For Each sql In {
                    "ALTER TABLE concerns DROP FOREIGN KEY fk_concerns_barangay, DROP INDEX ix_concerns_barangay, DROP COLUMN barangay_id",
                    "ALTER TABLE users DROP FOREIGN KEY fk_users_barangay, DROP INDEX ix_users_barangay, DROP COLUMN barangay_id, DROP COLUMN profile_photo, DROP COLUMN profile_photo_updated_at",
                    "DELETE FROM schema_migrations WHERE version >= 2",
                    "DELETE FROM role_permissions WHERE role_id = 1 AND permission_id = 1",
                    "INSERT INTO users (full_name, email, username, password_hash, role_id) VALUES ('Old Data', 'old@example.com', 'olddata', 'x', 3)"}
                    Using cmd As New MySql.Data.MySqlClient.MySqlCommand(sql, conn)
                        cmd.ExecuteNonQuery()
                    End Using
                Next
            End Using
            Dim before = Await DatabaseInitializer.CheckAsync(opts)
            Assert.Equal(SchemaState.SchemaOutdated, before.State)
            Assert.Equal(1, before.SchemaVersion)

            Await DatabaseInitializer.InitializeAsync(opts)
            Dim after = Await DatabaseInitializer.CheckAsync(opts)
            Assert.Equal(SchemaState.Ready, after.State)
            Assert.Equal(DatabaseInitializer.CurrentSchemaVersion, after.SchemaVersion)
            Using conn As New MySql.Data.MySqlClient.MySqlConnection(b.ConnectionString)
                conn.Open()
                Using cmd As New MySql.Data.MySqlClient.MySqlCommand("SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() AND column_name IN ('barangay_id', 'profile_photo', 'profile_photo_updated_at')", conn)
                    Assert.Equal(4L, Convert.ToInt64(cmd.ExecuteScalar()))
                End Using
                Using cmd As New MySql.Data.MySqlClient.MySqlCommand("SELECT COUNT(*) FROM users WHERE username = 'olddata'", conn)
                    Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar())) ' existing data kept
                End Using
                Using cmd As New MySql.Data.MySqlClient.MySqlCommand("SELECT COUNT(*) FROM role_permissions WHERE role_id = 1 AND permission_id = 1", conn)
                    Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar())) ' v3: administrators can submit concerns
                End Using
            End Using
            ' Running it again changes nothing.
            Await DatabaseInitializer.InitializeAsync(opts)
            Assert.Equal(SchemaState.Ready, (Await DatabaseInitializer.CheckAsync(opts)).State)
        Finally
            Using conn As New MySql.Data.MySqlClient.MySqlConnection(_fx.AdminConnection)
                conn.Open()
                Using cmd As New MySql.Data.MySqlClient.MySqlCommand($"DROP DATABASE IF EXISTS `{b.Database}`", conn)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        End Try
    End Function

End Class
