using IBM.WMQ;
using LoadTester.Plugins;
using System.Collections;
using System.Net;
using Tresvi.CommandParser;
using Tresvi.CommandParser.Exceptions;


namespace MQQueueMonitor
{
    //dotnet run -- -m "10.6.248.10:1414:CHANNEL1:MQGD" -q "BNA.CU2.PEDIDO,BNA.CU2.RESPUESTA"
    internal class Program
    {
        private const int MIN_REFRESH_INTERVAL = 25;

        static void Main(string[] args)
        {
            CliParameters options;

            try
            {
                object verb = Tresvi.CommandParser.CommandLine.Parse(args, typeof(CliParameters));
                options = (CliParameters)verb;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error al parsear los argumentos: {ex.Message}");
                Environment.Exit(1);
                return; // Para evitar el error de variable no asignada
            }

            // Parsear y validar mqConnection
            string ip;
            int port;
            string channel;
            string managerName;

            try
            {
                ParseMqConnection(options.MqConnection, out ip, out port, out channel, out managerName);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"Error en mqConnection: {ex.Message}");
                Environment.Exit(1);
                return;
            }

            var properties = new Hashtable
            {
                { MQC.HOST_NAME_PROPERTY, ip },
                { MQC.PORT_PROPERTY, port },
                { MQC.CHANNEL_PROPERTY, channel },/*
                { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_CLIENT } // conexión remota tipo cliente
               */
            };

            List<string> queues;
            try
            {
                queues = ParseQueues(options.QueuesNames);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"Error en queues: {ex.Message}");
                Environment.Exit(1);
                return;
            }

            if (options.RefreshInterval < MIN_REFRESH_INTERVAL)
            {
                Console.Error.WriteLine($"Error: refreshInterval debe ser mayor o igual que {MIN_REFRESH_INTERVAL}. Valor recibido: {options.RefreshInterval}");
                Environment.Exit(1);
                return;
            }

            try
            {
                MQQueueManager queueMgr = new(managerName, properties);

                // Inicializar estadísticas por cola
                Dictionary<string, QueueStatistics> queueStats = new();
                Dictionary<string, int> linePositions = new(); // Para rastrear las posiciones de las líneas
                
                ConsoleHProgressBar progressBar = new(40, 63, 88, true);

                Console.Clear();
                Console.WriteLine("Presione Ctrl+C para terminar el proceso...\n");

                // Mostrar estructura inicial de los párrafos
                foreach (string queueName in queues)
                {
                    int maxDepth = IbmMQPlugin.GetMaxDepth(queueMgr, queueName);
                    queueStats[queueName] = new QueueStatistics
                    {
                        QueueName = queueName,
                        MaxDepth = maxDepth
                    };

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Cola {queueName} (Prof. Maxima: {maxDepth})");
                    linePositions[$"{queueName}_Profundidad"] = Console.CursorTop;
                    Console.WriteLine($"Profundidad: 0");
                    linePositions[$"{queueName}_Min"] = Console.CursorTop;
                    Console.WriteLine($"Registro mín: 0 (00:00:00.00)");
                    linePositions[$"{queueName}_Max"] = Console.CursorTop;
                    Console.WriteLine($"Registro máx: 0 (00:00:00.00)");
                    linePositions[$"{queueName}_Saturation"] = Console.CursorTop;
                    Console.WriteLine($"Veces que saturó: 0");
                    linePositions[$"{queueName}_ProgressBar"] = Console.CursorTop;
                    Console.WriteLine("[                                        ]");
                    Console.WriteLine();
                    Console.WriteLine();
                    Console.ResetColor();
                }

                // Ocultar el cursor para evitar parpadeo durante las actualizaciones
                Console.CursorVisible = false;

                // Configurar manejador para restaurar el cursor al presionar Ctrl+C
                Console.CancelKeyPress += (sender, e) =>
                {
                    Console.CursorVisible = true;
                    e.Cancel = false; // Permitir que el programa termine normalmente
                };

                while (true)
                {
                    DateTime currentTime = DateTime.Now;

                    foreach (string queueName in queues)
                    {
                        int depth = IbmMQPlugin.GetDepth(queueMgr, queueName);
                        QueueStatistics stats = queueStats[queueName];

                        stats.CurrentDepth = depth;

                        if (depth < stats.MinDepth)
                        {
                            stats.MinDepth = depth;
                            stats.MinDepthTimestamp = currentTime;
                        }

                        if (depth > stats.MaxDepthRecorded)
                        {
                            stats.MaxDepthRecorded = depth;
                            stats.MaxDepthTimestamp = currentTime;
                        }

                        // Detectar saturación: pasar de un valor menor a la profundidad máxima a la profundidad máxima
                        if (depth >= stats.MaxDepth && !stats.WasAtMaxDepth)
                        {
                            stats.SaturationCount++;
                            stats.WasAtMaxDepth = true;
                        }
                        else if (depth < stats.MaxDepth)
                        {
                            stats.WasAtMaxDepth = false;
                        }

                        // Actualizar solo los valores en pantalla
                        UpdateReportLine(linePositions[$"{queueName}_Profundidad"], $"Profundidad: {stats.CurrentDepth}");
                        
                        if (stats.MinDepth == int.MaxValue)
                            UpdateReportLine(linePositions[$"{queueName}_Min"], "Registro mín: N/A");
                        else
                            UpdateReportLine(linePositions[$"{queueName}_Min"], $"Registro mín: {stats.MinDepth} ({stats.MinDepthTimestamp:HH:mm:ss.ff})");
                        
                        if (stats.MaxDepthRecorded == int.MinValue)
                            UpdateReportLine(linePositions[$"{queueName}_Max"], "Registro máx: N/A");
                        else
                            UpdateReportLine(linePositions[$"{queueName}_Max"], $"Registro máx: {stats.MaxDepthRecorded} ({stats.MaxDepthTimestamp:HH:mm:ss.ff})");
                        
                        UpdateReportLine(linePositions[$"{queueName}_Saturation"], $"Veces que saturó: {stats.SaturationCount}");
                        
                        // Actualizar barra de progreso con colores
                        progressBar.Update(linePositions[$"{queueName}_ProgressBar"], stats.CurrentDepth, stats.MaxDepth);
                    }

                    Thread.Sleep(options.RefreshInterval);
                }
            }
            catch (MQException ex)
            {
                Console.CursorVisible = true; // Restaurar cursor visible
                Console.WriteLine(ex);
                Console.WriteLine($"Reason = {ex.ReasonCode} Msg= {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.CursorVisible = true; // Restaurar cursor visible
                Console.Error.WriteLine(ex);
            }
            finally
            {
                // Asegurar que el cursor siempre se restaure al finalizar
                Console.CursorVisible = true;
            }

        }

