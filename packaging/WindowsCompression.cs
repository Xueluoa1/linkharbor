using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace LinkHarbor
{
    // Windows 8+ Compression API, LZMS buffer mode. No external runtime is needed.
    // https://learn.microsoft.com/windows/win32/cmpapi/using-the-compression-api
    public static class WindowsCompression
    {
        private const uint Lzms = 5;
        private const int InsufficientBuffer = 122;
        private const int MaximumPayloadSize = 512 * 1024 * 1024;

        [DllImport("cabinet.dll", SetLastError = true)]
        private static extern bool CreateCompressor(uint algorithm, IntPtr allocator, out IntPtr handle);
        [DllImport("cabinet.dll", SetLastError = true)]
        private static extern bool SetCompressorInformation(IntPtr handle, int informationClass, ref uint value, UIntPtr size);
        [DllImport("cabinet.dll", SetLastError = true)]
        private static extern bool Compress(IntPtr handle, byte[] input, UIntPtr inputSize, byte[] output, UIntPtr capacity, out UIntPtr written);
        [DllImport("cabinet.dll")]
        private static extern bool CloseCompressor(IntPtr handle);
        [DllImport("cabinet.dll", SetLastError = true)]
        private static extern bool CreateDecompressor(uint algorithm, IntPtr allocator, out IntPtr handle);
        [DllImport("cabinet.dll", SetLastError = true)]
        private static extern bool Decompress(IntPtr handle, byte[] input, UIntPtr inputSize, byte[] output, UIntPtr capacity, out UIntPtr written);
        [DllImport("cabinet.dll")]
        private static extern bool CloseDecompressor(IntPtr handle);

        public static void CompressFile(string inputPath, string outputPath)
        {
            byte[] input = File.ReadAllBytes(inputPath);
            IntPtr handle;
            if (!CreateCompressor(Lzms, IntPtr.Zero, out handle)) throw new Win32Exception();
            try
            {
                uint blockSize = 64 * 1024 * 1024;
                if (!SetCompressorInformation(handle, 1, ref blockSize, (UIntPtr)4)) throw new Win32Exception();
                UIntPtr size;
                if (!Compress(handle, input, (UIntPtr)input.Length, null, UIntPtr.Zero, out size) && Marshal.GetLastWin32Error() != InsufficientBuffer)
                    throw new Win32Exception();
                byte[] output = new byte[CheckedSize(size)];
                if (!Compress(handle, input, (UIntPtr)input.Length, output, (UIntPtr)output.Length, out size)) throw new Win32Exception();
                using (var file = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write)) file.Write(output, 0, CheckedSize(size));
            }
            finally { CloseCompressor(handle); }
        }

        public static Stream OpenPayload(Stream compressed)
        {
            if (compressed == null) throw new InvalidDataException("缺少内嵌运行组件。");
            byte[] input;
            using (compressed)
            {
                if (compressed.Length <= 0 || compressed.Length > MaximumPayloadSize) throw new InvalidDataException("组件大小无效。");
                input = new byte[(int)compressed.Length];
                int offset = 0;
                while (offset < input.Length)
                {
                    int read = compressed.Read(input, offset, input.Length - offset);
                    if (read == 0) throw new EndOfStreamException();
                    offset += read;
                }
            }
            IntPtr handle;
            if (!CreateDecompressor(Lzms, IntPtr.Zero, out handle)) throw new Win32Exception();
            try
            {
                UIntPtr size;
                if (!Decompress(handle, input, (UIntPtr)input.Length, null, UIntPtr.Zero, out size) && Marshal.GetLastWin32Error() != InsufficientBuffer)
                    throw new Win32Exception();
                byte[] output = new byte[CheckedSize(size)];
                if (!Decompress(handle, input, (UIntPtr)input.Length, output, (UIntPtr)output.Length, out size)) throw new Win32Exception();
                return new MemoryStream(output, 0, CheckedSize(size), false, false);
            }
            finally { CloseDecompressor(handle); }
        }

        private static int CheckedSize(UIntPtr size)
        {
            ulong value = size.ToUInt64();
            if (value == 0 || value > MaximumPayloadSize) throw new InvalidDataException("组件大小超过允许范围。");
            return (int)value;
        }
    }
}
