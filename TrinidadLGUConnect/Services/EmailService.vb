Imports System.Net
Imports System.Threading
Imports MailKit
Imports MailKit.Net.Smtp
Imports MailKit.Security
Imports MimeKit
Imports TrinidadLGUConnect.Configuration

Namespace Services

    ''' <summary>Raised when an email could not be delivered to the SMTP server. The message is safe to display.</summary>
    Public Class EmailDeliveryException
        Inherits Exception
        Public Sub New(message As String, Optional inner As Exception = Nothing)
            MyBase.New(message, inner)
        End Sub
    End Class

    ''' <summary>
    ''' Sends email through the configured SMTP server using MailKit. A call only returns normally when
    ''' the SMTP server accepted the message; every failure raises <see cref="EmailDeliveryException"/>.
    ''' </summary>
    Public Class EmailService

        Private ReadOnly _optionsProvider As Func(Of SmtpOptions)

        Public Sub New(optionsProvider As Func(Of SmtpOptions))
            _optionsProvider = optionsProvider
        End Sub

        Public ReadOnly Property IsConfigured As Boolean
            Get
                Return _optionsProvider().IsConfigured
            End Get
        End Property

        Public ReadOnly Property Options As SmtpOptions
            Get
                Return _optionsProvider()
            End Get
        End Property

        Public Async Function SendAsync(toAddress As String, toName As String, subject As String, textBody As String, htmlBody As String,
                                        Optional overrideOptions As SmtpOptions = Nothing) As Task
            Dim o = If(overrideOptions, _optionsProvider())
            If Not o.IsConfigured Then
                Throw New EmailDeliveryException("Email (SMTP) is not configured. An administrator must configure it in Settings.")
            End If

            Dim message As New MimeMessage()
            message.From.Add(New MailboxAddress(o.FromName, o.FromAddress))
            message.To.Add(New MailboxAddress(If(toName, ""), toAddress))
            message.Subject = subject
            Dim body As New BodyBuilder With {.TextBody = textBody, .HtmlBody = htmlBody}
            message.Body = body.ToMessageBody()

            Dim security As SecureSocketOptions
            Select Case If(o.Security, "").Trim().ToLowerInvariant()
                Case "none" : security = SecureSocketOptions.None
                Case "sslonconnect", "ssl" : security = SecureSocketOptions.SslOnConnect
                Case "auto" : security = SecureSocketOptions.Auto
                Case Else : security = SecureSocketOptions.StartTls
            End Select

            Using cts As New CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(5, o.TimeoutSeconds)))
                Using client As New SmtpClient()
                    client.Timeout = Math.Max(5, o.TimeoutSeconds) * 1000
                    Try
                        Await client.ConnectAsync(o.Host, o.Port, security, cts.Token).ConfigureAwait(False)
                        If Not String.IsNullOrEmpty(o.UserName) Then
                            Await client.AuthenticateAsync(New NetworkCredential(o.UserName, o.Password), cts.Token).ConfigureAwait(False)
                        End If
                        Await client.SendAsync(message, cts.Token).ConfigureAwait(False)
                        Await client.DisconnectAsync(True, cts.Token).ConfigureAwait(False)
                    Catch ex As AuthenticationException
                        AppLog.Error($"SMTP authentication failed for host {o.Host}:{o.Port}", ex)
                        Throw New EmailDeliveryException("The email server rejected the configured SMTP user name or password.", ex)
                    Catch ex As OperationCanceledException
                        AppLog.Error($"SMTP timeout for host {o.Host}:{o.Port}", ex)
                        Throw New EmailDeliveryException("The email server did not respond in time. Please try again later.", ex)
                    Catch ex As Exception When TypeOf ex Is SmtpCommandException OrElse TypeOf ex Is SmtpProtocolException OrElse
                                                TypeOf ex Is Sockets.SocketException OrElse TypeOf ex Is IOException OrElse
                                                TypeOf ex Is SslHandshakeException OrElse TypeOf ex Is ServiceNotConnectedException
                        AppLog.Error($"SMTP delivery failed via {o.Host}:{o.Port}", ex)
                        Throw New EmailDeliveryException("The email could not be sent. Please try again later or contact the LGU office.", ex)
                    End Try
                End Using
            End Using
            AppLog.Info($"Email '{subject}' accepted by SMTP server {o.Host}.")
        End Function

        ' ------------------------------------------------------------------ templates

        Private Shared Function Html(title As String, bodyHtml As String) As String
            Return "<div style=""font-family:Segoe UI,Arial,sans-serif;max-width:560px;margin:auto;border:1px solid #e3e8f0;border-radius:10px;overflow:hidden"">" &
                   "<div style=""background:#0b2e6b;color:#fff;padding:18px 24px""><div style=""font-size:18px;font-weight:700"">TRINIDAD LGU CONNECT</div>" &
                   "<div style=""font-size:12px;opacity:.85"">Community Concern Reporting and Monitoring System</div></div>" &
                   $"<div style=""padding:24px;color:#1f2a44""><h2 style=""margin-top:0;color:#0d2b5e;font-size:18px"">{WebUtility.HtmlEncode(title)}</h2>{bodyHtml}</div>" &
                   "<div style=""padding:12px 24px;background:#f3f6fb;color:#6b7a90;font-size:11px"">Municipality of Trinidad, Bohol &middot; A Cleaner, Safer and Better Trinidad for All</div></div>"
        End Function

        Private Shared Function CodeBlock(code As String) As String
            Return $"<p style=""font-size:26px;letter-spacing:4px;font-weight:700;background:#f3f6fb;padding:12px 16px;text-align:center;border-radius:8px"">{WebUtility.HtmlEncode(code)}</p>"
        End Function

        Public Function SendVerificationCodeAsync(toAddress As String, name As String, code As String, minutes As Integer) As Task
            Dim text = $"Hello {name},{vbLf}{vbLf}Your Trinidad LGU Connect verification code is: {code}{vbLf}{vbLf}It expires in {minutes} minutes. If you did not create an account, ignore this email."
            Dim body = $"<p>Hello {WebUtility.HtmlEncode(name)},</p><p>Use this code to verify your email address:</p>{CodeBlock(code)}<p>The code expires in {minutes} minutes and can be used once. If you did not create an account, you can ignore this email.</p>"
            Return SendAsync(toAddress, name, "Verify your Trinidad LGU Connect account", text, Html("Verify your email address", body))
        End Function

        Public Function SendPasswordResetCodeAsync(toAddress As String, name As String, code As String, minutes As Integer) As Task
            Dim text = $"Hello {name},{vbLf}{vbLf}A password reset was requested for your Trinidad LGU Connect account. Your reset code is: {code}{vbLf}{vbLf}It expires in {minutes} minutes and can be used once. If you did not request this, ignore this email - your password will not change."
            Dim body = $"<p>Hello {WebUtility.HtmlEncode(name)},</p><p>A password reset was requested for your account. Enter this code in the application:</p>{CodeBlock(code)}<p>The code expires in {minutes} minutes and can be used once. If you did not request this, ignore this email &mdash; your password will not change.</p>"
            Return SendAsync(toAddress, name, "Your Trinidad LGU Connect password reset code", text, Html("Password reset code", body))
        End Function

        Public Function SendPasswordChangedAsync(toAddress As String, name As String) As Task
            Dim text = $"Hello {name},{vbLf}{vbLf}The password for your Trinidad LGU Connect account was changed on {DateTime.Now:MMM dd, yyyy hh:mm tt}. If this was not you, contact the LGU office immediately."
            Dim body = $"<p>Hello {WebUtility.HtmlEncode(name)},</p><p>The password for your account was changed on {DateTime.Now:MMM dd, yyyy hh:mm tt}.</p><p>If this was not you, contact the LGU office immediately.</p>"
            Return SendAsync(toAddress, name, "Your Trinidad LGU Connect password was changed", text, Html("Password changed", body))
        End Function

        Public Function SendSubmissionReceiptAsync(toAddress As String, name As String, reportCode As String, category As String, location As String) As Task
            Dim text = $"Hello {name},{vbLf}{vbLf}We received your report {reportCode} ({category}) at {location}. You can track its status in Trinidad LGU Connect under My Reports."
            Dim body = $"<p>Hello {WebUtility.HtmlEncode(name)},</p><p>We received your report <b>{WebUtility.HtmlEncode(reportCode)}</b>.</p>" &
                       $"<p>Category: {WebUtility.HtmlEncode(category)}<br>Location: {WebUtility.HtmlEncode(location)}</p><p>You can track its status under <b>My Reports</b>.</p>"
            Return SendAsync(toAddress, name, $"Report {reportCode} received", text, Html("Report received", body))
        End Function

        Public Function SendStatusChangedAsync(toAddress As String, name As String, reportCode As String, newStatus As String, remarks As String) As Task
            Dim r = If(String.IsNullOrWhiteSpace(remarks), "", $"{vbLf}Remarks: {remarks}")
            Dim text = $"Hello {name},{vbLf}{vbLf}The status of your report {reportCode} is now: {newStatus}.{r}{vbLf}{vbLf}Open Trinidad LGU Connect to see the full status history."
            Dim rh = If(String.IsNullOrWhiteSpace(remarks), "", $"<p>Remarks: {WebUtility.HtmlEncode(remarks)}</p>")
            Dim body = $"<p>Hello {WebUtility.HtmlEncode(name)},</p><p>The status of your report <b>{WebUtility.HtmlEncode(reportCode)}</b> is now <b>{WebUtility.HtmlEncode(newStatus)}</b>.</p>{rh}<p>Open Trinidad LGU Connect to see the full status history.</p>"
            Return SendAsync(toAddress, name, $"Report {reportCode}: {newStatus}", text, Html("Status update", body))
        End Function

        Public Function SendTestAsync(toAddress As String, Optional overrideOptions As SmtpOptions = Nothing) As Task
            Return SendAsync(toAddress, "", "Trinidad LGU Connect SMTP test", "This is a test message from Trinidad LGU Connect. Your SMTP settings work.",
                             Html("SMTP test", "<p>This is a test message. Your SMTP settings work.</p>"), overrideOptions)
        End Function

    End Class

End Namespace