        /// <summary>
        /// Parsea el string mqConnection con formato IP:Puerto:Canal:NombreManager
        /// </summary>
        /// <param name="mqConnectionString">Cadena con formato IP:Puerto:Canal:NombreManager</param>
        /// <param name="ip">IP del servidor MQ</param>
        /// <param name="port">Puerto del servidor MQ</param>
        /// <param name="channel">Nombre del canal MQ</param>
        /// <param name="managerName">Nombre del Queue Manager</param>
        /// <exception cref="ArgumentException">Se lanza si el formato es incorrecto o algún parámetro es inválido</exception>
        static void ParseMqConnection(string mqConnectionString, out string ip, out int port, out string channel, out string managerName)
        {
            if (string.IsNullOrWhiteSpace(mqConnectionString))
                throw new ArgumentException("El parámetro mqConnection no puede estar vacío.");

            string[] partes = mqConnectionString.Split(':');
            
            if (partes.Length != 4)
            {
                throw new ArgumentException(
                    $"El formato de mqConnection es incorrecto. Se espera 'IP:Puerto:Canal:NombreManager' pero se recibió '{mqConnectionString}'. " +
                    $"Número de partes encontradas: {partes.Length} (se esperaban 4).");
            }

            ip = partes[0].Trim();
            if (string.IsNullOrWhiteSpace(ip))
                throw new ArgumentException("La IP del servidor MQ no puede estar vacía.");
            
            if (!IPAddress.TryParse(ip, out _))
                throw new ArgumentException($"La IP '{ip}' no es una dirección IP válida (IPv4 o IPv6).");

            string portStr = partes[1].Trim();
            if (string.IsNullOrWhiteSpace(portStr))
                throw new ArgumentException("El puerto no puede estar vacío.");

            if (!int.TryParse(portStr, out port) || port < 1 || port > 65535)
                throw new ArgumentException($"El puerto '{portStr}' no es válido. Debe ser un número entre 1 y 65535.");

            channel = partes[2].Trim();
            if (string.IsNullOrWhiteSpace(channel))
                throw new ArgumentException("El nombre del canal no puede estar vacío.");

            managerName = partes[3].Trim();
            if (string.IsNullOrWhiteSpace(managerName))
                throw new ArgumentException("El nombre del Queue Manager no puede estar vacío.");
        }


        /// <summary>
        /// Parsea el string de colas separadas por coma. Acepta hasta 2 colas.
        /// </summary>
        /// <param name="queuesString">Cadena con nombres de colas separadas por coma</param>
        /// <returns>Lista de nombres de colas (1 o 2 colas)</returns>
        /// <exception cref="ArgumentException">Se lanza si el formato es incorrecto o hay más de 2 colas</exception>
        static List<string> ParseQueues(string queuesString)
        {
            if (string.IsNullOrWhiteSpace(queuesString))
                throw new ArgumentException("El parámetro queues no puede estar vacío.");

            string[] queues = queuesString.Split(',');
            List<string> result = [];

            foreach (string queue in queues)
            {
                string trimmedQueue = queue.Trim();
                if (!string.IsNullOrWhiteSpace(trimmedQueue))
                {
                    result.Add(trimmedQueue);
                }
            }

            if (result.Count == 0)
                throw new ArgumentException("Debe proporcionarse al menos una cola válida.");

            if (result.Count > 2)
                throw new ArgumentException($"Se proporcionaron {result.Count} colas, pero solo se permiten hasta 2 colas.");

            return result;
        }

        /// <summary>
        /// Actualiza una línea específica del informe sin borrar el resto
        /// </summary>
        /// <param name="line">Número de línea a actualizar</param>
        /// <param name="text">Texto a mostrar</param>
        static void UpdateReportLine(int line, string text)
        {
            int currentTop = Console.CursorTop;
            int currentLeft = Console.CursorLeft;
            ConsoleColor originalColor = Console.ForegroundColor;

            // Mantener el cursor oculto (ya está oculto desde el inicio)
            Console.SetCursorPosition(0, line);
            Console.ForegroundColor = ConsoleColor.White; // Color blanco por defecto
            Console.Write(text.PadRight(Console.WindowWidth - 1)); // Limpiar el resto de la línea
            Console.ForegroundColor = originalColor;

            Console.SetCursorPosition(currentLeft, currentTop);
        }
    }
}
