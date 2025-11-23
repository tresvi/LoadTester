using System.Collections;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using IBM.WMQ;
using LoadTester.Plugins;

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
                { MQC.HOST_NAME_PROPERTY, "192.168.0.15" },
                //{ MQC.HOST_NAME_PROPERTY, "10.6.248.10" },
                { MQC.PORT_PROPERTY, 1414 },
                { MQC.CHANNEL_PROPERTY, "CHANNEL1" },/*
                { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_CLIENT } // conexión remota tipo cliente
               */
            };

            // Crear dos conexiones: una para hilos pares y otra para hilos impares
            MQQueueManager qmgr1 = null;
            MQQueueManager qmgr2 = null;
            MQQueue outputQueue1 = null;
            MQQueue outputQueue2 = null;

            try
            {
                // Conexión 1 (para hilos pares: 0, 2, 4, ...)
                qmgr1 = new MQQueueManager("MQGD", properties);
                outputQueue1 = IbmMQPlugin.OpenOutputQueue(qmgr1, OUTPUT_QUEUE, false);
                Console.WriteLine("Conexión 1 establecida");

                // Conexión 2 (para hilos impares: 1, 3, 5, ...)
                qmgr2 = new MQQueueManager("MQGD", properties);
                outputQueue2 = IbmMQPlugin.OpenOutputQueue(qmgr2, OUTPUT_QUEUE, false);
                Console.WriteLine("Conexión 2 establecida");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR: al conectarse al manager {ex.Message}", ex);
                Environment.Exit(1);
            }

            // Consultar profundidad inicial usando la conexión 1
            MQQueue inquireQueue = IbmMQPlugin.OpenOutputQueue(qmgr1, OUTPUT_QUEUE, true);
            int profundidad = inquireQueue.CurrentDepth;
            inquireQueue.Close();
            long tiempoLimiteTicks = MillisecondsToTicks(TIEMPO_CARGA_MS);

            // Envíos de prueba
            IbmMQPlugin.EnviarMensaje(outputQueue1, MENSAJE);
            IbmMQPlugin.EnviarMensaje(outputQueue2, MENSAJE);
            IbmMQPlugin.EnviarMensaje(outputQueue1, MENSAJE);
            
            int messageCounter = 0;
            long tiempoFinalizacionTicks = Stopwatch.GetTimestamp() + tiempoLimiteTicks;
            long tiempoInicioTicks = Stopwatch.GetTimestamp();

            // Paralelizar el envío de mensajes usando Parallel.For
            // Hilos pares (0, 2, 4...) usan conexión 1
            // Hilos impares (1, 3, 5...) usan conexión 2
            int numHilos = Environment.ProcessorCount;
            Console.WriteLine($"Número de hilos: {numHilos}");
            Console.WriteLine($"Profundidad inicial: {profundidad}");

            Parallel.For(0, numHilos, (hiloIndex) =>
            {
                // Determinar qué conexión usar según si el hilo es par o impar
                MQQueue queueActual = (hiloIndex % 2 == 0) ? outputQueue1 : outputQueue2;
                int conexionNum = (hiloIndex % 2 == 0) ? 1 : 2;

                while (Stopwatch.GetTimestamp() < tiempoFinalizacionTicks)
                {
                    try
                    {
                        IbmMQPlugin.EnviarMensaje(queueActual, MENSAJE);
                        Interlocked.Increment(ref messageCounter);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error en hilo {hiloIndex} (conexión {conexionNum}): {ex.Message}");
                    }
                }
            });

            // Cerrar ambas conexiones
            outputQueue1?.Close();
            outputQueue2?.Close();
            qmgr1?.Close();
            qmgr2?.Close();
            Console.WriteLine($"FIN: Msjes colocados: {messageCounter}");
            Environment.Exit(0);
            
        }

        /// <summary>
        /// Convierte milisegundos a ticks
        /// </summary>
        /// <param name="milliseconds">Valor en milisegundos</param>
        /// <returns>Valor en ticks (1 milisegundo = 10,000 ticks)</returns>
        static long MillisecondsToTicks(double milliseconds)
        {
            return (long)(milliseconds * TimeSpan.TicksPerMillisecond);
        }

        /// <summary>
        /// Convierte ticks a milisegundos
        /// </summary>
        /// <param name="ticks">Valor en ticks</param>
        /// <returns>Valor en milisegundos (10,000 ticks = 1 milisegundo)</returns>
        static double TicksToMilliseconds(long ticks)
        {
            return (double)ticks / TimeSpan.TicksPerMillisecond;
        }
    }
}
