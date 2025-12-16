using System.Collections;
using System.Diagnostics;
using IBM.WMQ;
using StampedeLoadTester.Models.CommandLineOptions;
using StampedeLoadTester.Services;
using Tresvi.CommandParser;
using Tresvi.CommandParser.Exceptions;
using System.Net;
using System.Threading;
using LoadTester.Plugins;

namespace StampedeLoadTester
{
    internal class Program
    {
        const string OUTPUT_QUEUE = "BNA.XX1.PEDIDO";
        const string MENSAJE = "    00000008500000020251118115559N0001   000000PC  01100500000000000000                        00307384";
        const int TIEMPO_CARGA_MS = 2000;
        const string IP_MQ_SERVER = "192.168.0.31";//"10.6.248.10"; //"192.168.1.37"; //"192.168.0.15";
        const string MANAGER_NAME = "MQGD";
        
static readonly List<Hashtable> _connectionProperties = new List<Hashtable>
{
    new Hashtable
    {
        { MQC.HOST_NAME_PROPERTY, IP_MQ_SERVER },
        { MQC.PORT_PROPERTY, 1414 },
        { MQC.CHANNEL_PROPERTY, "CHANNEL1" },
    },
    new Hashtable
    {
        { MQC.HOST_NAME_PROPERTY, IP_MQ_SERVER },
        { MQC.PORT_PROPERTY, 1414 },
        { MQC.CHANNEL_PROPERTY, "CHANNEL1" },
    },
    new Hashtable
    {
        { MQC.HOST_NAME_PROPERTY, IP_MQ_SERVER },
        { MQC.PORT_PROPERTY, 1414 },
        { MQC.CHANNEL_PROPERTY, "CHANNEL1" },
    }
};

