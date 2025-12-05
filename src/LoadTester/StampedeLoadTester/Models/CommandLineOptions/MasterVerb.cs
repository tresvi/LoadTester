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
    public List<string> Slaves { get; set; } = new List<string>();

    [Option("slavePort", 'p', false, "Puerto donde escucharán los esclavos. Todos los esclavos deberán usar el mismo. Default: " + SLAVE_PORT_DEFAULT )]
    public int SlavePort { get; set; } = int.Parse(SLAVE_PORT_DEFAULT);

    [Option("slaveTimeout", 'T', false, "Timeout de espera para que los esclavos respondan al maestro (en segundos). Si se omite, se usará el valor por defecto")]
    public int SlaveTimeout { get; set; } = SLAVE_TIMEOUT_DEFAULT;

    [Option("threadNumber", 't', false, "Número de hilos para el test de carga. Si se omite, se usará el nro de Threads de la CPU")]
    public int? ThreadNumber { get; set; } = Environment.ProcessorCount;


    internal List<IPAddress> GetSlaves()
    {
        return Slaves.Select(slave => IPAddress.Parse(slave)).ToList();
    }
}
