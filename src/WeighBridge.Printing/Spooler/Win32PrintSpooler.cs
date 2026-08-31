using System.Runtime.InteropServices;

namespace WeighBridge.Printing.Spooler;

/// <summary>
/// Safe Windows Print Spooler P/Invoke wrapper for raw ESC/POS and text submission.
/// </summary>
public static class Win32PrintSpooler
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct DOCINFO
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pDocName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pOutputFile;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string pDataType;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool OpenPrinter(string pPrinterName, out nint phPrinter, nint pDefault);

    [DllImport("winspool.drv", EntryPoint = "ClosePrinter", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClosePrinter(nint hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int StartDocPrinter(nint hPrinter, int level, ref DOCINFO pDocInfo);

    [DllImport("winspool.drv", EntryPoint = "EndDocPrinter", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EndDocPrinter(nint hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartPagePrinter", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool StartPagePrinter(nint hPrinter);

    [DllImport("winspool.drv", EntryPoint = "EndPagePrinter", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EndPagePrinter(nint hPrinter);

    [DllImport("winspool.drv", EntryPoint = "WritePrinter", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WritePrinter(nint hPrinter, nint pBytes, int dwCount, out int dwWritten);

    /// <summary>
    /// Sends a raw byte buffer directly to the specified Windows printer spooler.
    /// </summary>
    /// <param name="printerName">Name of the target Windows printer.</param>
    /// <param name="documentName">Title of the print job in the spooler queue.</param>
    /// <param name="bytes">Raw byte payload (e.g. ESC/POS or ASCII stream).</param>
    /// <returns>True if the job was successfully spooled.</returns>
    public static bool SendBytesToPrinter(string printerName, string documentName, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerName);
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length == 0) return true;

        if (!OpenPrinter(printerName, out nint hPrinter, 0))
        {
            int errorCode = Marshal.GetLastPInvokeError();
            throw new InvalidOperationException($"Failed to open printer '{printerName}'. Win32 error: {errorCode}");
        }

        try
        {
            var di = new DOCINFO
            {
                pDocName = documentName,
                pOutputFile = null,
                pDataType = "RAW"
            };

            int jobId = StartDocPrinter(hPrinter, 1, ref di);
            if (jobId == 0)
            {
                int errorCode = Marshal.GetLastPInvokeError();
                throw new InvalidOperationException($"Failed to start print document for '{printerName}'. Win32 error: {errorCode}");
            }

            try
            {
                if (!StartPagePrinter(hPrinter))
                {
                    int errorCode = Marshal.GetLastPInvokeError();
                    throw new InvalidOperationException($"Failed to start print page for '{printerName}'. Win32 error: {errorCode}");
                }

                try
                {
                    nint pUnmanagedBytes = Marshal.AllocCoTaskMem(bytes.Length);
                    try
                    {
                        Marshal.Copy(bytes, 0, pUnmanagedBytes, bytes.Length);
                        if (!WritePrinter(hPrinter, pUnmanagedBytes, bytes.Length, out int bytesWritten) || bytesWritten != bytes.Length)
                        {
                            int errorCode = Marshal.GetLastPInvokeError();
                            throw new InvalidOperationException($"Failed to write raw bytes to '{printerName}'. Win32 error: {errorCode}");
                        }
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(pUnmanagedBytes);
                    }
                }
                finally
                {
                    EndPagePrinter(hPrinter);
                }
            }
            finally
            {
                EndDocPrinter(hPrinter);
            }

            return true;
        }
        finally
        {
            ClosePrinter(hPrinter);
        }
    }
}