        static async Task Main(string[] args)
        {
            //var testDefinition = JsonSerializer.Deserialize<TestDefinition>(File.ReadAllText("test-definition.json"));  
            object verb = CommandLine.Parse(args, typeof(MasterVerb), typeof(SlaveVerb));

            if (verb is MasterVerb masterVerb)
            {
                await RunAsMaster(masterVerb);
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
/*
            using TestManager manager = new(MANAGER_NAME, OUTPUT_QUEUE, MENSAJE, _connectionProperties);
            manager.InicializarConexiones();

            using MQQueue inquireQueue = manager.AbrirQueueInquire();
            int profundidad = inquireQueue.CurrentDepth;

            Console.WriteLine("Iniciando...");
*/
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


        private static async Task RunAsMaster(MasterVerb masterVerb)
        {
            Console.WriteLine($"Archivo: {masterVerb.File}");
            Console.WriteLine($"Slaves: {string.Join(", ", masterVerb.Slaves)}");
            Console.WriteLine($"SlaveTimeout: {masterVerb.SlaveTimeout}");
            Console.WriteLine($"ThreadNumber: {masterVerb.ThreadNumber}");

            RemoteControllerService remoteController = new RemoteControllerService();
            IReadOnlyList<IPAddress> ipSlaves;
            using TestManager testManager = new("MQGD", OUTPUT_QUEUE, MENSAJE, _connectionProperties);
            
            CancellationTokenSource? monitorProfCts = null;
            Task<Dictionary<int, int>>? taskMonitor = null;
            
            try
            {
                Console.Write("Inicializando conexiones MQ en el master...");
                testManager.InicializarConexiones();
                Console.WriteLine(": OK");

                if (masterVerb.ClearQueue)
                {
                    Console.Write("Vaciando cola de salida...");
                    float msjesEliminadosPorSegundo = testManager.VaciarCola(OUTPUT_QUEUE);
                    Console.WriteLine($": OK ({msjesEliminadosPorSegundo:F2} msjes/s)");
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("Realizando WarmUp en el master...");
                testManager.EnviarMensajesPrueba();
                Console.WriteLine(": OK");


                ipSlaves = masterVerb.GetSlaves();
                if (ipSlaves.Count != 0)
                {
                    Console.WriteLine("\n\n***********Verificando acceso a instancias en modo slave***********\n");

                    bool allPingsOk = WaitForSlavesPing(remoteController, ipSlaves, masterVerb.SlavePort, masterVerb.SlaveTimeout);
                    if (!allPingsOk) throw new Exception("No se han podido contactar a todos los esclavos");

                    bool allConnectionsOk = InitializeSlavesConnections(remoteController, ipSlaves, masterVerb.SlavePort, masterVerb.SlaveTimeout);
                    if (!allConnectionsOk) throw new Exception("No se han podido inicializar todas las conexiones MQ en los esclavos");

                    bool allWarmUpsOk = DoWarmUpSlaves(remoteController, ipSlaves, masterVerb.SlavePort, masterVerb.SlaveTimeout);
                    if (!allWarmUpsOk) throw new Exception("No se ha podido realizar warmup en todas las conexiones MQ de los esclavos");

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Ejecutando Test de carga en los esclavos...");
                    ExecuteRemoteTestsAsync(remoteController, ipSlaves, masterVerb.SlavePort, masterVerb.SlaveTimeout);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR: {ex}");
                return;
            }

            Console.WriteLine("Iniciando monitoreo de profundidad de la cola...");
            monitorProfCts = new CancellationTokenSource();    
            taskMonitor = testManager.MonitorearProfundidadColaAsync(OUTPUT_QUEUE, monitorProfCts.Token);

            Console.ForegroundColor = ConsoleColor.Green;
            int numHilos = masterVerb.ThreadNumber;
            Console.WriteLine($"Ejecutando test de carga en el master a {numHilos} hilos...");
            int nroMensajesColocados = ExecuteWriteQueueTest(testManager, numHilos);
            Console.ResetColor();

            // Detener el monitoreo inmediatamente después del test (antes de obtener resultados de slaves)
            Dictionary<int, int> profundidades = new Dictionary<int, int>();
            if (monitorProfCts != null && taskMonitor != null)
            {
                monitorProfCts.Cancel();
                profundidades = await taskMonitor;
                Console.WriteLine($"Se realizaron {profundidades.Count} lecturas");
                // Ejemplo de uso: convertir milisegundos a hora
                foreach (var kvp in profundidades)
                {
                    TimeSpan tiempo = TimeSpan.FromMilliseconds(kvp.Key);
                    Console.WriteLine($"Tiempo: {tiempo.TotalSeconds:F2}s - Profundidad: {kvp.Value}");
                }
            }

            List<(int? result, string ipSlave)> resultados = await GetSlavesResultsAsync(remoteController, ipSlaves, masterVerb.SlavePort, masterVerb.SlaveTimeout);
            PrintResults(resultados, nroMensajesColocados);


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
            remoteManager.Listen(slaveVerb.Port, cts.Token, "MQGD", OUTPUT_QUEUE, MENSAJE, _connectionProperties);
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
        private static bool InitializeSlavesConnections(RemoteControllerService remoteController, IReadOnlyList<IPAddress> ipSlaves, int slavePort, int slaveTimeout)
        {
            bool allSlavesInitialized = true;

            foreach (IPAddress ip in ipSlaves)
            {
                Console.Write($"Inicializando conexiones MQ en slave {ip}:{slavePort}...");
                try
                {
                    bool success = remoteController.SendInitConCommandAsync(ip, slavePort, TimeSpan.FromSeconds(slaveTimeout)).GetAwaiter().GetResult();

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

        /// <summary>
        /// Ejecuta el warmup en todos los esclavos remotos
        /// </summary>
        /// <param name="remoteController">Servicio de control remoto</param>
        /// <param name="ipSlaves">Lista de IPs de los esclavos</param>
        /// <param name="slavePort">Puerto donde están escuchando los esclavos</param>
        /// <param name="slaveTimeout">Timeout para la respuesta de los esclavos</param>
        private static bool DoWarmUpSlaves(RemoteControllerService remoteController, IReadOnlyList<IPAddress> ipSlaves, int slavePort, int slaveTimeout)
        {
            bool allSlavesWarmedUp = true;

            foreach (IPAddress ip in ipSlaves)
            {
                Console.Write($"Ejecutando warmup en slave {ip}:{slavePort}...");
                try
                {
                    bool success = remoteController.SendWarmUpCommandAsync(ip, slavePort, TimeSpan.FromSeconds(slaveTimeout)).GetAwaiter().GetResult();

                    if (success)
                        Console.WriteLine($": OK");
                    else
                        Console.WriteLine($": ERROR - El esclavo respondió con ERROR o timeout");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($": ERROR {ex.Message}");
                    allSlavesWarmedUp = false;
                }
            }
            return allSlavesWarmedUp;
        }


        /// <summary>
        /// Obtiene los resultados (mensajes colocados) de todos los esclavos en paralelo
        /// </summary>
        /// <param name="remoteController">Servicio de control remoto</param>
        /// <param name="ipSlaves">Lista de IPs de los esclavos</param>
        /// <param name="slavePort">Puerto donde están escuchando los esclavos</param>
        /// <param name="slaveTimeout">Timeout para la respuesta de los esclavos</param>
        /// <returns>Lista de tuplas con el resultado (int?) y la IP del esclavo (string). null si no se pudo obtener el resultado</returns>
        private static async Task<List<(int? result, string ipSlave)>> GetSlavesResultsAsync(
            RemoteControllerService remoteController, 
            IReadOnlyList<IPAddress> ipSlaves,
            int slavePort, 
            int slaveTimeout)
        {
            var tasks = ipSlaves.Select(async ip =>
            {
                try
                {
                    int? result = await remoteController.SendGetLastResultCommandAsync(ip, slavePort, TimeSpan.FromSeconds(slaveTimeout));
                    return (result, ip.ToString());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error obteniendo resultado de {ip}:{slavePort}: {ex.Message}");
                    return ((int?)null, ip.ToString());
                }
            });

            var results = await Task.WhenAll(tasks);
            return results.ToList();
        }

        private static void ExecuteRemoteTestsAsync(RemoteControllerService remoteController, IReadOnlyList<IPAddress> ipSlaves, 
            int slavePort, int slaveTimeout)
        {
            // Iniciar todas las tareas en paralelo sin esperarlas (fire-and-forget)
            foreach (IPAddress ip in ipSlaves)
            {
                // Capturar la IP en una variable local para evitar problemas de closure
                IPAddress slaveIp = ip;
                
                // Iniciar la tarea directamente sin Task.Run, ya que SendStartCommandAsync es async I/O
                _ = Task.Run(async () =>
                {
                    Console.WriteLine($"Iniciando test en slave {slaveIp}:{slavePort}");
                    try
                    {
                        bool success = await remoteController.SendStartCommandAsync(slaveIp, slavePort, TimeSpan.FromSeconds(slaveTimeout));
                        
                        if (success)
                            Console.WriteLine($"Test en slave {slaveIp}:{slavePort} iniciado: OK");
                        else
                            Console.WriteLine($"Test en slave {slaveIp}:{slavePort} iniciado: ERROR");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"ERROR al iniciar Test en slave {slaveIp}:{slavePort}: {ex.Message}");
                    }
                });
            }
        }


        private static int ExecuteWriteQueueTest(TestManager manager, int numHilos)
        {
            TimeSpan duracionEnsayo = TimeSpan.FromMilliseconds(TIEMPO_CARGA_MS);
            return manager.EjecutarWriteQueueLoadTest(duracionEnsayo, numHilos);
        }


        private static void PrintResults(List<(int? result, string ipSlave)> slaveResults, int masterResult)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n-----------------RESULTADOS-----------------");

            int sumaSlaves = 0;
            foreach (var slaveResult in slaveResults)
            {
                int nroMensajes = slaveResult.result ?? 0;
                sumaSlaves += nroMensajes;
                //Console.WriteLine($"Mensajes colocados por el esclavo {slaveResult.ipSlave}: {nroMensajes}");
                Console.WriteLine($"Mensajes colocados por el esclavo {slaveResult.ipSlave}: ".PadRight(60) + nroMensajes);
            }

            Console.WriteLine($"Mensajes colocados por maestro: ".PadRight(60) + masterResult);
            Console.WriteLine($"______".PadLeft(66));
            Console.WriteLine($"Total de mensajes colocados: ".PadRight(60) + (masterResult + sumaSlaves));
            Console.ResetColor();
        }

 
    }
}
