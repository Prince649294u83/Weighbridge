using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WeighBridge.SerialDiagnostic
{
    public class OfflineReplayer
    {
        private readonly SerialDiagnosticOptions _options;
        private readonly SerialReceiver _receiver;

        public OfflineReplayer(SerialDiagnosticOptions options, SerialReceiver receiver)
        {
            _options = options;
            _receiver = receiver;
        }

        public async Task RunReplayAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_options.Replay) || !File.Exists(_options.Replay))
                throw new FileNotFoundException($"Replay file not found: {_options.Replay}");

            Console.WriteLine($"Starting OFFLINE REPLAY from {_options.Replay}...");
            await _receiver.Capture.LogMessageAsync($"LEVEL 1: Replay file opened successfully.");

            using var fileStream = new FileStream(_options.Replay, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[256]; // Read in chunks to simulate serial reception
            
            while (!cancellationToken.IsCancellationRequested)
            {
                int count = await fileStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (count == 0)
                {
                    Console.WriteLine("\nEnd of replay file reached.");
                    break;
                }

                // Simulate slight delay between chunks like a real serial port
                await Task.Delay(15, cancellationToken);
                
                await _receiver.ProcessChunkAsync(buffer, count, 15);
            }
        }
    }
}
