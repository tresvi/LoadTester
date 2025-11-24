using System.Collections;
using System.Diagnostics;
using IBM.WMQ;

namespace StampedeLoadTester
{
    internal class Program
    {
        const string OUTPUT_QUEUE = "BNA.XX1.PEDIDO";
        const string MENSAJE = "    00000008500000020251118115559N0001   000000PC  01100500000000000000                        00307384";
        const int TIEMPO_CARGA_MS = 1000;

        static void Main(string[] args)
        {
            Console.WriteLine("Iniciando...");

            var properties = new Hashtable
            {
                //{ MQC.HOST_NAME_PROPERTY, "10.6.248.10" },
                { MQC.HOST_NAME_PROPERTY, "192.168.0.15" },
                { MQC.PORT_PROPERTY, 1415 },
                { MQC.CHANNEL_PROPERTY, "CHANNEL1" },
            };

            using TestManager manager = new("MQGD", OUTPUT_QUEUE, MENSAJE, properties);
            manager.InicializarConexiones();

            using MQQueue inquireQueue = manager.AbrirQueueInquire();
            int profundidad = inquireQueue.CurrentDepth;


            manager.EnviarMensajesPrueba();

            int numHilos = Environment.ProcessorCount;
            Console.WriteLine($"Número de hilos: {numHilos}");
            Console.WriteLine($"Profundidad inicial: {profundidad}");

            TimeSpan duracionEnsayo = TimeSpan.FromMilliseconds(TIEMPO_CARGA_MS);
            int messageCounter = manager.EjecutarHilosCarga(duracionEnsayo, numHilos);
            Console.WriteLine($"FIN: Msjes colocados: {messageCounter}");
        }
    }
}
