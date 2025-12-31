using Tresvi.CommandParser.Attributes.Validation;
using Tresvi.CommandParser.Attributtes.Keywords;

namespace MainframeSimulator.Models
{
    public class CliParameters
    {
        [Option("mqConnection", 'm', true, "Cadena que representa los parametros de conexion al servidor MQ conla siguiente estructura: MQServerIp;Port;Channel;ManagerName. Ej: 192.168.0.31;1414;CHANNEL1;MQGD ")]
        public string MqConnection { get; set; } = string.Empty;

        [Option("delay", 'd', false, "Retardo intencional en milisegundos que se le interpondra a la respuesta. (Opcional, si no se especifica, será 0 milisegundos)")]
        public int Delay { get; set; } = 0;

        [Option("input", 'i', true, "Cola de entrada donde se escucharán las peticiones")]
        public string? InputQueue { get; set; }

        [Option("output", 'o', true, "Cola de salida en donde el programa escribirá la salida")]
        public string? OutputQueue { get; set; }

        [Flag("verbose", 'v', "Modo verbose: imprime mensajes cuando se reciben o envían mensajes")]
        public bool Verbose { get; set; } = false;

        [Option("threadNumber", 't', false, "Número de threads a ejecutar el proceso. (Opcional, si no se especifica, será el valor de Environment.ProcessorCount)")]
        public int ThreadNumber { get; set; } = Environment.ProcessorCount;

        [Option("executionMode", 'e', false, "Modo de operación: 'echo': responde un echo de la solicitud, 'flush': simplemente vacía la cola de entrada sin nada")]
        [EnumeratedValidation(["echo", "flush"])]
        public string ExecutionMode { get; set; } = "echo";
    }
}

