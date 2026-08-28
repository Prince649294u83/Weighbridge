using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WeighBridge.SerialDiagnostic
{
    public class SerialReceiver
    {
        private readonly SerialDiagnosticOptions _options;
        private readonly SerialCapture _capture;

        // Telemetry Counters
        public int TotalBytesReceived { get; private set; }
        public int TotalNullBytes { get; private set; }
        public int FramesExtracted { get; private set; }
        public int FramesParsed { get; private set; }
        public int FramesRejected { get; internal set; }
        public string LastWeight { get; internal set; } = "N/A";
        public DateTime? LastTimestamp { get; internal set; }

        public SerialCapture Capture => _capture;

        public SerialReceiver(SerialDiagnosticOptions options, SerialCapture capture)
        {
            _options = options;
            _capture = capture;
        }

        public async Task RunLiveCaptureAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_options.Port))
                throw new InvalidOperationException("Port is required.");

            using var port = new SerialPort
            {
                PortName = _options.Port,
                BaudRate = _options.Baud,
                DataBits = _options.DataBits,
                Parity = Enum.Parse<Parity>(_options.Parity, true),
                StopBits = Enum.Parse<StopBits>(_options.StopBits, true),
                Handshake = Enum.Parse<Handshake>(_options.Handshake, true),
                DtrEnable = _options.Dtr,
                RtsEnable = _options.Rts,
                ReadTimeout = 1000 // To allow cancellation checking
            };

            try
            {
                Console.WriteLine($"Opening port {port.PortName}...");
                port.Open();
                Console.WriteLine("CONNECTED\n");
                await _capture.LogMessageAsync("LEVEL 1: Port opened successfully.");

                Console.WriteLine("Listening for data...");
                Console.WriteLine("Press Q or Ctrl+C to stop.\n");

                var buffer = new byte[4096];
                var stopwatch = new Stopwatch();
                long lastRxTicks = 0;

                stopwatch.Start();

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        // Explicitly use BaseStream for raw reading
                        int count = await port.BaseStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                        if (count > 0)
                        {
                            long currentTicks = stopwatch.ElapsedMilliseconds;
                            int msSinceLastRx = lastRxTicks == 0 ? 0 : (int)(currentTicks - lastRxTicks);
                            lastRxTicks = currentTicks;

                            LastTimestamp = DateTime.Now;
                            TotalBytesReceived += count;

                            // Count nulls
                            for (int i = 0; i < count; i++)
                            {
                                if (buffer[i] == 0x00) TotalNullBytes++;
                            }

                            // If this is the first bytes we receive, log Level 2
                            if (TotalBytesReceived == count)
                            {
                                await _capture.LogMessageAsync("LEVEL 2: Bytes received.");
                            }

                            await ProcessChunkAsync(buffer, count, msSinceLastRx);
                        }
                    }
                    catch (TimeoutException)
                    {
                        // Expected if no data arrives within ReadTimeout. Loop continues.
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nERROR: {ex.Message}");
                await _capture.LogMessageAsync($"ERROR: {ex.Message}");
            }
            finally
            {
                if (port.IsOpen)
                {
                    port.Close();
                }
                Console.WriteLine("\nConnection Closed.");
            }
        }

        public async Task ProcessChunkAsync(byte[] buffer, int count, int msSinceLastRx)
        {
            // Format HEX and ASCII for Console
            var hex = BitConverter.ToString(buffer, 0, count).Replace("-", " ");
            var asciiBuilder = new StringBuilder(count);
            for (int i = 0; i < count; i++)
            {
                char c = (char)buffer[i];
                if (char.IsControl(c) || c > 127)
                    asciiBuilder.Append('.');
                else
                    asciiBuilder.Append(c);
            }

            // Print to Console
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] RX CHUNK: {count} bytes | {msSinceLastRx} ms since previous RX");
            Console.WriteLine($"HEX   : {hex}");
            Console.WriteLine($"ASCII : {asciiBuilder}");
            
            double nullRatio = TotalBytesReceived > 0 ? (double)TotalNullBytes / TotalBytesReceived * 100.0 : 0;
            if (nullRatio > 90.0 && count > 10)
            {
                Console.WriteLine($"WARNING: input stream is dominated by 0x00 bytes. (Null ratio: {nullRatio:F2}%)");
            }
            Console.WriteLine();

            // Log to Capture files
            await _capture.LogChunkAsync(buffer, 0, count, msSinceLastRx, TotalNullBytes, TotalBytesReceived);

            if (_options.Parser)
            {
                // To be implemented: Pipe to frame extractor and parser
                // For now, update counters dummy values or leave 0
            }
        }

        public void PrintTelemetry()
        {
            Console.WriteLine("----------------------------------------------------");
            Console.WriteLine($"Bytes received    : {TotalBytesReceived}");
            
            double nullRatio = TotalBytesReceived > 0 ? (double)TotalNullBytes / TotalBytesReceived * 100.0 : 0;
            Console.WriteLine($"Null bytes        : {TotalNullBytes}");
            Console.WriteLine($"Null ratio        : {nullRatio:F2}%");
            
            Console.WriteLine($"Frames extracted  : {FramesExtracted}");
            Console.WriteLine($"Frames parsed     : {FramesParsed}");
            Console.WriteLine($"Frames rejected   : {FramesRejected}");
            Console.WriteLine($"Last weight       : {LastWeight}");
            if (LastTimestamp.HasValue)
                Console.WriteLine($"Last timestamp    : {LastTimestamp.Value:HH:mm:ss.fff}");
            Console.WriteLine("----------------------------------------------------");
        }
    }
}
