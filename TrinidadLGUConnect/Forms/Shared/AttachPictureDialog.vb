Imports TrinidadLGUConnect.Controls
Imports TrinidadLGUConnect.Helpers
Imports TrinidadLGUConnect.Models
Imports TrinidadLGUConnect.Services

Namespace Forms

    ''' <summary>
    ''' "Select and Attach Picture" (reference screen 5): real Windows file picker or drag-and-drop, live
    ''' previews, remove-before-attach, and validation of type and size. Works on a copy of the list; only
    ''' "Attach" returns the changes.
    ''' </summary>
    Public Class AttachPictureDialog
        Inherits DialogBase

        Private ReadOnly _items As List(Of PendingAttachment)
        Private ReadOnly _maxBytes As Long
        Private ReadOnly _maxCount As Integer
        Private ReadOnly _drop As New DropZone()
        Private ReadOnly _thumbs As New FlowLayoutPanel With {.AutoScroll = True, .WrapContents = True, .BackColor = Color.White, .Padding = New Padding(0)}
        Private ReadOnly _msg As Label = MessageLabel()
        Private ReadOnly _count As Label = Subtle("")
        Private ReadOnly _attach As New ModernButton("Attach")
        Private ReadOnly _cancel As New ModernButton("Cancel", ButtonKind.Outline)

        Public Sub New(existing As IEnumerable(Of PendingAttachment), maxBytes As Long, maxCount As Integer)
            MyBase.New("Select and Attach Picture", 620, 600)
            _items = existing.ToList()
            _maxBytes = maxBytes
            _maxCount = maxCount
            _drop.Hint = $"JPG, PNG (Max size {TextFormat.FileSize(maxBytes)}, up to {maxCount} pictures)"

            Dim s As New VStack(Body, 24, 20)
            Dim title = Heading("Select and Attach Picture", 12.5F)
            s.AddAuto(title, 14)
            s.Add(_drop, 210, 14)
            s.Add(_thumbs, 150, 4)
            s.AddAuto(_count, 2)
            _msg.Height = 38
            s.Add(_msg, 38, 6)
            _cancel.Size = New Size(130, 40)
            _attach.Size = New Size(150, 40)
            _attach.Location = New Point(Body.Width - 24 - _attach.Width, s.Y)
            _cancel.Location = New Point(_attach.Left - 14 - _cancel.Width, s.Y)
            Body.Controls.Add(_cancel)
            Body.Controls.Add(_attach)

            AcceptButton = _attach
            CancelButton = _cancel
            AddHandler _drop.BrowseRequested, AddressOf Browse
            AddHandler _drop.FilesDropped, Async Sub(sender, paths) Await AddFilesAsync(paths)
            AddHandler _attach.Click, Sub()
                                          DialogResult = DialogResult.OK
                                          Close()
                                      End Sub
            AddHandler _cancel.Click, Sub() Close()
            RenderThumbs()
        End Sub

        ''' <summary>The attachments after the user pressed Attach.</summary>
        Public ReadOnly Property Result As List(Of PendingAttachment)
            Get
                Return _items
            End Get
        End Property

        Private Async Sub Browse(sender As Object, e As EventArgs)
            Using dlg As New OpenFileDialog With {
                .Title = "Choose pictures", .Filter = AttachmentService.FileDialogFilter, .Multiselect = True,
                .CheckFileExists = True, .RestoreDirectory = True}
                If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
                Await AddFilesAsync(dlg.FileNames)
            End Using
        End Sub

        ''' <summary>Used when files were dropped on the Report a Concern page before the dialog opened.</summary>
        Public Function AddFilesFromDropAsync(paths As String()) As Task
            Return AddFilesAsync(paths)
        End Function

        Private Async Function AddFilesAsync(paths As String()) As Task
            _msg.ForeColor = Theme.Danger
            _msg.Text = ""
            Dim room = _maxCount - _items.Count
            If room <= 0 Then
                _msg.Text = $"You can attach up to {_maxCount} pictures. Remove one to add another."
                Return
            End If
            Dim toProcess = paths.Take(room).ToArray()
            Dim skipped = paths.Length - toProcess.Length
            Dim maxB = _maxBytes
            Dim r = Await TryRunAsync(Me, Function() Task.Run(
                Function()
                    Dim ok As New List(Of PendingAttachment)()
                    Dim errors As New List(Of String)()
                    For Each p In toProcess
                        Try
                            ok.Add(AttachmentService.PrepareFromFile(p, maxB))
                        Catch ex As ValidationException
                            errors.Add(ex.Message)
                        End Try
                    Next
                    Return (ok, errors)
                End Function), {_attach, _cancel, _drop})
            If Not r.Ok Then Return
            ' Ignore exact duplicates of pictures already attached.
            For Each a In r.Value.ok
                If _items.Any(Function(x) x.Sha256 = a.Sha256) Then
                    r.Value.errors.Add($"'{a.FileName}' is already attached.")
                Else
                    _items.Add(a)
                End If
            Next
            If skipped > 0 Then r.Value.errors.Add($"{skipped} picture(s) were not added because the limit is {_maxCount}.")
            If r.Value.errors.Count > 0 Then _msg.Text = String.Join(" ", r.Value.errors)
            RenderThumbs()
        End Function

        Private Sub RenderThumbs()
            _thumbs.SuspendLayout()
            For Each c As Control In _thumbs.Controls.Cast(Of Control)().ToList()
                c.Dispose()
            Next
            _thumbs.Controls.Clear()
            For i = 0 To _items.Count - 1
                Dim idx = i
                Dim t As New ThumbnailTile With {.Size = New Size(176, 132), .ShowRemove = True, .Margin = New Padding(0, 0, 14, 10), .Index = i,
                                                 .AccessibleName = _items(i).FileName}
                t.SetImageBytes(_items(i).Thumbnail)
                AddHandler t.Opened, Sub() ImageViewerForm.Open(Me, _items.Select(Function(x) ViewerImage.FromPending(x)), idx)
                AddHandler t.RemoveClicked, Sub()
                                                _items.RemoveAt(idx)
                                                RenderThumbs()
                                            End Sub
                _thumbs.Controls.Add(t)
            Next
            _thumbs.ResumeLayout()
            _count.Text = If(_items.Count = 0, "No pictures selected.", $"{_items.Count} of {_maxCount} pictures selected · {TextFormat.FileSize(_items.Sum(Function(x) x.SizeBytes))} after optimisation")
        End Sub

    End Class

    ''' <summary>Change password for the signed-in user (requires the current password).</summary>
    Public Class ChangePasswordDialog
        Inherits DialogBase

        Private ReadOnly _current As New InputField("Current password", Glyph.Lock, password:=True)
        Private ReadOnly _new As New InputField("New password", Glyph.Lock, password:=True)
        Private ReadOnly _confirm As New InputField("Confirm new password", Glyph.Lock, password:=True)
        Private ReadOnly _msg As Label = MessageLabel()

        Public Sub New()
            MyBase.New("Change Password", 480, 470)
            Dim fields As New Dictionary(Of String, FieldBase) From {{"CurrentPassword", _current}, {"NewPassword", _new}, {"ConfirmPassword", _confirm}}
            Dim s As New VStack(Body, 32, 26)
            s.Add(DialogHeader("Change Password", "Other signed-in sessions will be signed out.", Glyph.Lock), 56, 16)
            s.AddAuto(FieldLabel("Current Password"), 4)
            s.Add(_current, 38, 10)
            s.AddAuto(FieldLabel("New Password"), 4)
            s.Add(_new, 38, 3)
            s.AddAuto(Subtle("At least 8 characters with upper- and lower-case letters, a number and a symbol.", 7.75F), 8)
            s.AddAuto(FieldLabel("Confirm New Password"), 4)
            s.Add(_confirm, 38, 6)
            _msg.Height = 36
            s.Add(_msg, 36, 4)
            Dim save As New ModernButton("Update Password", ButtonKind.Primary, Glyph.CheckMark)
            Dim cancel As New ModernButton("Cancel", ButtonKind.Outline)
            s.AddRow(42, 12, 0, save, cancel)
            AcceptButton = save
            CancelButton = cancel
            AddHandler cancel.Click, Sub() Close()
            AddHandler save.Click, Async Sub()
                                       _msg.Text = ""
                                       Dim c = _current.Text, n = _new.Text, f = _confirm.Text
                                       Dim r = Await TryRunAsync(Of Boolean)(Me, Async Function()
                                                                                      Await AppServices.Instance.Auth.ChangePasswordAsync(AppServices.Instance.Session, c, n, f)
                                                                                      Return True
                                                                                  End Function, {save, cancel}, showErrors:=False)
                                       If Not r.Ok Then
                                           If TypeOf r.Error Is SessionExpiredException Then
                                               ShowError(Me, r.Error)
                                               Close()
                                               Return
                                           End If
                                           _msg.Text = FriendlyMessage(r.Error)
                                           MarkField(fields, r.Error)
                                           Return
                                       End If
                                       Info(Me, "Your password has been changed.", "Password updated")
                                       DialogResult = DialogResult.OK
                                       Close()
                                   End Sub
        End Sub
    End Class

End Namespace
