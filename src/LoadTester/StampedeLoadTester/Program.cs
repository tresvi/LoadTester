using System.Collections;
using System.Diagnostics;
using IBM.WMQ;
using StampedeLoadTester.Models.CommandLineOptions;
using StampedeLoadTester.Services;
using Tresvi.CommandParser;
using Tresvi.CommandParser.Exceptions;
using System.Net;

namespace StampedeLoadTester
{
    internal class Program
    {
        const string OUTPUT_QUEUE = "BNA.XX1.PEDIDO";
        const string MENSAJE = "    00000008500000020251118115559N0001   000000PC  01100500000000000000                        00307384";
        const int TIEMPO_CARGA_MS = 2000;
        const string IP_MQ_SERVER = "192.168.0.31";//"10.6.248.10"; //"192.168.1.37"; //"192.168.0.15";
        const string CHANNEL = "CHANNEL1";
        
        static readonly Hashtable _properties1414 = new()
        {
            { MQC.HOST_NAME_PROPERTY, IP_MQ_SERVER },
            { MQC.PORT_PROPERTY, 1414 },
            { MQC.CHANNEL_PROPERTY, "CHANNEL1" },
        };

        static readonly Hashtable _properties1415 = new()
        {
            { MQC.HOST_NAME_PROPERTY, IP_MQ_SERVER },
            { MQC.PORT_PROPERTY, /*1415 */ 1414 },
            { MQC.CHANNEL_PROPERTY, "CHANNEL1" },
        };

        static readonly Hashtable _properties1416 = new()
        {
            { MQC.HOST_NAME_PROPERTY, IP_MQ_SERVER },
            { MQC.PORT_PROPERTY, /*1416*/ 1414 },
            { MQC.CHANNEL_PROPERTY, "CHANNEL1" },
        };

        static void Main(string[] args)
        {
            //var testDefinition = JsonSerializer.Deserialize<TestDefinition>(File.ReadAllText("test-definition.json"));  
            object verb = CommandLine.Parse(args, typeof(MasterVerb), typeof(SlaveVerb));

            if (verb is MasterVerb masterVerb)
            {
                RunAsMaster(masterVerb);
            }
            else if (verb is SlaveVerb slaveVerb)
            {
                RunAsSlave(slaveVerb);
            }
            else
            {
                Console.WriteLine("Verbo no reconocido. Se opera normalmente...");
            }

//return;

            using TestManager manager = new("MQGD", OUTPUT_QUEUE, MENSAJE, _properties1414, _properties1415, _properties1416);
            manager.InicializarConexiones();

            using MQQueue inquireQueue = manager.AbrirQueueInquire();
            int profundidad = inquireQueue.CurrentDepth;


            Console.WriteLine("Iniciando...");

/*

*/
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



/*
            manager.EnviarMensajesPrueba();


            TimeSpan duracionEnsayo = TimeSpan.FromMilliseconds(TIEMPO_CARGA_MS);
            int messageCounter = manager.EjecutarHilosCarga(duracionEnsayo, numHilos);
            Console.WriteLine($"FIN: Msjes colocados: {messageCounter}");
            */
        }


