using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace WeighBridge.SerialDiagnostic
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("====================================================");
            Console.WriteLine(" WeighBridge Serial Diagnostic");
            Console.WriteLine("====================================================\n");

            var options = ParseArguments(args);

            if (options == null)
            {
                PrintHelp();
                return;
            }

            if (!string.IsNullOrEmpty(options.Replay))
            {
                // To be implemented: Replay mode
                Console.WriteLine($"Running in OFFLINE REPLAY mode using {options.Replay}");
                Console.WriteLine("To be implemented.");
            }
            else
            {
                if (string.IsNullOrEmpty(options.Port))
                {
                    Console.WriteLine("ERROR: --port is required for live capture mode.");
                    return;
                }

                await RunLiveCaptureAsync(options);
            }
        }

        private static async Task RunLiveCaptureAsync(SerialDiagnosticOptions options)
        {
            using var cts = new CancellationTokenSource();
            
            Console.CancelKeyPress += (s, e) =>
            {
                Console.WriteLine("\nCancellation requested...");
                e.Cancel = true; // Prevent process termination
                cts.Cancel();
            };

            if (options.Duration > 0)
            {
                cts.CancelAfter(TimeSpan.FromSeconds(options.Duration));
            }

            using var capture = new SerialCapture("SerialCapture", options);
            var receiver = new SerialReceiver(options, capture);

            Console.WriteLine($"Port      : {options.Port}");
            Console.WriteLine($"Baud      : {options.Baud}");
            Console.WriteLine($"Data Bits : {options.DataBits}");
            Console.WriteLine($"Parity    : {options.Parity}");
            Console.WriteLine($"Stop Bits : {options.StopBits}");
            Console.WriteLine($"Handshake : {options.Handshake}");
            Console.WriteLine($"DTR       : {options.Dtr}");
            Console.WriteLine($"RTS       : {options.Rts}");
            Console.WriteLine($"Duration  : {(options.Duration > 0 ? options.Duration + "s" : "Infinite")}");
            Console.WriteLine($"Mode      : {(options.Parser ? "RAW + PARSER" : "RAW")}\n");
            
            await receiver.RunLiveCaptureAsync(cts.Token);

            receiver.PrintTelemetry();
        }

        private static SerialDiagnosticOptions? ParseArguments(string[] args)
        {
            var options = new SerialDiagnosticOptions();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "--port": if (i + 1 < args.Length) options.Port = args[++i]; break;
                    case "--baud": if (i + 1 < args.Length && int.TryParse(args[++i], out int b)) options.Baud = b; break;
                    case "--databits": if (i + 1 < args.Length && int.TryParse(args[++i], out int db)) options.DataBits = db; break;
                    case "--parity": if (i + 1 < args.Length) options.Parity = args[++i]; break;
                    case "--stopbits": if (i + 1 < args.Length) options.StopBits = args[++i]; break;
                    case "--handshake": if (i + 1 < args.Length) options.Handshake = args[++i]; break;
                    case "--dtr": if (i + 1 < args.Length && bool.TryParse(args[++i], out bool dtr)) options.Dtr = dtr; break;
                    case "--rts": if (i + 1 < args.Length && bool.TryParse(args[++i], out bool rts)) options.Rts = rts; break;
                    case "--duration": if (i + 1 < args.Length && int.TryParse(args[++i], out int dur)) options.Duration = dur; break;
                    case "--replay": if (i + 1 < args.Length) options.Replay = args[++i]; break;
                    case "--parser": options.Parser = true; break;
                    case "--help": return null;
                }
            }
            return options;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  WeighBridge.SerialDiagnostic.exe --port COM3 --baud 2400 [options]");
            Console.WriteLine("\nOptions:");
            Console.WriteLine("  --port <string>       COM Port name (required unless --replay is used)");
            Console.WriteLine("  --baud <int>          Baud rate (default 9600)");
            Console.WriteLine("  --dataBits <int>      Data bits (default 8)");
            Console.WriteLine("  --parity <string>     Parity: None, Odd, Even, Mark, Space (default None)");
            Console.WriteLine("  --stopBits <string>   Stop bits: None, One, OnePointFive, Two (default One)");
            Console.WriteLine("  --handshake <string>  Handshake: None, RequestToSend, RequestToSendXOnXOff, XOnXOff (default None)");
            Console.WriteLine("  --dtr <bool>          DTR Enable (default false)");
            Console.WriteLine("  --rts <bool>          RTS Enable (default false)");
            Console.WriteLine("  --duration <int>      Capture duration in seconds (default 0 = infinite)");
            Console.WriteLine("  --replay <path>       Replay a previously captured .bin file instead of reading from a COM port");
            Console.WriteLine("  --parser              Enable parser mode to feed frames to the Weight parser");
        }
    }
}
