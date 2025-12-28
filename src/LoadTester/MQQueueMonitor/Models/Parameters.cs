using Tresvi.CommandParser.Attributtes.Keywords;

namespace MQQueueMonitor.Models;

[Verb("monitor", "Monitorea la profundidad de una cola MQ", true)]
public class CliParameters
{
    [Option("mqConnection", 'm', true, "Cadena que representa los parametros de conexion al servidor MQ conla siguiente estructura: MQServerIp:Port:Channel:ManagerName. Ej: 192.168.0.31:1414:CHANNEL1:MQGD ")]
    public string MqConnection { get; set; } = "";

    [Option("queues", 'q', true, "Nombre de la/las colas MQ a monitorear. Pueden colocarse hasta 2 colas si se separan los nombres por comas. Ej: 'BNA.CU1.PEDIDO, BNA.CU1.RESPUESTA'")]
    public string QueuesNames { get; set; } = "";

    [Option("refreshInterval", 'r', false, "Tiempo de refresco en milisegundos. Valor por defecto 100, mínimo: 10")]
    public int RefreshInterval { get; set; } = 100;
}

