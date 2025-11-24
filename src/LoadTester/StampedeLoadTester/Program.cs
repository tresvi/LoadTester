using System.Collections;
using System.Diagnostics;
using IBM.WMQ;
using LoadTester.Plugins;

namespace StampedeLoadTester
{
    internal class Program
    {
        const string OUTPUT_QUEUE = "BNA.XX1.PEDIDO";
        const string MENSAJE = "    00000008500000020251118115559N0001   000000PC  01100500000000000000                        00307384";
        const int TIEMPO_CARGA_MS = 1000;

        static Hashtable? _properties;
        static MQQueueManager? _qmgr1;
        static MQQueueManager? _qmgr2;
        static MQQueueManager? _qmgr3;
        static MQQueueManager? _qmgr4;
        static MQQueue? _outputQueue1;
        static MQQueue? _outputQueue2;
        static MQQueue? _outputQueue3;
        static MQQueue? _outputQueue4;

        static void Main(string[] args)
        {
            Console.WriteLine("Iniciando...");

            InicializarConexiones();

            // Consultar profundidad inicial usando la conexión 1
            MQQueue inquireQueue = IbmMQPlugin.OpenOutputQueue(_qmgr1!, OUTPUT_QUEUE, true);
            int profundidad = inquireQueue.CurrentDepth;
            inquireQueue.Close();
            long tiempoLimiteTicks = MillisecondsToTicks(TIEMPO_CARGA_MS);

            // Envíos de prueba
            IbmMQPlugin.EnviarMensaje(_outputQueue1!, MENSAJE);
            IbmMQPlugin.EnviarMensaje(_outputQueue2!, MENSAJE);
            IbmMQPlugin.EnviarMensaje(_outputQueue3!, MENSAJE);
            IbmMQPlugin.EnviarMensaje(_outputQueue4!, MENSAJE);
            


            int numHilos = Environment.ProcessorCount;
            Console.WriteLine($"Número de hilos: {numHilos}");
            Console.WriteLine($"Profundidad inicial: {profundidad}");

            int messageCounter = EjecutarHilosCarga(tiempoLimiteTicks, numHilos);

            // Cerrar las cuatro conexiones
            CerrarConexiones();
            Console.WriteLine($"FIN: Msjes colocados: {messageCounter}");
            Environment.Exit(0);
            
        }

        static int EjecutarHilosCarga(long tiempoLimiteTicks, int numHilos)
        {
            int messageCounter = 0;

            Parallel.For(0, numHilos, hiloIndex =>
            {
                // Determinar qué conexión usar según hiloIndex % 4
                MQQueue queueActual = (hiloIndex % 4) switch
                {
                    0 => _outputQueue1!,
                    1 => _outputQueue2!,
                    2 => _outputQueue3!,
                    _ => _outputQueue4!,
                };

                long tiempoInicioTicks = Stopwatch.GetTimestamp();
                long tiempoFinalizacionTicks = tiempoInicioTicks + tiempoLimiteTicks;
                while (Stopwatch.GetTimestamp() < tiempoFinalizacionTicks)
                {
                  //  try
                    {
                        IbmMQPlugin.EnviarMensaje(queueActual, MENSAJE);
                        Interlocked.Increment(ref messageCounter);
                    }
                  /*  catch (Exception ex)
                    {
                        Console.WriteLine($"Error en hilo {hiloIndex} (conexión {conexionNum}): {ex.Message}");
                    }
                    */
                }
                long elapsedTicks = Stopwatch.GetTimestamp() - tiempoInicioTicks;
                double elapsedMs = elapsedTicks * 1000.0 / Stopwatch.Frequency;
                Console.WriteLine($"Hilo {hiloIndex} tardó {elapsedMs} ms");
            });

            return messageCounter;
        }


        static void InicializarConexiones()
        {
            _properties ??= new Hashtable
            {
                //{ MQC.HOST_NAME_PROPERTY, "10.6.248.10" },
                { MQC.HOST_NAME_PROPERTY, "192.168.0.15" },
                { MQC.PORT_PROPERTY, 1415 },
                { MQC.CHANNEL_PROPERTY, "CHANNEL1" },
            };

            try
            {
                _qmgr1 = new MQQueueManager("MQGD", _properties);
                _outputQueue1 = IbmMQPlugin.OpenOutputQueue(_qmgr1, OUTPUT_QUEUE, false);
                Console.WriteLine("Conexión 1 establecida");

                _qmgr2 = new MQQueueManager("MQGD", _properties);
                _outputQueue2 = IbmMQPlugin.OpenOutputQueue(_qmgr2, OUTPUT_QUEUE, false);
                Console.WriteLine("Conexión 2 establecida");

                _qmgr3 = new MQQueueManager("MQGD", _properties);
                _outputQueue3 = IbmMQPlugin.OpenOutputQueue(_qmgr3, OUTPUT_QUEUE, false);
                Console.WriteLine("Conexión 3 establecida");

                _qmgr4 = new MQQueueManager("MQGD", _properties);
                _outputQueue4 = IbmMQPlugin.OpenOutputQueue(_qmgr4, OUTPUT_QUEUE, false);
                Console.WriteLine("Conexión 4 establecida");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR: al conectarse al manager {ex.Message}", ex);
                CerrarConexiones();
                Environment.Exit(1);
            }
        }

        static void CerrarConexiones()
        {
            void Cerrar(MQQueueManager? qmgr, MQQueue? queue, int idx)
            {
                try
                {
                    queue?.Close();
                    qmgr?.Close();
                    Console.WriteLine($"Conexión {idx} cerrada");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al cerrar conexión {idx}: {ex.Message}");
                }
            }

            Cerrar(_qmgr1, _outputQueue1, 1);
            Cerrar(_qmgr2, _outputQueue2, 2);
            Cerrar(_qmgr3, _outputQueue3, 3);
            Cerrar(_qmgr4, _outputQueue4, 4);
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
