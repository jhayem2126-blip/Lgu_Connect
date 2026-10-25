Imports System.Threading
Imports TrinidadLGUConnect.Forms
Imports TrinidadLGUConnect.Services

''' <summary>Entry point of Trinidad LGU Connect.</summary>
Public Module Program

    <STAThread>
    Public Sub Main()
        ' The UI is laid out in 96-DPI pixels and uses many owner-drawn controls, so Windows scales it uniformly;
        ' GDI text (labels, TextRenderer) stays sharp in this mode.
        Application.SetHighDpiMode(HighDpiMode.DpiUnawareGdiScaled)
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException)
        AddHandler Application.ThreadException, AddressOf OnThreadException
        AddHandler AppDomain.CurrentDomain.UnhandledException, AddressOf OnUnhandledException
        AddHandler TaskScheduler.UnobservedTaskException, Sub(s, e)
                                                              AppLog.Error("Unobserved task exception", e.Exception)
                                                              e.SetObserved()
                                                          End Sub
        AppLog.Info($"Application started (version {GetType(Program).Assembly.GetName().Version}).")
        Application.Run(New AppController())
        AppLog.Info("Application exited.")
    End Sub

    Private Sub OnThreadException(sender As Object, e As ThreadExceptionEventArgs)
        ' UI-thread exceptions that escaped a handler: log and show a safe message instead of crashing.
        Dim owner = AppController.Current?.ActiveForm
        Try
            ShowError(owner, e.Exception)
        Catch
            AppLog.Error("Failed to display an error", e.Exception)
        End Try
    End Sub

    Private Sub OnUnhandledException(sender As Object, e As UnhandledExceptionEventArgs)
        AppLog.Error("Fatal unhandled exception", TryCast(e.ExceptionObject, Exception))
        MessageBox.Show("An unexpected error occurred and the application must close. Details were written to the application log:" &
                        vbLf & AppLog.LogDirectory, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error)
    End Sub

End Module
