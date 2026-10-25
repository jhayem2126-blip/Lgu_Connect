Namespace Models

    ''' <summary>Matches roles.id in the database.</summary>
    Public Enum UserRole As Byte
        Administrator = 1
        Staff = 2
        Resident = 3
    End Enum

    ''' <summary>Matches concern_statuses.id in the database.</summary>
    Public Enum ConcernStatus As Byte
        Submitted = 1
        UnderReview = 2
        InProgress = 3
        Resolved = 4
        Closed = 5
    End Enum

    ''' <summary>Permission codes; must match permissions.code in the database.</summary>
    Public NotInheritable Class Permission
        Private Sub New()
        End Sub
        Public Const ConcernCreate As String = "concern.create"
        Public Const ConcernViewOwn As String = "concern.view_own"
        Public Const ConcernResidentUpdate As String = "concern.resident_update"
        Public Const ConcernViewAll As String = "concern.view_all"
        Public Const ConcernUpdateStatus As String = "concern.update_status"
        Public Const DashboardAdmin As String = "dashboard.admin"
        Public Const ReportGenerate As String = "report.generate"
        Public Const ReportExport As String = "report.export"
        Public Const UserView As String = "user.view"
        Public Const UserManage As String = "user.manage"
        Public Const UserManageAdmins As String = "user.manage_admins"
        Public Const SettingsManage As String = "settings.manage"
        Public Const AuditView As String = "audit.view"
    End Class

    Public Module EnumText

        Public Function DisplayName(status As ConcernStatus) As String
            Select Case status
                Case ConcernStatus.Submitted : Return "Submitted"
                Case ConcernStatus.UnderReview : Return "Under Review"
                Case ConcernStatus.InProgress : Return "In Progress"
                Case ConcernStatus.Resolved : Return "Resolved"
                Case ConcernStatus.Closed : Return "Closed"
                Case Else : Return status.ToString()
            End Select
        End Function

        Public Function DisplayName(role As UserRole) As String
            Select Case role
                Case UserRole.Administrator : Return "Admin"
                Case UserRole.Staff : Return "Staff"
                Case Else : Return "Resident"
            End Select
        End Function

        Public ReadOnly Property AllStatuses As ConcernStatus()
            Get
                Return {ConcernStatus.Submitted, ConcernStatus.UnderReview, ConcernStatus.InProgress, ConcernStatus.Resolved, ConcernStatus.Closed}
            End Get
        End Property

        Public Function ReportCode(id As Integer) As String
            Return "CON-" & id.ToString("000")
        End Function

        Public Function UserCode(id As Integer) As String
            Return "USR-" & id.ToString("000")
        End Function

    End Module

End Namespace
