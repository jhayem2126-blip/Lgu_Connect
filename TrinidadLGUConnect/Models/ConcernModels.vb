Namespace Models

    Public Class Category
        Public Property Id As Integer
        Public Property Code As String = ""
        Public Property Name As String = ""
        Public Property ShortName As String = ""
        Public Property ColorHex As String = "#8A97AB"
        Public Property SortOrder As Integer
        Public Property Subcategories As New List(Of Subcategory)()

        Public Overrides Function ToString() As String
            Return Name
        End Function
    End Class

    Public Class Subcategory
        Public Property Id As Integer
        Public Property CategoryId As Integer
        Public Property Name As String = ""

        Public Overrides Function ToString() As String
            Return Name
        End Function
    End Class

    Public Class Barangay
        Public Property Id As Integer
        Public Property Name As String = ""

        Public Overrides Function ToString() As String
            Return Name
        End Function
    End Class

    Public Class ConcernSummary
        Public Property Id As Integer
        Public Property ReportCode As String = ""
        Public Property ResidentId As Integer
        Public Property ResidentName As String = ""
        Public Property CategoryId As Integer
        Public Property CategoryName As String = ""
        Public Property SubcategoryName As String
        Public Property Location As String = ""
        Public Property BarangayId As Integer?
        Public Property BarangayName As String
        Public Property Status As ConcernStatus
        Public Property ReportedAt As DateTime
        Public Property CreatedAt As DateTime
        Public Property UpdatedAt As DateTime
        Public Property AttachmentCount As Integer

        Public ReadOnly Property StatusName As String
            Get
                Return DisplayName(Status)
            End Get
        End Property

        ''' <summary>"Purok 3, near the chapel, Brgy. San Isidro" (older reports without a barangay show the location only).</summary>
        Public ReadOnly Property LocationDisplay As String
            Get
                If String.IsNullOrEmpty(BarangayName) Then Return Location
                Return If(String.IsNullOrWhiteSpace(Location), BarangayDisplay, $"{Location}, {BarangayDisplay}")
            End Get
        End Property

        ''' <summary>Barangay first for tables, where the column may be cut off: "Brgy. Soom · Purok 2, near the chapel".</summary>
        Public ReadOnly Property LocationListText As String
            Get
                If String.IsNullOrEmpty(BarangayName) Then Return Location
                Return If(String.IsNullOrWhiteSpace(Location), BarangayDisplay, $"{BarangayDisplay} · {Location}")
            End Get
        End Property

        Public ReadOnly Property BarangayDisplay As String
            Get
                If String.IsNullOrEmpty(BarangayName) Then Return ""
                Return If(BarangayName = "Poblacion", "Poblacion", "Brgy. " & BarangayName)
            End Get
        End Property
    End Class

    Public Class ConcernDetail
        Inherits ConcernSummary
        Public Property Description As String = ""
        Public Property ResidentEmail As String = ""
        Public Property Attachments As New List(Of AttachmentInfo)()
        Public Property History As New List(Of StatusHistoryEntry)()
    End Class

    Public Class AttachmentInfo
        Public Property Id As Integer
        Public Property ConcernId As Integer
        Public Property FileName As String = ""
        Public Property ContentType As String = ""
        Public Property SizeBytes As Long
        Public Property Width As Integer
        Public Property Height As Integer
        Public Property Thumbnail As Byte()

        Public ReadOnly Property SizeText As String
            Get
                Return Helpers.TextFormat.FileSize(SizeBytes)
            End Get
        End Property
    End Class

    Public Class StatusHistoryEntry
        Public Property Id As Long
        Public Property ConcernId As Integer
        Public Property FromStatus As ConcernStatus?
        Public Property ToStatus As ConcernStatus
        Public Property Remarks As String
        Public Property EffectiveDate As DateTime?
        Public Property ChangedById As Integer
        Public Property ChangedByName As String = ""
        Public Property ChangedByRole As UserRole
        Public Property ChangedAt As DateTime

        ''' <summary>Label shown in timelines: residents see "LGU Staff" instead of personnel names.</summary>
        Public Property ChangedByDisplay As String = ""
    End Class

    Public Class ConcernFilter
        Public Property Search As String
        Public Property CategoryId As Integer?
        Public Property Status As ConcernStatus?
        Public Property Location As String
        Public Property BarangayId As Integer?
        Public Property DateFrom As DateTime?
        Public Property DateTo As DateTime?
        Public Property Page As Integer = 1
        Public Property PageSize As Integer = 10
    End Class

    Public Class PagedResult(Of T)
        Public Property Items As New List(Of T)()
        Public Property TotalCount As Integer
        Public Property Page As Integer = 1
        Public Property PageSize As Integer = 10

        Public ReadOnly Property TotalPages As Integer
            Get
                If PageSize <= 0 Then Return 1
                Return Math.Max(1, CInt(Math.Ceiling(TotalCount / CDbl(PageSize))))
            End Get
        End Property

        Public ReadOnly Property FirstIndex As Integer
            Get
                If TotalCount = 0 Then Return 0
                Return (Page - 1) * PageSize + 1
            End Get
        End Property

        Public ReadOnly Property LastIndex As Integer
            Get
                Return Math.Min(TotalCount, Page * PageSize)
            End Get
        End Property
    End Class

    ''' <summary>An image that has been validated, re-encoded and thumbnailed but not yet saved.</summary>
    Public Class PendingAttachment
        Public Property FileName As String = ""
        Public Property ContentType As String = ""
        Public Property Data As Byte()
        Public Property Thumbnail As Byte()
        Public Property Width As Integer
        Public Property Height As Integer
        Public Property Sha256 As String = ""

        Public ReadOnly Property SizeBytes As Long
            Get
                Return If(Data Is Nothing, 0, Data.LongLength)
            End Get
        End Property
    End Class

    Public Class NewConcernRequest
        Public Property SubmissionKey As Guid = Guid.NewGuid()
        Public Property CategoryId As Integer
        Public Property SubcategoryId As Integer?
        Public Property BarangayId As Integer?
        Public Property Location As String = ""
        Public Property Description As String = ""
        Public Property ReportedDate As DateTime = DateTime.Today
        Public Property Attachments As New List(Of PendingAttachment)()
    End Class

    Public Class StatusUpdateRequest
        Public Property ConcernId As Integer
        Public Property ExpectedCurrentStatus As ConcernStatus
        Public Property NewStatus As ConcernStatus
        Public Property Remarks As String
        Public Property EffectiveDate As DateTime?
    End Class

    Public Class StatusTransition
        Public Property FromStatus As ConcernStatus
        Public Property ToStatus As ConcernStatus
        Public Property ActorScope As String = ""
    End Class

    Public Class CountItem
        Public Property Label As String = ""
        Public Property Value As Integer
        Public Property ColorHex As String = "#1E6FE8"
        Public Property Key As Integer
    End Class

    Public Class DashboardStats
        Public Property Total As Integer
        Public Property ByStatus As New Dictionary(Of ConcernStatus, Integer)()
        Public Property ByCategory As New List(Of CountItem)()
        Public Property Recent As New List(Of ConcernSummary)()

        Public Function Count(status As ConcernStatus) As Integer
            Dim v As Integer
            If ByStatus.TryGetValue(status, v) Then Return v
            Return 0
        End Function
    End Class

End Namespace
