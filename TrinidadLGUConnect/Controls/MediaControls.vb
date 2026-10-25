Imports System.ComponentModel
Imports System.Drawing.Drawing2D
Imports System.Drawing.Text
Imports TrinidadLGUConnect.Helpers
Imports TrinidadLGUConnect.Models
Imports TrinidadLGUConnect.Services

Namespace Controls

    ''' <summary>Rounded image tile. Clicking (or Enter) raises Opened; the optional red × raises RemoveClicked.</summary>
    Public Class ThumbnailTile
        Inherits Control

        Private _image As Image
        Private _hover As Boolean
        Private _removeHover As Boolean

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property ShowRemove As Boolean
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property Radius As Integer = 8
        ''' <summary>Index of the picture within its report (used to open the viewer at the right image).</summary>
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property Index As Integer

        Public Event Opened As EventHandler
        Public Event RemoveClicked As EventHandler

        Public Sub New()
            SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw Or ControlStyles.UserPaint Or ControlStyles.Selectable, True)
            Size = New Size(160, 120)
            Cursor = Cursors.Hand
            TabStop = True
            AccessibleRole = AccessibleRole.Graphic
        End Sub

        ''' <summary>Takes ownership of the image (disposed with the control).</summary>
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property Image As Image
            Get
                Return _image
            End Get
            Set(value As Image)
                If _image IsNot Nothing AndAlso _image IsNot value Then _image.Dispose()
                _image = value
                Invalidate()
            End Set
        End Property

        Public Sub SetImageBytes(data As Byte())
            Image = AttachmentService.TryDecode(data)
        End Sub

        Private ReadOnly Property RemoveRect As Rectangle
            Get
                Return New Rectangle(Width - 26, 4, 22, 22)
            End Get
        End Property

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            Theme.PaintParent(Me, g)
            Theme.HighQuality(g)
            Dim r As New RectangleF(0, 0, Width - 1, Height - 1)
            Using p = Theme.RoundedRect(r, Radius)
                Dim old = g.Clip
                g.SetClip(p)
                If _image IsNot Nothing Then
                    ' Cover-fit
                    Dim scale = Math.Max(Width / CDbl(_image.Width), Height / CDbl(_image.Height))
                    Dim w = CSng(_image.Width * scale), h = CSng(_image.Height * scale)
                    g.DrawImage(_image, (Width - w) / 2, (Height - h) / 2, w, h)
                Else
                    Using b As New SolidBrush(Color.FromArgb(236, 240, 246))
                        g.FillPath(b, p)
                    End Using
                    Theme.DrawGlyph(g, Glyph.Photo, ClientRectangle, Theme.Placeholder, 18)
                    TextRenderer.DrawText(g, "Picture unavailable", Theme.F(7.5F), New Rectangle(0, Height - 22, Width, 18), Theme.Muted, TextFormatFlags.NoPrefix Or TextFormatFlags.HorizontalCenter)
                End If
                If _hover OrElse Focused Then
                    Using b As New SolidBrush(Color.FromArgb(70, 13, 43, 94))
                        g.FillPath(b, p)
                    End Using
                    Theme.DrawGlyph(g, Glyph.ZoomIn, ClientRectangle, Color.White, 16)
                End If
                g.Clip = old
                Using pen As New Pen(Theme.CardBorder)
                    g.DrawPath(pen, p)
                End Using
            End Using
            If ShowRemove Then
                Dim rr = RemoveRect
                Using b As New SolidBrush(If(_removeHover, Color.FromArgb(200, 40, 50), Theme.Danger))
                    g.FillEllipse(b, rr)
                End Using
                Theme.DrawGlyph(g, Glyph.Cancel, rr, Color.White, 7)
            End If
        End Sub

        Protected Overrides Sub OnMouseEnter(e As EventArgs)
            _hover = True : Invalidate() : MyBase.OnMouseEnter(e)
        End Sub
        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            _hover = False : _removeHover = False : Invalidate() : MyBase.OnMouseLeave(e)
        End Sub
        Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
            MyBase.OnMouseMove(e)
            Dim over = ShowRemove AndAlso RemoveRect.Contains(e.Location)
            If over <> _removeHover Then
                _removeHover = over
                Invalidate()
            End If
        End Sub
        Protected Overrides Sub OnGotFocus(e As EventArgs)
            Invalidate() : MyBase.OnGotFocus(e)
        End Sub
        Protected Overrides Sub OnLostFocus(e As EventArgs)
            Invalidate() : MyBase.OnLostFocus(e)
        End Sub

        Protected Overrides Sub OnMouseClick(e As MouseEventArgs)
            MyBase.OnMouseClick(e)
            If e.Button <> MouseButtons.Left Then Return
            If ShowRemove AndAlso RemoveRect.Contains(e.Location) Then
                RaiseEvent RemoveClicked(Me, EventArgs.Empty)
            Else
                RaiseEvent Opened(Me, EventArgs.Empty)
            End If
        End Sub

        Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
            Return keyData = Keys.Enter OrElse keyData = Keys.Delete OrElse MyBase.IsInputKey(keyData)
        End Function

        Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
            MyBase.OnKeyDown(e)
            If e.KeyCode = Keys.Enter OrElse e.KeyCode = Keys.Space Then RaiseEvent Opened(Me, EventArgs.Empty)
            If e.KeyCode = Keys.Delete AndAlso ShowRemove Then RaiseEvent RemoveClicked(Me, EventArgs.Empty)
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing AndAlso _image IsNot Nothing Then
                _image.Dispose()
                _image = Nothing
            End If
            MyBase.Dispose(disposing)
        End Sub
    End Class

    ''' <summary>Dashed "Click to choose a picture or drag and drop here" area. Accepts dropped files.</summary>
    Public Class DropZone
        Inherits Control

        Private _hover As Boolean
        Private _dragging As Boolean

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property Compact As Boolean
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property Hint As String = "JPG, PNG (Max size 5MB)"

        Public Event BrowseRequested As EventHandler
        Public Event FilesDropped(sender As Object, paths As String())

        Public Sub New()
            SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw Or ControlStyles.UserPaint Or ControlStyles.Selectable, True)
            AllowDrop = True
            Cursor = Cursors.Hand
            TabStop = True
            AccessibleRole = AccessibleRole.PushButton
            AccessibleName = "Choose pictures to attach"
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            Theme.PaintParent(Me, g)
            Theme.HighQuality(g)
            Dim r As New RectangleF(1, 1, Width - 3, Height - 3)
            Dim fill = If(_dragging, Color.FromArgb(228, 238, 255), If(_hover OrElse Focused, Color.FromArgb(240, 246, 255), Color.FromArgb(248, 250, 253)))
            Using p = Theme.RoundedRect(r, 8), b As New SolidBrush(fill), pen As New Pen(If(_dragging OrElse Focused, Theme.Primary, Color.FromArgb(176, 192, 216)), 1.3F) With {.DashStyle = DashStyle.Dash}
                g.FillPath(b, p)
                g.DrawPath(pen, p)
            End Using
            If Compact Then
                Dim textW = 200
                Dim totalW = 34 + textW
                Dim x = (Width - totalW) \ 2
                Theme.DrawGlyph(g, Glyph.Camera, New Rectangle(x, 0, 30, Height - 18), Theme.Navy, 15)
                TextRenderer.DrawText(g, "Click to attach image", Theme.F(9, FontStyle.Bold), New Rectangle(x + 36, Height \ 2 - 26, textW, 18), Theme.Ink, TextFormatFlags.NoPrefix Or TextFormatFlags.Left)
                TextRenderer.DrawText(g, "or drag and drop", Theme.F(8.5F), New Rectangle(x + 36, Height \ 2 - 9, textW, 18), Theme.Muted, TextFormatFlags.NoPrefix Or TextFormatFlags.Left)
                TextRenderer.DrawText(g, Hint, Theme.F(7.75F), New Rectangle(0, Height \ 2 + 12, Width, 16), Theme.Muted, TextFormatFlags.NoPrefix Or TextFormatFlags.HorizontalCenter)
            Else
                Dim iconBox As New Rectangle((Width - 56) \ 2, Height \ 2 - 74, 56, 48)
                Using p = Theme.RoundedRect(iconBox, 8), pen As New Pen(Theme.Navy, 2), b As New SolidBrush(Color.FromArgb(226, 236, 250))
                    g.FillPath(b, p)
                    g.DrawPath(pen, p)
                End Using
                Theme.DrawGlyph(g, Glyph.Photo, iconBox, Theme.Navy, 18)
                TextRenderer.DrawText(g, "Click to choose a picture", Theme.F(10), New Rectangle(0, Height \ 2 - 14, Width, 22), Theme.Ink, TextFormatFlags.NoPrefix Or TextFormatFlags.HorizontalCenter)
                TextRenderer.DrawText(g, "or drag and drop here", Theme.F(10), New Rectangle(0, Height \ 2 + 8, Width, 22), Theme.Ink, TextFormatFlags.NoPrefix Or TextFormatFlags.HorizontalCenter)
                TextRenderer.DrawText(g, Hint, Theme.F(8.25F), New Rectangle(0, Height \ 2 + 38, Width, 18), Theme.Muted, TextFormatFlags.NoPrefix Or TextFormatFlags.HorizontalCenter)
            End If
        End Sub

        Protected Overrides Sub OnMouseEnter(e As EventArgs)
            _hover = True : Invalidate() : MyBase.OnMouseEnter(e)
        End Sub
        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            _hover = False : Invalidate() : MyBase.OnMouseLeave(e)
        End Sub
        Protected Overrides Sub OnGotFocus(e As EventArgs)
            Invalidate() : MyBase.OnGotFocus(e)
        End Sub
        Protected Overrides Sub OnLostFocus(e As EventArgs)
            Invalidate() : MyBase.OnLostFocus(e)
        End Sub
        Protected Overrides Sub OnClick(e As EventArgs)
            MyBase.OnClick(e)
            RaiseEvent BrowseRequested(Me, EventArgs.Empty)
        End Sub
        Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
            MyBase.OnKeyDown(e)
            If e.KeyCode = Keys.Enter OrElse e.KeyCode = Keys.Space Then RaiseEvent BrowseRequested(Me, EventArgs.Empty)
        End Sub

        Protected Overrides Sub OnDragEnter(e As DragEventArgs)
            MyBase.OnDragEnter(e)
            If e.Data IsNot Nothing AndAlso e.Data.GetDataPresent(DataFormats.FileDrop) Then
                e.Effect = DragDropEffects.Copy
                _dragging = True
                Invalidate()
            Else
                e.Effect = DragDropEffects.None
            End If
        End Sub

        Protected Overrides Sub OnDragLeave(e As EventArgs)
            MyBase.OnDragLeave(e)
            _dragging = False
            Invalidate()
        End Sub

        Protected Overrides Sub OnDragDrop(e As DragEventArgs)
            MyBase.OnDragDrop(e)
            _dragging = False
            Invalidate()
            Dim files = TryCast(e.Data?.GetData(DataFormats.FileDrop), String())
            If files IsNot Nothing AndAlso files.Length > 0 Then RaiseEvent FilesDropped(Me, files)
        End Sub
    End Class

    Public Class TimelineStep
        Public Property Title As String = ""
        Public Property Color As Color
        Public Property Done As Boolean
        Public Property Lines As New List(Of String)()
    End Class

    ''' <summary>Vertical status timeline (Submitted → Under Review → In Progress → Resolved → Closed).</summary>
    Public Class StatusTimeline
        Inherits Control

        Private _steps As New List(Of TimelineStep)()
        Private Const StepGap As Integer = 14

        Public Sub New()
            SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw Or ControlStyles.UserPaint, True)
            BackColor = Color.White
        End Sub

        Public Sub SetSteps(steps As IEnumerable(Of TimelineStep))
            _steps = steps.ToList()
            AccessibleDescription = String.Join("; ", _steps.Select(Function(s) s.Title & ": " & String.Join(" ", s.Lines)))
            Height = PreferredHeightFor(Width)
            Invalidate()
        End Sub

        Private Function LineHeights(w As Integer) As List(Of Integer)
            Dim res As New List(Of Integer)()
            For Each s In _steps
                Dim h = 20
                For Each l In s.Lines
                    h += TextRenderer.MeasureText(l, Theme.F(8.25F), New Size(Math.Max(50, w - 44), 1000), TextFormatFlags.NoPrefix Or TextFormatFlags.WordBreak).Height + 1
                Next
                res.Add(h)
            Next
            Return res
        End Function

        Public Function PreferredHeightFor(w As Integer) As Integer
            Return LineHeights(w).Sum() + _steps.Count * StepGap + 8
        End Function

        Protected Overrides Sub OnResize(e As EventArgs)
            MyBase.OnResize(e)
            Dim h = PreferredHeightFor(Width)
            If Math.Abs(h - Height) > 2 Then Height = h
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            g.Clear(BackColor)
            Theme.HighQuality(g)
            Dim heights = LineHeights(Width)
            Dim y = 4
            Dim centers As New List(Of Integer)()
            For i = 0 To _steps.Count - 1
                centers.Add(y + 9)
                y += heights(i) + StepGap
            Next
            For i = 0 To _steps.Count - 2
                Using pen As New Pen(If(_steps(i + 1).Done, Theme.Blend(_steps(i + 1).Color, Color.White, 0.4), Color.FromArgb(220, 226, 236)), 2)
                    g.DrawLine(pen, 13, centers(i) + 10, 13, centers(i + 1) - 10)
                End Using
            Next
            y = 4
            For i = 0 To _steps.Count - 1
                Dim s = _steps(i)
                Dim c As New Rectangle(4, centers(i) - 9, 18, 18)
                If s.Done Then
                    Using b As New SolidBrush(s.Color)
                        g.FillEllipse(b, c)
                    End Using
                    Theme.DrawGlyph(g, Glyph.CheckMark, c, Color.White, 7)
                Else
                    Using b As New SolidBrush(Color.White), pen As New Pen(Theme.Blend(s.Color, Color.White, 0.25), 2.4F)
                        g.FillEllipse(b, c)
                        g.DrawEllipse(pen, Rectangle.Inflate(c, -1, -1))
                    End Using
                End If
                TextRenderer.DrawText(g, s.Title, Theme.F(9.25F, FontStyle.Bold), New Rectangle(34, y, Width - 36, 20), s.Color, TextFormatFlags.NoPrefix Or TextFormatFlags.Left Or TextFormatFlags.VerticalCenter)
                Dim ly = y + 20
                For Each l In s.Lines
                    Dim sz = TextRenderer.MeasureText(l, Theme.F(8.25F), New Size(Math.Max(50, Width - 44), 1000), TextFormatFlags.NoPrefix Or TextFormatFlags.WordBreak)
                    TextRenderer.DrawText(g, l, Theme.F(8.25F), New Rectangle(34, ly, Width - 40, sz.Height), Theme.Muted, TextFormatFlags.NoPrefix Or TextFormatFlags.WordBreak Or TextFormatFlags.Left)
                    ly += sz.Height + 1
                Next
                y += heights(i) + StepGap
            Next
        End Sub

        ''' <summary>Builds the five canonical steps from saved history records.</summary>
        Public Shared Function BuildSteps(detail As ConcernDetail) As List(Of TimelineStep)
            Dim steps As New List(Of TimelineStep)()
            Dim currentOrder = CInt(detail.Status)
            For Each st In AllStatuses
                Dim entries = detail.History.Where(Function(h) h.ToStatus = st).ToList()
                Dim stepItem As New TimelineStep With {.Title = DisplayName(st), .Color = Theme.StatusSolid(st)}
                If st = ConcernStatus.Closed AndAlso entries.Count = 0 Then stepItem.Color = Theme.Muted
                If entries.Count > 0 Then
                    Dim last = entries.Last()
                    stepItem.Done = True
                    stepItem.Lines.Add($"{TextFormat.DateTimeText(last.ChangedAt)}  ·  by {last.ChangedByDisplay}")
                    If Not String.IsNullOrWhiteSpace(last.Remarks) Then stepItem.Lines.Add("""" & last.Remarks & """")
                    If entries.Count > 1 Then stepItem.Lines.Add($"(reached {entries.Count} times)")
                ElseIf detail.Status = ConcernStatus.Closed OrElse CInt(st) < currentOrder Then
                    stepItem.Lines.Add("- Skipped")
                Else
                    stepItem.Lines.Add("- Pending")
                End If
                steps.Add(stepItem)
            Next
            Return steps
        End Function
    End Class

    Public Enum BrandStyle
        ''' <summary>Narrow navy panel with the photo fading in at the bottom (resident login/registration).</summary>
        SidePanel
        ''' <summary>Full-bleed photo with a blue overlay (admin login).</summary>
        FullBackground
        ''' <summary>Full-bleed photo with the decorative white and gold band (landing/splash).</summary>
        Splash
    End Enum

    ''' <summary>Trinidad LGU Connect branding: seal, name, subtitle and tagline over the landscape photo.</summary>
    Public Class BrandPanel
        Inherits Control

        Private ReadOnly _style As BrandStyle

        ''' <summary>Horizontal space reserved on the right (e.g. for the login card in FullBackground style).</summary>
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property TextAreaWidth As Integer

        ''' <summary>Optional photo instead of the default for this style.</summary>
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property Photo As Image

        Public Sub New(style As BrandStyle)
            _style = style
            SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw Or ControlStyles.UserPaint, True)
            BackColor = Theme.SidebarTop
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            Theme.HighQuality(g)
            Dim photo = If(Me.Photo, If(_style = BrandStyle.SidePanel, Assets.PanelPhoto, Assets.Background))
            If _style = BrandStyle.SidePanel Then
                Using br As New LinearGradientBrush(ClientRectangle, Color.FromArgb(12, 52, 120), Color.FromArgb(10, 38, 92), LinearGradientMode.Vertical)
                    g.FillRectangle(br, ClientRectangle)
                End Using
                If photo IsNot Nothing Then
                    Dim h = CInt(Height * 0.42)
                    Dim dest As New Rectangle(0, Height - h, Width, h)
                    DrawCover(g, photo, dest)
                    Using fade As New LinearGradientBrush(New Rectangle(0, dest.Y - 1, Width, CInt(h * 0.55) + 2), Color.FromArgb(255, 10, 40, 96), Color.FromArgb(0, 10, 40, 96), LinearGradientMode.Vertical)
                        g.FillRectangle(fade, 0, dest.Y, Width, CInt(h * 0.55))
                    End Using
                End If
            Else
                If photo IsNot Nothing Then DrawCover(g, photo, ClientRectangle)
                Using overlay As New LinearGradientBrush(ClientRectangle, Color.FromArgb(150, 8, 40, 110), Color.FromArgb(90, 8, 40, 110), LinearGradientMode.Vertical)
                    g.FillRectangle(overlay, ClientRectangle)
                End Using
                If _style = BrandStyle.Splash Then DrawBand(g)
            End If

            Dim areaW = If(TextAreaWidth > 0, TextAreaWidth, Width)
            Dim sealSize = Math.Min(If(_style = BrandStyle.SidePanel, 96, 118), CInt(areaW * 0.36))
            Dim top = If(_style = BrandStyle.SidePanel, CInt(Height * 0.1), CInt(Height * 0.13))
            If _style = BrandStyle.Splash Then top = CInt(Height * 0.08)
            Dim cx = areaW \ 2
            If Assets.Seal IsNot Nothing Then g.DrawImage(Assets.Seal, cx - sealSize \ 2, top, sealSize, sealSize)
            Dim y = top + sealSize + 14
            Dim titleSize = If(_style = BrandStyle.SidePanel, 15.0F, 21.0F)
            DrawCentered(g, "TRINIDAD LGU CONNECT", Theme.F(titleSize, FontStyle.Bold), Color.White, cx, y, areaW)
            y += CInt(titleSize * 2.0F)
            Dim subSize = If(_style = BrandStyle.SidePanel, 10.0F, 13.0F)
            DrawCentered(g, "Community Concern Reporting", Theme.F(subSize), Color.White, cx, y, areaW)
            y += CInt(subSize * 1.85F)
            DrawCentered(g, "and Monitoring System", Theme.F(subSize), Color.White, cx, y, areaW)
            y += CInt(subSize * 2.4F)
            Dim lineW = CInt(Math.Min(areaW * 0.72, 340))
            Using pen As New Pen(Color.FromArgb(150, 255, 255, 255), 1)
                g.DrawLine(pen, cx - lineW \ 2, y, cx + lineW \ 2, y)
            End Using
            Using b As New SolidBrush(Color.White)
                g.FillEllipse(b, cx - 3, y - 3, 6, 6)
            End Using
            y += 16
            Dim tagFont = Assets.TaglineFont(If(_style = BrandStyle.SidePanel, 13.0F, 16.0F))
            DrawCentered(g, "A Cleaner, Safer and Better", tagFont, Color.White, cx, y, areaW)
            y += CInt(tagFont.Size * 1.8F)
            DrawCentered(g, "Trinidad for All", tagFont, Color.White, cx, y, areaW)
        End Sub

        Private Sub DrawBand(g As Graphics)
            Dim h = Height
            Dim w = Width
            Using white As New Pen(Color.FromArgb(235, 255, 255, 255), Math.Max(4, h / 90.0F)), gold As New Pen(Color.FromArgb(245, 190, 60), Math.Max(4, h / 80.0F))
                Using band As New GraphicsPath()
                    band.AddBezier(-10, h * 0.78F, w * 0.25F, h * 1.02F, w * 0.6F, h * 1.02F, w + 10, h * 0.72F)
                    band.AddLine(w + 10, h * 0.72F, w + 10, h + 10)
                    band.AddLine(w + 10, h + 10, -10, h + 10)
                    band.CloseFigure()
                    Using br As New LinearGradientBrush(New Rectangle(0, CInt(h * 0.7), w, CInt(h * 0.3) + 1), Color.FromArgb(19, 76, 168), Color.FromArgb(12, 49, 112), LinearGradientMode.Vertical)
                        g.FillPath(br, band)
                    End Using
                End Using
                g.DrawBezier(white, -10, h * 0.76F, w * 0.25F, h * 1.0F, w * 0.6F, h * 1.0F, w + 10, h * 0.7F)
                g.DrawBezier(gold, w * 0.35F, h + 5, w * 0.6F, h * 0.96F, w * 0.85F, h * 0.86F, w + 10, h * 0.74F)
            End Using
        End Sub

        Private Shared Sub DrawCentered(g As Graphics, text As String, font As Font, color As Color, cx As Integer, y As Integer, areaW As Integer)
            TextRenderer.DrawText(g, text, font, New Rectangle(cx - areaW \ 2, y, areaW, CInt(font.Size * 2.2F)), color, TextFormatFlags.NoPrefix Or TextFormatFlags.HorizontalCenter Or TextFormatFlags.NoPadding)
        End Sub

        Public Shared Sub DrawCover(g As Graphics, img As Image, dest As Rectangle)
            Dim scale = Math.Max(dest.Width / CDbl(img.Width), dest.Height / CDbl(img.Height))
            Dim w = CSng(img.Width * scale), h = CSng(img.Height * scale)
            Dim old = g.Clip
            g.SetClip(dest)
            g.DrawImage(img, dest.X + (dest.Width - w) / 2, dest.Y + (dest.Height - h) / 2, w, h)
            g.Clip = old
        End Sub
    End Class

    ''' <summary>Images and fonts loaded from Resources\Images next to the executable.</summary>
    Public Module Assets

        Private _seal As Image
        Private _background As Image
        Private _panel As Image
        Private _loaded As Boolean
        Private _tagFamily As String

        Private Sub EnsureLoaded()
            If _loaded Then Return
            _loaded = True
            Dim dir = Configuration.AppConfig.Current.ImagesDirectory
            _seal = LoadImage(Path.Combine(dir, "trinidad-seal.png"))
            _background = LoadImage(Path.Combine(dir, "login-background.jpg"))
            _panel = LoadImage(Path.Combine(dir, "panel-town.jpg"))
            _coast = LoadImage(Path.Combine(dir, "panel-coast.jpg"))
        End Sub

        Private _coast As Image

        Public ReadOnly Property CoastPhoto As Image
            Get
                EnsureLoaded()
                Return If(_coast, PanelPhoto)
            End Get
        End Property

        Private Function LoadImage(file As String) As Image
            Try
                If Not IO.File.Exists(file) Then
                    AppLog.Warn($"Image asset missing: {file}")
                    Return Nothing
                End If
                Return AttachmentService.TryDecode(IO.File.ReadAllBytes(file))
            Catch ex As IOException
                AppLog.Warn($"Image asset unreadable: {file}", ex)
                Return Nothing
            End Try
        End Function

        Public ReadOnly Property Seal As Image
            Get
                EnsureLoaded()
                Return _seal
            End Get
        End Property

        Public ReadOnly Property Background As Image
            Get
                EnsureLoaded()
                Return _background
            End Get
        End Property

        Public ReadOnly Property PanelPhoto As Image
            Get
                EnsureLoaded()
                Return If(_panel, _background)
            End Get
        End Property

        ''' <summary>Serif italic for the tagline (the reference uses a calligraphic serif).</summary>
        Public Function TaglineFont(size As Single) As Font
            If _tagFamily Is Nothing Then
                Using fonts As New InstalledFontCollection()
                    Dim names = fonts.Families.Select(Function(ff) ff.Name).ToList()
                    _tagFamily = {"Palatino Linotype", "Georgia", "Cambria", "Times New Roman"}.FirstOrDefault(Function(n) names.Contains(n))
                    If _tagFamily Is Nothing Then _tagFamily = Theme.FontName
                End Using
            End If
            Dim f As Font = Nothing
            If Not TagFonts.TryGetValue(size, f) Then
                f = New Font(_tagFamily, size, FontStyle.Italic, GraphicsUnit.Point)
                TagFonts(size) = f
            End If
            Return f
        End Function

        Private ReadOnly TagFonts As New Dictionary(Of Single, Font)()

    End Module

    ''' <summary>Scrollable on-screen preview of a report, drawn by the same renderer as print and PDF.</summary>
    Public Class ReportPreviewControl
        Inherits ScrollableControl

        Private _renderer As ReportRenderer
        Private ReadOnly _pageCache As New List(Of Bitmap)()
        Private _cacheWidth As Integer = -1

        Public Sub New()
            SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw Or ControlStyles.UserPaint, True)
            AutoScroll = True
            BackColor = Color.FromArgb(232, 237, 245)
        End Sub

        Public Sub SetReport(report As ReportResult)
            ClearCache()
            If report Is Nothing Then
                _renderer = Nothing
            Else
                _renderer = New ReportRenderer(report, Assets.Seal)
                Using bmp As New Bitmap(10, 10), g = Graphics.FromImage(bmp)
                    Dim canvas As New GdiReportCanvas(g)
                    _renderer.Layout(ReportRenderer.LetterWidth, ReportRenderer.LetterHeight, canvas)
                    canvas.DisposeFonts()
                End Using
            End If
            Invalidate()
        End Sub

        Public ReadOnly Property PageCount As Integer
            Get
                Return If(_renderer Is Nothing, 0, _renderer.PageCount)
            End Get
        End Property

        Private Sub ClearCache()
            For Each b In _pageCache : b.Dispose() : Next
            _pageCache.Clear()
            _cacheWidth = -1
        End Sub

        Private Function PageWidthPx() As Integer
            Return Math.Max(200, Math.Min(ClientSize.Width - 40, 980))
        End Function

        Private Sub EnsureCache()
            If _renderer Is Nothing Then Return
            Dim w = PageWidthPx()
            If w = _cacheWidth AndAlso _pageCache.Count = _renderer.PageCount Then Return
            ClearCache()
            _cacheWidth = w
            Dim pxPerPt = w / ReportRenderer.LetterWidth
            Dim h = CInt(ReportRenderer.LetterHeight * pxPerPt)
            For i = 0 To _renderer.PageCount - 1
                Dim bmp As New Bitmap(w, h)
                bmp.SetResolution(96, 96)
                Using g = Graphics.FromImage(bmp)
                    Dim canvas As New GdiReportCanvas(g)
                    ' PageUnit is Point (96/72 px per pt at 96 DPI); scale so one point = pxPerPt pixels.
                    g.ScaleTransform(CSng(pxPerPt / (96.0 / 72.0)), CSng(pxPerPt / (96.0 / 72.0)))
                    _renderer.DrawPage(canvas, i)
                    canvas.DisposeFonts()
                End Using
                _pageCache.Add(bmp)
            Next
            Dim totalH = _pageCache.Sum(Function(b) b.Height + 20) + 20
            AutoScrollMinSize = New Size(0, totalH)
        End Sub

        Protected Overrides Sub OnResize(e As EventArgs)
            MyBase.OnResize(e)
            Invalidate()
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            e.Graphics.Clear(BackColor)
            If _renderer Is Nothing Then
                TextRenderer.DrawText(e.Graphics, "Generate a report to see its preview.", Theme.F(9.5F), ClientRectangle, Theme.Muted, TextFormatFlags.NoPrefix Or TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter)
                Return
            End If
            EnsureCache()
            Dim y = 20 + AutoScrollPosition.Y
            For Each bmp In _pageCache
                Dim x = (ClientSize.Width - bmp.Width) \ 2
                Using shadow As New SolidBrush(Color.FromArgb(40, 0, 0, 0))
                    e.Graphics.FillRectangle(shadow, x + 3, y + 3, bmp.Width, bmp.Height)
                End Using
                e.Graphics.DrawImage(bmp, New Rectangle(x, y, bmp.Width, bmp.Height))
                y += bmp.Height + 20
            Next
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then ClearCache()
            MyBase.Dispose(disposing)
        End Sub
    End Class

End Namespace
