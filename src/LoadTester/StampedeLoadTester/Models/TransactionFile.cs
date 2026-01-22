using System.Text.Json.Serialization;
using System.Text.Json;

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
        public string[] Transacciones { get; set; } = [];


        /// <summary>
        /// Lee un archivo JSON y carga sus datos en la instancia actual.
        /// Compatible con AOT (Ahead-Of-Time compilation) usando source generators.
        /// </summary>
        /// <param name="filePath">Ruta del archivo JSON a leer</param>
        /// <exception cref="FileNotFoundException">Si el archivo no existe</exception>
        /// <exception cref="JsonException">Si el JSON no es válido o no coincide con el modelo</exception>
        public void Load(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"El archivo no existe: {filePath}");

            string jsonContent = File.ReadAllText(filePath);
            TransactionFile? loaded = JsonSerializer.Deserialize(jsonContent, TransactionFileJsonContext.Default.TransactionFile);
            
            if (loaded == null)
                throw new JsonException($"No se pudo deserializar el archivo JSON: {filePath}");

            // Cargar los datos en la instancia actual
            this.Version = loaded.Version;
            this.InputQueue = loaded.InputQueue;
            this.OutputQueue = loaded.OutputQueue;
            this.Transacciones = loaded.Transacciones;
        }
    }

    [JsonSourceGenerationOptions(WriteIndented = false)]
    [JsonSerializable(typeof(TransactionFile))]
    internal partial class TransactionFileJsonContext : JsonSerializerContext
    {
    }
}
