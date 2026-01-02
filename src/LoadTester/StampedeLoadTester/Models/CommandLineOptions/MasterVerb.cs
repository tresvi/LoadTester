using System;
using System.Net;
using Tresvi.CommandParser.Attributes.Validation;
using Tresvi.CommandParser.Attributtes.Keywords;

namespace StampedeLoadTester.Models.CommandLineOptions;

[Verb("master", "Ejecuta o coordina el ensayo de carga", true)]
public class MasterVerb
{
    private const int SLAVE_TIMEOUT_DEFAULT = 5;
    private const string SLAVE_PORT_DEFAULT = "8888";

    [FileExists()]
    [Option("file", 'f', true, "Archivo con las transacciones a eviar")]
    public string? File { get; set; }

    [Option("slaves", 's', false, "Lista de IPs de los esclavos separados por punto y coma (1.1.1.1;2.2.2.2;3.3.3.3)")]
    public string Slaves { get; set; } = "";

    [Option("slavePort", 'p', false, "Puerto donde escucharán los esclavos. Todos los esclavos deberán usar el mismo. Default: " + SLAVE_PORT_DEFAULT )]
    public int SlavePort { get; set; } = int.Parse(SLAVE_PORT_DEFAULT);

    [Option("slaveTimeout", 'T', false, "Timeout de espera para que los esclavos respondan al maestro (en segundos). Si se omite, se usará el valor por defecto")]
    public int SlaveTimeout { get; set; } = SLAVE_TIMEOUT_DEFAULT;

    [Option("threadNumber", 't', false, "Número de hilos para el test de carga. Si se omite, se usará el nro de Threads de la CPU")]
    public int ThreadNumber { get; set; } = Environment.ProcessorCount;
    
    [Option("mqConnection", 'm', true, "Cadena que representa los parametros de conexion al servidor MQ con la siguiente estructura: MQServerIp:Port:Channel:ManagerName. Ej: 192.168.0.31;1414;CHANNEL1;MQGD ")]
    public string MqConnection { get; set; } = "";

    [Option("duration", 'd', true, "Duracion de prueba en segundos.")]
    public int Duration {get; set;}

    [Option("IputQueue", 'i', true, "Cola de entrada para recibir los mensajes.")]
    public string InputQueue { get; set;} = "";

    [Option("OutputQueue", 'o', true, "Cola de salida para enviar los mensajes.")]
    public string OutputQueue { get; set;} = "";

    [Option("rateLimitDelay", 'r', false, "Delay intencional en microsegundos entre cada mensaje enviado. Actúa como lastre para controlar la tasa de envío y ralentizar la ejecución. Default: 0 (sin delay)")]
    public int RateLimitDelay {get; set;} = 0;

    internal IReadOnlyList<IPAddress> GetSlaves()
    {
        try
        {	
            if (string.IsNullOrEmpty(Slaves)) return Array.Empty<IPAddress>();
            return Slaves.Split(';').Select(slave => IPAddress.Parse(slave.Trim())).ToList();
        }
        catch (Exception ex)
        {
            throw new Exception($"Error al parsear las IPs de los esclavos {ex.Message}");
        }
    }


}
