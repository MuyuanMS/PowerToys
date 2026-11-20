param($Image)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
if ($Image.mimeType -notin @('image/png', 'image/jpeg')) {
    throw 'Choose one PNG or JPEG image.'
}

Add-Type -AssemblyName System.Drawing
$inputStream = [IO.MemoryStream]::new([Convert]::FromBase64String($Image.data))
$outputStream = [IO.MemoryStream]::new()
$bitmap = $null
try {
    $bitmap = [Drawing.Bitmap]::new($inputStream)
    $bitmap.RotateFlip([Drawing.RotateFlipType]::Rotate90FlipNone)
    $bitmap.Save($outputStream, [Drawing.Imaging.ImageFormat]::Png)
    @{
        image = @{
            mimeType = 'image/png'
            data = [Convert]::ToBase64String($outputStream.ToArray())
        }
    } | ConvertTo-Json -Compress
}
finally {
    if ($bitmap) { $bitmap.Dispose() }
    $outputStream.Dispose()
    $inputStream.Dispose()
}