        private static void RunAsMaster(MasterVerb masterVerb)
        {
            Console.WriteLine($"Archivo: {masterVerb.File}");
            Console.WriteLine($"Slaves: {string.Join(", ", masterVerb.Slaves)}");
            Console.WriteLine($"SlaveTimeout: {masterVerb.SlaveTimeout}");
            Console.WriteLine($"ThreadNumber: {masterVerb.ThreadNumber}");

            RemoteControllerService remoteController = new RemoteControllerService();
            IReadOnlyList<IPAddress> ipSlaves;

            try
            {
                ipSlaves = masterVerb.GetSlaves();
                if (ipSlaves.Count != 0)
                {
                    Console.WriteLine("\n\n***********Verificando acceso a instancias en modo slave***********\n");

                    bool allPingsOk = WaitForSlavesPing(remoteController, ipSlaves, masterVerb.SlavePort, masterVerb.SlaveTimeout);
                    if (!allPingsOk) throw new Exception("No se han podido contactar a todos los esclavos");

                    bool allConnectionsOk = InitializeRemoteConnections(remoteController, ipSlaves, masterVerb.SlavePort, masterVerb.SlaveTimeout);
                    if (!allConnectionsOk) throw new Exception("No se han podido inicializar todas las conexiones MQ en los esclavos");

                    //bool allSlavesStartedOk = InitializeRemoteConnections(remoteController, ipSlaves, masterVerb.SlavePort, masterVerb.SlaveTimeout);
                    //if (!allSlavesStartedOk) throw new Exception("No se han podido inicializar todas las conexiones MQ en los esclavos");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR: {ex}");
                return;
            }

            Console.Write("Inicializando conexiones MQ en el master...: ");
            using TestManager manager = new("MQGD", OUTPUT_QUEUE, MENSAJE, _properties1414, _properties1415, _properties1416);
            manager.InicializarConexiones();
            Console.WriteLine("OK");

            Console.Write("Abriendo queue inquire en el master...: ");
            using MQQueue inquireQueue = manager.AbrirQueueInquire();
            int profundidad = inquireQueue.CurrentDepth;
            Console.WriteLine($"OK, profundidad: {profundidad}");


            int numHilos = masterVerb.ThreadNumber ?? 6;//Environment.ProcessorCount;
            Console.WriteLine($"Número de hilos: {numHilos}");

            ExecuteWriteQueueTest(manager, numHilos);
            
            //Sincronizar relojes de los esclavos
            /*
            try
            {
                SyncSlavesClocks(remoteController, ipSlaves, masterVerb.SlavePort, masterVerb.SlaveTimeout);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error al sincronizar los relojes de los esclavos: {ex.Message}");
                throw new Exception($"Error al sincronizar los relojes de los esclavos: {ex.Message}", ex);
            }
            */
        }

        private static void RunAsSlave(SlaveVerb slaveVerb)
        {
            Console.WriteLine($"Iniciando en modo esclavo, escuchando en puerto {slaveVerb.Port}...");
            var remoteManager = new RemoteControllerService();
            var cts = new CancellationTokenSource();
            remoteManager.Listen(slaveVerb.Port, cts.Token);
        }


    /// <summary>
    /// Recorre todas las IPs de los esclavos y espera a que respondan a un ping
    /// </summary>
    /// <param name="remoteController"></param>
    /// <param name="ipSlaves"></param>
    /// <param name="slavePort"></param>
    /// <param name="slaveTimeout"></param>
        private static bool WaitForSlavesPing(RemoteControllerService remoteController, IReadOnlyList<IPAddress> ipSlaves, int slavePort, int slaveTimeout)
        {   
            bool allSlavesResponded = true;

            foreach (IPAddress ip in ipSlaves)
            {
                Console.Write($"Verificando slave {ip}:{slavePort}:...");
                try
                {
                    TimeSpan responseTime = remoteController.Ping(ip, slavePort, TimeSpan.FromSeconds(slaveTimeout));
                    Console.WriteLine($": OK en {responseTime.TotalMilliseconds} ms");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($": ERROR {ex.Message}");
                    allSlavesResponded = false;
                }
            }

            return allSlavesResponded;
        }

        /// <summary>
        /// Inicializa las conexiones MQ en todos los esclavos remotos
        /// </summary>
        /// <param name="remoteController">Servicio de control remoto</param>
        /// <param name="ipSlaves">Lista de IPs de los esclavos</param>
        /// <param name="slavePort">Puerto donde están escuchando los esclavos</param>
        /// <param name="slaveTimeout">Timeout para la respuesta de los esclavos</param>
        private static bool InitializeRemoteConnections(RemoteControllerService remoteController, IReadOnlyList<IPAddress> ipSlaves, int slavePort, int slaveTimeout)
        {
            bool allSlavesInitialized = true;

            foreach (IPAddress ip in ipSlaves)
            {
                Console.Write($"Inicializando conexiones MQ en slave {ip}:{slavePort}:...");
                try
                {
                    bool success = remoteController.SendInitConCommand(ip, slavePort, TimeSpan.FromSeconds(slaveTimeout));
                    if (success)
                        Console.WriteLine($": OK");
                    else
                        Console.WriteLine($": ERROR - El esclavo respondió con ERROR o timeout");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($": ERROR {ex.Message}");
                    allSlavesInitialized = false;
                }
            }
            return allSlavesInitialized;
        }


        private static void ExecuteWriteQueueTest(TestManager manager, int numHilos)
        {
            manager.EnviarMensajesPrueba();

            TimeSpan duracionEnsayo = TimeSpan.FromMilliseconds(TIEMPO_CARGA_MS);
            int messageCounter = manager.EjecutarHilosCarga(duracionEnsayo, numHilos);
            Console.WriteLine($"FIN: Msjes colocados: {messageCounter}");
        }
    }
}
