Imports System.Drawing.Drawing2D
Imports TrinidadLGUConnect.Controls
Imports TrinidadLGUConnect.Helpers
Imports TrinidadLGUConnect.Models
Imports TrinidadLGUConnect.Services

Namespace Forms

    ''' <summary>
    ''' "My Profile" for every role (resident, staff, administrator): profile photo, name, contact number,
    ''' barangay and address. Email and username are changed only by an administrator in User Management.
    ''' </summary>
    Public Class ProfilePage
        Inherits PageBase

        Private ReadOnly _avatar As New AvatarCircle With {.Size = New Size(96, 96)}
        Private ReadOnly _changePhoto As New ModernButton("Change Photo", ButtonKind.Outline, Glyph.Camera) With {.Size = New Size(150, 36)}
        Private ReadOnly _removePhoto As New ModernButton("Remove Photo", ButtonKind.Secondary, Glyph.Delete) With {.Size = New Size(150, 36)}
        Private ReadOnly _name As New InputField("Full name")
        Private ReadOnly _email As New InputField("Email")
        Private ReadOnly _username As New InputField("Username")
        Private ReadOnly _contact As New InputField("e.g. 0912 345 6789")
        Private ReadOnly _barangay As New SelectField(editable:=True)
        Private ReadOnly _address As New InputField("House number, street or purok")
        Private ReadOnly _save As New ModernButton("Save Changes", ButtonKind.Primary, Glyph.CheckMark) With {.Size = New Size(170, 40)}
        Private ReadOnly _msg As Label = MessageLabel()
        Private ReadOnly _photoMsg As Label = MessageLabel()
        Private ReadOnly _summaryName As Label = Heading("", 13)
        Private ReadOnly _summaryMeta As Label = Subtle("", 9)
        Private ReadOnly _fields As Dictionary(Of String, FieldBase)
        Private _user As UserAccount
        Private _maxPhotoBytes As Long = 5L * 1024 * 1024

        Public Sub New()
            AutoScrollMinSize = New Size(760, 620)
            _fields = New Dictionary(Of String, FieldBase) From {{"FullName", _name}, {"ContactNumber", _contact}, {"Barangay", _barangay}}
            _email.ReadOnly = True
            _username.ReadOnly = True
            _name.MaxLength = 120
            _contact.MaxLength = 20
            _address.MaxLength = 200
            Dim card As New CardPanel With {.Dock = DockStyle.Fill}
            Controls.Add(card)
            AddTopDown(Me, BuildHeader("My Profile", "Your personal information and profile picture.", Glyph.Contact), Gap(12))

            _avatar.Location = New Point(28, 24)
            card.Controls.Add(_avatar)
            _summaryName.Location = New Point(140, 26)
            _summaryMeta.Location = New Point(142, 56)
            card.Controls.Add(_summaryName)
            card.Controls.Add(_summaryMeta)
            _changePhoto.Location = New Point(140, 82)
            _removePhoto.Location = New Point(300, 82)
            card.Controls.Add(_changePhoto)
            card.Controls.Add(_removePhoto)
            _photoMsg.Location = New Point(462, 90)
            _photoMsg.Width = 420
            card.Controls.Add(_photoMsg)

            Dim s As New VStack(card, 28, 144, 28)
            s.AddRow(18, 20, 4, CType(FieldLabel("Full Name", True), Control), FieldLabel("Contact Number"))
            s.AddRow(38, 20, 14, _name, _contact)
            s.AddRow(18, 20, 4, CType(FieldLabel("Email Address"), Control), FieldLabel("Username"))
            s.AddRow(38, 20, 4, _email, _username)
            s.AddAuto(Subtle("Your email address and username can only be changed by an LGU administrator.", 8.25F), 14)
            s.AddRow(18, 20, 4, CType(FieldLabel("Barangay"), Control), FieldLabel("Address (house no., street or purok)"))
            s.AddRow(38, 20, 8, _barangay, _address)
            _msg.Height = 30
            s.Add(_msg, 30, 4)
            _save.Location = New Point(28, s.Y)
            card.Controls.Add(_save)

            AddHandler _save.Click, AddressOf OnSave
            AddHandler _changePhoto.Click, AddressOf OnChangePhoto
            AddHandler _removePhoto.Click, AddressOf OnRemovePhoto
        End Sub

        Public Overrides ReadOnly Property Title As String
            Get
                Return "My Profile"
            End Get
        End Property

        Public Overrides ReadOnly Property NavKey As String
            Get
                Return "profile"
            End Get
        End Property

        Public Overrides ReadOnly Property RefreshOnDataChange As Boolean
            Get
                Return False
            End Get
        End Property

        Public Overrides Async Function RefreshDataAsync() As Task
            Dim r = Await RunAsync(Async Function()
                                       Dim u = Await Svc.Users.GetCurrentUserAsync(Session)
                                       Dim photo = Await Svc.Users.GetMyPhotoAsync(Session)
                                       Dim barangays = Await Svc.Concerns.GetBarangaysAsync()
                                       Dim limits = Await Svc.Concerns.GetAttachmentLimitsAsync()
                                       Return (u, photo, barangays, limits.MaxBytes)
                                   End Function)
            If Not r.Ok OrElse IsDisposed Then Return
            _user = r.Value.u
            _maxPhotoBytes = r.Value.MaxBytes
            _name.Text = _user.FullName
            _email.Text = _user.Email
            _username.Text = _user.Username
            _contact.Text = If(_user.ContactNumber, "")
            _address.Text = If(_user.Address, "")
            Fill(_barangay, BarangayOptions(r.Value.barangays, "Not set"), _user.BarangayId)
            _summaryName.Text = _user.FullName
            _summaryMeta.Text = $"{_user.RoleName}  ·  Member since {TextFormat.ShortDate(_user.CreatedAt)}" &
                                If(String.IsNullOrEmpty(_user.BarangayName), "", $"  ·  {_user.BarangayName}")
            _avatar.Image = AttachmentService.TryDecode(r.Value.photo)
            _removePhoto.Enabled = r.Value.photo IsNot Nothing
        End Function

        Private Async Sub OnSave(sender As Object, e As EventArgs)
            If _user Is Nothing Then Return
            _msg.Text = ""
            If _barangay.HasUnmatchedText Then
                _msg.ForeColor = Theme.Danger
                _msg.Text = $"'{_barangay.Combo.Text.Trim()}' is not a barangay of Trinidad. Pick one from the list."
                _barangay.HasError = True
                Return
            End If
            Dim p As New ProfileUpdate With {.FullName = _name.Text, .ContactNumber = _contact.Text, .Address = _address.Text,
                                             .BarangayId = SelectedInt(_barangay), .NotifyEmail = _user.NotifyEmail}
            Dim r = Await TryRunAsync(Of Boolean)(Me, Async Function()
                                                           Await Svc.Users.UpdateProfileAsync(Session, p)
                                                           Return True
                                                       End Function, {_save}, showErrors:=False)
            If Not r.Ok Then
                If TypeOf r.Error Is SessionExpiredException Then
                    ShowError(FindForm(), r.Error)
                    Return
                End If
                _msg.ForeColor = Theme.Danger
                _msg.Text = FriendlyMessage(r.Error)
                MarkField(_fields, r.Error)
                Return
            End If
            _msg.ForeColor = Theme.Success
            _msg.Text = "Your profile has been saved."
            Await RefreshDataAsync()
            Await Shell.RefreshIdentityAsync()
        End Sub

        Private Async Sub OnChangePhoto(sender As Object, e As EventArgs)
            _photoMsg.Text = ""
            Dim path As String
            Using dlg As New OpenFileDialog With {.Title = "Choose a profile picture", .Filter = AttachmentService.FileDialogFilter, .CheckFileExists = True, .RestoreDirectory = True}
                If dlg.ShowDialog(FindForm()) <> DialogResult.OK Then Return
                path = dlg.FileName
            End Using
            Dim maxBytes = _maxPhotoBytes
            Dim r = Await TryRunAsync(Of Boolean)(Me, Async Function()
                                                           Dim info As New FileInfo(path)
                                                           If info.Length > maxBytes Then
                                                               Throw New ValidationException($"The picture is {TextFormat.FileSize(info.Length)}. The maximum size is {TextFormat.FileSize(maxBytes)}.")
                                                           End If
                                                           Dim bytes = Await File.ReadAllBytesAsync(path)
                                                           Await Svc.Users.SetMyPhotoAsync(Session, bytes, info.Name, maxBytes)
                                                           Return True
                                                       End Function, {_changePhoto, _removePhoto}, showErrors:=False)
            If Not r.Ok Then
                _photoMsg.ForeColor = Theme.Danger
                _photoMsg.Text = If(TypeOf r.Error Is IOException OrElse TypeOf r.Error Is UnauthorizedAccessException,
                                    "The picture could not be read. Close any program using it and try again.", FriendlyMessage(r.Error))
                Return
            End If
            _photoMsg.ForeColor = Theme.Success
            _photoMsg.Text = "Profile picture updated."
            Await RefreshDataAsync()
            Await Shell.RefreshIdentityAsync()
        End Sub

        Private Async Sub OnRemovePhoto(sender As Object, e As EventArgs)
            If Not Confirm(FindForm(), "Remove your profile picture?", "Remove Photo") Then Return
            _photoMsg.Text = ""
            If Await RunAsync(Function() Svc.Users.RemoveMyPhotoAsync(Session), _changePhoto, _removePhoto) Then
                _photoMsg.ForeColor = Theme.Success
                _photoMsg.Text = "Profile picture removed."
                Await RefreshDataAsync()
                Await Shell.RefreshIdentityAsync()
            End If
        End Sub

    End Class

    ''' <summary>Round profile picture, or a person icon when there is none. Owns its image.</summary>
    Public Class AvatarCircle
        Inherits Control

        Private _image As Image

        Public Sub New()
            SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.UserPaint Or ControlStyles.ResizeRedraw, True)
        End Sub

        <ComponentModel.DesignerSerializationVisibility(ComponentModel.DesignerSerializationVisibility.Hidden)>
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

        Protected Overrides Sub OnPaint(e As PaintEventArgs)
            Dim g = e.Graphics
            Theme.PaintParent(Me, g)
            Theme.HighQuality(g)
            Dim r As New Rectangle(0, 0, Width - 1, Height - 1)
            If _image IsNot Nothing Then
                Using p As New GraphicsPath()
                    p.AddEllipse(r)
                    Dim old = g.Clip
                    g.SetClip(p)
                    g.DrawImage(_image, r)
                    g.Clip = old
                End Using
                Using pen As New Pen(Theme.CardBorder, 2)
                    g.DrawEllipse(pen, r)
                End Using
            Else
                Using b As New SolidBrush(Color.FromArgb(226, 236, 252))
                    g.FillEllipse(b, r)
                End Using
                Theme.DrawGlyph(g, Glyph.Contact, ClientRectangle, Theme.Primary, Height / 3.2F)
            End If
        End Sub

        Protected Overrides Sub Dispose(disposing As Boolean)
            If disposing Then Image = Nothing
            MyBase.Dispose(disposing)
        End Sub
    End Class

End Namespace
