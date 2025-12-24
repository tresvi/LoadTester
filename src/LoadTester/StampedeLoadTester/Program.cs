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
using MathNet.Numerics.Statistics;
using StampedeLoadTester.Models;

namespace StampedeLoadTester
{
    //dotnet run -- -f "xxx" -s "192.168.0.31, 192.168.0.24" -p 8888 -c -m "192.168.0.31:1414:CHANNEL1:MQGD" -d 2 -i "BNA.XX1.RESPUESTA" -o "BNA.XX1.PEDIDO"
    //dotnet run -- -f "xxx" -c -m "10.6.248.10:1414:CHANNEL1:MQGD" -d 2 -i "BNA.CU1.RESPUESTA" -o "BNA.CU1.PEDIDO"
    //dotnet run -- -f "xxx" -c -m "10.6.248.10:1414:CHANNEL1:MQGD" -d 2 -i "BNA.CU2.RESPUESTA" -o "BNA.CU2.PEDIDO"
    internal class Program
    {
        //const string MENSAJE = "    00000008500000020251118115559N0001   000000PC  01100500000000000000                        00307384";
        const string MENSAJE = "    00000008500000020251118114435G00111  000000DGPC011005590074200180963317";
        /// <summary>
        /// Crea una lista de Hashtables con las propiedades de conexión MQ basadas en MqConnectionParams
        /// Genera 3 entradas (para soportar hasta 4 hilos de conexión en TestManager)
        /// </summary>
        private static List<Hashtable> CreateConnectionProperties(MqConnectionParams mqParams)
        {
            return new List<Hashtable>
            {
                new Hashtable
                {
                    { MQC.HOST_NAME_PROPERTY, mqParams.MqServerIp },
                    { MQC.PORT_PROPERTY, mqParams.MqServerPort },
                    { MQC.CHANNEL_PROPERTY, mqParams.MqServerChannel },
                },
                new Hashtable
                {
                    { MQC.HOST_NAME_PROPERTY, mqParams.MqServerIp },
                    { MQC.PORT_PROPERTY, mqParams.MqServerPort },
                    { MQC.CHANNEL_PROPERTY, mqParams.MqServerChannel },
                },
                new Hashtable
                {
                    { MQC.HOST_NAME_PROPERTY, mqParams.MqServerIp },
                    { MQC.PORT_PROPERTY, mqParams.MqServerPort },
                    { MQC.CHANNEL_PROPERTY, mqParams.MqServerChannel },
                }
            };
        }

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
        }


