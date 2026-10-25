Imports System.ComponentModel
Imports System.Drawing.Drawing2D

Namespace Controls

    ''' <summary>White rounded card with a hairline border. Children are laid out by the caller inside Padding.</summary>
    Public Class CardPanel
        Inherits Panel

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property Radius As Integer = 10
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property BorderColor As Color = Theme.CardBorder
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property FillColor As Color = Theme.CardBack

        Public Sub New()
            SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw Or ControlStyles.UserPaint, True)
            BackColor = Theme.CardBack
            Padding = New Padding(16)
        End Sub

        Private _title As String = ""

        ''' <summary>Optional heading drawn at the top-left of the card (content should start below y = 50).</summary>
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property Title As String
            Get
                Return _title
            End Get
            Set(value As String)
                _title = If(value, "")
                AccessibleName = _title
                Invalidate()
            End Set
        End Property

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property TitleSize As Single = 11

        Protected Overrides Sub OnPaintBackground(e As PaintEventArgs)
            Theme.PaintParent(Me, e.Graphics)
            Theme.HighQuality(e.Graphics)
            Dim r As New RectangleF(0.5F, 0.5F, Width - 1.5F, Height - 1.5F)
            Using p = Theme.RoundedRect(r, Radius), b As New SolidBrush(FillColor), pen As New Pen(BorderColor)
                e.Graphics.FillPath(b, p)
                If BorderColor <> Color.Transparent Then e.Graphics.DrawPath(pen, p)
            End Using
            If _title.Length > 0 Then
                TextRenderer.DrawText(e.Graphics, _title, Theme.F(TitleSize, FontStyle.Bold), New Rectangle(18, 14, Width - 36, 26), Theme.Navy,
                                      TextFormatFlags.NoPrefix Or TextFormatFlags.Left Or TextFormatFlags.Top Or TextFormatFlags.EndEllipsis)
            End If
        End Sub
    End Class

    Public Enum ButtonKind
        Primary
        Outline
        Secondary
        Danger
        Ghost
        Success
    End Enum

    ''' <summary>Rounded, owner-drawn button with optional icon glyph. Keeps standard Button behaviour (focus, Enter/Space, AcceptButton).</summary>
    Public Class ModernButton
        Inherits Button

        Private _hover As Boolean
        Private _pressed As Boolean
        Private _kind As ButtonKind = ButtonKind.Primary
        Private _glyphText As String = ""

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property Kind As ButtonKind
            Get
                Return _kind
            End Get
            Set(value As ButtonKind)
                _kind = value
                Invalidate()
            End Set
        End Property

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property GlyphText As String
            Get
                Return _glyphText
            End Get
            Set(value As String)
                _glyphText = If(value, "")
                Invalidate()
            End Set
        End Property

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property Radius As Integer = 6

        ''' <summary>Optional override fill colour (used for coloured action tiles).</summary>
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property AccentColor As Color = Color.Empty

        Public Sub New()
            SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.UserPaint Or ControlStyles.ResizeRedraw, True)
            FlatStyle = FlatStyle.Flat
            FlatAppearance.BorderSize = 0
            Font = Theme.F(9.5F, FontStyle.Bold)
            Cursor = Cursors.Hand
            Height = 38
            UseVisualStyleBackColor = False
            UseMnemonic = False
        End Sub

        Public Sub New(text As String, Optional kind As ButtonKind = ButtonKind.Primary, Optional glyph As String = "")
            Me.New()
            Me.Text = text
            _kind = kind
            _glyphText = glyph
        End Sub

        Protected Overrides Sub OnMouseEnter(e As EventArgs)
            _hover = True : Invalidate() : MyBase.OnMouseEnter(e)
        End Sub
        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            _hover = False : _pressed = False : Invalidate() : MyBase.OnMouseLeave(e)
        End Sub
        Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
            _pressed = True : Invalidate() : MyBase.OnMouseDown(e)
        End Sub
        Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
            _pressed = False : Invalidate() : MyBase.OnMouseUp(e)
        End Sub
        Protected Overrides Sub OnEnabledChanged(e As EventArgs)
            Invalidate() : MyBase.OnEnabledChanged(e)
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            Theme.PaintParent(Me, g)
            Theme.HighQuality(g)
            Dim fill, fore, border As Color
            Select Case _kind
                Case ButtonKind.Primary, ButtonKind.Danger, ButtonKind.Success
                    Dim baseC = If(_kind = ButtonKind.Danger, Theme.Danger, If(_kind = ButtonKind.Success, Theme.Success, Theme.Primary))
                    If Not AccentColor.IsEmpty Then baseC = AccentColor
                    fill = If(_pressed, Theme.Blend(baseC, Color.Black, 0.2), If(_hover, Theme.Blend(baseC, Color.Black, 0.1), baseC))
                    fore = Color.White
                    border = fill
                Case ButtonKind.Outline
                    fill = If(_pressed, Color.FromArgb(226, 236, 252), If(_hover, Color.FromArgb(240, 246, 255), Color.White))
                    fore = Theme.Primary
                    border = Color.FromArgb(190, 206, 232)
                Case ButtonKind.Secondary
                    fill = If(_pressed, Color.FromArgb(226, 231, 240), If(_hover, Color.FromArgb(236, 240, 246), Color.FromArgb(245, 247, 251)))
                    fore = Theme.Ink
                    border = Theme.InputBorder
                Case Else
                    fill = If(_hover, Color.FromArgb(236, 242, 252), If(Parent IsNot Nothing, Parent.BackColor, Color.White))
                    fore = Theme.Primary
                    border = fill
            End Select
            If Not Enabled Then
                fill = Theme.Blend(fill, Color.White, 0.45)
                fore = Theme.Blend(fore, Color.White, 0.4)
                border = Theme.Blend(border, Color.White, 0.45)
            End If
            Dim r As New RectangleF(0.5F, 0.5F, Width - 1.5F, Height - 1.5F)
            Using p = Theme.RoundedRect(r, Radius), b As New SolidBrush(fill), pen As New Pen(border)
                g.FillPath(b, p)
                g.DrawPath(pen, p)
            End Using
            If Focused AndAlso ShowFocusCues Then
                Using pen As New Pen(Theme.Blend(fore, fill, 0.5)) With {.DashStyle = DashStyle.Dot}
                    g.DrawPath(pen, Theme.RoundedRect(New RectangleF(3, 3, Width - 7, Height - 7), Math.Max(1, Radius - 2)))
                End Using
            End If

            Dim textSize = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPrefix Or TextFormatFlags.NoPadding)
            Dim glyphW = If(_glyphText.Length > 0, CInt(Font.Size * 1.9F), 0)
            Dim gap = If(_glyphText.Length > 0 AndAlso Text.Length > 0, 6, 0)
            Dim total = glyphW + gap + textSize.Width
            Dim x = (Width - total) \ 2
            If _glyphText.Length > 0 Then
                Theme.DrawGlyph(g, _glyphText, New Rectangle(x, 0, glyphW, Height), fore, Font.Size * 1.05F)
            End If
            TextRenderer.DrawText(g, Text, Font, New Rectangle(x + glyphW + gap, 0, textSize.Width + 2, Height), fore,
                                  TextFormatFlags.NoPrefix Or TextFormatFlags.VerticalCenter Or TextFormatFlags.Left Or TextFormatFlags.NoPadding Or TextFormatFlags.SingleLine)
        End Sub
    End Class

    ''' <summary>Clickable text link (e.g. "Register here", "Forgot password?").</summary>
    Public Class LinkText
        Inherits Label

        Private ReadOnly _normal As Font
        Private ReadOnly _hover As Font

        Public Sub New(text As String, Optional size As Single = 9.0F, Optional bold As Boolean = True)
            Me.Text = text
            AutoSize = True
            Dim style = If(bold, FontStyle.Bold, FontStyle.Regular)
            _normal = Theme.F(size, style)
            _hover = Theme.F(size, style Or FontStyle.Underline)
            Font = _normal
            ForeColor = Theme.Primary
            BackColor = Color.Transparent
            UseMnemonic = False
            Cursor = Cursors.Hand
            TabStop = True
        End Sub

        Protected Overrides Sub OnMouseEnter(e As EventArgs)
            Font = _hover
            MyBase.OnMouseEnter(e)
        End Sub

        Protected Overrides Sub OnMouseLeave(e As EventArgs)
            Font = _normal
            MyBase.OnMouseLeave(e)
        End Sub

        Protected Overrides Function ProcessMnemonic(charCode As Char) As Boolean
            Return False
        End Function

        Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
            If e.KeyCode = Keys.Enter OrElse e.KeyCode = Keys.Space Then
                OnClick(EventArgs.Empty)
                e.Handled = True
            End If
            MyBase.OnKeyDown(e)
        End Sub
    End Class

    ''' <summary>Base for the rounded input wrappers: paints border, focus ring and error state.</summary>
    Public MustInherit Class FieldBase
        Inherits UserControl

        Private _hasError As Boolean
        Protected FocusedInside As Boolean

        Public Sub New()
            SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw Or ControlStyles.UserPaint, True)
            BackColor = Theme.InputBack
            Height = 38
            Font = Theme.F(9.5F)
        End Sub

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property HasError As Boolean
            Get
                Return _hasError
            End Get
            Set(value As Boolean)
                _hasError = value
                Invalidate()
            End Set
        End Property

        Protected Sub TrackFocus(c As Control)
            AddHandler c.GotFocus, Sub()
                                       FocusedInside = True
                                       HasError = False
                                       Invalidate()
                                   End Sub
            AddHandler c.LostFocus, Sub()
                                        FocusedInside = False
                                        Invalidate()
                                    End Sub
        End Sub

        Protected Overrides Sub OnPaintBackground(e As PaintEventArgs)
            Theme.PaintParent(Me, e.Graphics)
            Theme.HighQuality(e.Graphics)
            Dim border = If(_hasError, Theme.Danger, If(FocusedInside, Theme.Primary, Theme.InputBorder))
            Dim r As New RectangleF(0.5F, 0.5F, Width - 1.5F, Height - 1.5F)
            Using p = Theme.RoundedRect(r, 6), b As New SolidBrush(If(Enabled, BackColor, Color.FromArgb(246, 248, 251))), pen As New Pen(border, If(FocusedInside OrElse _hasError, 1.4F, 1.0F))
                e.Graphics.FillPath(b, p)
                e.Graphics.DrawPath(pen, p)
            End Using
        End Sub
    End Class

    ''' <summary>Rounded text input with placeholder, optional leading icon and show/hide password toggle.</summary>
    Public Class InputField
        Inherits FieldBase

        Public ReadOnly Box As New TextBox()
        Private ReadOnly _eye As Label
        Private _leftGlyph As String = ""
        Private ReadOnly _isPassword As Boolean
        Private _revealed As Boolean

        Public Sub New(Optional placeholder As String = "", Optional leftGlyph As String = "", Optional password As Boolean = False, Optional multiline As Boolean = False)
            _leftGlyph = leftGlyph
            _isPassword = password
            Box.BorderStyle = BorderStyle.None
            Box.Font = Theme.F(9.5F)
            Box.ForeColor = Theme.Ink
            Box.BackColor = Theme.InputBack
            Box.PlaceholderText = placeholder
            Box.Multiline = multiline
            If multiline Then
                Box.ScrollBars = ScrollBars.None
                Box.AcceptsReturn = True
                Box.WordWrap = True
            Else
                ' Lets the single-line box be sized explicitly so descenders are never clipped.
                Box.AutoSize = False
            End If
            If password Then Box.UseSystemPasswordChar = True
            Controls.Add(Box)
            TrackFocus(Box)
            If password Then
                _eye = New Label With {.Text = Glyph.Eye, .Font = Theme.IconFont(11), .ForeColor = Theme.Muted, .Cursor = Cursors.Hand,
                    .TextAlign = ContentAlignment.MiddleCenter, .Size = New Size(30, 30), .BackColor = Theme.InputBack, .AccessibleName = "Show password"}
                AddHandler _eye.Click, Sub() Reveal(Not _revealed)
                Controls.Add(_eye)
            End If
            If multiline Then Height = 90
            AddHandler Click, Sub() Box.Focus()
        End Sub

        Public Sub Reveal(show As Boolean)
            _revealed = show
            Box.UseSystemPasswordChar = Not show
            _eye.Text = If(show, Glyph.EyeHide, Glyph.Eye)
            _eye.AccessibleName = If(show, "Hide password", "Show password")
        End Sub

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Overrides Property Text As String
            Get
                Return Box.Text
            End Get
            Set(value As String)
                Box.Text = value
            End Set
        End Property

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property MaxLength As Integer
            Get
                Return Box.MaxLength
            End Get
            Set(value As Integer)
                Box.MaxLength = value
            End Set
        End Property

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property [ReadOnly] As Boolean
            Get
                Return Box.ReadOnly
            End Get
            Set(value As Boolean)
                Box.ReadOnly = value
                Box.BackColor = If(value, Color.FromArgb(246, 248, 251), Theme.InputBack)
                BackColor = Box.BackColor
            End Set
        End Property

        Public Shadows Event TextChanged As EventHandler

        Protected Overrides Sub OnCreateControl()
            MyBase.OnCreateControl()
            AddHandler Box.TextChanged, Sub(s, e) RaiseEvent TextChanged(Me, e)
        End Sub

        Protected Overrides Sub OnLayout(e As LayoutEventArgs)
            MyBase.OnLayout(e)
            ' The base constructor triggers layout before this class's fields are initialised.
            If Box Is Nothing OrElse _leftGlyph Is Nothing Then Return
            Dim left = If(_leftGlyph.Length > 0, 36, 12)
            Dim right = If(_isPassword, 36, 10)
            If Box.Multiline Then
                Box.SetBounds(left, 9, Math.Max(10, Width - left - right), Math.Max(10, Height - 16))
            Else
                Dim th = TextRenderer.MeasureText("Ág", Box.Font, Size.Empty, TextFormatFlags.NoPadding).Height + 2
                Box.SetBounds(left, (Height - th) \ 2, Math.Max(10, Width - left - right), th)
            End If
            If _eye IsNot Nothing Then _eye.SetBounds(Width - 34, (Height - 30) \ 2, 30, 30)
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            MyBase.OnPaint(e)
            If Not String.IsNullOrEmpty(_leftGlyph) AndAlso Box IsNot Nothing Then
                Dim top = If(Box.Multiline, 0, 0)
                Theme.DrawGlyph(e.Graphics, _leftGlyph, New Rectangle(8, top, 26, If(Box.Multiline, 38, Height)), Theme.Muted, 10.5F)
            End If
        End Sub

        Protected Overrides Sub OnGotFocus(e As EventArgs)
            MyBase.OnGotFocus(e)
            Box.Focus()
        End Sub
    End Class

    ''' <summary>ComboBox that paints over the classic 3-D frame and arrow button with a flat chevron.</summary>
    Public Class FlatCombo
        Inherits ComboBox

        Private Const WM_PAINT As Integer = &HF

        ''' <summary>Colour for the shown item (set by SelectField); Nothing = default ink.</summary>
        Public Property DisplayColor As Func(Of Object, Color)

        Protected Overrides Sub WndProc(ByRef m As Message)
            MyBase.WndProc(m)
            If m.Msg = WM_PAINT AndAlso IsHandleCreated Then
                Using g = Graphics.FromHwnd(Handle)
                    Dim r = ClientRectangle
                    Dim back = If(Not Enabled, Color.FromArgb(246, 248, 251), If(Parent IsNot Nothing, Parent.BackColor, BackColor))
                    If DropDownStyle = ComboBoxStyle.DropDownList Then
                        ' Windows' themed painting and the owner-draw pass can both draw the text of an
                        ' unfocused list, which makes it look smeared/bold. Repaint the whole face once.
                        Using b As New SolidBrush(back)
                            g.FillRectangle(b, r)
                        End Using
                        Dim item = SelectedItem
                        Dim text = If(item Is Nothing, Me.Text, GetItemText(item))
                        Dim isPlaceholder = item Is Nothing OrElse (TypeOf item Is ListOption AndAlso DirectCast(item, ListOption).Value Is Nothing)
                        Dim fore = If(Not Enabled OrElse isPlaceholder, Theme.Muted, If(DisplayColor IsNot Nothing, DisplayColor(item), Theme.Ink))
                        TextRenderer.DrawText(g, text, Font, New Rectangle(5, 0, Math.Max(10, r.Width - 32), r.Height), fore,
                                              TextFormatFlags.NoPrefix Or TextFormatFlags.VerticalCenter Or TextFormatFlags.Left Or TextFormatFlags.EndEllipsis Or TextFormatFlags.SingleLine)
                    Else
                        Using pen As New Pen(back)
                            g.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1)
                            g.DrawRectangle(pen, 1, 1, r.Width - 3, r.Height - 3)
                        End Using
                    End If
                    Dim btn As New Rectangle(r.Width - 24, 2, 22, r.Height - 4)
                    Using b As New SolidBrush(back)
                        g.FillRectangle(b, btn)
                    End Using
                    Theme.DrawGlyph(g, Glyph.ChevronDown, btn, If(Enabled, Theme.Muted, Theme.Placeholder), 7.5F)
                End Using
            End If
        End Sub

        Protected Overrides Sub OnSelectedIndexChanged(e As EventArgs)
            MyBase.OnSelectedIndexChanged(e)
            Invalidate()
        End Sub

        Protected Overrides Sub OnEnabledChanged(e As EventArgs)
            MyBase.OnEnabledChanged(e)
            Invalidate()
        End Sub

        Protected Overrides Sub OnLostFocus(e As EventArgs)
            MyBase.OnLostFocus(e)
            Invalidate()
        End Sub

        Protected Overrides Sub OnGotFocus(e As EventArgs)
            MyBase.OnGotFocus(e)
            Invalidate()
        End Sub
    End Class

    ''' <summary>Rounded drop-down list. Items are shown with their ToString text.</summary>
    Public Class SelectField
        Inherits FieldBase

        Public ReadOnly Combo As New FlatCombo()

        ''' <summary>Optional per-item text colour (e.g. coloured statuses).</summary>
        Public Property ItemColor As Func(Of Object, Color)

        ''' <summary>Optional per-item display text.</summary>
        Public Property ItemText As Func(Of Object, String)

        Private ReadOnly _editable As Boolean

        ''' <param name="editable">When True the user can also type: the text auto-completes from the list
        ''' and is matched to an item (case-insensitive) when the value is read.</param>
        Public Sub New(Optional editable As Boolean = False)
            _editable = editable
            Combo.DropDownStyle = If(editable, ComboBoxStyle.DropDown, ComboBoxStyle.DropDownList)
            If editable Then
                Combo.AutoCompleteMode = AutoCompleteMode.SuggestAppend
                Combo.AutoCompleteSource = AutoCompleteSource.ListItems
                AddHandler Combo.Leave, Sub() ResolveTyped()
            End If
            Combo.FlatStyle = FlatStyle.Flat
            Combo.Font = Theme.F(9.5F)
            Combo.BackColor = Theme.InputBack
            Combo.ForeColor = Theme.Ink
            Combo.DrawMode = DrawMode.OwnerDrawFixed
            Combo.ItemHeight = 24
            Combo.IntegralHeight = False
            Combo.MaxDropDownItems = 12
            AddHandler Combo.DrawItem, AddressOf DrawComboItem
            Combo.DisplayColor = Function(o) If(ItemColor IsNot Nothing, ItemColor(o), Theme.Ink)
            Controls.Add(Combo)
            TrackFocus(Combo)
        End Sub

        Private Sub DrawComboItem(sender As Object, e As DrawItemEventArgs)
            If e.Index < 0 Then Return
            Dim item = Combo.Items(e.Index)
            Dim selected = (e.State And DrawItemState.Selected) = DrawItemState.Selected AndAlso (e.State And DrawItemState.ComboBoxEdit) = 0
            Using b As New SolidBrush(If(selected, Theme.RowSelected, Theme.InputBack))
                e.Graphics.FillRectangle(b, e.Bounds)
            End Using
            Dim isPlaceholder = TypeOf item Is ListOption AndAlso DirectCast(item, ListOption).Value Is Nothing
            Dim fore = If(isPlaceholder, Theme.Muted, If(ItemColor IsNot Nothing, ItemColor(item), Theme.Ink))
            Dim text = If(ItemText IsNot Nothing, ItemText(item), Combo.GetItemText(item))
            ' Regular weight everywhere; colour alone distinguishes statuses.
            TextRenderer.DrawText(e.Graphics, text, Combo.Font, New Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 4, e.Bounds.Height), fore,
                                  TextFormatFlags.NoPrefix Or TextFormatFlags.VerticalCenter Or TextFormatFlags.Left Or TextFormatFlags.EndEllipsis)
        End Sub

        Protected Overrides Sub OnLayout(e As LayoutEventArgs)
            MyBase.OnLayout(e)
            If Combo Is Nothing Then Return
            Combo.SetBounds(6, (Height - Combo.Height) \ 2, Math.Max(20, Width - 12), Combo.Height)
        End Sub

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property SelectedItem As Object
            Get
                If _editable Then ResolveTyped()
                Return Combo.SelectedItem
            End Get
            Set(value As Object)
                Combo.SelectedItem = value
            End Set
        End Property

        ''' <summary>True when the user typed text that does not match any item.</summary>
        Public ReadOnly Property HasUnmatchedText As Boolean
            Get
                Return _editable AndAlso Combo.SelectedItem Is Nothing AndAlso Combo.Text.Trim().Length > 0
            End Get
        End Property

        ''' <summary>Selects the item whose text matches what was typed (ignoring case and a "Brgy." prefix).</summary>
        Public Sub ResolveTyped()
            If Not _editable Then Return
            Dim typed = Combo.Text.Trim()
            If Combo.SelectedItem IsNot Nothing AndAlso String.Equals(Combo.GetItemText(Combo.SelectedItem), typed, StringComparison.OrdinalIgnoreCase) Then Return
            Dim key = typed
            For Each prefix In {"brgy.", "brgy", "barangay"}
                If key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) Then key = key.Substring(prefix.Length).Trim()
            Next
            For i = 0 To Combo.Items.Count - 1
                If String.Equals(Combo.GetItemText(Combo.Items(i)), key, StringComparison.OrdinalIgnoreCase) Then
                    If Combo.SelectedIndex <> i Then Combo.SelectedIndex = i
                    Return
                End If
            Next
            If Combo.SelectedIndex <> -1 Then
                Dim keepText = Combo.Text
                Combo.SelectedIndex = -1
                Combo.Text = keepText
            End If
        End Sub

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property SelectedIndex As Integer
            Get
                Return Combo.SelectedIndex
            End Get
            Set(value As Integer)
                Combo.SelectedIndex = value
            End Set
        End Property

        Public ReadOnly Property Items As ComboBox.ObjectCollection
            Get
                Return Combo.Items
            End Get
        End Property

        Public Event SelectionChanged As EventHandler

        Protected Overrides Sub OnCreateControl()
            MyBase.OnCreateControl()
            AddHandler Combo.SelectedIndexChanged, Sub(s, e) RaiseEvent SelectionChanged(Me, e)
        End Sub

        Protected Overrides Sub OnGotFocus(e As EventArgs)
            MyBase.OnGotFocus(e)
            Combo.Focus()
        End Sub
    End Class

    ''' <summary>A value with a display label for drop-downs.</summary>
    Public Class ListOption
        Public Property Text As String
        Public Property Value As Object

        Public Sub New(text As String, value As Object)
            Me.Text = text
            Me.Value = value
        End Sub

        Public Overrides Function ToString() As String
            Return Text
        End Function
    End Class

    ''' <summary>Rounded date box (MM/dd/yyyy) with a calendar pop-up. Can be optional (empty) when AllowEmpty is set.</summary>
    Public Class DateField
        Inherits FieldBase

        Private _value As DateTime?
        Private ReadOnly _clear As Label
        Private _popup As ToolStripDropDown

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property AllowEmpty As Boolean
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property MinDate As DateTime = New DateTime(2000, 1, 1)
        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property MaxDate As DateTime = DateTime.Today.AddYears(1)

        Public Event ValueChanged As EventHandler

        Public Sub New(Optional allowEmpty As Boolean = False)
            Me.AllowEmpty = allowEmpty
            If Not allowEmpty Then _value = DateTime.Today
            TabStop = True
            Cursor = Cursors.Hand
            _clear = New Label With {.Text = Glyph.Cancel, .Font = Theme.IconFont(7.5F), .ForeColor = Theme.Muted, .Size = New Size(22, 22),
                                     .TextAlign = ContentAlignment.MiddleCenter, .Cursor = Cursors.Hand, .Visible = False, .BackColor = Theme.InputBack,
                                     .AccessibleName = "Clear date"}
            AddHandler _clear.Click, Sub() Value = Nothing
            Controls.Add(_clear)
            AddHandler GotFocus, Sub()
                                     FocusedInside = True
                                     Invalidate()
                                 End Sub
            AddHandler LostFocus, Sub()
                                      FocusedInside = False
                                      Invalidate()
                                  End Sub
        End Sub

        <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
        Public Property Value As DateTime?
            Get
                Return _value
            End Get
            Set(v As DateTime?)
                If v.HasValue Then v = v.Value.Date
                If Not v.HasValue AndAlso Not AllowEmpty Then Return
                If Nullable.Equals(v, _value) Then Return
                _value = v
                _clear.Visible = AllowEmpty AndAlso _value.HasValue
                Invalidate()
                RaiseEvent ValueChanged(Me, EventArgs.Empty)
            End Set
        End Property

        Protected Overrides Sub OnLayout(e As LayoutEventArgs)
            MyBase.OnLayout(e)
            If _clear Is Nothing Then Return
            _clear.SetBounds(Width - 52, (Height - 22) \ 2, 22, 22)
        End Sub

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            MyBase.OnPaint(e)
            Dim text = If(_value.HasValue, _value.Value.ToString("MM/dd/yyyy", Globalization.CultureInfo.InvariantCulture), "mm/dd/yyyy")
            TextRenderer.DrawText(e.Graphics, text, Font, New Rectangle(12, 0, Width - If(_clear IsNot Nothing AndAlso _clear.Visible, 62, 40), Height), If(_value.HasValue, Theme.Ink, Theme.Placeholder),
                                  TextFormatFlags.NoPrefix Or TextFormatFlags.VerticalCenter Or TextFormatFlags.Left)
            Theme.DrawGlyph(e.Graphics, Glyph.Calendar, New Rectangle(Width - 30, 0, 24, Height), Theme.Muted, 10)
        End Sub

        Protected Overrides Sub OnClick(e As EventArgs)
            MyBase.OnClick(e)
            Focus()
            ShowCalendar()
        End Sub

        Protected Overrides Function IsInputKey(keyData As Keys) As Boolean
            If keyData = Keys.Down OrElse keyData = Keys.Space OrElse keyData = Keys.Enter Then Return True
            Return MyBase.IsInputKey(keyData)
        End Function

        Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
            MyBase.OnKeyDown(e)
            If e.KeyCode = Keys.Down OrElse e.KeyCode = Keys.Space OrElse e.KeyCode = Keys.Enter OrElse (e.Alt AndAlso e.KeyCode = Keys.Down) Then
                ShowCalendar()
                e.Handled = True
            ElseIf (e.KeyCode = Keys.Delete OrElse e.KeyCode = Keys.Back) AndAlso AllowEmpty Then
                Value = Nothing
            End If
        End Sub

        Private Sub ShowCalendar()
            If _popup IsNot Nothing AndAlso _popup.Visible Then Return
            Dim cal As New MonthCalendar With {.MaxSelectionCount = 1, .MinDate = MinDate, .MaxDate = MaxDate, .ShowToday = True}
            Dim initial = If(_value, DateTime.Today)
            If initial < MinDate Then initial = MinDate
            If initial > MaxDate Then initial = MaxDate
            cal.SetDate(initial)
            Dim host As New ToolStripControlHost(cal) With {.Margin = Padding.Empty, .Padding = Padding.Empty, .AutoSize = False, .Size = cal.Size}
            _popup = New ToolStripDropDown With {.Padding = Padding.Empty}
            _popup.Items.Add(host)
            AddHandler cal.DateSelected, Sub(s, ev)
                                             Value = ev.Start
                                             _popup.Close()
                                         End Sub
            AddHandler cal.KeyDown, Sub(s, ev)
                                        If ev.KeyCode = Keys.Enter Then
                                            Value = cal.SelectionStart
                                            _popup.Close()
                                        ElseIf ev.KeyCode = Keys.Escape Then
                                            _popup.Close()
                                        End If
                                    End Sub
            AddHandler _popup.Closed, Sub() Focus()
            _popup.Show(Me, New Point(0, Height))
            cal.Focus()
        End Sub
    End Class

    ''' <summary>Label helpers used by every screen.</summary>
    Public Module UiFactory

        Public Function Lbl(text As String, Optional size As Single = 9.5F, Optional style As FontStyle = FontStyle.Regular, Optional color As Color = Nothing) As Label
            Return New Label With {.Text = text, .AutoSize = True, .Font = Theme.F(size, style), .ForeColor = If(color.IsEmpty, Theme.Ink, color), .BackColor = Color.Transparent, .UseMnemonic = False}
        End Function

        ''' <summary>A field caption, optionally with a red required asterisk.</summary>
        Public Function FieldLabel(text As String, Optional required As Boolean = False) As Control
            If Not required Then Return Lbl(text, 9, FontStyle.Bold, Theme.Ink)
            Dim p As New FlowLayoutPanel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .WrapContents = False, .Margin = Padding.Empty, .Padding = Padding.Empty, .BackColor = Color.Transparent}
            Dim a = Lbl(text, 9, FontStyle.Bold, Theme.Ink)
            a.Margin = Padding.Empty
            Dim b = Lbl("*", 9, FontStyle.Bold, Theme.Required)
            b.Margin = New Padding(0)
            p.Controls.Add(a)
            p.Controls.Add(b)
            Return p
        End Function

        Public Function Heading(text As String, Optional size As Single = 15) As Label
            Return Lbl(text, size, FontStyle.Bold, Theme.Navy)
        End Function

        Public Function Subtle(text As String, Optional size As Single = 9) As Label
            Return Lbl(text, size, FontStyle.Regular, Theme.Muted)
        End Function

        ''' <summary>Small inline message area for validation and status text.</summary>
        Public Function MessageLabel() As Label
            Return New Label With {.AutoSize = False, .Height = 20, .Font = Theme.F(8.75F), .ForeColor = Theme.Danger, .Text = "", .BackColor = Color.Transparent}
        End Function

    End Module

End Namespace
