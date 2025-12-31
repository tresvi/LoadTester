using Tresvi.CommandParser.Attributtes.Keywords;


namespace StampedeLoadTester.Models.CommandLineOptions;

[Verb("slave", "Queda a la espera de la señal del amo para ejecutar el ensayo de carga que este le solicite", false)]
internal class SlaveVerb
{
    private const string SLAVE_PORT_DEFAULT = "8888";

    [Option("port", 'p', false, "Puerto para donde el esclavo se quedará escuchando al maestro. Default: + " + SLAVE_PORT_DEFAULT)]
    public int Port { get; set; } = int.Parse(SLAVE_PORT_DEFAULT);


    [Option("threadNumber", 't', false, "Número de hilos para el test de carga. Si se omite, se usará el nro de Threads de la CPU")]
    public int? ThreadNumber { get; set; } = Environment.ProcessorCount;

    [Option("mqConnection", 'm', true, "Cadena que representa los parametros de conexion al servidor MQ conla siguiente estructura: MQServerIp:Port:Channel:ManagerName. Ej: 192.168.0.31:1414:CHANNEL1:MQGD ")]
    public string MqConnection { get; set; } = "";

    [Option("InputQueue", 'i', true, "Cola de entrada para recibir los mensajes.")]
    public string InputQueue { get; set;} = "";

    [Option("OutputQueue", 'o', true, "Cola de salida para enviar los mensajes.")]
    public string OutputQueue { get; set;} = "";

    [Option("rateLimitDelay", 'r', false, "Delay intencional en microsegundos entre cada mensaje enviado. Actúa como lastre para controlar la tasa de envío y ralentizar la ejecución. Default: 0 (sin delay)")]
    public int RateLimitDelay {get; set;} = 0;

}

