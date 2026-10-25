Imports TrinidadLGUConnect.Data
Imports TrinidadLGUConnect.Helpers
Imports TrinidadLGUConnect.Services

Namespace Forms

    Public Module UiHelpers

        Public Const AppTitle As String = "Trinidad LGU Connect"

        ''' <summary>
        ''' Replaces message boxes during automated UI tests (a modal box would block the test). Nothing sets
        ''' this in the application, so users always see real message boxes.
        ''' </summary>
        Friend Property MessageHook As Func(Of String, MessageBoxButtons, MessageBoxIcon, DialogResult)

        Private Function ShowMessage(owner As IWin32Window, text As String, title As String, buttons As MessageBoxButtons, icon As MessageBoxIcon,
                                     Optional defaultButton As MessageBoxDefaultButton = MessageBoxDefaultButton.Button1) As DialogResult
            If MessageHook IsNot Nothing Then Return MessageHook(text, buttons, icon)
            Return MessageBox.Show(owner, text, title, buttons, icon, defaultButton)
        End Function

        ''' <summary>Maps exceptions to messages that are safe for ordinary users; unexpected errors are logged with details.</summary>
        Public Function FriendlyMessage(ex As Exception) As String
            Dim e = ex
            While TypeOf e Is AggregateException AndAlso e.InnerException IsNot Nothing
                e = e.InnerException
            End While
            If TypeOf e Is ValidationException OrElse TypeOf e Is AuthorizationException OrElse TypeOf e Is RateLimitException OrElse
               TypeOf e Is ConcurrencyException OrElse TypeOf e Is DataAccessException OrElse TypeOf e Is EmailDeliveryException OrElse
               TypeOf e Is SessionExpiredException Then
                Return e.Message
            End If
            AppLog.Error("Unexpected error", e)
            Return "Something went wrong while processing your request. Please try again. If the problem continues, contact the system administrator (details were written to the application log)."
        End Function

        Public Function Unwrap(ex As Exception) As Exception
            Dim e = ex
            While TypeOf e Is AggregateException AndAlso e.InnerException IsNot Nothing
                e = e.InnerException
            End While
            Return e
        End Function

        Public Sub ShowError(owner As IWin32Window, ex As Exception)
            Dim e = Unwrap(ex)
            If TypeOf e Is SessionExpiredException AndAlso AppController.Current IsNot Nothing AndAlso AppServices.IsInitialized AndAlso AppServices.Instance.Session IsNot Nothing Then
                AppController.Current.ForceLogout(e.Message)
                Return
            End If
            Dim icon = If(TypeOf e Is ValidationException OrElse TypeOf e Is ConcurrencyException OrElse TypeOf e Is RateLimitException, MessageBoxIcon.Warning, MessageBoxIcon.Error)
            ShowMessage(owner, FriendlyMessage(e), AppTitle, MessageBoxButtons.OK, icon)
        End Sub

        Public Sub Info(owner As IWin32Window, message As String, Optional title As String = AppTitle)
            ShowMessage(owner, message, title, MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Sub

        Public Sub Warn(owner As IWin32Window, message As String)
            ShowMessage(owner, message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Sub

        Public Function Confirm(owner As IWin32Window, message As String, Optional title As String = AppTitle) As Boolean
            Return ShowMessage(owner, message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) = DialogResult.Yes
        End Function

        ''' <summary>
        ''' Runs service work on a background thread (MySQL Connector/NET's async methods complete synchronously,
        ''' and password hashing is CPU-bound), shows a wait cursor, disables the given controls while running,
        ''' and reports failures with a friendly message. Returns Ok = False when the work failed.
        ''' </summary>
        Public Async Function TryRunAsync(Of T)(owner As Control, work As Func(Of Task(Of T)), Optional disable As IEnumerable(Of Control) = Nothing,
                                               Optional showErrors As Boolean = True) As Task(Of (Ok As Boolean, Value As T, [Error] As Exception))
            Dim form = owner?.FindForm()
            Dim toDisable = If(disable, Enumerable.Empty(Of Control)()).Where(Function(c) c IsNot Nothing AndAlso c.Enabled).ToList()
            For Each c In toDisable : c.Enabled = False : Next
            Dim page = TryCast(owner, PageBase)
            If page IsNot Nothing Then page.SetBusy(True)
            If form IsNot Nothing Then form.UseWaitCursor = True
            Try
                Dim value = Await Task.Run(work)
                Return (True, value, Nothing)
            Catch ex As Exception
                If showErrors AndAlso owner IsNot Nothing AndAlso Not owner.IsDisposed Then ShowError(form, ex)
                Return (False, Nothing, Unwrap(ex))
            Finally
                If form IsNot Nothing AndAlso Not form.IsDisposed Then form.UseWaitCursor = False
                If page IsNot Nothing AndAlso Not page.IsDisposed Then page.SetBusy(False)
                For Each c In toDisable
                    If Not c.IsDisposed Then c.Enabled = True
                Next
            End Try
        End Function

        ''' <summary>Overload for work without a result.</summary>
        Public Async Function TryRunAsync(owner As Control, work As Func(Of Task), Optional disable As IEnumerable(Of Control) = Nothing,
                                         Optional showErrors As Boolean = True) As Task(Of Boolean)
            Dim r = Await TryRunAsync(Of Boolean)(owner,
                Async Function()
                    Await work()
                    Return True
                End Function, disable, showErrors)
            Return r.Ok
        End Function

        ''' <summary>Marks and focuses the input named by a ValidationException (if it is in <paramref name="fields"/>).</summary>
        Public Sub MarkField(fields As IDictionary(Of String, Controls.FieldBase), ex As Exception)
            Dim v = TryCast(Unwrap(ex), ValidationException)
            If v Is Nothing OrElse v.Field Is Nothing OrElse fields Is Nothing Then Return
            Dim f As Controls.FieldBase = Nothing
            If fields.TryGetValue(v.Field, f) AndAlso f IsNot Nothing AndAlso Not f.IsDisposed Then
                f.Focus()
                f.HasError = True
            End If
        End Sub

        Public ReadOnly Property Session As Models.UserSession
            Get
                Return AppServices.Instance.Session
            End Get
        End Property

        Public ReadOnly Property Svc As AppServices
            Get
                Return AppServices.Instance
            End Get
        End Property

        ''' <summary>Limits a window's size (and minimum size) to the screen's working area and centres it.</summary>
        Public Sub FitToScreen(f As Form, preferred As Size, minimum As Size)
            Dim wa = Screen.PrimaryScreen.WorkingArea
            Dim w = Math.Min(preferred.Width, wa.Width - 16)
            Dim h = Math.Min(preferred.Height, wa.Height - 16)
            f.MinimumSize = New Size(Math.Min(minimum.Width, w), Math.Min(minimum.Height, h))
            f.Size = New Size(w, h)
            If preferred.Height > wa.Height - 16 AndAlso TypeOf f Is MainShellForm Then f.WindowState = FormWindowState.Maximized
        End Sub

        ''' <summary>Applies the common window chrome (icon, font).</summary>
        Public Sub ApplyWindowDefaults(f As Form)
            f.Font = Controls.Theme.F(9.5F)
            f.AutoScaleMode = AutoScaleMode.Dpi
            f.BackColor = Controls.Theme.AppBack
            Dim iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "app.ico")
            If File.Exists(iconPath) Then
                Try
                    f.Icon = New Icon(iconPath)
                Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is IOException
                End Try
            End If
        End Sub

    End Module

End Namespace
