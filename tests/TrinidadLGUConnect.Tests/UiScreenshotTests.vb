Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.Threading
Imports TrinidadLGUConnect.Data
Imports TLC = TrinidadLGUConnect.Controls
Imports TrinidadLGUConnect.Forms
Imports TrinidadLGUConnect.Models
Imports TrinidadLGUConnect.Services

''' <summary>
''' Opens every screen with realistic data, waits for its database loads, and saves a screenshot.
''' Doubles as a smoke test: any message box (error) raised while loading fails the test.
''' Screenshots go to TLC_SCREENSHOT_DIR (or bin\...\screenshots).
''' </summary>
<Collection("db")>
Public Class UiScreenshotTests

    Private ReadOnly _fx As DatabaseFixture
    Private ReadOnly _messages As New List(Of String)()
    Private ReadOnly _dir As String

    Public Sub New(fx As DatabaseFixture)
        _fx = fx
        _dir = If(Environment.GetEnvironmentVariable("TLC_SCREENSHOT_DIR"), Path.Combine(AppContext.BaseDirectory, "screenshots"))
        Directory.CreateDirectory(_dir)
    End Sub

    Private Shared ReadOnly Property Svc As AppServices
        Get
            Return AppServices.Instance
        End Get
    End Property

    ' ------------------------------------------------------------------ sample data

    Private Shared Function Photo(kind As String, w As Integer, h As Integer) As PendingAttachment
        Using bmp As New Bitmap(w, h)
            Using g = Graphics.FromImage(bmp)
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim sky = If(kind = "night", Color.FromArgb(18, 30, 70), Color.FromArgb(150, 196, 236))
                Using b As New LinearGradientBrush(New Rectangle(0, 0, w, h \ 2), sky, Color.White, LinearGradientMode.Vertical)
                    g.FillRectangle(b, 0, 0, w, h \ 2)
                End Using
                Using b As New SolidBrush(If(kind = "flood", Color.FromArgb(150, 120, 80), Color.FromArgb(120, 124, 128)))
                    g.FillRectangle(b, 0, h \ 2, w, h \ 2)
                End Using
                Using b As New SolidBrush(Color.FromArgb(70, 140, 60))
                    g.FillRectangle(b, 0, h \ 2 - 20, w \ 4, 40)
                    g.FillRectangle(b, w * 3 \ 4, h \ 2 - 20, w \ 4, 40)
                End Using
                Select Case kind
                    Case "pothole"
                        Using b As New SolidBrush(Color.FromArgb(60, 55, 50))
                            g.FillEllipse(b, w \ 3, h * 2 \ 3, w \ 3, h \ 6)
                        End Using
                    Case "flood"
                        Using b As New SolidBrush(Color.FromArgb(150, 110, 90, 60))
                            g.FillRectangle(b, 0, h * 3 \ 5, w, h * 2 \ 5)
                        End Using
                    Case "night"
                        Using p As New Pen(Color.FromArgb(40, 40, 40), 8)
                            g.DrawLine(p, w * 2 \ 3, h - 10, w * 2 \ 3, h \ 5)
                        End Using
                        Using b As New SolidBrush(Color.FromArgb(255, 240, 180))
                            g.FillEllipse(b, w * 2 \ 3 - 18, h \ 5 - 10, 36, 20)
                        End Using
                    Case Else
                        Using b As New SolidBrush(Color.FromArgb(40, 120, 60))
                            g.FillRectangle(b, w \ 3, h \ 2, w \ 3, h \ 4)
                        End Using
                End Select
            End Using
            Using ms As New MemoryStream()
                bmp.Save(ms, ImageFormat.Jpeg)
                Return AttachmentService.Prepare(ms.ToArray(), $"{kind}_photo.jpg", 5 * 1024 * 1024)
            End Using
        End Using
    End Function

    Private Async Function SeedAsync() As Task(Of (Admin As UserSession, Juan As UserSession, FirstConcern As Integer, AdminConcern As Integer))
        Dim admin = (Await Svc.Auth.LoginAsync("ana_admin", DatabaseFixture.AdminPassword, LoginPortal.Personnel)).Session
        Dim suffix = Guid.NewGuid().ToString("N").Substring(0, 4)
        Dim people = {("Juan Dela Cruz", "juan" & suffix, UserRole.Resident), ("Maria Santos", "maria" & suffix, UserRole.Resident),
                      ("Pedro Reyes", "pedro" & suffix, UserRole.Resident), ("Carlo Razon", "carlo" & suffix, UserRole.Staff),
                      ("Luis Garcia", "luis" & suffix, UserRole.Staff)}
        For Each person In people
            Await Svc.Users.CreateAsync(admin, New UserEditRequest With {.FullName = person.Item1, .Username = person.Item2, .Email = person.Item2 & "@example.com",
                                                                        .Role = person.Item3, .IsActive = True, .EmailVerified = True, .NewPassword = "Sample#Pass2025"})
        Next
        Dim login = Async Function(u As String) (Await Svc.Auth.LoginAsync(u, "Sample#Pass2025", LoginPortal.Resident)).Session
        Dim juan = Await login("juan" & suffix)
        Dim maria = Await login("maria" & suffix)
        Dim pedro = Await login("pedro" & suffix)
        Await Svc.Users.SetMyPhotoAsync(juan, Photo("waste", 480, 480).Data, "juan.jpg", 5 * 1024 * 1024)
        Await Svc.Users.UpdateProfileAsync(juan, New ProfileUpdate With {.FullName = "Juan Dela Cruz", .ContactNumber = "0912 345 6789", .Address = "Purok 3, Mabini Street", .BarangayId = 15, .NotifyEmail = True})
        Dim cats = Await Svc.Concerns.GetCategoriesAsync()
        Dim cat = Function(code As String) cats.First(Function(c) c.Code = code)

        Dim barangays = Await Svc.Concerns.GetBarangaysAsync()
        Dim landmarks = {"Purok 2, near the chapel", "Purok 4, along the national road", "Near the public market", "Purok 1, beside the school", "Near the barangay hall"}
        Dim submit = Async Function(s As UserSession, code As String, loc As String, desc As String, daysAgo As Integer, pics As String()) As Task(Of Integer)
                         ' "Brgy. San Isidro, Trinidad" -> barangay San Isidro + a street-level landmark.
                         Dim brgy = If(barangays.FirstOrDefault(Function(b) loc.Contains(b.Name)), barangays.First(Function(b) b.Name = "Poblacion"))
                         Dim req As New NewConcernRequest With {.CategoryId = cat(code).Id, .BarangayId = brgy.Id, .Location = landmarks(Math.Abs(desc.Length) Mod landmarks.Length),
                                                                .Description = desc, .ReportedDate = DateTime.Today.AddDays(-daysAgo)}
                         For Each k In pics : req.Attachments.Add(Photo(k, 800, 600)) : Next
                         Return (Await Svc.Concerns.SubmitAsync(s, req)).ConcernId
                     End Function
        Dim move = Async Function(id As Integer, path As (ConcernStatus, String)()) As Task
                       Dim current = ConcernStatus.Submitted
                       For Each stp In path
                           Await Svc.Concerns.UpdateStatusAsync(admin, New StatusUpdateRequest With {.ConcernId = id, .ExpectedCurrentStatus = current, .NewStatus = stp.Item1, .Remarks = stp.Item2})
                           current = stp.Item1
                       Next
                   End Function

        Dim first = Await submit(juan, "ROADS", "Poblacion, Trinidad", "The road has a large pothole that is dangerous for vehicles and pedestrians, especially during rainy days.", 3, {"pothole", "pothole"})
        Dim c2 = Await submit(juan, "WASTE", "Brgy. San Isidro, Trinidad", "Garbage has not been collected for more than a week near the chapel.", 5, {"waste"})
        Await move(c2, {(ConcernStatus.UnderReview, "For assessment"), (ConcernStatus.InProgress, "Collection truck scheduled")})
        Dim c3 = Await submit(juan, "STREETLIGHTS", "Brgy. San Vicente, Trinidad", "Two streetlights near the elementary school are not working.", 8, {"night"})
        Await move(c3, {(ConcernStatus.UnderReview, "Inspection requested"), (ConcernStatus.InProgress, "Replacement bulbs ordered"), (ConcernStatus.Resolved, "Streetlights replaced")})
        Dim c4 = Await submit(juan, "WATER", "Brgy. Soom, Trinidad", "No water supply since yesterday morning.", 13, {})
        Await move(c4, {(ConcernStatus.UnderReview, "Checking with the water district"), (ConcernStatus.Closed, "Supply restored by the water district")})

        Dim adminConcern = Await submit(pedro, "ROADS", "Bahi-an, Trinidad", "There are several potholes on the road near the Bahi-an area. It is causing problems for vehicles and pedestrians, especially during rainy days.", 6, {"pothole"})
        Await move(adminConcern, {(ConcernStatus.UnderReview, "For assessment"), (ConcernStatus.InProgress, "Road repair scheduled")})
        Dim extras = {
            (maria, "FLOODING", "Brgy. San Isidro, Trinidad", "Flooded street after heavy rain; drainage is clogged.", 4, "flood", 1),
            (maria, "WASTE", "Poblacion, Trinidad", "Overflowing bins at the public market.", 2, "waste", 0),
            (pedro, "FACILITIES", "Poblacion, Trinidad", "Broken benches at the town plaza.", 9, "", 4),
            (maria, "ENVIRONMENTAL", "Brgy. Guinobatan, Trinidad", "Illegal cutting of trees along the river.", 11, "", 2),
            (pedro, "STREETLIGHTS", "Brgy. Banlasan, Trinidad", "Flickering streetlight at the corner.", 1, "night", 0),
            (maria, "OTHER", "Brgy. Kauswagan, Trinidad", "Stray dogs roaming near the school.", 7, "", 3),
            (pedro, "WATER", "Brgy. Tagum Norte, Trinidad", "Leaking water pipe along the highway.", 10, "", 5),
            (maria, "ROADS", "Brgy. La Union, Trinidad", "Road obstruction due to fallen tree.", 12, "", 4)}
        For Each x In extras
            Dim id = Await submit(x.Item1, x.Item2, x.Item3, x.Item4, x.Item5, If(x.Item6 = "", Array.Empty(Of String)(), {x.Item6}))
            Select Case x.Item7
                Case 1 : Await move(id, {(ConcernStatus.UnderReview, "For assessment")})
                Case 2 : Await move(id, {(ConcernStatus.UnderReview, "For assessment"), (ConcernStatus.InProgress, "Team dispatched")})
                Case 3 : Await move(id, {(ConcernStatus.UnderReview, "For assessment"), (ConcernStatus.InProgress, "Team dispatched"), (ConcernStatus.Resolved, "Completed")})
                Case 4 : Await move(id, {(ConcernStatus.UnderReview, "For assessment"), (ConcernStatus.InProgress, "Team dispatched"), (ConcernStatus.Resolved, "Completed"), (ConcernStatus.Closed, "Verified")})
                Case 5 : Await move(id, {(ConcernStatus.Closed, "Duplicate of an earlier report")})
            End Select
        Next
        Return (admin, juan, first, adminConcern)
    End Function

    ' ------------------------------------------------------------------ UI plumbing

    Private Shared Sub RunSta(action As Action)
        Dim failure As Exception = Nothing
        Dim t As New Thread(Sub()
                                Try
                                    action()
                                Catch ex As Exception
                                    failure = ex
                                End Try
                            End Sub)
        t.SetApartmentState(ApartmentState.STA)
        t.Start()
        t.Join()
        If failure IsNot Nothing Then Throw New Exception("UI test failed: " & failure.ToString(), failure)
    End Sub

    Private Shared Sub Pump(ms As Integer)
        Dim sw = Stopwatch.StartNew()
        While sw.ElapsedMilliseconds < ms
            Application.DoEvents()
            Thread.Sleep(10)
        End While
    End Sub

    <Runtime.InteropServices.StructLayout(Runtime.InteropServices.LayoutKind.Sequential)>
    Private Structure RECT
        Public Left, Top, Right, Bottom As Integer
    End Structure

    <Runtime.InteropServices.DllImport("dwmapi.dll")>
    Private Shared Function DwmGetWindowAttribute(hwnd As IntPtr, attribute As Integer, ByRef value As RECT, size As Integer) As Integer
    End Function

    Private Sub Capture(f As Form, name As String, Optional waitMs As Integer = 1800)
        f.StartPosition = FormStartPosition.Manual
        f.Location = New Point(20, 20)
        If Not f.Visible Then f.Show()
        f.TopMost = True
        f.Activate()
        Pump(waitMs)
        ' The window's physical bounds (the test process is DPI-unaware, the screen may not be at 100%).
        Dim r As RECT
        Dim bounds = If(DwmGetWindowAttribute(f.Handle, 9, r, Runtime.InteropServices.Marshal.SizeOf(Of RECT)()) = 0,
                        Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom), f.Bounds)
        Using bmp As New Bitmap(bounds.Width, bounds.Height)
            Using g = Graphics.FromImage(bmp)
                g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size)
            End Using
            bmp.Save(Path.Combine(_dir, name & ".png"), ImageFormat.Png)
        End Using
        f.TopMost = False
        Assert.True(_messages.Count = 0, $"Screen '{name}' raised a message: {String.Join(" | ", _messages)}")
    End Sub

    Private Shared Function Descendants(root As Control) As IEnumerable(Of Control)
        Return root.Controls.Cast(Of Control)().SelectMany(Function(c) {c}.Concat(Descendants(c)))
    End Function

    ''' <summary>Labels and input kinds of the Report a Concern form, in layout order, to compare the two roles' forms.</summary>
    Private Shared Function FormSignature(page As Control) As List(Of String)
        Return Descendants(page).Where(Function(c) TypeOf c Is Label OrElse TypeOf c Is TLC.FieldBase OrElse TypeOf c Is TLC.DropZone OrElse TypeOf c Is TLC.ModernButton).
            Select(Function(c) $"{c.GetType().Name}:{If(TypeOf c Is TLC.FieldBase, "", c.Text)}").ToList()
    End Function

    <DbFact>
    <Trait("Category", "UI")>
    Public Sub CaptureAllScreens()
        Dim data = SeedAsync().GetAwaiter().GetResult()
        RunSta(Sub()
                   Application.SetHighDpiMode(HighDpiMode.DpiUnawareGdiScaled)
                   Application.EnableVisualStyles()
                   SynchronizationContext.SetSynchronizationContext(New WindowsFormsSynchronizationContext())
                   UiHelpers.MessageHook = Function(text, buttons, icon)
                                               If icon = MessageBoxIcon.Error OrElse icon = MessageBoxIcon.Warning Then _messages.Add(text)
                                               Return If(buttons = MessageBoxButtons.YesNo, DialogResult.No, DialogResult.OK)
                                           End Function
                   Try
                       ' Authentication screens
                       Using f As New LandingForm(skipBootstrap:=True) : Capture(f, "01-landing") : End Using
                       Using f As New ResidentLoginForm() : Capture(f, "02-resident-login", 600) : End Using
                       Using f As New RegistrationForm() : Capture(f, "03-registration", 600) : End Using
                       Using f As New AdminLoginForm() : Capture(f, "04-admin-login", 600) : End Using
                       Using f As New ForgotPasswordDialog() : Capture(f, "05-forgot-password", 500) : End Using
                       Using f As New VerifyEmailDialog("juan@example.com", justSent:=True) : Capture(f, "06-verify-email", 500) : End Using
                       Using f As New FirstRunSetupForm() : Capture(f, "07-first-run-setup", 500) : End Using
                       Dim sampleDb As New Configuration.DatabaseOptions With {.Server = "localhost", .Port = 3306, .Database = "trinidad_lgu_connect", .UserId = "tlc_app"}
                       Dim sampleCheck As New SchemaCheckResult With {.State = SchemaState.DatabaseMissing, .ServerVersion = "9.7.0", .Message = "The database 'trinidad_lgu_connect' does not exist yet."}
                       Using f As New DatabaseSetupForm(sampleDb, sampleCheck) : Capture(f, "08-database-setup", 500) : End Using

                       ' Resident
                       Dim residentForm As List(Of String)
                       AppServices.Instance.Session = data.Juan
                       Using shell As New MainShellForm(data.Juan)
                           Capture(shell, "10-resident-dashboard", 2500)
                           Assert.Equal(FormWindowState.Maximized, shell.WindowState)
                           shell.NavigateRoot("report") : Capture(shell, "11-report-concern")
                           residentForm = FormSignature(shell.CurrentPage)
                           Dim pics = {Photo("pothole", 900, 600), Photo("flood", 900, 600), Photo("night", 900, 600)}
                           Using dlg As New AttachPictureDialog(pics, 5 * 1024 * 1024, 5) : Capture(dlg, "12-attach-picture", 600) : End Using
                           shell.NavigateRoot("myreports") : Capture(shell, "13-my-reports")
                           shell.Navigate(New ResidentConcernDetailsPage(data.FirstConcern)) : Capture(shell, "14-concern-details")
                           Dim detail = Svc.Concerns.GetDetailAsync(data.Juan, data.FirstConcern).GetAwaiter().GetResult()
                           Using viewer As New ImageViewerForm(detail.Attachments.Select(Function(a) ViewerImage.FromAttachment(a)), 1)
                               Capture(viewer, "15-image-viewer", 1500)
                           End Using
                           shell.NavigateRoot("profile") : Capture(shell, "16-profile")
                           shell.NavigateRoot("settings") : Capture(shell, "17-resident-settings")
                           AppServices.Instance.Session = Nothing
                           shell.Close()
                       End Using

                       ' Administrator
                       AppServices.Instance.Session = data.Admin
                       Using shell As New MainShellForm(data.Admin)
                           Capture(shell, "20-admin-dashboard", 2500)
                           Assert.Equal(FormWindowState.Maximized, shell.WindowState)
                           shell.NavigateRoot("concerns") : Capture(shell, "21-submitted-concerns")
                           ' Add Concern opens the resident's Report a Concern form, unchanged.
                           Dim addButton = Descendants(shell.CurrentPage).OfType(Of TLC.ModernButton)().Single(Function(b) b.Text = "Add Concern")
                           addButton.PerformClick()
                           Assert.IsType(Of ReportConcernPage)(shell.CurrentPage)
                           Capture(shell, "34-admin-add-concern")
                           Assert.Equal(residentForm, FormSignature(shell.CurrentPage))
                           Assert.Equal("concerns", shell.CurrentPage.NavKey)
                           shell.GoBack()
                           Assert.IsType(Of SubmittedConcernsPage)(shell.CurrentPage)
                           shell.Navigate(New AdminConcernDetailsPage(data.AdminConcern)) : Capture(shell, "22-admin-concern-details")
                           shell.Navigate(New UpdateStatusPage(data.AdminConcern)) : Capture(shell, "23-update-status")
                           shell.NavigateRoot("reports") : Capture(shell, "24-reports", 2500)
                           ' Report Type, Date Range, From, To and Generate: one row, same top and bottom edges, equal gaps.
                           Dim checkFilterRow = Sub()
                                                    Dim generate = Descendants(shell.CurrentPage).OfType(Of TLC.ModernButton)().Single(Function(b) b.Text = "Generate")
                                                    Dim row = Descendants(generate.Parent).OfType(Of TLC.FieldBase)().Where(Function(c) c.Parent Is generate.Parent).
                                                        Cast(Of Control)().Append(generate).Select(Function(c) c.RectangleToScreen(c.ClientRectangle)).OrderBy(Function(r) r.Left).ToList()
                                                    Assert.Equal(5, row.Count)
                                                    Assert.All(row, Sub(r)
                                                                        Assert.Equal(row(0).Top, r.Top)
                                                                        Assert.Equal(row(0).Bottom, r.Bottom)
                                                                    End Sub)
                                                    Dim gaps = row.Skip(1).Select(Function(r, i) r.Left - row(i).Right).ToList()
                                                    Assert.All(gaps, Sub(g) Assert.Equal(gaps(0), g))
                                                    Assert.True(row.Last().Right <= shell.CurrentPage.RectangleToScreen(shell.CurrentPage.ClientRectangle).Right, "Generate is cut off.")
                                                End Sub
                           checkFilterRow()
                           shell.WindowState = FormWindowState.Normal
                           shell.Size = New Size(1024, 660)
                           Capture(shell, "24b-reports-small-window", 800)
                           checkFilterRow()
                           shell.WindowState = FormWindowState.Maximized
                           Pump(300)
                           shell.Navigate(New ReportPreviewPage(New ReportRequest With {.Type = ReportType.ConcernSummary, .FromDate = DateTime.Today.AddDays(-29), .ToDate = DateTime.Today}))
                           Capture(shell, "25-report-preview", 2500)
                           shell.NavigateRoot("users") : Capture(shell, "26-user-management")
                           Dim viewed = Svc.Users.SearchAsync(data.Admin, New UserFilter With {.Search = "juan"}).GetAwaiter().GetResult().Items.First()
                           Using dlg As New UserProfileDialog(viewed.Id, True) : Capture(dlg, "33-user-profile-view", 1200) : End Using
                           Dim u = Svc.Users.SearchAsync(data.Admin, New UserFilter With {.Search = "juan"}).GetAwaiter().GetResult().Items.First()
                           Using dlg As New UserEditDialog(u, True, False, Svc.Concerns.GetBarangaysAsync().GetAwaiter().GetResult()) : Capture(dlg, "27-user-edit", 500) : End Using
                           shell.NavigateRoot("profile") : Capture(shell, "32-admin-profile")
                           shell.NavigateRoot("settings") : Capture(shell, "28-admin-settings", 2500)
                           Using dlg As New SmtpSettingsDialog() : Capture(dlg, "29-smtp-settings", 500) : End Using
                           Using dlg As New ChangePasswordDialog() : Capture(dlg, "30-change-password", 500) : End Using
                           Dim hist = Svc.Concerns.GetDetailAsync(data.Admin, data.AdminConcern).GetAwaiter().GetResult()
                           Using dlg As New StatusHistoryDialog(hist) : Capture(dlg, "31-status-history", 500) : End Using
                           AppServices.Instance.Session = Nothing
                           shell.Close()
                       End Using
                   Finally
                       UiHelpers.MessageHook = Nothing
                       AppServices.Instance.Session = Nothing
                   End Try
               End Sub)
    End Sub

End Class
