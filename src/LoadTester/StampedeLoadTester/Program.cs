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
                { MQC.PORT_PROPERTY, 1414 },
                { MQC.CHANNEL_PROPERTY, "CHANNEL1" },/*
                { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_CLIENT } // conexión remota tipo cliente
               */
            };

            MQQueueManager qmgr = null;
            MQQueue outputQueue = null;

            try
            {
                qmgr = new MQQueueManager("MQGD", properties);
                outputQueue = IbmMQPlugin.OpenOutputQueue(qmgr, OUTPUT_QUEUE, false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR: al conectarse al manager {ex.Message}", ex);
                Environment.Exit(1);
            }

            MQQueue inquireQueue = IbmMQPlugin.OpenOutputQueue(qmgr, OUTPUT_QUEUE, true);
            int profundidad = inquireQueue.CurrentDepth;
            long tiempoLimiteTicks = MillisecondsToTicks(TIEMPO_CARGA_MS);

            
            IbmMQPlugin.EnviarMensaje(outputQueue, MENSAJE);
            IbmMQPlugin.EnviarMensaje(outputQueue, MENSAJE);
            IbmMQPlugin.EnviarMensaje(outputQueue, MENSAJE);
            
            int messageCounter = 0;
            long tiempoFinalizacionTicks = Stopwatch.GetTimestamp() + tiempoLimiteTicks;
            long tiempoInicioTicks = Stopwatch.GetTimestamp();

            // Paralelizar el envío de mensajes usando Parallel.For
            // Cada hilo ejecutará el loop hasta que se cumpla el tiempo límite
            int numHilos = Environment.ProcessorCount;

/*            
            while (Stopwatch.GetTimestamp() < tiempoFinalizacionTicks)
            {
                IbmMQPlugin.EnviarMensaje(outputQueue, MENSAJE);
                messageCounter++;
            }
*/

            Parallel.For(0, numHilos, (hiloIndex) =>
            {
                while (Stopwatch.GetTimestamp() < tiempoFinalizacionTicks)
                {
                    try
                    {
                        IbmMQPlugin.EnviarMensaje(outputQueue, MENSAJE);
                        Interlocked.Increment(ref messageCounter);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error en hilo {hiloIndex}: {ex.Message}");
                    }
                }
            });

            outputQueue.Close();
            qmgr.Close();
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
