Namespace Models

    Public Class UserAccount
        Public Property Id As Integer
        Public Property FullName As String = ""
        Public Property Email As String = ""
        Public Property Username As String = ""
        Friend Property PasswordHash As String = ""
        Public Property Role As UserRole
        Public Property ContactNumber As String
        Public Property Address As String
        Public Property BarangayId As Integer?
        Public Property BarangayName As String
        Public Property HasPhoto As Boolean
        ''' <summary>The account created by first-run setup: cannot be deactivated, deleted or demoted.</summary>
        Public Property IsPrimaryAdmin As Boolean
        Public Property IsActive As Boolean
        Public Property EmailVerified As Boolean
        Public Property NotifyEmail As Boolean
        Public Property FailedLoginCount As Integer
        Public Property LockoutUntil As DateTime?
        Public Property LastLoginAt As DateTime?
        Public Property CreatedAt As DateTime

        Public ReadOnly Property DisplayId As String
            Get
                Return UserCode(Id)
            End Get
        End Property

        Public ReadOnly Property RoleName As String
            Get
                Return DisplayName(Role)
            End Get
        End Property
    End Class

    ''' <summary>
    ''' The authenticated session held by the client. It is only a handle: the services
    ''' re-validate the session token, the user's active flag and role against MySQL
    ''' before every privileged operation.
    ''' </summary>
    Public Class UserSession
        Public Property SessionId As Long
        Friend Property Token As String = ""
        Public Property UserId As Integer
        Public Property Username As String = ""
        Public Property FullName As String = ""
        Public Property Email As String = ""
        Public Property Role As UserRole
        Public Property Permissions As New HashSet(Of String)(StringComparer.Ordinal)
        Public Property ExpiresAt As DateTime

        Public Function Has(permissionCode As String) As Boolean
            Return Permissions.Contains(permissionCode)
        End Function

        Public ReadOnly Property RoleTitle As String
            Get
                Select Case Role
                    Case UserRole.Administrator : Return "System Administrator"
                    Case UserRole.Staff : Return "LGU Staff"
                    Case Else : Return "Resident"
                End Select
            End Get
        End Property
    End Class

    Public Class LoginResult
        Public Property Success As Boolean
        Public Property Session As UserSession
        Public Property Message As String = ""
        Public Property RequiresEmailVerification As Boolean
        Public Property UnverifiedUserEmail As String
        Public Property WrongPortal As Boolean
    End Class

    Public Class RegistrationRequest
        Public Property FullName As String = ""
        Public Property Email As String = ""
        Public Property Username As String = ""
        Public Property Password As String = ""
        Public Property ConfirmPassword As String = ""
        Public Property BarangayId As Integer?
    End Class

    Public Class RegistrationResult
        Public Property UserId As Integer
        Public Property RequiresVerification As Boolean
        Public Property VerificationEmailSent As Boolean
        Public Property EmailError As String
    End Class

    Public Class UserEditRequest
        Public Property Id As Integer
        Public Property FullName As String = ""
        Public Property Email As String = ""
        Public Property Username As String = ""
        Public Property Role As UserRole
        Public Property IsActive As Boolean = True
        Public Property EmailVerified As Boolean = True
        Public Property ContactNumber As String
        Public Property BarangayId As Integer?
        ''' <summary>Required for new users; optional (Nothing = unchanged) for existing users.</summary>
        Public Property NewPassword As String
    End Class

    Public Class UserFilter
        Public Property Search As String
        Public Property Role As UserRole?
        Public Property IsActive As Boolean?
        Public Property Page As Integer = 1
        Public Property PageSize As Integer = 10
    End Class

    Public Class ProfileUpdate
        Public Property FullName As String = ""
        Public Property ContactNumber As String
        Public Property Address As String
        Public Property BarangayId As Integer?
        Public Property NotifyEmail As Boolean
    End Class

End Namespace