        private static async Task RunAsMaster(MasterVerb masterVerb)
        {
            Console.WriteLine($"Archivo: {masterVerb.File}");
            Console.WriteLine($"Slaves: {string.Join(", ", masterVerb.Slaves)}");
            Console.WriteLine($"SlaveTimeout: {masterVerb.SlaveTimeout}");
            Console.WriteLine($"ThreadNumber: {masterVerb.ThreadNumber}");
            Console.WriteLine($"MQConnection: {masterVerb.MqConnection}");

            IReadOnlyList<IPAddress> ipSlaves;
            MqConnectionParams mqConnParams = new();

            try
            {
                ipSlaves = masterVerb.GetSlaves();
                mqConnParams.LoadMqConnectionParams(masterVerb.MqConnection, masterVerb.OutputQueue, masterVerb.InputQueue);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR validando parametros de entrada: {ex.Message}");
                return;
            }

            RemoteControllerService remoteController = new();
            List<Hashtable> connectionProperties = CreateConnectionProperties(mqConnParams);
            using TestManager testManager = new(mqConnParams.MqManagerName, mqConnParams.OutputQueue, MENSAJE, connectionProperties);
            
            CancellationTokenSource? monitorProfCts = null;
            Task<Dictionary<int, int>>? taskMonitor = null;
            
            try
            {
                Console.Write("Inicializando conexiones MQ en el master...");
                testManager.InicializarConexiones();
                Console.WriteLine(": OK");

                if (masterVerb.ClearQueue)
                {
                    float msjesEliminadosPorSegundo = 0;
                    Console.Write("Vaciando cola de pedido...");
                    msjesEliminadosPorSegundo = testManager.VaciarCola(mqConnParams.OutputQueue);
                    Console.WriteLine($": OK ({msjesEliminadosPorSegundo:F2} msjes/s)");

                    Console.Write("Vaciando cola de respuesta...");
                    msjesEliminadosPorSegundo = testManager.VaciarCola(mqConnParams.InputQueue);
                    Console.WriteLine($": OK ({msjesEliminadosPorSegundo:F2} msjes/s)");
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("Realizando WarmUp en el master...");
                testManager.EnviarMensajesPrueba();
                Console.WriteLine(": OK");

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
            taskMonitor = testManager.MonitorearProfundidadColaAsync(mqConnParams.OutputQueue, monitorProfCts.Token);

            Console.ForegroundColor = ConsoleColor.Green;
            int numHilos = masterVerb.ThreadNumber;
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.ff} - Ejecutando test de carga en el master a {numHilos} hilos, aguarde {masterVerb.Duration} segundos...");
            int nroMensajesColocados = ExecuteWriteQueueTest(testManager, numHilos, masterVerb.Duration * 1000);
            Console.ResetColor();

            // Detener el monitoreo inmediatamente después del test (antes de obtener resultados de slaves)
            Dictionary<int, int> profundidades = [];
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
            
            Console.Write($"\nEsperando a que la cola {mqConnParams.OutputQueue} procese todos los mensajes...");
            testManager.WaitForQueueEmptied(mqConnParams.OutputQueue);
            Console.WriteLine($": OK");
            Console.WriteLine($"Recibiendo respuestas y actualizando put date time...");
            testManager.RecibirRespuestasYActualizarPutDateTime(testManager.MensajesEnviados!, mqConnParams.InputQueue);
            PrintMessagesResults2(testManager.MensajesEnviados);

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
            
            MqConnectionParams mqConnectionParams = new();
            try
            {
                mqConnectionParams.LoadMqConnectionParams(slaveVerb.MqConnection, slaveVerb.OutputQueue, slaveVerb.InputQueue);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR validando parámetros de conexión MQ: {ex.Message}");
                return;
            }
            
            List<Hashtable> connectionProperties = CreateConnectionProperties(mqConnectionParams);
            var remoteManager = new RemoteControllerService();
            var cts = new CancellationTokenSource();
            remoteManager.Listen(slaveVerb.Port, cts.Token, mqConnectionParams.MqManagerName, mqConnectionParams.OutputQueue, MENSAJE, connectionProperties);
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


        private static int ExecuteWriteQueueTest(TestManager manager, int numHilos, int durationMs)
        {
            TimeSpan duracionEnsayo = TimeSpan.FromMilliseconds(durationMs);
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

        private static void PrintMessagesResults(List<TestManager.MensajeEnviado>[]? mensajesEnviados)
        {
            if (mensajesEnviados == null)
            {
                Console.WriteLine("No hay mensajes para imprimir.");
                return;
            }

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("\n-----------------MENSAJES ENVIADOS-----------------");
            int contadorMsjes = 0;

            for (int hiloIndex = 0; hiloIndex < mensajesEnviados.Length; hiloIndex++)
            {
                var listaMensajes = mensajesEnviados[hiloIndex];
                
                if (listaMensajes == null || listaMensajes.Count == 0)
                {
                    Console.WriteLine($"Hilo {hiloIndex}: Sin mensajes");
                    continue;
                }

                foreach (var mensaje in listaMensajes)
                {
                    // Convertir MessageId a string hexadecimal
                    string messageIdHex = mensaje.MessageId != null 
                        ? BitConverter.ToString(mensaje.MessageId).Replace("-", "") 
                        : "N/A";
                    
                    // Calcular diferencia en milisegundos entre RequestPutDateTime y ResponsePutDateTime
                    string diferenciaMs;
                    if (mensaje.ResponsePutDateTime == default(DateTime))
                    {
                        diferenciaMs = "N/A";
                    }
                    else
                    {
                        double diferencia = (mensaje.ResponsePutDateTime - mensaje.RequestPutDateTime).TotalMilliseconds;
                        diferenciaMs = $"{diferencia} ms";
                    }
                    
                    Console.WriteLine($"Hilo {hiloIndex} - NroMsje {++contadorMsjes} - {mensaje.RequestPutDateTime:yyyy-MM-dd HH:mm:ss.fff} - Diferencia: {diferenciaMs} - MessageId: {messageIdHex}");
                }
            }
            
            Console.ResetColor();
        }


        private static void PrintMessagesResults2(List<TestManager.MensajeEnviado>[]? mensajesEnviados)
        {
            if (mensajesEnviados == null)
            {
                Console.WriteLine("No hay mensajes para imprimir.");
                return;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n================== ESTADÍSTICAS DE LATENCIA ==================");
            
            // Recolectar todas las diferencias válidas y encontrar tiempos primero/último
            List<double> diferenciasMs = new List<double>();
            int totalMensajes = 0;
            int mensajesConRespuesta = 0;
            int mensajesSinRespuesta = 0;
            DateTime? primerMensaje = null;
            DateTime? ultimoMensaje = null;

            for (int hiloIndex = 0; hiloIndex < mensajesEnviados.Length; hiloIndex++)
            {
                var listaMensajes = mensajesEnviados[hiloIndex];
                
                if (listaMensajes == null || listaMensajes.Count == 0)
                    continue;

                foreach (var mensaje in listaMensajes)
                {
                    totalMensajes++;
                    
                    // Trackear primer y último mensaje para calcular throughput
                    if (!primerMensaje.HasValue || mensaje.RequestPutDateTime < primerMensaje.Value)
                        primerMensaje = mensaje.RequestPutDateTime;
                    if (!ultimoMensaje.HasValue || mensaje.RequestPutDateTime > ultimoMensaje.Value)
                        ultimoMensaje = mensaje.RequestPutDateTime;
                    
                    if (mensaje.ResponsePutDateTime == default(DateTime))
                    {
                        mensajesSinRespuesta++;
                    }
                    else
                    {
                        mensajesConRespuesta++;
                        double diferencia = (mensaje.ResponsePutDateTime - mensaje.RequestPutDateTime).TotalMilliseconds;
                        diferenciasMs.Add(diferencia);
                    }
                }
            }

            Console.WriteLine($"Total de mensajes enviados: {totalMensajes}");
            Console.WriteLine($"Mensajes con respuesta: {mensajesConRespuesta}");
            Console.WriteLine($"Mensajes sin respuesta: {mensajesSinRespuesta}");
            
            // Calcular throughput (mensajes por segundo)
            if (primerMensaje.HasValue && ultimoMensaje.HasValue && totalMensajes > 0)
            {
                double tiempoTotalSegundos = (ultimoMensaje.Value - primerMensaje.Value).TotalSeconds;
                if (tiempoTotalSegundos > 0)
                {
                    double throughput = totalMensajes / tiempoTotalSegundos;
                    Console.WriteLine($"\n-------------------------- Throughput -------------------------");
                    Console.WriteLine($"Tiempo total de envío:   {tiempoTotalSegundos:F2} s");
                    Console.WriteLine($"Throughput:              {throughput:F2} mensajes/segundo");
                    
                    // Throughput de respuestas recibidas
                    if (mensajesConRespuesta > 0)
                    {
                        double throughputRespuestas = mensajesConRespuesta / tiempoTotalSegundos;
                        Console.WriteLine($"Throughput respuestas:   {throughputRespuestas:F2} respuestas/segundo");
                    }
                }
            }

            if (diferenciasMs.Count == 0)
            {
                Console.WriteLine("⚠️  No hay mensajes con respuesta para calcular estadísticas.");
                Console.ResetColor();
                return;
            }

            var estadisticas = new DescriptiveStatistics(diferenciasMs);
            
            double promedio = estadisticas.Mean;
            double desviacionEstandar = estadisticas.StandardDeviation;
            double minimo = estadisticas.Minimum;
            double maximo = estadisticas.Maximum;
            
            // Percentiles usando MathNet.Numerics (métodos estáticos de Statistics)
            double p25 = Statistics.Percentile(diferenciasMs, 25);
            double p50 = Statistics.Median(diferenciasMs); // Mediana es igual P50)
            double p75 = Statistics.Percentile(diferenciasMs, 75);
            double p95 = Statistics.Percentile(diferenciasMs, 95);
            double p99 = Statistics.Percentile(diferenciasMs, 99);

            Console.WriteLine("\n----- Latencia (RequestPutDateTime → ResponsePutDateTime) -----");
            Console.WriteLine($"Promedio:              {promedio:F2} ms");
            Console.WriteLine($"Desviación estándar:   {desviacionEstandar:F2} ms");
            Console.WriteLine($"Mínimo:                {minimo:F2} ms");
            Console.WriteLine($"Máximo:                {maximo:F2} ms");

            Console.WriteLine($"\n------------------------ Percentiles -------------------------");
            Console.WriteLine($"P25:                   {p25:F2} ms");
            Console.WriteLine($"P50 (Mediana):         {p50:F2} ms");
            Console.WriteLine($"P75:                   {p75:F2} ms");
            Console.WriteLine($"P95:                   {p95:F2} ms");
            Console.WriteLine($"P99:                   {p99:F2} ms");

            // Estadística adicional: Coeficiente de variación
            double coeficienteVariacion = promedio > 0 ? (desviacionEstandar / promedio) * 100 : 0;
            Console.WriteLine($"\n------------------------ Variabilidad ------------------------");
            Console.WriteLine($"Coeficiente de variación: {coeficienteVariacion:F2}%");
            
            if (coeficienteVariacion < 15)
                Console.WriteLine("  → Baja variabilidad (proceso consistente)");
            else if (coeficienteVariacion < 35)
                Console.WriteLine("  → Variabilidad moderada");
            else
                Console.WriteLine("  → Alta variabilidad (proceso inconsistente)");

            // Estadística adicional: Tasa de éxito
            double tasaExito = totalMensajes > 0 ? (mensajesConRespuesta / (double)totalMensajes) * 100 : 0;
            Console.WriteLine($"\n----------------------- Tasa de Éxito ------------------------");
            Console.WriteLine($"Tasa de respuestas recibidas: {tasaExito:F2}%");

            Console.WriteLine("==============================================================\n");
            Console.ResetColor();
        }
 
    }
}
