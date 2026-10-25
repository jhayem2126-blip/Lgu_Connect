Imports System.Drawing.Imaging
Imports TrinidadLGUConnect.Controls
Imports TrinidadLGUConnect.Data
Imports TrinidadLGUConnect.Helpers
Imports TrinidadLGUConnect.Models
Imports TrinidadLGUConnect.Services

<Assembly: CollectionBehavior(DisableTestParallelization:=True)>

Public Class PasswordHasherTests

    <Fact>
    Public Sub HashAndVerifyRoundTrip()
        Dim h = PasswordHasher.Hash("Correct#Horse9")
        Assert.StartsWith("pbkdf2-sha256$600000$", h)
        Assert.True(PasswordHasher.Verify("Correct#Horse9", h))
        Assert.False(PasswordHasher.Verify("correct#horse9", h))
        Assert.False(PasswordHasher.Verify("", h))
    End Sub

    <Fact>
    Public Sub SaltsAreUnique()
        Assert.NotEqual(PasswordHasher.Hash("Same#Pass1"), PasswordHasher.Hash("Same#Pass1"))
    End Sub

    <Fact>
    Public Sub RejectsMalformedHashesAndDetectsWeakParameters()
        Assert.False(PasswordHasher.Verify("x", "plaintext"))
        Assert.False(PasswordHasher.Verify("x", "pbkdf2-sha256$abc$AAAA$AAAA"))
        Assert.True(PasswordHasher.NeedsRehash(PasswordHasher.Hash("Abc#12345", 1000)))
        Assert.False(PasswordHasher.NeedsRehash(PasswordHasher.Hash("Abc#12345")))
    End Sub

End Class

Public Class ValidationTests

    <Theory>
    <InlineData("juan@example.com", True)>
    <InlineData("Juan.Dela-Cruz@trinidad.gov.ph", True)>
    <InlineData("juan@", False)>
    <InlineData("juan example.com", False)>
    <InlineData("juan@localhost", False)>
    <InlineData("", False)>
    Public Sub Email(value As String, expected As Boolean)
        Assert.Equal(expected, IsValidEmail(value))
    End Sub

    <Theory>
    <InlineData("juan123", True)>
    <InlineData("ana_admin", True)>
    <InlineData("ab", False)>
    <InlineData("_juan", False)>
    <InlineData("juan cruz", False)>
    <InlineData("juan;drop", False)>
    Public Sub Username(value As String, expected As Boolean)
        Assert.Equal(expected, IsValidUsername(value))
    End Sub

    <Fact>
    Public Sub PasswordPolicy()
        Assert.Null(PasswordProblem("Trinidad#2025"))
        Assert.NotNull(PasswordProblem("short1!"))
        Assert.NotNull(PasswordProblem("alllowercase1!"))
        Assert.NotNull(PasswordProblem("ALLUPPERCASE1!"))
        Assert.NotNull(PasswordProblem("NoNumbers!!"))
        Assert.NotNull(PasswordProblem("NoSymbols123"))
        Assert.NotNull(PasswordProblem("Juan123#Pass", "juan123"))
    End Sub

    <Fact>
    Public Sub CleanTextStripsControlCharactersAndEnforcesLength()
        Assert.Equal("Pothole near school", CleanText("  Pothole" & ChrW(7) & " near school  ", 50))
        Assert.Equal("line1" & vbLf & "line2", CleanText("line1" & vbCrLf & "line2", 50, multiline:=True))
        Assert.Throws(Of ValidationException)(Function() CleanText(New String("x"c, 51), 50))
    End Sub

    <Fact>
    Public Sub OneTimeCodesAreWellFormedAndHashNormalised()
        Dim code = SecureRandom.OneTimeCode()
        Assert.Matches("^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$", code)
        Assert.Equal(SecureRandom.HashCode(code), SecureRandom.HashCode(code.ToLowerInvariant().Replace("-", " ")))
        Assert.NotEqual(SecureRandom.HashCode(code), SecureRandom.HashCode(SecureRandom.OneTimeCode()))
    End Sub

End Class

