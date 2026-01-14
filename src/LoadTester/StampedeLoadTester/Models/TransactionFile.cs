using System.Text.Json.Serialization;

namespace StampedeLoadTester.Models
{
    internal class TransactionFile
    {
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("inputQueue")]
        public string InputQueue { get; set; } = string.Empty;

        [JsonPropertyName("outputQueue")]
        public string OutputQueue { get; set; } = string.Empty;

        [JsonPropertyName("transacciones")]
        public List<string> Transacciones { get; set; } = [];
    }
}
