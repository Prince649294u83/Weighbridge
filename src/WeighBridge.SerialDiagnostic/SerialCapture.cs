using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace WeighBridge.SerialDiagnostic
{
    public class SerialCapture : IDisposable
    {
        private readonly FileStream _binStream;
        private readonly StreamWriter _txtWriter;
        private readonly DateTime _startTime;

        public SerialCapture(string prefix, SerialDiagnosticOptions options)
        {
            _startTime = DateTime.Now;
            string timestamp = _startTime.ToString("yyyy-MM-dd_HHmm");
            string basePath = $"{prefix}_{timestamp}";
            
            _binStream = new FileStream($"{basePath}.bin", FileMode.Create, FileAccess.Write, FileShare.Read);
            _txtWriter = new StreamWriter($"{basePath}.txt", false, Encoding.UTF8);

            WriteTextHeader(options);
        }

        private void WriteTextHeader(SerialDiagnosticOptions options)
        {
            _txtWriter.WriteLine("====================================================");
            _txtWriter.WriteLine(" WeighBridge Serial Diagnostic");
            _txtWriter.WriteLine("====================================================");
            _txtWriter.WriteLine($"Timestamp : {_startTime:yyyy-MM-dd HH:mm:ss.fff}");
            _txtWriter.WriteLine($"Port      : {options.Port}");
            _txtWriter.WriteLine($"Baud      : {options.Baud}");
            _txtWriter.WriteLine($"DataBits  : {options.DataBits}");
            _txtWriter.WriteLine($"Parity    : {options.Parity}");
            _txtWriter.WriteLine($"StopBits  : {options.StopBits}");
            _txtWriter.WriteLine($"Handshake : {options.Handshake}");
            _txtWriter.WriteLine($"DTR       : {options.Dtr}");
            _txtWriter.WriteLine($"RTS       : {options.Rts}");
            _txtWriter.WriteLine($"Mode      : {(options.Parser ? "RAW + PARSER" : "RAW")}");
            _txtWriter.WriteLine($"Duration  : {(options.Duration > 0 ? options.Duration + " seconds" : "Infinite")}");
            _txtWriter.WriteLine("----------------------------------------------------\n");
            _txtWriter.Flush();
        }

        public async Task LogChunkAsync(byte[] buffer, int offset, int count, int msSinceLastRx, int totalNulls, int totalBytes)
        {
            // Write raw binary
            await _binStream.WriteAsync(buffer.AsMemory(offset, count));
            await _binStream.FlushAsync();

            // Format HEX and ASCII
            var hex = BitConverter.ToString(buffer, offset, count).Replace("-", " ");
            
            var asciiBuilder = new StringBuilder(count);
            for (int i = 0; i < count; i++)
            {
                char c = (char)buffer[offset + i];
                if (char.IsControl(c) || c > 127)
                    asciiBuilder.Append('.');
                else
                    asciiBuilder.Append(c);
            }

            // Write to Text file
            await _txtWriter.WriteLineAsync($"[{DateTime.Now:HH:mm:ss.fff}] RX CHUNK: {count} bytes | {msSinceLastRx} ms since previous RX");
            await _txtWriter.WriteLineAsync($"HEX   : {hex}");
            await _txtWriter.WriteLineAsync($"ASCII : {asciiBuilder}");
            
            double nullRatio = totalBytes > 0 ? (double)totalNulls / totalBytes * 100.0 : 0;
            if (nullRatio > 90.0 && count > 10)
            {
                await _txtWriter.WriteLineAsync($"WARNING : Input stream is dominated by 0x00 bytes. (Null ratio: {nullRatio:F2}%)");
            }
            
            await _txtWriter.WriteLineAsync();
            await _txtWriter.FlushAsync();
        }

        public async Task LogMessageAsync(string message)
        {
            await _txtWriter.WriteLineAsync($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
            await _txtWriter.FlushAsync();
        }

        public void Dispose()
        {
            _binStream.Dispose();
            _txtWriter.Dispose();
        }
    }
}