Public Class WorkflowTests

    Private Shared ReadOnly Seeded As StatusTransition() = {
        T(1, 2, "staff"), T(1, 5, "staff"), T(2, 3, "staff"), T(2, 5, "staff"), T(3, 4, "staff"), T(3, 2, "staff"),
        T(4, 5, "staff"), T(4, 3, "staff"), T(1, 5, "resident"), T(4, 5, "resident")}

    Private Shared Function T(f As Integer, toS As Integer, scope As String) As StatusTransition
        Return New StatusTransition With {.FromStatus = CType(f, ConcernStatus), .ToStatus = CType(toS, ConcernStatus), .ActorScope = scope}
    End Function

    <Fact>
    Public Sub StaffWorkflowFollowsConfiguredTransitions()
        Assert.Equal({ConcernStatus.UnderReview, ConcernStatus.Closed}, StatusWorkflow.Allowed(Seeded, ConcernStatus.Submitted, "staff"))
        Assert.True(StatusWorkflow.IsAllowed(Seeded, ConcernStatus.InProgress, ConcernStatus.Resolved, "staff"))
        Assert.False(StatusWorkflow.IsAllowed(Seeded, ConcernStatus.Submitted, ConcernStatus.Resolved, "staff"))
        Assert.Empty(StatusWorkflow.Allowed(Seeded, ConcernStatus.Closed, "staff"))
    End Sub

    <Fact>
    Public Sub ResidentsCanOnlyWithdrawOrConfirm()
        Assert.Equal({ConcernStatus.Closed}, StatusWorkflow.Allowed(Seeded, ConcernStatus.Submitted, "resident"))
        Assert.Empty(StatusWorkflow.Allowed(Seeded, ConcernStatus.UnderReview, "resident"))
        Assert.Empty(StatusWorkflow.Allowed(Seeded, ConcernStatus.InProgress, "resident"))
        Assert.False(StatusWorkflow.IsAllowed(Seeded, ConcernStatus.Submitted, ConcernStatus.UnderReview, "resident"))
    End Sub

    <Fact>
    Public Sub EmbeddedSchemaMatchesTheseTransitions()
        Dim sql = DatabaseInitializer.LoadSchemaScript()
        For Each tr In Seeded
            Assert.Contains($"({CInt(tr.FromStatus)}, {CInt(tr.ToStatus)}, '{tr.ActorScope}')", sql)
        Next
    End Sub

End Class

Public Class SchemaScriptTests

    <Fact>
    Public Sub ScriptSplitsIntoStatementsAndIsNonDestructive()
        Dim sql = DatabaseInitializer.LoadSchemaScript()
        Dim statements = DatabaseInitializer.SplitStatements(sql)
        Assert.Equal(20, statements.Where(Function(s) s.StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase)).Count())
        Assert.Equal(10, statements.Where(Function(s) s.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)).Count())
        Assert.Equal(30, statements.Count)
        Assert.All(statements, Sub(s) Assert.DoesNotContain("--", s.Split(ControlChars.Lf).First()))
        ' Check the executable statements (comments removed), not the explanatory header.
        Dim upper = String.Join(vbLf, statements).ToUpperInvariant()
        Assert.DoesNotContain("DROP ", upper)
        Assert.DoesNotContain("TRUNCATE", upper)
        Assert.DoesNotContain("DELETE FROM", upper)
        Assert.All(statements.Where(Function(s) s.StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase)), Sub(s) Assert.Contains("IF NOT EXISTS", s))
        Assert.All(statements.Where(Function(s) s.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)), Sub(s) Assert.StartsWith("INSERT IGNORE", s))
    End Sub

    <Fact>
    Public Sub DatabaseNamesAreValidated()
        Assert.Equal("trinidad_lgu_connect", DatabaseInitializer.EnsureSafeName("trinidad_lgu_connect"))
        Assert.Throws(Of ValidationException)(Function() DatabaseInitializer.EnsureSafeName("x`; DROP DATABASE y"))
    End Sub

End Class

