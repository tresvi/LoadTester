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
using Tresvi.CommandParser.Attributtes.Keywords;

namespace StampedeLoadTester
{
    //dotnet run -- -f "xxx" -s "192.168.0.31, 192.168.0.24" -p 8888 -m "192.168.0.31:1414:CHANNEL1:MQGD" -d 2 -i "BNA.XX1.RESPUESTA" -o "BNA.XX1.PEDIDO"
    //dotnet run -- -f "xxx" -m "10.6.248.10:1414:CHANNEL1:MQGD" -d 2 -i "BNA.CU1.RESPUESTA" -o "BNA.CU1.PEDIDO"
    //dotnet run -- -f "xxx" -m "10.6.248.10:1414:CHANNEL1:MQGD" -d 2 -i "BNA.CU2.RESPUESTA" -o "BNA.CU2.PEDIDO"
    //TODO: Cuando se alcanza a ver la cola de msjes de Respuesta vacios, dejar de intentar recuperar las respuestas, porque todos van a dar MQRC_NO_MSG_AVAILABLE eternamente
    //TODO: Hacer que el ensayo se detenga por cola llena y continue con el siguiente punto
    //TODO: Evaluar si la funcion ClearQueue es necesaria, ya que siempre deberia vaciarlas
    //TODO: Implementar lectura de trxs desde archivos.
    //TODO: Implementar salida de trxs a archivos
    //TODO: Hacer algun programa de analisis de resultados
    internal class Program
    {
        //const string MENSAJE = "    00000008500000020251118115559N0001   000000PC  01100500000000000000                        00307384";
        //const string MENSAJE = "    00000008500000020251118114435G00111  000000DGPC011005590074200180963317";

        const string MENSAJE = "    00000777700000020251118114435%XXXXXX%000000  BD011005590074200180963317";
        
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

            object verb;
            try 
            {
                verb = CommandLine.Parse(args, typeof(MasterVerb), typeof(SlaveVerb));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR validando parametros de entrada: {ex}");
                return;
            }
            
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
                Console.Error.WriteLine($"ERROR: Verbo no implementado");
                return;
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
                if (masterVerb.Duration < 0)
                    throw new Exception($"La duracion del ensayo no puede ser negativa. Valor: ({masterVerb.Duration})");

                if (masterVerb.RateLimitDelay < 0)
                    throw new Exception($"El delay de lastre no puede ser negativo. Valor: ({masterVerb.RateLimitDelay})");

                if (masterVerb.ThreadNumber > Environment.ProcessorCount)
                    throw new Exception($"El nro de hilos ({masterVerb.ThreadNumber}) no puede ser mayor al nro de CPUs ({Environment.ProcessorCount})");
                
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

                Console.Write("Vaciando cola de pedido...");
                float msjesEliminadosPorSegundo = testManager.VaciarCola(mqConnParams.OutputQueue);
                Console.WriteLine($": OK ({msjesEliminadosPorSegundo:F2} msjes/s)");

                Console.Write("Vaciando cola de respuesta...");
                msjesEliminadosPorSegundo = testManager.VaciarCola(mqConnParams.InputQueue);
                Console.WriteLine($": OK ({msjesEliminadosPorSegundo:F2} msjes/s)");

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
            (int nroMensajesColocados, bool colaLlena) = ExecuteWriteQueueTest(testManager, numHilos, masterVerb.Duration * 1000, masterVerb.RateLimitDelay);
            if (colaLlena) Console.WriteLine($"Se alcanzó la capacidad de la cola de entrada. Se detiene la inyección de mensajes");
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

            Console.Write($"\nEsperando a que se procesen todos los mensajes de la cola {mqConnParams.OutputQueue} ...");
            List<(DateTime hora, int profundidad)> medicionesProfundidad = [];
            testManager.WaitForQueueEmptied(mqConnParams.OutputQueue, measurements: out medicionesProfundidad);
            Console.WriteLine($": OK");

            PrintQueueStatistics(medicionesProfundidad);
            
