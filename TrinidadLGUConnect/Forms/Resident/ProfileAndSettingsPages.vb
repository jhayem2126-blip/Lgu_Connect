Imports TrinidadLGUConnect.Controls
Imports TrinidadLGUConnect.Helpers
Imports TrinidadLGUConnect.Models
Imports TrinidadLGUConnect.Services

Namespace Forms

    ''' <summary>Resident settings: notification preference, password change and app information.</summary>
    Public Class ResidentSettingsPage
        Inherits PageBase

        Private ReadOnly _notify As New CheckBox With {.Text = "Email me when the status of one of my reports changes", .AutoSize = True, .Font = Theme.F(9.5F), .ForeColor = Theme.Ink, .BackColor = Color.White}
        Private ReadOnly _saveNotify As New ModernButton("Save Preference", ButtonKind.Primary) With {.Size = New Size(160, 38)}
        Private ReadOnly _notifyMsg As Label = MessageLabel()
        Private ReadOnly _emailState As Label = Subtle("", 8.75F)
        Private _user As UserAccount

        Public Sub New()
            AutoScrollMinSize = New Size(760, 560)
            Dim grid As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .RowCount = 2, .BackColor = Color.Transparent}
            grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
            grid.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
            grid.RowStyles.Add(New RowStyle(SizeType.Absolute, 230))
            grid.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
            Controls.Add(grid)
            AddTopDown(Me, BuildHeader("Settings", "Manage your notifications and account security.", Glyph.Settings), Gap(12))

            Dim notifyCard = TitledCard("Notifications")
            notifyCard.Margin = New Padding(0, 0, 8, 12)
            _notify.Location = New Point(20, 56)
            notifyCard.Controls.Add(_notify)
            _emailState.Location = New Point(38, 84)
            _emailState.MaximumSize = New Size(380, 0)
            notifyCard.Controls.Add(_emailState)
            _saveNotify.Location = New Point(20, 128)
            notifyCard.Controls.Add(_saveNotify)
            _notifyMsg.Location = New Point(20, 176)
            _notifyMsg.Width = 400
            notifyCard.Controls.Add(_notifyMsg)
            grid.Controls.Add(notifyCard, 0, 0)

            Dim secCard = TitledCard("Account Security")
            secCard.Margin = New Padding(8, 0, 0, 12)
            Dim secText = Subtle("Change your password regularly and never share it. Changing it signs out your other sessions.", 9)
            secText.MaximumSize = New Size(380, 0)
            secText.Location = New Point(20, 56)
            secCard.Controls.Add(secText)
            Dim change As New ModernButton("Change Password", ButtonKind.Outline, Glyph.Lock) With {.Size = New Size(190, 40), .Location = New Point(20, 128)}
            secCard.Controls.Add(change)
            grid.Controls.Add(secCard, 1, 0)

            Dim aboutCard = TitledCard("About")
            aboutCard.Margin = New Padding(0, 0, 8, 0)
            Dim about = Subtle($"Trinidad LGU Connect version {GetType(ResidentSettingsPage).Assembly.GetName().Version.ToString(3)}" & vbLf &
                               "Community Concern Reporting and Monitoring System" & vbLf & "Municipality of Trinidad, Bohol" & vbLf & vbLf &
                               "Your reports, pictures and contact details are used only by LGU personnel to address community concerns.", 9)
            about.MaximumSize = New Size(400, 0)
            about.Location = New Point(20, 56)
            aboutCard.Controls.Add(about)
            grid.Controls.Add(aboutCard, 0, 1)

            AddHandler change.Click, Sub()
                                         Using dlg As New ChangePasswordDialog()
                                             dlg.ShowDialog(FindForm())
                                         End Using
                                     End Sub
            AddHandler _saveNotify.Click, AddressOf OnSaveNotify
        End Sub

        Public Overrides ReadOnly Property Title As String
            Get
                Return "Settings"
            End Get
        End Property

        Public Overrides ReadOnly Property NavKey As String
            Get
                Return "settings"
            End Get
        End Property

        Public Overrides ReadOnly Property RefreshOnDataChange As Boolean
            Get
                Return False
            End Get
        End Property

        Public Overrides Async Function RefreshDataAsync() As Task
            Dim r = Await RunAsync(Function() Svc.Users.GetCurrentUserAsync(Session))
            If Not r.Ok OrElse IsDisposed Then Return
            _user = r.Value
            _notify.Checked = _user.NotifyEmail
            _emailState.Text = $"Notifications are sent to {_user.Email}." & If(Svc.Email.IsConfigured, "", " (Email delivery is currently not configured by the LGU, so no emails are sent.)")
        End Function

        Private Async Sub OnSaveNotify(sender As Object, e As EventArgs)
            If _user Is Nothing Then Return
            Dim p As New ProfileUpdate With {.FullName = _user.FullName, .ContactNumber = _user.ContactNumber, .Address = _user.Address, .NotifyEmail = _notify.Checked}
            _notifyMsg.Text = ""
            If Await RunAsync(Function() Svc.Users.UpdateProfileAsync(Session, p), _saveNotify) Then
                _notifyMsg.ForeColor = Theme.Success
                _notifyMsg.Text = "Preference saved."
                _user.NotifyEmail = _notify.Checked
            End If
        End Sub

    End Class

End Namespace
