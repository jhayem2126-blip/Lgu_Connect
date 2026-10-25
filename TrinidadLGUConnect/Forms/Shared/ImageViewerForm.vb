Imports System.Drawing.Drawing2D
Imports TrinidadLGUConnect.Controls
Imports TrinidadLGUConnect.Helpers
Imports TrinidadLGUConnect.Models
Imports TrinidadLGUConnect.Services

Namespace Forms

    ''' <summary>One picture in the viewer. Bytes are loaded on demand from the attachment record (or come from memory before submission).</summary>
    Public Class ViewerImage
        Public Property FileName As String = ""
        Public Property SizeText As String = ""
        Public Property Loader As Func(Of Task(Of Byte()))

        ''' <summary>A saved attachment: loads its full image through the access-checked service.</summary>
        Public Shared Function FromAttachment(a As AttachmentInfo) As ViewerImage
            Dim id = a.Id
            Return New ViewerImage With {
                .FileName = a.FileName, .SizeText = a.SizeText,
                .Loader = Async Function()
                              Dim data = Await AppServices.Instance.Concerns.GetAttachmentAsync(AppServices.Instance.Session, id)
                              Return data.Data
                          End Function}
        End Function

        ''' <summary>A picture selected but not yet submitted.</summary>
        Public Shared Function FromPending(p As PendingAttachment) As ViewerImage
            Dim bytes = p.Data
            Return New ViewerImage With {.FileName = p.FileName, .SizeText = TextFormat.FileSize(p.SizeBytes), .Loader = Function() Task.FromResult(bytes)}
        End Function
    End Class

    ''' <summary>Large image preview with Previous/Next, Zoom In/Out, Fit to Window, Actual Size, file name and Close.</summary>
    Public Class ImageViewerForm
        Inherits Form

        Private ReadOnly _items As List(Of ViewerImage)
        Private ReadOnly _cache As New Dictionary(Of Integer, Image)()
        Private _index As Integer
        Private ReadOnly _canvas As New ImageCanvas With {.Dock = DockStyle.Fill}
        Private ReadOnly _bar As New Panel With {.Dock = DockStyle.Top, .Height = 56, .BackColor = Color.FromArgb(17, 24, 39)}
        Private ReadOnly _name As New Label With {.AutoSize = False, .ForeColor = Color.White, .Font = Theme.F(10, FontStyle.Bold), .BackColor = Color.Transparent, .AutoEllipsis = True}
        Private ReadOnly _info As New Label With {.AutoSize = False, .ForeColor = Color.FromArgb(170, 180, 198), .Font = Theme.F(8.5F), .BackColor = Color.Transparent}
        Private ReadOnly _prev As ModernButton
        Private ReadOnly _next As ModernButton
        Private ReadOnly _zoomLabel As New Label With {.AutoSize = False, .ForeColor = Color.White, .Font = Theme.F(8.5F), .TextAlign = ContentAlignment.MiddleCenter, .Width = 54, .BackColor = Color.Transparent}

        Public Sub New(items As IEnumerable(Of ViewerImage), startIndex As Integer)
            _items = items.ToList()
            _index = Math.Max(0, Math.Min(startIndex, _items.Count - 1))
            ApplyWindowDefaults(Me)
            Text = $"{AppTitle} - Image Viewer"
            BackColor = Color.FromArgb(11, 15, 25)
            Size = New Size(1100, 760)
            MinimumSize = New Size(700, 480)
            StartPosition = FormStartPosition.CenterParent
            KeyPreview = True
            ShowInTaskbar = False

            Controls.Add(_canvas)
            Controls.Add(_bar)
            _bar.Controls.Add(_name)
            _bar.Controls.Add(_info)
            _prev = ToolButton(Glyph.ChevronLeft, "Previous (Left arrow)")
            _next = ToolButton(Glyph.ChevronRight, "Next (Right arrow)")
            Dim zoomOut = ToolButton(Glyph.ZoomOut, "Zoom out (-)")
            Dim zoomIn = ToolButton(Glyph.ZoomIn, "Zoom in (+)")
            Dim fit = ToolButton(Glyph.FitPage, "Fit to window (0)")
            Dim actual = ToolButton(Glyph.ActualSize, "Actual size (1)")
            Dim close = New ModernButton("Close", ButtonKind.Primary) With {.Width = 84, .Height = 36}
            Dim tools As Control() = {_prev, _next, zoomOut, _zoomLabel, zoomIn, fit, actual, close}
            For Each t In tools
                t.Anchor = AnchorStyles.Top Or AnchorStyles.Right
                _bar.Controls.Add(t)
            Next
            _layoutBar = Sub()
                             Dim x = _bar.Width - 14
                             For i = tools.Length - 1 To 0 Step -1
                                 x -= tools(i).Width
                                 tools(i).Location = New Point(x, (_bar.Height - tools(i).Height) \ 2)
                                 x -= 6
                             Next
                             _name.SetBounds(16, 8, Math.Max(80, x - 24), 22)
                             _info.SetBounds(16, 30, Math.Max(80, x - 24), 18)
                         End Sub
            AddHandler _bar.Resize, Sub() _layoutBar()
            AddHandler _prev.Click, Sub() ShowIndex(_index - 1)
            AddHandler _next.Click, Sub() ShowIndex(_index + 1)
            AddHandler zoomIn.Click, Sub() _canvas.ZoomBy(1.25)
            AddHandler zoomOut.Click, Sub() _canvas.ZoomBy(0.8)
            AddHandler fit.Click, Sub() _canvas.FitToWindow()
            AddHandler actual.Click, Sub() _canvas.ActualSize()
            AddHandler close.Click, Sub() Me.Close()
            AddHandler _canvas.ZoomChanged, Sub() _zoomLabel.Text = If(_canvas.HasImage, $"{Math.Round(_canvas.Zoom * 100)}%", "")
            CancelButton = close
        End Sub

        Private Function ToolButton(glyphText As String, tip As String) As ModernButton
            Dim b As New ModernButton("", ButtonKind.Secondary, glyphText) With {.Size = New Size(38, 36), .AccessibleName = tip}
            Dim tt As New ToolTip()
            tt.SetToolTip(b, tip)
            Return b
        End Function

        Private ReadOnly _layoutBar As Action

        Protected Overrides Sub OnLoad(e As EventArgs)
            MyBase.OnLoad(e)
            _layoutBar()
        End Sub

        Protected Overrides Sub OnShown(e As EventArgs)
            MyBase.OnShown(e)
            ShowIndex(_index)
        End Sub

        Private Async Sub ShowIndex(i As Integer)
            If _items.Count = 0 Then Return
            If i < 0 OrElse i >= _items.Count Then Return
            _index = i
            Dim item = _items(i)
            _name.Text = item.FileName
            _info.Text = $"Picture {i + 1} of {_items.Count}" & If(String.IsNullOrEmpty(item.SizeText), "", $"  ·  {item.SizeText}")
            _prev.Enabled = i > 0
            _next.Enabled = i < _items.Count - 1
            Dim img As Image = Nothing
            If Not _cache.TryGetValue(i, img) Then
                _canvas.SetMessage("Loading picture...")
                Dim r = Await TryRunAsync(Me, item.Loader, showErrors:=False)
                If _index <> i OrElse IsDisposed Then Return
                If Not r.Ok Then
                    _canvas.SetMessage("This picture could not be loaded. " & FriendlyMessage(r.Error))
                    Return
                End If
                img = AttachmentService.TryDecode(r.Value)
                If img Is Nothing Then
                    _canvas.SetMessage("This picture file is damaged or is not a supported image.")
                    Return
                End If
                _cache(i) = img
            End If
            _canvas.SetImage(img)
            _info.Text &= $"  ·  {img.Width} × {img.Height} px"
        End Sub

        Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
            Select Case keyData
                Case Keys.Left : ShowIndex(_index - 1) : Return True
                Case Keys.Right : ShowIndex(_index + 1) : Return True
                Case Keys.Oemplus, Keys.Add : _canvas.ZoomBy(1.25) : Return True
                Case Keys.OemMinus, Keys.Subtract : _canvas.ZoomBy(0.8) : Return True
                Case Keys.D0, Keys.NumPad0 : _canvas.FitToWindow() : Return True
                Case Keys.D1, Keys.NumPad1 : _canvas.ActualSize() : Return True
            End Select
            Return MyBase.ProcessCmdKey(msg, keyData)
        End Function

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then
                For Each img In _cache.Values : img.Dispose() : Next
                _cache.Clear()
            End If
            MyBase.Dispose(disposing)
        End Sub

        ''' <summary>Opens the viewer modally at the given picture.</summary>
        Public Shared Sub Open(owner As IWin32Window, items As IEnumerable(Of ViewerImage), startIndex As Integer)
            Dim list = items.ToList()
            If list.Count = 0 Then Return
            Using v As New ImageViewerForm(list, startIndex)
                v.ShowDialog(owner)
            End Using
        End Sub
    End Class

    ''' <summary>Zoomable, pannable image surface.</summary>
    Public Class ImageCanvas
        Inherits Control

        Private _image As Image
        Private _zoom As Double = 1
        Private _fit As Boolean = True
        Private _offset As PointF
        Private _dragStart As Point
        Private _dragOffset As PointF
        Private _dragging As Boolean
        Private _message As String = ""

        Public Event ZoomChanged As EventHandler

        Public Sub New()
            SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw Or ControlStyles.UserPaint Or ControlStyles.Selectable, True)
            BackColor = Color.FromArgb(11, 15, 25)
        End Sub

        Public ReadOnly Property Zoom As Double
            Get
                Return _zoom
            End Get
        End Property

        Public ReadOnly Property HasImage As Boolean
            Get
                Return _image IsNot Nothing
            End Get
        End Property

        ''' <summary>The image is owned by the caller (the viewer's cache).</summary>
        Public Sub SetImage(img As Image)
            _image = img
            _message = ""
            FitToWindow()
        End Sub

        Public Sub SetMessage(text As String)
            _image = Nothing
            _message = text
            Invalidate()
            RaiseEvent ZoomChanged(Me, EventArgs.Empty)
        End Sub

        Private Function FitZoom() As Double
            If _image Is Nothing Then Return 1
            Return Math.Min(1.0, Math.Min((Width - 40) / CDbl(_image.Width), (Height - 40) / CDbl(_image.Height)))
        End Function

        Public Sub FitToWindow()
            _fit = True
            _zoom = FitZoom()
            _offset = PointF.Empty
            Invalidate()
            RaiseEvent ZoomChanged(Me, EventArgs.Empty)
        End Sub

        Public Sub ActualSize()
            _fit = False
            _zoom = 1
            _offset = PointF.Empty
            Invalidate()
            RaiseEvent ZoomChanged(Me, EventArgs.Empty)
        End Sub

        Public Sub ZoomBy(factor As Double)
            If _image Is Nothing Then Return
            _fit = False
            _zoom = Math.Max(0.05, Math.Min(8, _zoom * factor))
            Invalidate()
            RaiseEvent ZoomChanged(Me, EventArgs.Empty)
        End Sub

        Protected Overrides Sub OnResize(e As EventArgs)
            MyBase.OnResize(e)
            If _fit Then FitToWindow()
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(BackColor)
            If _image Is Nothing Then
                TextRenderer.DrawText(g, _message, Theme.F(10), ClientRectangle, Color.FromArgb(190, 198, 212), TextFormatFlags.NoPrefix Or TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.WordBreak)
                Return
            End If
            g.InterpolationMode = If(_zoom >= 2, InterpolationMode.NearestNeighbor, InterpolationMode.HighQualityBicubic)
            g.PixelOffsetMode = PixelOffsetMode.Half
            Dim w = CSng(_image.Width * _zoom), h = CSng(_image.Height * _zoom)
            g.DrawImage(_image, (Width - w) / 2 + _offset.X, (Height - h) / 2 + _offset.Y, w, h)
        End Sub

        Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
            MyBase.OnMouseWheel(e)
            ZoomBy(If(e.Delta > 0, 1.15, 1 / 1.15))
        End Sub

        Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
            MyBase.OnMouseDown(e)
            Focus()
            If e.Button = MouseButtons.Left AndAlso _image IsNot Nothing Then
                _dragging = True
                _dragStart = e.Location
                _dragOffset = _offset
                Cursor = Cursors.SizeAll
            End If
        End Sub

        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            If _dragging Then
                _offset = New PointF(_dragOffset.X + e.X - _dragStart.X, _dragOffset.Y + e.Y - _dragStart.Y)
                Invalidate()
            End If
        End Sub

        Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
            MyBase.OnMouseUp(e)
            _dragging = False
            Cursor = Cursors.Default
        End Sub

        Protected Overrides Sub OnDoubleClick(e As EventArgs)
            MyBase.OnDoubleClick(e)
            If _fit Then ActualSize() Else FitToWindow()
        End Sub
    End Class

End Namespace
