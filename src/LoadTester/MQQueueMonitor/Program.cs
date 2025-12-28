using IBM.WMQ;
using MQQueueMonitor.Models;
using MQQueueMonitor.ConsoleComponents;
using System.Collections;
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
            MQConnection mqConnection = new();
            List<string> queues;

            try
            {
                object verb = Tresvi.CommandParser.CommandLine.Parse(args, typeof(CliParameters));
                options = (CliParameters)verb;
                mqConnection.Load(options.MqConnection);
                queues = MQConnection.ParseQueues(options.QueuesNames);
            }
            catch (CommandParserBaseException ex)
            {
                Console.Error.WriteLine($"Error al parsear los argumentos: {ex.Message}");
                Environment.Exit(1);
                return;
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"Error en parametros Coneccion MQ: {ex.Message}");
                Environment.Exit(1);
                return;
            }

            var properties = new Hashtable
            {
                { MQC.HOST_NAME_PROPERTY, mqConnection.Ip },
                { MQC.PORT_PROPERTY, mqConnection.Port },
                { MQC.CHANNEL_PROPERTY, mqConnection.Channel }
            };

            if (options.RefreshInterval < MIN_REFRESH_INTERVAL)
            {
                Console.Error.WriteLine($"Error: refreshInterval debe ser mayor o igual que {MIN_REFRESH_INTERVAL}. Valor recibido: {options.RefreshInterval}");
                Environment.Exit(1);
                return;
            }

            MQQueueManager queueMgr = null;
            Dictionary<string, MQQueue> openQueues = new();
            
            try
            {
                queueMgr = new MQQueueManager(mqConnection.ManagerName, properties);

                // Inicializar estadísticas por cola y abrir colas para consulta (INQUIRE)
                Dictionary<string, QueueStatistics> queueStats = [];
                Dictionary<string, int> linePositions = []; 
                
                ConsoleHProgressBar progressBar = new(40, 63, 88, true);

                Console.Clear();
                Console.WriteLine("Presione Ctrl+C para terminar el proceso...\n");

                int openOptions = MQC.MQOO_INQUIRE | MQC.MQOO_FAIL_IF_QUIESCING;
                
                foreach (string queueName in queues)
                {
                    MQQueue queue = queueMgr.AccessQueue(queueName, openOptions);
                    openQueues[queueName] = queue;
                    
                    int maxDepth = queue.MaximumDepth;
                    queueStats[queueName] = new QueueStatistics(queueName, maxDepth);

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

                Console.CursorVisible = false;
                Console.CancelKeyPress += (sender, e) =>
                {
                    Console.CursorVisible = true;
                    e.Cancel = false; // Permitir que el programa termine normalmente
                };

                while (true)
                {
                    foreach (string queueName in queues)
                    {
                        MQQueue queue = openQueues[queueName];
                        int depth = queue.CurrentDepth;
                        QueueStatistics stats = queueStats[queueName];
                        stats.Update(depth);

                        // Actualizar solo los valores en pantalla
                        UpdateReportLine(linePositions[$"{queueName}_Profundidad"], $"Profundidad: {stats.CurrentDepth}");
                        
                        if (!stats.HasMinDepth)
                            UpdateReportLine(linePositions[$"{queueName}_Min"], "Registro mín: N/A");
                        else
                            UpdateReportLine(linePositions[$"{queueName}_Min"], $"Registro mín: {stats.MinDepth} ({stats.MinDepthTimestamp:HH:mm:ss.ff})");
                        
                        if (!stats.HasMaxDepthRecorded)
                            UpdateReportLine(linePositions[$"{queueName}_Max"], "Registro máx: N/A");
                        else
                            UpdateReportLine(linePositions[$"{queueName}_Max"], $"Registro máx: {stats.MaxDepthRecorded} ({stats.MaxDepthTimestamp:HH:mm:ss.ff})");
                        
                        UpdateReportLine(linePositions[$"{queueName}_Saturation"], $"Veces que saturó: {stats.SaturationCount}");
                        progressBar.Update(linePositions[$"{queueName}_ProgressBar"], stats.CurrentDepth, stats.MaxDepth);
                    }

                    Thread.Sleep(options.RefreshInterval);
                }
            }
            catch (MQException ex)
            {
                Console.WriteLine(ex);
                Console.WriteLine($"Reason = {ex.ReasonCode} Msg= {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }
            finally
            {
                // Cerrar todas las colas abiertas
                foreach (var queue in openQueues.Values)
                {
                    try
                    {
                        queue?.Close();
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Error al cerrar cola: {ex.Message}");
                    }
                }
                
                try
                {
                    queueMgr?.Disconnect();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error al desconectar Queue Manager: {ex.Message}");
                }
                
                Console.CursorVisible = true;
            }

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
