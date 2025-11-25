using System.Collections;
using System.Diagnostics;
using IBM.WMQ;

namespace StampedeLoadTester
{
    internal class Program
    {
        const string OUTPUT_QUEUE = "BNA.XX1.PEDIDO";
        const string MENSAJE = "    00000008500000020251118115559N0001   000000PC  01100500000000000000                        00307384";
        const int TIEMPO_CARGA_MS = 5000;

        static void Main(string[] args)
        {

            if (args.Length > 0)
            {
                var remoteManager = new RemoteManager();

                if (args[0] == "-s")
                {
                    // En un equipo (servidor)
                    Console.WriteLine("Iniciando servidor...");
                    var cts = new CancellationTokenSource();
                    remoteManager.Listen(8888, cts.Token);
                }
                else if (args[0] == "-c")
                {
                    // En otro equipo (cliente)
                    Console.WriteLine("Iniciando cliente...");
                    //TimeSpan? responseTime = remoteManager.Ping("192.168.0.15", 8888, TimeSpan.FromSeconds(5));
                    TimeSpan? responseTime = remoteManager.Ping("192.168.56.1", 8888, TimeSpan.FromSeconds(5));
                    if (responseTime.HasValue)
                    {
                        Console.WriteLine($"Tiempo de respuesta: {responseTime.Value.TotalMilliseconds} ms");
                    }
                }
                else
                {
                    Console.WriteLine("Argumento no reconocido. Se opera normalmente...");
                }
            }

            Console.WriteLine("Iniciando...");

            const string IP_MQ_SERVER = "192.168.1.37"; //"192.168.0.15";
            const string CHANNEL = "CHANNEL1";

            var properties1414 = new Hashtable
            {
                { MQC.HOST_NAME_PROPERTY, IP_MQ_SERVER },
                { MQC.PORT_PROPERTY, 1414 },
                { MQC.CHANNEL_PROPERTY, "CHANNEL1" },
            };

            var properties1415 = new Hashtable
            {
                { MQC.HOST_NAME_PROPERTY, IP_MQ_SERVER },
                { MQC.PORT_PROPERTY, 1415 },
                { MQC.CHANNEL_PROPERTY, "CHANNEL1" },
            };

            var properties1416 = new Hashtable
            {
                { MQC.HOST_NAME_PROPERTY, IP_MQ_SERVER },
                { MQC.PORT_PROPERTY, 1416 },
                { MQC.CHANNEL_PROPERTY, "CHANNEL1" },
            };

            using TestManager manager = new("MQGD", OUTPUT_QUEUE, MENSAJE, properties1414, properties1415, properties1416);
            manager.InicializarConexiones();

            using MQQueue inquireQueue = manager.AbrirQueueInquire();
            int profundidad = inquireQueue.CurrentDepth;

/*
int inquireCounter = 0;
            long tiempoLimiteinquire = (long)(TimeSpan.FromMilliseconds(10000).TotalSeconds * Stopwatch.Frequency);

            Parallel.For(0, 4, hiloIndex =>
            {
                long horaInicio = Stopwatch.GetTimestamp();
                long horaFin = horaInicio + tiempoLimiteinquire;

                while (Stopwatch.GetTimestamp() < horaFin)
                {
                    
                    int profundidad = inquireQueue.CurrentDepth;
                    Interlocked.Increment(ref inquireCounter);
                }

                double elapsedMs = (Stopwatch.GetTimestamp() - horaInicio) * 1000.0 / Stopwatch.Frequency;
                Console.WriteLine($"Hilo {hiloIndex} tardó {elapsedMs:F2} ms");
            });
            Console.WriteLine($"Msjes inquired: {inquireCounter}");
            return; //para probar el inquire
*/



            manager.EnviarMensajesPrueba();

            int numHilos = 4;//Environment.ProcessorCount;
            Console.WriteLine($"Número de hilos: {numHilos}");
            Console.WriteLine($"Profundidad inicial: {profundidad}");

            TimeSpan duracionEnsayo = TimeSpan.FromMilliseconds(TIEMPO_CARGA_MS);
            int messageCounter = manager.EjecutarHilosCarga(duracionEnsayo, numHilos);
            Console.WriteLine($"FIN: Msjes colocados: {messageCounter}");
        }
    }
}
