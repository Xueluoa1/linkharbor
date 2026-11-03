$ErrorActionPreference = 'Stop'
$buildDir = Join-Path $PSScriptRoot '../build'
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null
$source = @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using LinkHarbor;

class IconGenerator
{
    static void Main(string[] args)
    {
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        byte[][] images = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
        {
            int size = sizes[i];
            using (var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bitmap))
            using (var memory = new MemoryStream())
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var fill = new SolidBrush(Color.FromArgb(246,249,251)))
                    g.FillEllipse(fill, 0.5f, 0.5f, size-1f, size-1f);
                using (var border = new Pen(Theme.Border, Math.Max(0.65f, size/100f)))
                    g.DrawEllipse(border, 1f, 1f, size-2f, size-2f);
                // Reuse the exact chain geometry drawn by the top-left logo.
                Theme.Icon(g, "link", new RectangleF(size*.12f,size*.17f,size*.72f,size*.72f), Theme.Ink);
                bitmap.Save(memory, ImageFormat.Png);
                images[i] = memory.ToArray();
                if (size == 256) bitmap.Save(args[1], ImageFormat.Png);
            }
        }
        using (var writer = new BinaryWriter(File.Create(args[0])))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            int offset = 6 + 16*sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0);
                writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(images[i].Length); writer.Write(offset);
                offset += images[i].Length;
            }
            foreach (var bytes in images) writer.Write(bytes);
        }
    }
}
'@
$generatorSource = Join-Path $buildDir 'IconGenerator.cs'
[IO.File]::WriteAllText($generatorSource, $source, [Text.Encoding]::UTF8)
$generatorExe = Join-Path $buildDir 'IconGenerator.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $compiler /nologo /target:exe /nowarn:0649 /codepage:65001 /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$generatorExe" $generatorSource (Join-Path $PSScriptRoot 'Theme.cs')
if ($LASTEXITCODE -ne 0) { throw 'Icon generator compilation failed' }
& $generatorExe (Join-Path $PSScriptRoot 'app.ico') (Join-Path $buildDir 'app-icon-preview.png')
if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed' }
