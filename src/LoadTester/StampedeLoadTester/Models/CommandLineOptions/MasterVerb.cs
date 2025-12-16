using System.Net;
using Tresvi.CommandParser.Attributtes.Keywords;

namespace StampedeLoadTester.Models.CommandLineOptions;

[Verb("master", "Ejecuta o coordina el ensayo de carga", true)]
public class MasterVerb
{
    private const int SLAVE_TIMEOUT_DEFAULT = 5;
    private const string SLAVE_PORT_DEFAULT = "8888";

    [Option("file", 'f', true, "Archivo con la descripcion del ensayo a realizar")]
    public string? File { get; set; }

    [Option("slaves", 's', false, "Lista de IPs de los esclavos separados por coma (1.1.1.1, 2.2.2.2, 3.3.3.3)")]
    public string Slaves { get; set; } = "";

    [Option("slavePort", 'p', false, "Puerto donde escucharán los esclavos. Todos los esclavos deberán usar el mismo. Default: " + SLAVE_PORT_DEFAULT )]
    public int SlavePort { get; set; } = int.Parse(SLAVE_PORT_DEFAULT);

    [Option("slaveTimeout", 'T', false, "Timeout de espera para que los esclavos respondan al maestro (en segundos). Si se omite, se usará el valor por defecto")]
    public int SlaveTimeout { get; set; } = SLAVE_TIMEOUT_DEFAULT;

    [Option("threadNumber", 't', false, "Número de hilos para el test de carga. Si se omite, se usará el nro de Threads de la CPU")]
    public int ThreadNumber { get; set; } = Environment.ProcessorCount;
    
    [Flag("clearQ", 'c', "Limpia la cola de salida antes de ejecutar el test de carga")]
    public bool ClearQueue { get; set; } = false;


    internal IReadOnlyList<IPAddress> GetSlaves()
    {
        try
        {	
            if (string.IsNullOrEmpty(Slaves)) return Array.Empty<IPAddress>();
            return Slaves.Split(',').Select(slave => IPAddress.Parse(slave.Trim())).ToList();
        }
        catch (Exception ex)
        {
            throw new Exception($"Error al parsear las IPs de los esclavos {ex.Message}");
        }
    }
}
