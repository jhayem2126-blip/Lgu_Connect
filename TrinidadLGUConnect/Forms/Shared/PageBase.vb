Imports TrinidadLGUConnect.Controls
Imports TrinidadLGUConnect.Models
Imports TrinidadLGUConnect.Services

Namespace Forms

    ''' <summary>
    ''' A screen hosted inside <see cref="MainShellForm"/>. The shell keeps a back stack of pages, so
    ''' "Close"/"Cancel" return to the previous screen, which is then refreshed from the database.
    ''' </summary>
    Public Class PageBase
        Inherits UserControl

        Private _busyCount As Integer

        Public Property Shell As MainShellForm

        Public Sub New()
            BackColor = Theme.AppBack
            Font = Theme.F(9.5F)
            Dock = DockStyle.Fill
            Padding = New Padding(24, 18, 24, 18)
            AutoScroll = True
            DoubleBuffered = True
        End Sub

        ''' <summary>Window-title suffix, e.g. "My Reports".</summary>
        Public Overridable ReadOnly Property Title As String
            Get
                Return ""
            End Get
        End Property

        ''' <summary>Sidebar item highlighted while this page is shown.</summary>
        Public Overridable ReadOnly Property NavKey As String
            Get
                Return ""
            End Get
        End Property

        ''' <summary>Reload data from the database. Called on first show, on Back and after data changes.</summary>
        Public Overridable Function RefreshDataAsync() As Task
            Return Task.CompletedTask
        End Function

        ''' <summary>When True, the page reloads automatically after any concern submission or status change.</summary>
        Public Overridable ReadOnly Property RefreshOnDataChange As Boolean
            Get
                Return True
            End Get
        End Property

        Protected ReadOnly Property Session As UserSession
            Get
                Return AppServices.Instance.Session
            End Get
        End Property

        Protected ReadOnly Property Svc As AppServices
            Get
                Return AppServices.Instance
            End Get
        End Property

        Friend Sub SetBusy(busy As Boolean)
            _busyCount = Math.Max(0, _busyCount + If(busy, 1, -1))
            Shell?.SetBusy(_busyCount > 0)
        End Sub

        ''' <summary>Runs work in the background with the page's busy indicator and error handling.</summary>
        Protected Function RunAsync(Of T)(work As Func(Of Task(Of T)), ParamArray disable As Control()) As Task(Of (Ok As Boolean, Value As T, [Error] As Exception))
            Return UiHelpers.TryRunAsync(Me, work, disable)
        End Function

        Protected Function RunAsync(work As Func(Of Task), ParamArray disable As Control()) As Task(Of Boolean)
            Return UiHelpers.TryRunAsync(Me, work, disable)
        End Function

        ' ---------------------------------------------------------------- layout helpers

        ''' <summary>Page header: icon (optional), title and subtitle.</summary>
        Protected Function BuildHeader(title As String, Optional subtitle As String = Nothing, Optional glyphText As String = Nothing) As Control
            Dim p As New Panel With {.Dock = DockStyle.Top, .Height = If(subtitle Is Nothing, 40, 58), .BackColor = Color.Transparent}
            Dim x = 0
            If glyphText IsNot Nothing Then
                Dim icon As New Label With {.Text = glyphText, .Font = Theme.IconFont(18), .ForeColor = Theme.Primary, .AutoSize = False,
                                            .Size = New Size(44, 44), .TextAlign = ContentAlignment.MiddleCenter, .BackColor = Color.FromArgb(226, 236, 252), .Location = New Point(0, 4)}
                p.Controls.Add(icon)
                x = 54
            End If
            Dim t = Heading(title, 15)
            t.Location = New Point(x, 2)
            p.Controls.Add(t)
            If subtitle IsNot Nothing Then
                Dim s = Subtle(subtitle, 9)
                s.Location = New Point(x + 2, 32)
                p.Controls.Add(s)
            End If
            Return p
        End Function

        ''' <summary>A vertical spacer docked to the top.</summary>
        Protected Shared Function Gap(h As Integer) As Control
            Return New Panel With {.Dock = DockStyle.Top, .Height = h, .BackColor = Color.Transparent}
        End Function

        ''' <summary>Adds top-docked controls so they appear in the given order (WinForms docks in reverse).</summary>
        Protected Sub AddTopDown(parent As Control, ParamArray items As Control())
            For i = items.Length - 1 To 0 Step -1
                parent.Controls.Add(items(i))
            Next
        End Sub

    End Class

End Namespace
