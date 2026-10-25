Imports MySql.Data.MySqlClient
Imports TrinidadLGUConnect.Data
Imports TrinidadLGUConnect.Helpers
Imports TrinidadLGUConnect.Models
Imports TrinidadLGUConnect.Repositories

Namespace Services

    ''' <summary>User administration and self-service profile changes. All rules are enforced here, not in the UI.</summary>
    Public Class UserService

        Private ReadOnly _db As Db
        Private ReadOnly _authz As AuthorizationService

        Public Sub New(db As Db, authz As AuthorizationService)
            _db = db
            _authz = authz
        End Sub

        Public Async Function SearchAsync(session As UserSession, filter As UserFilter) As Task(Of PagedResult(Of UserAccount))
            If filter.Search IsNot Nothing AndAlso filter.Search.Length > 100 Then filter.Search = filter.Search.Substring(0, 100)
            Return Await _db.RunAsync(
                Async Function(conn)
                    Await _authz.DemandAsync(conn, session, Permission.UserView).ConfigureAwait(False)
                    Dim result = Await UserRepository.SearchAsync(conn, filter).ConfigureAwait(False)
                    Dim ownerId = Await GetPrimaryAdminIdAsync(conn, Nothing).ConfigureAwait(False)
                    For Each u In result.Items
                        u.IsPrimaryAdmin = ownerId.HasValue AndAlso u.Id = ownerId.Value
                    Next
                    Return result
                End Function).ConfigureAwait(False)
        End Function

        ''' <summary>
        ''' The primary administrator is the account created by the first-run setup. It can never be deactivated,
        ''' deleted or demoted, and only its owner can edit it (so another administrator cannot take it over by
        ''' changing its email or password).
        ''' </summary>
        Public Shared Async Function GetPrimaryAdminIdAsync(conn As MySqlConnection, tx As MySqlTransaction) As Task(Of Integer?)
            Using cmd = Db.Cmd(conn, "SELECT setting_value FROM app_settings WHERE setting_key = @k", tx).P("@k", SettingKeys.PrimaryAdminUserId)
                Dim v = Await cmd.ExecuteScalarAsync().ConfigureAwait(False)
                Dim id As Integer
                If v IsNot Nothing AndAlso v IsNot DBNull.Value AndAlso Integer.TryParse(CStr(v), id) Then Return id
            End Using
            Return Nothing
        End Function

        Private Shared Sub GuardPrimaryAdmin(existing As UserAccount, ownerId As Integer?, actor As Actor, Optional deactivating As Boolean = False,
                                             Optional deleting As Boolean = False, Optional roleChanged As Boolean = False)
            If Not ownerId.HasValue OrElse existing.Id <> ownerId.Value Then Return
            If deleting Then Throw New ValidationException("This is the primary administrator account. It cannot be deleted.")
            If deactivating Then Throw New ValidationException("This is the primary administrator account. It cannot be deactivated.")
            If roleChanged Then Throw New ValidationException("This is the primary administrator account. Its role cannot be changed.")
            If actor.UserId <> ownerId.Value Then
                Throw New AuthorizationException("This is the primary administrator account. Only its owner can change it.")
            End If
        End Sub

        Public Async Function GetCurrentUserAsync(session As UserSession) As Task(Of UserAccount)
            Return Await _db.RunAsync(
                Async Function(conn)
                    Dim actor = Await _authz.ResolveAsync(conn, session).ConfigureAwait(False)
                    Return actor.User
                End Function).ConfigureAwait(False)
        End Function

        Private Shared Sub ValidateFields(req As UserEditRequest, isNew As Boolean)
            req.FullName = CleanText(req.FullName, 120)
            req.Email = NormalizeEmail(req.Email)
            req.Username = If(req.Username, "").Trim()
            req.ContactNumber = If(String.IsNullOrWhiteSpace(req.ContactNumber), Nothing, req.ContactNumber.Trim())
            Require(IsValidFullName(req.FullName), "Enter a valid full name.", "FullName")
            Require(IsValidEmail(req.Email), "Enter a valid email address.", "Email")
            Require(IsValidUsername(req.Username), "Username must be 4-30 characters: letters, numbers, dots or underscores.", "Username")
            Require(IsValidPhone(req.ContactNumber), "Enter a valid contact number.", "ContactNumber")
            Require([Enum].IsDefined(GetType(UserRole), req.Role), "Select a valid role.", "Role")
            If isNew Then Require(Not String.IsNullOrEmpty(req.NewPassword), "Enter an initial password.", "Password")
            If Not String.IsNullOrEmpty(req.NewPassword) Then
                Dim problem = PasswordProblem(req.NewPassword, req.Username, req.Email)
                Require(problem Is Nothing, problem, "Password")
            End If
        End Sub

        ''' <summary>Rejects a barangay id that is not in the barangays table (Nothing is allowed).</summary>
        Private Shared Async Function EnsureBarangayAsync(conn As MySqlConnection, tx As MySqlTransaction, barangayId As Integer?) As Task
            If Not barangayId.HasValue Then Return
            If Not (Await ConcernRepository.GetBarangaysAsync(conn, tx).ConfigureAwait(False)).Any(Function(b) b.Id = barangayId.Value) Then
                Throw New ValidationException("Select a barangay from the list.", "Barangay")
            End If
        End Function

        Public Async Function CreateAsync(session As UserSession, req As UserEditRequest) As Task(Of Integer)
            ValidateFields(req, isNew:=True)
            Dim hash = PasswordHasher.Hash(req.NewPassword)
            Return Await _db.InTransactionAsync(
                Async Function(conn, tx)
                    Dim actor = Await _authz.DemandAsync(conn, session, Permission.UserManage).ConfigureAwait(False)
                    If req.Role = UserRole.Administrator AndAlso Not actor.Has(Permission.UserManageAdmins) Then
                        Throw New AuthorizationException("You are not allowed to create administrator accounts.")
                    End If
                    If Await UserRepository.UsernameExistsAsync(conn, req.Username, 0, tx).ConfigureAwait(False) Then Throw New ValidationException("This username is already taken.", "Username")
                    If Await UserRepository.EmailExistsAsync(conn, req.Email, 0, tx).ConfigureAwait(False) Then Throw New ValidationException("This email address is already registered.", "Email")
                    Await EnsureBarangayAsync(conn, tx, req.BarangayId).ConfigureAwait(False)
                    Dim u As New UserAccount With {
                        .FullName = req.FullName, .Email = req.Email, .Username = req.Username, .Role = req.Role,
                        .IsActive = req.IsActive, .EmailVerified = req.EmailVerified, .ContactNumber = req.ContactNumber, .BarangayId = req.BarangayId}
                    Dim id = Await UserRepository.InsertAsync(conn, tx, u, hash, actor.UserId).ConfigureAwait(False)
                    Await AuditRepository.InsertAsync(conn, tx, actor.UserId, "user.created", "user", UserCode(id),
                        $"{req.Username} as {DisplayName(req.Role)}{If(req.IsActive, "", " (inactive)")}").ConfigureAwait(False)
                    Return id
                End Function).ConfigureAwait(False)
        End Function

        Public Async Function UpdateAsync(session As UserSession, req As UserEditRequest) As Task
            ValidateFields(req, isNew:=False)
            Dim newHash = If(String.IsNullOrEmpty(req.NewPassword), Nothing, PasswordHasher.Hash(req.NewPassword))
            Await _db.InTransactionAsync(
                Async Function(conn, tx)
                    Dim actor = Await _authz.DemandAsync(conn, session, Permission.UserManage).ConfigureAwait(False)
                    Dim existing = Await UserRepository.GetByIdAsync(conn, req.Id, tx, forUpdate:=True).ConfigureAwait(False)
                    If existing Is Nothing Then Throw New ValidationException("The user no longer exists.")
                    Dim roleChanged = existing.Role <> req.Role
                    Dim touchesAdmin = existing.Role = UserRole.Administrator OrElse req.Role = UserRole.Administrator
                    If touchesAdmin AndAlso Not actor.Has(Permission.UserManageAdmins) Then
                        Throw New AuthorizationException("You are not allowed to modify administrator accounts or grant the administrator role.")
                    End If
                    If existing.Id = actor.UserId Then
                        If roleChanged Then Throw New AuthorizationException("You cannot change your own role.")
                        If Not req.IsActive Then Throw New ValidationException("You cannot deactivate your own account.")
                    End If
                    GuardPrimaryAdmin(existing, Await GetPrimaryAdminIdAsync(conn, tx).ConfigureAwait(False), actor,
                                      deactivating:=existing.IsActive AndAlso Not req.IsActive, roleChanged:=roleChanged)
                    If existing.Role = UserRole.Administrator AndAlso existing.IsActive AndAlso (roleChanged OrElse Not req.IsActive) Then
                        If Await UserRepository.CountActiveAdminsAsync(conn, tx).ConfigureAwait(False) <= 1 Then
                            Throw New ValidationException("At least one active administrator must remain.")
                        End If
                    End If
                    If Await UserRepository.UsernameExistsAsync(conn, req.Username, req.Id, tx).ConfigureAwait(False) Then Throw New ValidationException("This username is already taken.", "Username")
                    If Await UserRepository.EmailExistsAsync(conn, req.Email, req.Id, tx).ConfigureAwait(False) Then Throw New ValidationException("This email address is already registered.", "Email")
                    Await EnsureBarangayAsync(conn, tx, req.BarangayId).ConfigureAwait(False)

                    Await UserRepository.UpdateAsync(conn, tx, req).ConfigureAwait(False)
                    If newHash IsNot Nothing Then
                        Await UserRepository.UpdatePasswordAsync(conn, tx, req.Id, newHash).ConfigureAwait(False)
                    End If
                    If newHash IsNot Nothing OrElse roleChanged OrElse (existing.IsActive AndAlso Not req.IsActive) Then
                        ' Force re-login so the new password/role/status takes effect everywhere.
                        Await SessionRepository.RevokeAllForUserAsync(conn, tx, req.Id, If(req.Id = actor.UserId, actor.SessionId, 0L)).ConfigureAwait(False)
                    End If

                    Dim changes As New List(Of String)()
                    If existing.FullName <> req.FullName Then changes.Add("name")
                    If existing.Email <> req.Email Then changes.Add("email")
                    If existing.Username <> req.Username Then changes.Add($"username {existing.Username} -> {req.Username}")
                    If existing.EmailVerified <> req.EmailVerified Then changes.Add($"email verified -> {req.EmailVerified}")
                    If If(existing.ContactNumber, "") <> If(req.ContactNumber, "") Then changes.Add("contact number")
                    If Not Nullable.Equals(existing.BarangayId, req.BarangayId) Then changes.Add("barangay")
                    If newHash IsNot Nothing Then changes.Add("password reset by administrator")
                    Await AuditRepository.InsertAsync(conn, tx, actor.UserId, "user.updated", "user", UserCode(req.Id), If(changes.Count = 0, "no field changes", String.Join("; ", changes))).ConfigureAwait(False)
                    If roleChanged Then
                        Await AuditRepository.InsertAsync(conn, tx, actor.UserId, "user.role_changed", "user", UserCode(req.Id), $"{DisplayName(existing.Role)} -> {DisplayName(req.Role)}").ConfigureAwait(False)
                    End If
                    If existing.IsActive <> req.IsActive Then
                        Await AuditRepository.InsertAsync(conn, tx, actor.UserId, If(req.IsActive, "user.activated", "user.deactivated"), "user", UserCode(req.Id), Nothing).ConfigureAwait(False)
                    End If
                    Return True
                End Function).ConfigureAwait(False)
        End Function

        Public Async Function SetActiveAsync(session As UserSession, userId As Integer, active As Boolean) As Task
            Await _db.InTransactionAsync(
                Async Function(conn, tx)
                    Dim actor = Await _authz.DemandAsync(conn, session, Permission.UserManage).ConfigureAwait(False)
                    Dim existing = Await UserRepository.GetByIdAsync(conn, userId, tx, forUpdate:=True).ConfigureAwait(False)
                    If existing Is Nothing Then Throw New ValidationException("The user no longer exists.")
                    If existing.Id = actor.UserId AndAlso Not active Then Throw New ValidationException("You cannot deactivate your own account.")
                    GuardPrimaryAdmin(existing, Await GetPrimaryAdminIdAsync(conn, tx).ConfigureAwait(False), actor, deactivating:=Not active)
                    If existing.Role = UserRole.Administrator AndAlso Not actor.Has(Permission.UserManageAdmins) Then Throw New AuthorizationException("You are not allowed to modify administrator accounts.")
                    If existing.Role = UserRole.Administrator AndAlso existing.IsActive AndAlso Not active AndAlso
                       Await UserRepository.CountActiveAdminsAsync(conn, tx).ConfigureAwait(False) <= 1 Then
                        Throw New ValidationException("At least one active administrator must remain.")
                    End If
                    Await UserRepository.SetActiveAsync(conn, tx, userId, active).ConfigureAwait(False)
                    If Not active Then Await SessionRepository.RevokeAllForUserAsync(conn, tx, userId).ConfigureAwait(False)
                    Await AuditRepository.InsertAsync(conn, tx, actor.UserId, If(active, "user.activated", "user.deactivated"), "user", UserCode(userId), existing.Username).ConfigureAwait(False)
                    Return True
                End Function).ConfigureAwait(False)
        End Function

        ''' <summary>Deletes an account that has never been used for reports. Accounts with records must be deactivated instead.</summary>
        Public Async Function DeleteAsync(session As UserSession, userId As Integer) As Task
            Await _db.InTransactionAsync(
                Async Function(conn, tx)
                    Dim actor = Await _authz.DemandAsync(conn, session, Permission.UserManage).ConfigureAwait(False)
                    Dim existing = Await UserRepository.GetByIdAsync(conn, userId, tx, forUpdate:=True).ConfigureAwait(False)
                    If existing Is Nothing Then Throw New ValidationException("The user no longer exists.")
                    If existing.Id = actor.UserId Then Throw New ValidationException("You cannot delete your own account.")
                    GuardPrimaryAdmin(existing, Await GetPrimaryAdminIdAsync(conn, tx).ConfigureAwait(False), actor, deleting:=True)
                    If existing.Role = UserRole.Administrator Then
                        If Not actor.Has(Permission.UserManageAdmins) Then Throw New AuthorizationException("You are not allowed to delete administrator accounts.")
                        If existing.IsActive AndAlso Await UserRepository.CountActiveAdminsAsync(conn, tx).ConfigureAwait(False) <= 1 Then
                            Throw New ValidationException("At least one active administrator must remain.")
                        End If
                    End If
                    If Await UserRepository.HasRecordsAsync(conn, tx, userId).ConfigureAwait(False) Then
                        Throw New ValidationException($"{existing.Username} has reports or status updates on record, so the account cannot be deleted. Deactivate it instead to keep the history intact.")
                    End If
                    Await UserRepository.DeleteAsync(conn, tx, userId).ConfigureAwait(False)
                    Await AuditRepository.InsertAsync(conn, tx, actor.UserId, "user.deleted", "user", UserCode(userId), $"{existing.Username} ({DisplayName(existing.Role)})").ConfigureAwait(False)
                    Return True
                End Function).ConfigureAwait(False)
        End Function

        Public Async Function UpdateProfileAsync(session As UserSession, p As ProfileUpdate) As Task
            p.FullName = CleanText(p.FullName, 120)
            p.ContactNumber = If(String.IsNullOrWhiteSpace(p.ContactNumber), Nothing, p.ContactNumber.Trim())
            p.Address = If(String.IsNullOrWhiteSpace(p.Address), Nothing, CleanText(p.Address, 200))
            Require(IsValidFullName(p.FullName), "Enter a valid full name.", "FullName")
            Require(IsValidPhone(p.ContactNumber), "Enter a valid contact number (digits, spaces, +, - and parentheses).", "ContactNumber")
            Await _db.InTransactionAsync(
                Async Function(conn, tx)
                    Dim actor = Await _authz.ResolveAsync(conn, session).ConfigureAwait(False)
                    Await EnsureBarangayAsync(conn, tx, p.BarangayId).ConfigureAwait(False)
                    Await UserRepository.UpdateProfileAsync(conn, tx, actor.UserId, p).ConfigureAwait(False)
                    Await AuditRepository.InsertAsync(conn, tx, actor.UserId, "user.profile_updated", "user", UserCode(actor.UserId), Nothing).ConfigureAwait(False)
                    Return True
                End Function).ConfigureAwait(False)
            session.FullName = p.FullName
        End Function

        Public Class UserProfileView
            Public Property User As UserAccount
            Public Property Photo As Byte()
            Public Property ReportCounts As Dictionary(Of ConcernStatus, Integer)
        End Class

        ''' <summary>Read-only profile of any user for personnel with user.view (photo, details, report counts).</summary>
        Public Async Function GetUserProfileAsync(session As UserSession, userId As Integer) As Task(Of UserProfileView)
            Return Await _db.RunAsync(
                Async Function(conn)
                    Await _authz.DemandAsync(conn, session, Permission.UserView).ConfigureAwait(False)
                    Dim u = Await UserRepository.GetByIdAsync(conn, userId).ConfigureAwait(False)
                    If u Is Nothing Then Throw New ValidationException("The user no longer exists.")
                    Dim ownerId = Await GetPrimaryAdminIdAsync(conn, Nothing).ConfigureAwait(False)
                    u.IsPrimaryAdmin = ownerId.HasValue AndAlso u.Id = ownerId.Value
                    Return New UserProfileView With {
                        .User = u,
                        .Photo = Await UserRepository.GetPhotoAsync(conn, userId).ConfigureAwait(False),
                        .ReportCounts = Await ConcernRepository.StatusCountsAsync(conn, userId).ConfigureAwait(False)}
                End Function).ConfigureAwait(False)
        End Function

        ''' <summary>The signed-in user's own profile photo (Nothing when none is set).</summary>
        Public Async Function GetMyPhotoAsync(session As UserSession) As Task(Of Byte())
            Return Await _db.RunAsync(
                Async Function(conn)
                    Dim actor = Await _authz.ResolveAsync(conn, session).ConfigureAwait(False)
                    Return Await UserRepository.GetPhotoAsync(conn, actor.UserId).ConfigureAwait(False)
                End Function).ConfigureAwait(False)
        End Function

        ''' <summary>Validates, crops and saves a new profile photo for the signed-in user (any role).</summary>
        Public Async Function SetMyPhotoAsync(session As UserSession, fileBytes As Byte(), fileName As String, maxBytes As Long) As Task
            Dim avatar = AttachmentService.PrepareAvatar(fileBytes, fileName, maxBytes)
            Await _db.InTransactionAsync(
                Async Function(conn, tx)
                    Dim actor = Await _authz.ResolveAsync(conn, session).ConfigureAwait(False)
                    Await UserRepository.SetPhotoAsync(conn, tx, actor.UserId, avatar).ConfigureAwait(False)
                    Await AuditRepository.InsertAsync(conn, tx, actor.UserId, "user.photo_updated", "user", UserCode(actor.UserId), Nothing).ConfigureAwait(False)
                    Return True
                End Function).ConfigureAwait(False)
        End Function

        Public Async Function RemoveMyPhotoAsync(session As UserSession) As Task
            Await _db.InTransactionAsync(
                Async Function(conn, tx)
                    Dim actor = Await _authz.ResolveAsync(conn, session).ConfigureAwait(False)
                    Await UserRepository.SetPhotoAsync(conn, tx, actor.UserId, Nothing).ConfigureAwait(False)
                    Await AuditRepository.InsertAsync(conn, tx, actor.UserId, "user.photo_removed", "user", UserCode(actor.UserId), Nothing).ConfigureAwait(False)
                    Return True
                End Function).ConfigureAwait(False)
        End Function

        Public Async Function GetAuditLogAsync(session As UserSession, limit As Integer) As Task(Of List(Of AuditEntry))
            Return Await _db.RunAsync(
                Async Function(conn)
                    Await _authz.DemandAsync(conn, session, Permission.AuditView).ConfigureAwait(False)
                    Return Await AuditRepository.RecentAsync(conn, Math.Min(Math.Max(1, limit), 500)).ConfigureAwait(False)
                End Function).ConfigureAwait(False)
        End Function

    End Class

    ''' <summary>
    ''' First-run creation of the initial administrator. Allowed only while no administrator exists and
    ''' setup.completed is false; the check and insert run in one transaction holding a row lock on the
    ''' setup.completed setting, so two workstations cannot both complete setup.
    ''' </summary>
    Public Class SetupService

        Private ReadOnly _db As Db

        Public Sub New(db As Db)
            _db = db
        End Sub

        Public Async Function IsSetupRequiredAsync() As Task(Of Boolean)
            Return Await _db.RunAsync(
                Async Function(conn)
                    Dim admins = Await UserRepository.CountAdminsAsync(conn).ConfigureAwait(False)
                    Dim settings = Await SettingsRepository.GetAllAsync(conn).ConfigureAwait(False)
                    Dim done As String = Nothing
                    settings.TryGetValue(SettingKeys.SetupCompleted, done)
                    Return admins = 0 AndAlso Not String.Equals(done, "true", StringComparison.OrdinalIgnoreCase)
                End Function).ConfigureAwait(False)
        End Function

        Public Async Function CreateInitialAdminAsync(fullName As String, email As String, username As String, password As String, confirm As String) As Task
            Dim req As New RegistrationRequest With {.FullName = fullName, .Email = email, .Username = username, .Password = password, .ConfirmPassword = confirm}
            req.FullName = CleanText(req.FullName, 120)
            req.Email = NormalizeEmail(req.Email)
            req.Username = If(req.Username, "").Trim()
            Require(IsValidFullName(req.FullName), "Enter a valid full name.", "FullName")
            Require(IsValidEmail(req.Email), "Enter a valid email address.", "Email")
            Require(IsValidUsername(req.Username), "Username must be 4-30 characters: letters, numbers, dots or underscores.", "Username")
            Dim problem = PasswordProblem(req.Password, req.Username, req.Email)
            Require(problem Is Nothing, problem, "Password")
            Require(req.Password.Length >= 12, "The administrator password must be at least 12 characters long.", "Password")
            Require(req.Password = req.ConfirmPassword, "Passwords do not match.", "ConfirmPassword")
            Dim hash = PasswordHasher.Hash(req.Password)

            Await _db.InTransactionAsync(
                Async Function(conn, tx)
                    Dim done = Await SettingsRepository.GetForUpdateAsync(conn, tx, SettingKeys.SetupCompleted).ConfigureAwait(False)
                    If String.Equals(done, "true", StringComparison.OrdinalIgnoreCase) OrElse
                       Await UserRepository.CountAdminsAsync(conn, tx).ConfigureAwait(False) > 0 Then
                        Throw New AuthorizationException("Initial setup has already been completed. Ask an existing administrator to create your account.")
                    End If
                    If Await UserRepository.UsernameExistsAsync(conn, req.Username, 0, tx).ConfigureAwait(False) Then Throw New ValidationException("This username is already taken.", "Username")
                    If Await UserRepository.EmailExistsAsync(conn, req.Email, 0, tx).ConfigureAwait(False) Then Throw New ValidationException("This email address is already registered.", "Email")
                    Dim u As New UserAccount With {.FullName = req.FullName, .Email = req.Email, .Username = req.Username, .Role = UserRole.Administrator, .IsActive = True, .EmailVerified = True}
                    Dim id = Await UserRepository.InsertAsync(conn, tx, u, hash, Nothing).ConfigureAwait(False)
                    Await SettingsRepository.UpsertAsync(conn, tx, SettingKeys.SetupCompleted, "true", id).ConfigureAwait(False)
                    Await SettingsRepository.UpsertAsync(conn, tx, SettingKeys.PrimaryAdminUserId, id.ToString(Globalization.CultureInfo.InvariantCulture), id).ConfigureAwait(False)
                    Await AuditRepository.InsertAsync(conn, tx, id, "setup.initial_admin_created", "user", UserCode(id), req.Username).ConfigureAwait(False)
                    Return True
                End Function).ConfigureAwait(False)
        End Function

    End Class

End Namespace
