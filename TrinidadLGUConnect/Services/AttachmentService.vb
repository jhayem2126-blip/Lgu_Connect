Imports System.Drawing.Drawing2D
Imports System.Drawing.Imaging
Imports System.Runtime.InteropServices
Imports TrinidadLGUConnect.Helpers
Imports TrinidadLGUConnect.Models

Namespace Services

    ''' <summary>
    ''' Validates and prepares pictures for storage. A file is accepted only if its extension is .jpg/.jpeg/.png,
    ''' its size is within the limit, its first bytes are a real JPEG/PNG signature and GDI+ can fully decode it.
    ''' The image is then re-encoded (which strips EXIF metadata such as GPS location and neutralises crafted
    ''' payloads), downscaled if very large, and a thumbnail is generated. Only the bytes are stored - the
    ''' user's original file path is never persisted or trusted afterwards.
    ''' </summary>
    Public Class AttachmentService

        Public Shared ReadOnly AllowedExtensions As String() = {".jpg", ".jpeg", ".png"}
        Public Const FileDialogFilter As String = "Pictures (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png"
        Public Const MaxStoredDimension As Integer = 2560
        Public Const ThumbnailWidth As Integer = 360
        Public Const ThumbnailHeight As Integer = 270
        Private Const MaxPixelCount As Long = 60_000_000

        Public Shared Function PrepareFromFile(filePath As String, maxBytes As Long) As PendingAttachment
            If String.IsNullOrWhiteSpace(filePath) OrElse Not File.Exists(filePath) Then
                Throw New ValidationException("The selected file could not be found.")
            End If
            Dim ext = Path.GetExtension(filePath).ToLowerInvariant()
            If Not AllowedExtensions.Contains(ext) Then
                Throw New ValidationException($"'{Path.GetFileName(filePath)}' is not a supported picture. Use JPG or PNG files.")
            End If
            Dim info As New FileInfo(filePath)
            If info.Length = 0 Then Throw New ValidationException($"'{info.Name}' is empty.")
            If info.Length > maxBytes Then
                Throw New ValidationException($"'{info.Name}' is {TextFormat.FileSize(info.Length)}. The maximum picture size is {TextFormat.FileSize(maxBytes)}.")
            End If
            Dim bytes As Byte()
            Try
                bytes = File.ReadAllBytes(filePath)
            Catch ex As Exception When TypeOf ex Is IOException OrElse TypeOf ex Is UnauthorizedAccessException
                Throw New ValidationException($"'{info.Name}' could not be read. Close any program using it and try again.")
            End Try
            Return Prepare(bytes, info.Name, maxBytes)
        End Function

        Public Shared Function Prepare(bytes As Byte(), fileName As String, maxBytes As Long) As PendingAttachment
            Dim safeName = SanitizeFileName(fileName)
            If bytes Is Nothing OrElse bytes.Length = 0 Then Throw New ValidationException($"'{safeName}' is empty.")
            If bytes.LongLength > maxBytes Then Throw New ValidationException($"'{safeName}' is larger than {TextFormat.FileSize(maxBytes)}.")
            Dim isPng = IsPngSignature(bytes)
            Dim isJpeg = IsJpegSignature(bytes)
            If Not isPng AndAlso Not isJpeg Then
                Throw New ValidationException($"'{safeName}' is not a valid JPG or PNG image.")
            End If

            Dim original As Image
            Try
                Using ms As New MemoryStream(bytes, writable:=False)
                    Using decoded = Image.FromStream(ms, useEmbeddedColorManagement:=False, validateImageData:=True)
                        ' Force a full decode while the stream is open.
                        original = New Bitmap(decoded)
                    End Using
                End Using
            Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is OutOfMemoryException OrElse TypeOf ex Is ExternalException
                Throw New ValidationException($"'{safeName}' could not be opened as a picture. It may be damaged.")
            End Try

            Using original
                If CLng(original.Width) * original.Height > MaxPixelCount Then
                    Throw New ValidationException($"'{safeName}' has too many pixels ({original.Width} x {original.Height}).")
                End If
                Dim orientation = ReadOrientation(bytes)
                Using oriented = ApplyOrientation(original, orientation)
                    Dim scale = Math.Min(1.0, MaxStoredDimension / CDbl(Math.Max(oriented.Width, oriented.Height)))
                    Dim w = Math.Max(1, CInt(Math.Round(oriented.Width * scale)))
                    Dim h = Math.Max(1, CInt(Math.Round(oriented.Height * scale)))
                    Using stored = Resize(oriented, w, h, preserveAlpha:=isPng)
                        Dim data = If(isPng, EncodePng(stored), EncodeJpeg(stored, 88L))
                        Dim thumb As Byte()
                        Using t = CreateThumbnail(stored)
                            thumb = EncodeJpeg(t, 82L)
                        End Using
                        Dim finalName = Path.ChangeExtension(safeName, If(isPng, ".png", ".jpg"))
                        Return New PendingAttachment With {
                            .FileName = finalName,
                            .ContentType = If(isPng, "image/png", "image/jpeg"),
                            .Data = data,
                            .Thumbnail = thumb,
                            .Width = w,
                            .Height = h,
                            .Sha256 = SecureRandom.Sha256Hex(data)
                        }
                    End Using
                End Using
            End Using
        End Function

        Public Const AvatarSize As Integer = 256

        ''' <summary>
        ''' Validates a profile picture exactly like a report picture (type, size, signature, full decode,
        ''' re-encoding) and returns a centre-cropped 256 x 256 JPEG.
        ''' </summary>
        Public Shared Function PrepareAvatar(bytes As Byte(), fileName As String, maxBytes As Long) As Byte()
            Dim validated = Prepare(bytes, fileName, maxBytes)
            Using img = TryDecode(validated.Data)
                If img Is Nothing Then Throw New ValidationException("The picture could not be read.")
                Dim side = Math.Min(img.Width, img.Height)
                Dim src As New Rectangle((img.Width - side) \ 2, (img.Height - side) \ 2, side, side)
                Using bmp As New Bitmap(AvatarSize, AvatarSize, PixelFormat.Format24bppRgb)
                    Using g = Graphics.FromImage(bmp)
                        g.Clear(Color.White)
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality
                        g.DrawImage(img, New Rectangle(0, 0, AvatarSize, AvatarSize), src, GraphicsUnit.Pixel)
                    End Using
                    Return EncodeJpeg(bmp, 88L)
                End Using
            End Using
        End Function

        Public Shared Function SanitizeFileName(name As String) As String
            Dim n = Path.GetFileName(If(name, "")).Trim()
            Dim invalid = Path.GetInvalidFileNameChars()
            n = New String(n.Where(Function(c) Not invalid.Contains(c) AndAlso Not Char.IsControl(c)).ToArray())
            If n.Length = 0 Then n = "picture.jpg"
            If n.Length > 120 Then n = n.Substring(0, 100) & Path.GetExtension(n)
            Return n
        End Function

        Public Shared Function IsPngSignature(b As Byte()) As Boolean
            Dim sig As Byte() = {&H89, &H50, &H4E, &H47, &HD, &HA, &H1A, &HA}
            Return b.Length >= 8 AndAlso b.Take(8).SequenceEqual(sig)
        End Function

        Public Shared Function IsJpegSignature(b As Byte()) As Boolean
            Return b.Length >= 3 AndAlso b(0) = &HFF AndAlso b(1) = &HD8 AndAlso b(2) = &HFF
        End Function

        ''' <summary>Decodes stored image bytes for display. Returns Nothing if they cannot be decoded.</summary>
        Public Shared Function TryDecode(data As Byte()) As Image
            If data Is Nothing OrElse data.Length = 0 Then Return Nothing
            Try
                Using ms As New MemoryStream(data, writable:=False)
                    Using img = Image.FromStream(ms, False, True)
                        Return New Bitmap(img)
                    End Using
                End Using
            Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is OutOfMemoryException OrElse TypeOf ex Is ExternalException
                AppLog.Warn("Stored image could not be decoded.", ex)
                Return Nothing
            End Try
        End Function

        Private Shared Function ReadOrientation(bytes As Byte()) As Integer
            Try
                Using ms As New MemoryStream(bytes, writable:=False)
                    Using img = Image.FromStream(ms, False, False)
                        If img.PropertyIdList.Contains(&H112) Then
                            Dim item = img.GetPropertyItem(&H112)
                            If item IsNot Nothing AndAlso item.Value IsNot Nothing AndAlso item.Value.Length >= 2 Then
                                Return BitConverter.ToUInt16(item.Value, 0)
                            End If
                        End If
                    End Using
                End Using
            Catch ex As Exception When TypeOf ex Is ArgumentException OrElse TypeOf ex Is ExternalException
            End Try
            Return 1
        End Function

        Private Shared Function ApplyOrientation(img As Image, orientation As Integer) As Bitmap
            Dim bmp As New Bitmap(img)
            Select Case orientation
                Case 2 : bmp.RotateFlip(RotateFlipType.RotateNoneFlipX)
                Case 3 : bmp.RotateFlip(RotateFlipType.Rotate180FlipNone)
                Case 4 : bmp.RotateFlip(RotateFlipType.Rotate180FlipX)
                Case 5 : bmp.RotateFlip(RotateFlipType.Rotate90FlipX)
                Case 6 : bmp.RotateFlip(RotateFlipType.Rotate90FlipNone)
                Case 7 : bmp.RotateFlip(RotateFlipType.Rotate270FlipX)
                Case 8 : bmp.RotateFlip(RotateFlipType.Rotate270FlipNone)
            End Select
            Return bmp
        End Function

        Private Shared Function Resize(img As Image, w As Integer, h As Integer, preserveAlpha As Boolean) As Bitmap
            Dim bmp As New Bitmap(w, h, If(preserveAlpha, PixelFormat.Format32bppArgb, PixelFormat.Format24bppRgb))
            Using g = Graphics.FromImage(bmp)
                If Not preserveAlpha Then g.Clear(Color.White)
                g.InterpolationMode = InterpolationMode.HighQualityBicubic
                g.SmoothingMode = SmoothingMode.HighQuality
                g.PixelOffsetMode = PixelOffsetMode.HighQuality
                g.CompositingQuality = CompositingQuality.HighQuality
                Using attrs As New ImageAttributes()
                    attrs.SetWrapMode(WrapMode.TileFlipXY)
                    g.DrawImage(img, New Rectangle(0, 0, w, h), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, attrs)
                End Using
            End Using
            Return bmp
        End Function

        ''' <summary>Center-cropped thumbnail with a fixed 4:3 aspect ratio.</summary>
        Public Shared Function CreateThumbnail(img As Image) As Bitmap
            Dim targetRatio = ThumbnailWidth / CDbl(ThumbnailHeight)
            Dim srcRatio = img.Width / CDbl(img.Height)
            Dim src As Rectangle
            If srcRatio > targetRatio Then
                Dim cw = CInt(img.Height * targetRatio)
                src = New Rectangle((img.Width - cw) \ 2, 0, cw, img.Height)
            Else
                Dim ch = CInt(img.Width / targetRatio)
                src = New Rectangle(0, (img.Height - ch) \ 2, img.Width, ch)
            End If
            Dim bmp As New Bitmap(ThumbnailWidth, ThumbnailHeight, PixelFormat.Format24bppRgb)
            Using g = Graphics.FromImage(bmp)
                g.Clear(Color.White)
                g.InterpolationMode = InterpolationMode.HighQualityBicubic
                g.PixelOffsetMode = PixelOffsetMode.HighQuality
                g.DrawImage(img, New Rectangle(0, 0, ThumbnailWidth, ThumbnailHeight), src, GraphicsUnit.Pixel)
            End Using
            Return bmp
        End Function

        Public Shared Function EncodeJpeg(img As Image, quality As Long) As Byte()
            Dim codec = ImageCodecInfo.GetImageEncoders().First(Function(c) c.FormatID = ImageFormat.Jpeg.Guid)
            Using ps As New EncoderParameters(1)
                ps.Param(0) = New EncoderParameter(Imaging.Encoder.Quality, quality)
                Using ms As New MemoryStream()
                    img.Save(ms, codec, ps)
                    Return ms.ToArray()
                End Using
            End Using
        End Function

        Public Shared Function EncodePng(img As Image) As Byte()
            Using ms As New MemoryStream()
                img.Save(ms, ImageFormat.Png)
                Return ms.ToArray()
            End Using
        End Function

    End Class

End Namespace