Public Class ReportingTests

    <Fact>
    Public Sub DateRangePresets()
        Dim today = New DateTime(2025, 8, 23)
        Assert.Equal((New DateTime(2025, 7, 25), today), ReportingService.ResolveRange(DateRangePreset.Last30Days, today))
        Assert.Equal((New DateTime(2025, 7, 1), New DateTime(2025, 7, 31)), ReportingService.ResolveRange(DateRangePreset.LastMonth, today))
        Assert.Equal((New DateTime(2025, 8, 1), today), ReportingService.ResolveRange(DateRangePreset.ThisMonth, today))
        Assert.Equal((today, today), ReportingService.ResolveRange(DateRangePreset.Today, today))
    End Sub

    <Fact>
    Public Sub PeriodsIncludeEmptyDaysAndSwitchToMonthsForLongRanges()
        Dim c1 As New ConcernSummary With {.ReportedAt = New DateTime(2025, 8, 2, 12, 0, 0, DateTimeKind.Local).ToUniversalTime(), .Status = ConcernStatus.Resolved}
        Dim c2 As New ConcernSummary With {.ReportedAt = New DateTime(2025, 8, 2, 13, 0, 0, DateTimeKind.Local).ToUniversalTime(), .Status = ConcernStatus.Closed}
        Dim days = ReportingService.BuildPeriods({c1, c2}, New DateTime(2025, 8, 1), New DateTime(2025, 8, 3))
        Assert.Equal(3, days.Count)
        Assert.Equal({0, 2, 0}, days.Select(Function(d) d.Total))
        Assert.Equal(1, days(1).Resolved)
        Assert.Equal(1, days(1).Closed)
        Dim months = ReportingService.BuildPeriods({c1}, New DateTime(2025, 1, 1), New DateTime(2025, 8, 31))
        Assert.Equal(8, months.Count)
        Assert.Equal("Aug 2025", months.Last().Label)
    End Sub

    <Fact>
    Public Sub CsvEscapingAndFormulaInjectionProtection()
        Assert.Equal("plain", ExportService.CsvField("plain"))
        Assert.Equal("""a,b""", ExportService.CsvField("a,b"))
        Assert.Equal("""say """"hi""""""", ExportService.CsvField("say ""hi"""))
        Assert.Equal("'=HYPERLINK(1)", ExportService.CsvField("=HYPERLINK(1)"))
        Assert.Equal("'+1", ExportService.CsvField("+1"))
        Assert.Equal("'@SUM(A1)", ExportService.CsvField("@SUM(A1)"))
    End Sub

    <Fact>
    Public Sub ColorBlendWorksInBothDirections()
        ' Regression: blending toward black used to throw OverflowException (checked Byte arithmetic).
        Assert.Equal(Color.FromArgb(19, 91, 194), Theme.Blend(Theme.Primary, Color.Black, 0.1))
        Assert.Equal(Color.Black.ToArgb(), Theme.Blend(Color.White, Color.Black, 1).ToArgb())
        Assert.Equal(Color.White.ToArgb(), Theme.Blend(Color.Black, Color.White, 1).ToArgb())
        Assert.Equal(Theme.Primary.ToArgb(), Theme.Blend(Theme.Primary, Color.White, 0).ToArgb())
    End Sub

    <Fact>
    Public Sub NiceAxisMaximum()
        Assert.Equal(5, BarChart.NiceMax(0))
        Assert.Equal(10, BarChart.NiceMax(8))
        Assert.Equal(20, BarChart.NiceMax(16))
        Assert.Equal(50, BarChart.NiceMax(48))
        Assert.Equal(250, BarChart.NiceMax(201))
    End Sub

    Friend Shared Function SampleReport() As ReportResult
        Dim r As New ReportResult With {
            .Request = New ReportRequest With {.Type = ReportType.ConcernSummary, .FromDate = New DateTime(2025, 8, 1), .ToDate = New DateTime(2025, 8, 31)},
            .GeneratedAt = New DateTime(2025, 8, 31, 9, 0, 0), .GeneratedBy = "Test Admin",
            .Total = 3, .Submitted = 1, .Resolved = 1, .Closed = 1}
        r.ByStatus = AllStatuses.Select(Function(s) New StatusReportRow With {.Status = s, .Count = If(s = ConcernStatus.UnderReview OrElse s = ConcernStatus.InProgress, 0, 1), .Percent = 33.3}).ToList()
        r.ByCategory.Add(New CategoryReportRow With {.Category = "Roads & Transportation", .Total = 2, .Resolved = 1, .Closed = 1})
        r.ByCategory.Add(New CategoryReportRow With {.Category = "Streetlights", .Total = 1, .Submitted = 1})
        For i = 1 To 60
            r.Concerns.Add(New ConcernSummary With {.ReportCode = ReportCode(i), .ReportedAt = DateTime.UtcNow, .CategoryName = "Streetlights",
                                                    .Location = "=cmd|' /C calc'!A0 Poblacion", .Status = ConcernStatus.Submitted, .ResidentName = "Juan Dela Cruz"})
        Next
        r.ByDay = ReportingService.BuildPeriods(r.Concerns, r.Request.FromDate, r.Request.ToDate)
        Return r
    End Function

    <Fact>
    Public Sub ExportsProduceValidFilesWithTheData()
        Dim r = SampleReport()
        Dim dir = Path.Combine(Path.GetTempPath(), "tlc-tests-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(dir)
        Try
            Dim csv = Path.Combine(dir, "r.csv"), xlsx = Path.Combine(dir, "r.xlsx"), pdf = Path.Combine(dir, "r.pdf")
            ExportService.ExportCsv(r, csv)
            Dim text = File.ReadAllText(csv)
            Assert.Contains("CON-060", text)
            Assert.Contains("'=cmd", text)
            ExportService.ExportExcel(r, xlsx)
            Dim xb = File.ReadAllBytes(xlsx)
            Assert.Equal(AscW("P"c), CInt(xb(0)))
            Assert.Equal(AscW("K"c), CInt(xb(1)))
            Using wb As New ClosedXML.Excel.XLWorkbook(xlsx)
                Assert.Equal("CON-001", wb.Worksheet("Concerns").Cell(2, 1).GetString())
                Assert.Equal(61, wb.Worksheet("Concerns").LastRowUsed().RowNumber())
            End Using
            Using seal As New Bitmap(64, 64)
                ExportService.ExportPdf(r, seal, pdf)
            End Using
            Dim pb = File.ReadAllBytes(pdf)
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pb, 0, 4))
            Assert.True(pb.Length > 4000)
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"))
        Finally
            Directory.Delete(dir, True)
        End Try
    End Sub

    <Fact>
    Public Sub RendererPaginatesLongLists()
        Dim r = SampleReport()
        Dim renderer As New ReportRenderer(r, Nothing)
        Using bmp As New Bitmap(10, 10), g = Graphics.FromImage(bmp)
            renderer.Layout(ReportRenderer.LetterWidth, ReportRenderer.LetterHeight, New GdiReportCanvas(g))
        End Using
        Assert.True(renderer.PageCount >= 2, $"Expected multiple pages, got {renderer.PageCount}")
    End Sub

