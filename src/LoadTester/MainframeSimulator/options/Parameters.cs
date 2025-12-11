using Tresvi.CommandParser.Attributes.Validation;
using Tresvi.CommandParser.Attributtes.Keywords;

namespace MainframeSimulator.Options
{
    public class Parameters
    {
        [IPValidation]
        [Option("server", 's', true, "IP del servidor de colas MQ")]
        public string? Server { get; set; }

        [Option("manager", 'm', true, "Nombre del manager MQ")]
        public string? Manager { get; set; }

        [Option("port", 'p', true, "Puerto del listener MQ")]
        public int Port { get; set; }

        [Option("channel", 'c', true, "Nombre del canal MQ")]
        public string? Channel { get; set; }

        [Option("delay", 'd', false, "Retardo intencional en milisegundos que se le interpondra a la respuesta. (Opcional, si no se especifica, será 0 milisegundos)")] 
        public int Delay { get; set; } = 0;

        [Option("input", 'i', true, "Cola de entrada donde se escucharán las peticiones")]
        public string? InputQueue { get; set; }

        [Option("output", 'o', true, "Cola de salida en donde el programa escribirá la salida")]
        public string? OutputQueue { get; set; }

        [Flag("quiet", 'q', "Modo silencioso: no imprime mensajes cuando se reciben o envían mensajes")]
        public bool Quiet { get; set; } = false;
    }
}

