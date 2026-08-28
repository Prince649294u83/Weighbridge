using System;

namespace WeighBridge.SerialDiagnostic
{
    public class SerialDiagnosticOptions
    {
        public string? Port { get; set; }
        public int Baud { get; set; } = 9600;
        public int DataBits { get; set; } = 8;
        public string Parity { get; set; } = "None";
        public string StopBits { get; set; } = "One";
        public string Handshake { get; set; } = "None";
        public bool Dtr { get; set; } = false;
        public bool Rts { get; set; } = false;
        public int Duration { get; set; } = 0; // 0 means run until manually stopped
        public string? Replay { get; set; } // Path to .bin file
        public bool Parser { get; set; } = false; // Run parser mode
    }
}
