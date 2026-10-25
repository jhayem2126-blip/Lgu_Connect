Imports System.Drawing.Drawing2D
Imports System.Drawing.Text
Imports TrinidadLGUConnect.Models

Namespace Controls

    ''' <summary>Glyph code points from the Windows "Segoe Fluent Icons" / "Segoe MDL2 Assets" fonts.</summary>
    Public NotInheritable Class Glyph
        Private Sub New()
        End Sub
        Public Const Home As String = ChrW(&HE80F)
        Public Const Edit As String = ChrW(&HE70F)
        Public Const Document As String = ChrW(&HE8A5)
        Public Const ReportDoc As String = ChrW(&HE9F9)
        Public Const Contact As String = ChrW(&HE77B)
        Public Const Settings As String = ChrW(&HE713)
        Public Const SignOut As String = ChrW(&HF3B1)
        Public Const Search As String = ChrW(&HE721)
        Public Const Filter As String = ChrW(&HE71C)
        Public Const Calendar As String = ChrW(&HE787)
        Public Const Camera As String = ChrW(&HE722)
        Public Const Photo As String = ChrW(&HE91B)
        Public Const MapPin As String = ChrW(&HE707)
        Public Const CheckMark As String = ChrW(&HE73E)
        Public Const Cancel As String = ChrW(&HE711)
        Public Const Clock As String = ChrW(&HE823)
        Public Const Bell As String = ChrW(&HEA8F)
        Public Const Delete As String = ChrW(&HE74D)
        Public Const Car As String = ChrW(&HE804)
        Public Const Bulb As String = ChrW(&HEA80)
        Public Const Eye As String = ChrW(&HE890)
        Public Const EyeHide As String = ChrW(&HED1A)
        Public Const Lock As String = ChrW(&HE72E)
        Public Const People As String = ChrW(&HE716)
        Public Const Print As String = ChrW(&HE749)
        Public Const Download As String = ChrW(&HE896)
        Public Const Add As String = ChrW(&HE710)
        Public Const Refresh As String = ChrW(&HE72C)
        Public Const ChevronLeft As String = ChrW(&HE76B)
        Public Const ChevronRight As String = ChrW(&HE76C)
        Public Const ChevronDown As String = ChrW(&HE70D)
        Public Const Folder As String = ChrW(&HE8B7)
        Public Const Clipboard As String = ChrW(&HEADF)
        Public Const Mail As String = ChrW(&HE715)
        Public Const Shield As String = ChrW(&HEA18)
        Public Const Info As String = ChrW(&HE946)
        Public Const Warning As String = ChrW(&HE7BA)
        Public Const ZoomIn As String = ChrW(&HE8A3)
        Public Const ZoomOut As String = ChrW(&HE71F)
        Public Const FitPage As String = ChrW(&HE9A6)
        Public Const ActualSize As String = ChrW(&HE740)
        Public Const OpenWindow As String = ChrW(&HE8A7)
        Public Const Cloud As String = ChrW(&HE753)
        Public Const Drop As String = ChrW(&HEB42)
        Public Const Building As String = ChrW(&HEC08)
        Public Const Leaf As String = ChrW(&HEC0A)
        Public Const More As String = ChrW(&HE712)
        Public Const Database As String = ChrW(&HE81E)
        Public Const Key As String = ChrW(&HE8D7)
        Public Const Chart As String = ChrW(&HE9D2)
        Public Const Page As String = ChrW(&HE7C3)
        Public Const Grid As String = ChrW(&HE80A)
    End Class

    ''' <summary>Design tokens taken from the reference screens.</summary>
    Public Module Theme

        ' Surfaces
        Public ReadOnly AppBack As Color = Color.FromArgb(243, 246, 251)
        Public ReadOnly CardBack As Color = Color.White
        Public ReadOnly CardBorder As Color = Color.FromArgb(227, 232, 240)
        Public ReadOnly InputBorder As Color = Color.FromArgb(213, 220, 232)
        Public ReadOnly InputBack As Color = Color.White
        Public ReadOnly TableHeader As Color = Color.FromArgb(245, 247, 251)
        Public ReadOnly TableLine As Color = Color.FromArgb(234, 238, 245)
        Public ReadOnly RowHover As Color = Color.FromArgb(244, 248, 255)
        Public ReadOnly RowSelected As Color = Color.FromArgb(232, 240, 254)

        ' Sidebar
        Public ReadOnly SidebarTop As Color = Color.FromArgb(12, 49, 112)
        Public ReadOnly SidebarBottom As Color = Color.FromArgb(9, 36, 86)
        Public ReadOnly SidebarActive As Color = Color.FromArgb(30, 99, 214)
        Public ReadOnly SidebarHover As Color = Color.FromArgb(24, 64, 132)
        Public ReadOnly SidebarText As Color = Color.FromArgb(226, 234, 248)

        ' Text
        Public ReadOnly Navy As Color = Color.FromArgb(13, 43, 94)
        Public ReadOnly Ink As Color = Color.FromArgb(31, 42, 68)
        Public ReadOnly Muted As Color = Color.FromArgb(107, 122, 144)
        Public ReadOnly Placeholder As Color = Color.FromArgb(150, 160, 178)

        ' Actions
        Public ReadOnly Primary As Color = Color.FromArgb(21, 101, 216)
        Public ReadOnly PrimaryHover As Color = Color.FromArgb(15, 85, 189)
        Public ReadOnly PrimaryPressed As Color = Color.FromArgb(11, 70, 160)
        Public ReadOnly Danger As Color = Color.FromArgb(229, 72, 77)
        Public ReadOnly Success As Color = Color.FromArgb(34, 160, 107)
        Public ReadOnly Required As Color = Color.FromArgb(229, 72, 77)

        ' Statistic cards (taken from the dashboards)
        Public ReadOnly CardBlue As Color = Color.FromArgb(47, 123, 245)
        Public ReadOnly CardAmber As Color = Color.FromArgb(247, 176, 49)
        Public ReadOnly CardGreen As Color = Color.FromArgb(34, 166, 110)
        Public ReadOnly CardPurple As Color = Color.FromArgb(139, 92, 246)
        Public ReadOnly CardRed As Color = Color.FromArgb(234, 76, 96)

        Public Const FontName As String = "Segoe UI"
        Private _iconFontName As String
        Private ReadOnly FontCache As New Dictionary(Of String, Font)()

        Public Function F(size As Single, Optional style As FontStyle = FontStyle.Regular) As Font
            Dim key = $"{size}|{CInt(style)}"
            Dim result As Font = Nothing
            If Not FontCache.TryGetValue(key, result) Then
                result = New Font(FontName, size, style, GraphicsUnit.Point)
                FontCache(key) = result
            End If
            Return result
        End Function

        Public ReadOnly Property IconFontName As String
            Get
                If _iconFontName Is Nothing Then
                    Using fonts As New InstalledFontCollection()
                        Dim names = fonts.Families.Select(Function(ff) ff.Name).ToList()
                        _iconFontName = If(names.Contains("Segoe Fluent Icons"), "Segoe Fluent Icons", "Segoe MDL2 Assets")
                    End Using
                End If
                Return _iconFontName
            End Get
        End Property

        Public Function IconFont(size As Single) As Font
            Dim key = $"icon|{size}"
            Dim result As Font = Nothing
            If Not FontCache.TryGetValue(key, result) Then
                result = New Font(IconFontName, size, FontStyle.Regular, GraphicsUnit.Point)
                FontCache(key) = result
            End If
            Return result
        End Function

        ''' <summary>Draws an icon glyph centred in <paramref name="r"/>.</summary>
        Public Sub DrawGlyph(g As Graphics, glyphText As String, r As Rectangle, color As Color, size As Single)
            TextRenderer.DrawText(g, glyphText, IconFont(size), r, color,
                TextFormatFlags.NoPrefix Or TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine)
        End Sub

        Public Function GlyphBitmap(glyphText As String, size As Single, color As Color, Optional box As Integer = 0) As Bitmap
            Dim px = If(box > 0, box, CInt(size * 1.7F))
            Dim bmp As New Bitmap(px, px)
            Using g = Graphics.FromImage(bmp)
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit
                Using b As New SolidBrush(color), sf As New StringFormat With {.Alignment = StringAlignment.Center, .LineAlignment = StringAlignment.Center}
                    g.DrawString(glyphText, IconFont(size), b, New RectangleF(0, 0, px, px), sf)
                End Using
            End Using
            Return bmp
        End Function

        Public Function RoundedRect(r As RectangleF, radius As Single) As GraphicsPath
            Dim p As New GraphicsPath()
            If radius <= 0 Then
                p.AddRectangle(r)
                Return p
            End If
            Dim d = Math.Min(radius * 2, Math.Min(r.Width, r.Height))
            p.AddArc(r.X, r.Y, d, d, 180, 90)
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90)
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90)
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90)
            p.CloseFigure()
            Return p
        End Function

        ''' <summary>
        ''' Fills a control's background with what is behind it, so rounded corners blend in. Over a photo
        ''' (BrandPanel) the parent's actual pixels are drawn; elsewhere the parent's flat colour is used.
        ''' </summary>
        Public Sub PaintParent(c As Control, g As Graphics)
            Dim p = c.Parent
            ' Walk up through transparent containers to the first opaque background.
            While p IsNot Nothing AndAlso p.BackColor.A < 255 AndAlso Not TypeOf p Is BrandPanel
                p = p.Parent
            End While
            If TypeOf p Is BrandPanel Then
                ButtonRenderer.DrawParentBackground(g, c.ClientRectangle, c)
            Else
                g.Clear(If(p IsNot Nothing, p.BackColor, AppBack))
            End If
        End Sub

        Public Sub HighQuality(g As Graphics)
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.PixelOffsetMode = PixelOffsetMode.HighQuality
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit
            g.InterpolationMode = InterpolationMode.HighQualityBicubic
        End Sub

        Public Function Blend(a As Color, b As Color, t As Double) As Color
            Return Color.FromArgb(
                Mix(a.R, b.R, t), Mix(a.G, b.G, t), Mix(a.B, b.B, t))
        End Function

        ' Color channels are Byte; VB Byte arithmetic is checked, so subtracting a larger channel
        ' (e.g. blending toward black) would overflow. Widen to Integer first.
        Private Function Mix(x As Byte, y As Byte, t As Double) As Integer
            Dim v = CInt(x) + (CInt(y) - CInt(x)) * t
            Return Math.Max(0, Math.Min(255, CInt(Math.Round(v))))
        End Function

        Public Function FromHex(hex As String, Optional fallback As Color = Nothing) As Color
            Try
                Return ColorTranslator.FromHtml(hex)
            Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is FormatException
                Return If(fallback.IsEmpty, Muted, fallback)
            End Try
        End Function

        ' ---------------------------------------------------------------- status palettes

        ''' <summary>Light "pill" colours used in tables (background, foreground).</summary>
        Public Function BadgeColors(status As ConcernStatus) As (Back As Color, Fore As Color)
            Select Case status
                Case ConcernStatus.Submitted : Return (Color.FromArgb(255, 243, 219), Color.FromArgb(196, 120, 0))
                Case ConcernStatus.UnderReview : Return (Color.FromArgb(255, 235, 217), Color.FromArgb(214, 98, 11))
                Case ConcernStatus.InProgress : Return (Color.FromArgb(222, 245, 233), Color.FromArgb(22, 145, 80))
                Case ConcernStatus.Resolved : Return (Color.FromArgb(220, 243, 228), Color.FromArgb(17, 112, 59))
                Case Else : Return (Color.FromArgb(253, 228, 229), Color.FromArgb(209, 47, 54))
            End Select
        End Function

        ''' <summary>Solid colours used for filled status pills and the timeline.</summary>
        Public Function StatusSolid(status As ConcernStatus) As Color
            Select Case status
                Case ConcernStatus.Submitted : Return Color.FromArgb(30, 111, 232)
                Case ConcernStatus.UnderReview : Return Color.FromArgb(245, 140, 26)
                Case ConcernStatus.InProgress : Return Color.FromArgb(28, 163, 96)
                Case ConcernStatus.Resolved : Return Color.FromArgb(124, 77, 230)
                Case Else : Return Color.FromArgb(70, 84, 110)
            End Select
        End Function

        ''' <summary>Chart colours from the "Concerns by Status" donut and pie.</summary>
        Public Function StatusChart(status As ConcernStatus) As Color
            Select Case status
                Case ConcernStatus.Submitted : Return Color.FromArgb(47, 123, 245)
                Case ConcernStatus.UnderReview : Return Color.FromArgb(249, 140, 37)
                Case ConcernStatus.InProgress : Return Color.FromArgb(250, 190, 50)
                Case ConcernStatus.Resolved : Return Color.FromArgb(34, 170, 110)
                Case Else : Return Color.FromArgb(234, 76, 96)
            End Select
        End Function

        ''' <summary>Icon for a concern category code.</summary>
        Public Function CategoryGlyph(code As String) As String
            Select Case If(code, "").ToUpperInvariant()
                Case "WASTE" : Return Glyph.Delete
                Case "FLOODING" : Return Glyph.Cloud
                Case "ROADS" : Return Glyph.Car
                Case "STREETLIGHTS" : Return Glyph.Bulb
                Case "WATER" : Return Glyph.Drop
                Case "FACILITIES" : Return Glyph.Building
                Case "ENVIRONMENTAL" : Return Glyph.Leaf
                Case Else : Return Glyph.More
            End Select
        End Function

        Public ReadOnly CategoryPillBack As Color = Color.FromArgb(255, 236, 214)
        Public ReadOnly CategoryPillFore As Color = Color.FromArgb(200, 98, 12)

        Public Sub DrawPill(g As Graphics, text As String, r As Rectangle, back As Color, fore As Color, Optional font As Font = Nothing, Optional center As Boolean = True)
            Dim fnt = If(font, F(8.25F, FontStyle.Bold))
            Dim sz = TextRenderer.MeasureText(g, text, fnt, Size.Empty, TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding)
            Dim w = Math.Min(r.Width, sz.Width + 18)
            Dim h = Math.Min(r.Height, sz.Height + 8)
            Dim x = If(center, r.X + (r.Width - w) \ 2, r.X)
            Dim y = r.Y + (r.Height - h) \ 2
            Dim pill As New Rectangle(x, y, w, h)
            Dim old = g.SmoothingMode
            g.SmoothingMode = SmoothingMode.AntiAlias
            Using p = RoundedRect(pill, h / 2.0F), b As New SolidBrush(back)
                g.FillPath(b, p)
            End Using
            g.SmoothingMode = old
            TextRenderer.DrawText(g, text, fnt, pill, fore, TextFormatFlags.NoPrefix Or TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPadding Or TextFormatFlags.EndEllipsis)
        End Sub

    End Module

End Namespace