            Console.WriteLine($"Recibiendo respuestas y actualizando put date time...");

            // Juntar todas las listas en una sola y ordenarla por RequestPutDateTime, para maximizar la probabilidad de obtener 
            //las respuestas en orden y evitar lo mas posible que se venza la respuesta en la cola de respuestas dando un MQRC_NO_MSG_AVAILABLE
            List<MensajeEnviado> mensajesEnviadosOrdenados = testManager.MensajesEnviados!
                .Where(lista => lista != null)
                .SelectMany(lista => lista)
                .OrderBy(m => m.RequestPutDateTime)
                .ToList();

            testManager.RecibirRespuestasYActualizarPutDateTime(mensajesEnviadosOrdenados, mqConnParams.InputQueue);
            
            PrintMessagesResults2(mensajesEnviadosOrdenados);

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
                if (slaveVerb.RateLimitDelay < 0)
                    throw new Exception($"El delay de lastre no puede ser negativo. Valor: ({slaveVerb.RateLimitDelay})");
                
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


        private static (int messageCounter, bool colaLlena) ExecuteWriteQueueTest(TestManager manager, int numHilos, int durationMs, int delayMicroseconds)
        {
            TimeSpan duracionEnsayo = TimeSpan.FromMilliseconds(durationMs);
            return manager.EjecutarWriteQueueLoadTest(duracionEnsayo, numHilos, delayMicroseconds);
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


        private static void PrintMessagesResults2(List<MensajeEnviado>? mensajesEnviados)
        {
            if (mensajesEnviados == null || mensajesEnviados.Count == 0)
            {
                Console.WriteLine("No hay mensajes para imprimir.");
                return;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n================== ESTADÍSTICAS DE LATENCIA ==================");
            
            // Calcular métricas usando LINQ
            int totalMensajes = mensajesEnviados.Count;
            var mensajesConRespuestaList = mensajesEnviados
                .Where(m => m.ResponsePutDateTime != default(DateTime))
                .ToList();
            
            int mensajesConRespuesta = mensajesConRespuestaList.Count;
            int mensajesSinRespuesta = totalMensajes - mensajesConRespuesta;
            
            var diferenciasMs = mensajesConRespuestaList
                .Select(m => (m.ResponsePutDateTime - m.RequestPutDateTime).TotalMilliseconds)
                .ToList();
            
            DateTime horaPrimerMensaje = mensajesEnviados.Min(m => m.RequestPutDateTime);
            DateTime horaUltimoMensaje = mensajesEnviados.Max(m => m.RequestPutDateTime);

            Console.WriteLine($"Total de mensajes enviados: {totalMensajes}");
            Console.WriteLine($"Mensajes con respuesta: {mensajesConRespuesta}");
            Console.WriteLine($"Mensajes sin respuesta: {mensajesSinRespuesta}");
            
            // Calcular throughput (mensajes por segundo)
            if (totalMensajes > 0)
            {
                double tiempoTotalSegundos = (horaUltimoMensaje - horaPrimerMensaje).TotalSeconds;
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
                Console.WriteLine("No hay mensajes con respuesta para calcular estadísticas.");
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

        private static void PrintQueueStatistics(List<(DateTime hora, int profundidad)> mediciones)
        {
            if (mediciones == null || mediciones.Count < 4)
            {
                Console.WriteLine("No hay suficientes mediciones para calcular estadísticas (se requieren al menos 4 mediciones).");
                return;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n================== ESTADÍSTICAS DE PROCESAMIENTO DE COLA ==================");
            
            // Descartar primera y última medición - usar índices directamente sin LINQ
            int inicio = 1; // Empezar desde el segundo elemento
            int fin = mediciones.Count - 2; // Terminar en el penúltimo elemento
            
            if (fin < inicio)
            {
                Console.WriteLine("No hay suficientes mediciones después de descartar la primera y última.");
                Console.ResetColor();
                return;
            }

            // Calcular mensajes/segundo entre mediciones consecutivas
            List<double> mensajesPorSegundo = [];
            
            for (int i = inicio; i <= fin - 1; i++)
            {
                var medicionActual = mediciones[i];
                var medicionSiguiente = mediciones[i + 1];
                
                int profundidadAnterior = medicionActual.profundidad;
                int profundidadActual = medicionSiguiente.profundidad;
                
                // Calcular tiempo transcurrido en segundos
                double tiempoTranscurridoSegundos = (medicionSiguiente.hora - medicionActual.hora).TotalSeconds;
                
                // Si la profundidad disminuyó, significa que se procesaron mensajes
                if (profundidadAnterior > profundidadActual && tiempoTranscurridoSegundos > 0)
                {
                    int mensajesProcesados = profundidadAnterior - profundidadActual;
                    double tasa = mensajesProcesados / tiempoTranscurridoSegundos;
                    mensajesPorSegundo.Add(tasa);
                }
                // Si la profundidad aumentó o se mantuvo, no hubo procesamiento (o llegaron más mensajes)
                // No agregamos nada a la lista en este caso
            }

            if (mensajesPorSegundo.Count == 0)
            {
                Console.WriteLine("No se pudo calcular ninguna tasa de procesamiento (la cola no disminuyó entre mediciones).");
                Console.ResetColor();
                return;
            }

            // Calcular estadísticas usando MathNet.Numerics
            var estadisticas = new DescriptiveStatistics(mensajesPorSegundo);
            
            double promedio = estadisticas.Mean;
            double desviacionEstandar = estadisticas.StandardDeviation;
            double minimo = estadisticas.Minimum;
            double maximo = estadisticas.Maximum;
            
            // Percentiles usando MathNet.Numerics
            double p25 = Statistics.Percentile(mensajesPorSegundo, 25);
            double p50 = Statistics.Median(mensajesPorSegundo); // Mediana es igual P50
            double p75 = Statistics.Percentile(mensajesPorSegundo, 75);
            double p95 = Statistics.Percentile(mensajesPorSegundo, 95);
            double p99 = Statistics.Percentile(mensajesPorSegundo, 99);

            Console.WriteLine($"Total de intervalos analizados: {mensajesPorSegundo.Count}");
            Console.WriteLine($"\n----------- Velocidad de Procesamiento MQ+Mainframe ----------");
            Console.WriteLine($"Promedio:              {promedio:F2} mensajes/segundo");
            Console.WriteLine($"Desviación estándar:   {desviacionEstandar:F2} mensajes/segundo");
            Console.WriteLine($"Mínimo:                {minimo:F2} mensajes/segundo");
            Console.WriteLine($"Máximo:                {maximo:F2} mensajes/segundo");

            Console.WriteLine($"\n------------------------ Percentiles -------------------------");
            Console.WriteLine($"P25:                   {p25:F2} mensajes/segundo");
            Console.WriteLine($"P50 (Mediana):         {p50:F2} mensajes/segundo");
            Console.WriteLine($"P75:                   {p75:F2} mensajes/segundo");
            Console.WriteLine($"P95:                   {p95:F2} mensajes/segundo");
            Console.WriteLine($"P99:                   {p99:F2} mensajes/segundo");

            // Estadística adicional: Coeficiente de variación
            double coeficienteVariacion = promedio > 0 ? (desviacionEstandar / promedio) * 100 : 0;
            Console.WriteLine($"\n------------------------ Variabilidad ------------------------");
            Console.WriteLine($"Coeficiente de variación: {coeficienteVariacion:F2}%");
            
            if (coeficienteVariacion < 15)
                Console.WriteLine("  → Baja variabilidad (proceso consistente)");
            else if (coeficienteVariacion < 30)
                Console.WriteLine("  → Variabilidad moderada");
            else
                Console.WriteLine("  → Alta variabilidad (proceso inconsistente)");

            Console.WriteLine("==============================================================\n");
            Console.ResetColor();
        }
 
    }
}
