Namespace Forms

    ''' <summary>
    ''' Places controls top-to-bottom inside a container at a fixed left margin and full width,
    ''' anchored so they stretch horizontally when the container is resized.
    ''' </summary>
    Public Class VStack

        Private ReadOnly _parent As Control
        Private ReadOnly _left As Integer
        Private ReadOnly _right As Integer
        Private _y As Integer

        Public Sub New(parent As Control, left As Integer, top As Integer, Optional right As Integer = -1)
            _parent = parent
            _left = left
            _right = If(right < 0, left, right)
            _y = top
        End Sub

        Public ReadOnly Property Y As Integer
            Get
                Return _y
            End Get
        End Property

        Public ReadOnly Property Width As Integer
            Get
                Return _parent.ClientSize.Width - _left - _right
            End Get
        End Property

        ''' <summary>Adds a control across the full width.</summary>
        Public Function Add(Of T As Control)(c As T, Optional height As Integer = -1, Optional gapAfter As Integer = 10) As T
            c.Location = New Point(_left, _y)
            c.Width = Width
            If height > 0 Then c.Height = height
            c.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
            _parent.Controls.Add(c)
            _y += c.Height + gapAfter
            Return c
        End Function

        ''' <summary>Adds an auto-sized control (e.g. a label) without stretching it.</summary>
        Public Function AddAuto(Of T As Control)(c As T, Optional gapAfter As Integer = 4, Optional indent As Integer = 0) As T
            c.Location = New Point(_left + indent, _y)
            c.Anchor = AnchorStyles.Top Or AnchorStyles.Left
            _parent.Controls.Add(c)
            _y += c.PreferredSize.Height + gapAfter
            Return c
        End Function

        ''' <summary>Adds controls side by side sharing the full width equally.</summary>
        Public Sub AddRow(height As Integer, gap As Integer, gapAfter As Integer, ParamArray items As Control())
            Dim w = (Width - gap * (items.Length - 1)) \ items.Length
            Dim host As New TableLayoutPanel With {.Location = New Point(_left, _y), .Size = New Size(Width, height), .ColumnCount = items.Length, .RowCount = 1,
                                                   .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right, .Margin = Padding.Empty, .Padding = Padding.Empty,
                                                   .BackColor = Color.Transparent}
            For i = 0 To items.Length - 1
                host.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F / items.Length))
                items(i).Dock = DockStyle.Fill
                items(i).Margin = New Padding(If(i = 0, 0, gap \ 2), 0, If(i = items.Length - 1, 0, gap \ 2), 0)
                host.Controls.Add(items(i), i, 0)
            Next
            host.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
            _parent.Controls.Add(host)
            _y += height + gapAfter
        End Sub

        Public Sub Space(h As Integer)
            _y += h
        End Sub

    End Class

End Namespace