End Class

Public Class AttachmentTests

    Private Shared Function MakeImage(format As ImageFormat, w As Integer, h As Integer) As Byte()
        Using bmp As New Bitmap(w, h)
            Using g = Graphics.FromImage(bmp)
                g.Clear(Color.SteelBlue)
                g.FillEllipse(Brushes.Orange, 10, 10, w \ 2, h \ 2)
            End Using
            Using ms As New MemoryStream()
                bmp.Save(ms, format)
                Return ms.ToArray()
            End Using
        End Using
    End Function

    <Fact>
    Public Sub AcceptsJpegAndPngAndCreatesThumbnails()
        Dim jpg = AttachmentService.Prepare(MakeImage(ImageFormat.Jpeg, 800, 600), "road damage.jpg", 5 * 1024 * 1024)
        Assert.Equal("image/jpeg", jpg.ContentType)
        Assert.Equal(800, jpg.Width)
        Assert.True(AttachmentService.IsJpegSignature(jpg.Data))
        Assert.Equal(64, jpg.Sha256.Length)
        Using t = AttachmentService.TryDecode(jpg.Thumbnail)
            Assert.Equal(AttachmentService.ThumbnailWidth, t.Width)
            Assert.Equal(AttachmentService.ThumbnailHeight, t.Height)
        End Using
        Dim png = AttachmentService.Prepare(MakeImage(ImageFormat.Png, 300, 900), "flood.png", 5 * 1024 * 1024)
        Assert.Equal("image/png", png.ContentType)
        Assert.True(AttachmentService.IsPngSignature(png.Data))
    End Sub

    <Fact>
    Public Sub DownscalesVeryLargeImages()
        Dim big = AttachmentService.Prepare(MakeImage(ImageFormat.Jpeg, 4000, 3000), "big.jpg", 20 * 1024 * 1024)
        Assert.Equal(AttachmentService.MaxStoredDimension, big.Width)
        Assert.Equal(1920, big.Height)
    End Sub

    <Fact>
    Public Sub RejectsFakeOversizedAndUnsupportedFiles()
        Assert.Throws(Of ValidationException)(Function() AttachmentService.Prepare(Text.Encoding.UTF8.GetBytes("<script>alert(1)</script>"), "evil.jpg", 1024 * 1024))
        Assert.Throws(Of ValidationException)(Function() AttachmentService.Prepare(MakeImage(ImageFormat.Jpeg, 400, 300), "a.jpg", 100))
        Dim gif = MakeImage(ImageFormat.Gif, 50, 50)
        Assert.Throws(Of ValidationException)(Function() AttachmentService.Prepare(gif, "anim.gif", 1024 * 1024))
        Dim truncated = MakeImage(ImageFormat.Png, 200, 200).Take(60).ToArray()
        Assert.Throws(Of ValidationException)(Function() AttachmentService.Prepare(truncated, "broken.png", 1024 * 1024))
        Dim tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") & ".bmp")
        File.WriteAllBytes(tmp, MakeImage(ImageFormat.Bmp, 20, 20))
        Try
            Assert.Throws(Of ValidationException)(Function() AttachmentService.PrepareFromFile(tmp, 1024 * 1024))
        Finally
            File.Delete(tmp)
        End Try
    End Sub

    <Fact>
    Public Sub FileNamesAreSanitised()
        Assert.Equal("photo.jpg", AttachmentService.SanitizeFileName("C:\Users\x\photo.jpg"))
        Assert.Equal("picture.jpg", AttachmentService.SanitizeFileName("   "))
        Assert.DoesNotContain("..", AttachmentService.SanitizeFileName("..\..\secret.png"))
    End Sub

End Class
