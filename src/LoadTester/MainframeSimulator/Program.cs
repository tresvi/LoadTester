using IBM.WMQ;
using System.Text;
using System.Collections;
using Tresvi.CommandParser;
using MainframeSimulator.Options;
using Tresvi.CommandParser.Exceptions;
using System.Globalization;
using System.Diagnostics;

namespace MainframeSimulator
{
    //TODO: Agregar mensajes de retorno al sistema operativo cuando falla. Ya sea por error de operaciopn o error de parametros.

    enum ExecutionMode { echo, flush }

    /// <summary>
    /// Ejemplo de ejecución:
    /// dotnet run -- -s "192.168.0.31" -m "MQGD" -p 1414 -c "CHANNEL1" -i "BNA.XX1.PEDIDO" -o "BNA.XX1.RESPUESTA"
    /// agregar "-q" para modo silencioso
    /// </summary>
    internal class Program
    {
        private static long _totalRequests = 0;
        private static long _totalResponses = 0;
        private static int _statsLine = -1;

        static async Task Main(string[] args)
        {
            try
            {
                Parameters options = CommandLine.Parse<Parameters>(args);
                ExecutionMode executionMode;

                if (options.InputQueue?.ToUpper() == options.OutputQueue?.ToUpper())
                {
                    Console.WriteLine("Error: Input queue and output queue must be different to avoid infinite loops");
                    return;
                }

                if (options.ExecutionMode.ToLower() == "echo")
                    executionMode = ExecutionMode.echo;
                else if (options.ExecutionMode.ToLower() == "flush")
                    executionMode = ExecutionMode.flush;
                else
                {
                    Console.WriteLine("Error: Mode must be 'echo' or 'flush'");
                    return;
                }

                Console.WriteLine($"Connecting to MQ Server: {options.Server}:{options.Port}");
                Console.WriteLine($"Manager: {options.Manager}, Channel: {options.Channel}");
                Console.WriteLine($"Input Queue: {options.InputQueue}");
                if (executionMode == ExecutionMode.flush)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Mode: Flush (messages will be deleted without processing)");
                    Console.ResetColor();
                }
                else
                {
                    Console.WriteLine($"Output Queue: {options.OutputQueue}");
                    Console.WriteLine($"Delay: {options.Delay}ms");
                    Console.WriteLine($"Mode: {executionMode}");
                }
                Console.WriteLine("Starting mainframe simulator...");

                Hashtable connectionProperties = new()
                {
                    { MQC.HOST_NAME_PROPERTY, options.Server },
                    { MQC.PORT_PROPERTY, options.Port },
                    { MQC.CHANNEL_PROPERTY, options.Channel },
                    //{ MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_CLIENT }
                };

                // Conectar al MQ Manager
                MQQueueManager queueManager = new MQQueueManager(options.Manager, connectionProperties);
                Console.WriteLine("Connected to MQ Manager successfully");

                // Validar que las colas existen y son accesibles antes de iniciar los hilos de trabajo
                using (MQQueue testInputQueue = queueManager.AccessQueue(options.InputQueue, MQC.MQOO_INPUT_AS_Q_DEF + MQC.MQOO_FAIL_IF_QUIESCING))
                using (MQQueue testOutputQueue = queueManager.AccessQueue(options.OutputQueue, MQC.MQOO_OUTPUT + MQC.MQOO_FAIL_IF_QUIESCING))
                {
                    Console.WriteLine("Queues validated successfully");
                }

                // Crear token de cancelación para cierre ordenado
                using var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                    Console.WriteLine("\nShutting down...");
                };

                int numberOfThreads = options.ThreadNumber > 0 ? options.ThreadNumber : Environment.ProcessorCount;
                var tasks = new List<Task>();

                // Iniciar múltiples hilos para leer de la cola de entrada
                // Cada hilo abre sus propios manejadores de cola para seguridad de hilos
                for (int i = 0; i < numberOfThreads; i++)
                {
                    int threadId = i + 1;
                    tasks.Add(Task.Run(async () => await ProcessMessagesAsync(
                        queueManager,
                        options.InputQueue!,
                        options.OutputQueue!,
                        options.Delay,
                        options.Quiet,
                        executionMode,
                        threadId,
                        cts.Token)));
                }

                Console.WriteLine($"Started {numberOfThreads} worker threads. Press Ctrl+C to stop.");
                Console.WriteLine(); // Línea en blanco para las estadísticas
                _statsLine = Console.CursorTop;

                // Iniciar tarea para mostrar estadísticas cada segundo
                var statsTask = Task.Run(async () => await DisplayStatsAsync(executionMode, cts.Token));

                // Esperar todas las tareas o cancelación
                await Task.WhenAll(tasks).ConfigureAwait(false);
                
                // Cancelar la tarea de estadísticas
                cts.Cancel();
                await statsTask.ConfigureAwait(false);

                // Desconectar del MQ Manager
                queueManager.Disconnect();
                Console.WriteLine("Disconnected from MQ Manager");
            }
            catch (CommandParserBaseException ex)
            {
                Console.WriteLine($"Error al parsear los argumentos: {ex.Message}");
                return;
            }
            catch (MQException mqEx)
            {
                Console.WriteLine($"MQ Error: {mqEx.Message} (Reason Code: {mqEx.ReasonCode})");
                Environment.Exit(1);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Environment.Exit(1);
            }
        }

        /// <summary>
        /// Se conecta a la cola de entrada procesa el mensaje recibido y lo coloca en la cola de salida.
        /// Si el modo es "flush", solo elimina el mensaje sin procesarlo ni responder.
        /// </summary>
        static async Task ProcessMessagesAsync(
            MQQueueManager queueManager,
            string inputQueueName,
            string outputQueueName,
            int delayMs,
            bool quiet,
            ExecutionMode executionMode,
            int threadId,
            CancellationToken cancellationToken)
        {
            MQQueue? inputQueue, outputQueue = null;
            
            try
            {
                inputQueue = queueManager.AccessQueue(inputQueueName, MQC.MQOO_INPUT_AS_Q_DEF + MQC.MQOO_FAIL_IF_QUIESCING);
                if (executionMode != ExecutionMode.flush)
                {
                    outputQueue = queueManager.AccessQueue(outputQueueName, MQC.MQOO_OUTPUT + MQC.MQOO_FAIL_IF_QUIESCING);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Thread {threadId}] Error opening queues: {ex.Message}");
                return;
            }

            // Reutilizo objetos para reducir allocations
            var getMessageOptions = new MQGetMessageOptions
            {
                Options = MQC.MQGMO_WAIT | MQC.MQGMO_FAIL_IF_QUIESCING,
                WaitInterval = 1000 // Reducido de 5s a 1s para mejor responsividad
            };
            var putMessageOptions = new MQPutMessageOptions();
            
            var responseBuilder = new StringBuilder(1024);
            const string ECO_PREFIX = "eco ";

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        // Reutilizar el objeto mensaje en lugar de crear uno nuevo
                        var message = new MQMessage();
                        inputQueue.Get(message, getMessageOptions);
                        
                        byte[] messageId = message.MessageId;
                        message.Seek(0);
                        string messageText = message.ReadString(message.MessageLength);
                        Interlocked.Increment(ref _totalRequests);

                        if (!quiet)
                        {
                            string preview = messageText.Length > 50 ? messageText.Substring(0, 50) + "..." : messageText;
                            Console.WriteLine($"[Thread {threadId}] Received message: {preview} {message.PutDateTime:HH:mm:ss.ff}");
                        }

                        if (executionMode == ExecutionMode.flush) continue;

                        if (delayMs > 0)
                        {
                            if (delayMs < 50)
                                Thread.Sleep(delayMs); // Para delays pequeños, uso Sleep síncrono
                            else
                                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                        }

                        // Construir respuesta de forma más eficiente
                        responseBuilder.Clear();
                        responseBuilder.Append(ECO_PREFIX);
                        responseBuilder.Append(messageText);
                        string responseText = responseBuilder.ToString();
                        
                        var responseMessage = new MQMessage
                        {
                            MessageId = MQC.MQMI_NONE,
                            CorrelationId = messageId
                        };
                        responseMessage.WriteString(responseText);

                        outputQueue?.Put(responseMessage, putMessageOptions);
                        Interlocked.Increment(ref _totalResponses);
                        
                        if (!quiet)
                        {
                            string responsePreview = responseText.Length > 50 ? responseText.Substring(0, 50) + "..." : responseText;
                            Console.WriteLine($"[Thread {threadId}] Sent response: {responsePreview} {responseMessage.PutDateTime:HH:mm:ss.ff}");
                        }
                    }
                    catch (MQException mqEx)
                    {
                        if (mqEx.ReasonCode == MQC.MQRC_NO_MSG_AVAILABLE)
                        {
                            continue;
                        }
                        else if (mqEx.ReasonCode == MQC.MQRC_CONNECTION_BROKEN || 
                                mqEx.ReasonCode == MQC.MQRC_CONNECTION_QUIESCING)
                        {
                            // Problemas de conexión, salir del bucle
                            Console.WriteLine($"[Thread {threadId}] Connection issue: {mqEx.Message}");
                            break;
                        }
                        else
                        {
                            Console.WriteLine($"[Thread {threadId}] MQ Error: {mqEx.Message} (Reason: {mqEx.ReasonCode})");
                            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);    // Continuar procesando otros mensajes
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;    // Cancelación solicitada, sale ordenadamente
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Thread {threadId}] Error processing message: {ex.Message}");
                            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                try
                {
                    inputQueue?.Close();
                    outputQueue?.Close();
                }
                catch { }
            }

            Console.WriteLine($"[Thread {threadId}] Worker thread stopped");
        }


        /// <summary>
        /// Muestra las estadísticas de respuestas por segundo, actualizándose cada segundo en el mismo 
        /// de la consola.
        /// </summary>
        static async Task DisplayStatsAsync(ExecutionMode executionMode, CancellationToken cancellationToken)
        {
            long lastCount = 0;
            DateTime lastTime = DateTime.UtcNow;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                    
                    long currentResponsesCount = Interlocked.Read(ref _totalResponses);
                    long currentRequestsCount = Interlocked.Read(ref _totalRequests);
                    DateTime currentTime = DateTime.UtcNow;
                    
                    long responsesInLastSecond = currentResponsesCount - lastCount;
                    double timeElapsed = (currentTime - lastTime).TotalSeconds;
                    double responsesPerSecond = timeElapsed > 0 ? responsesInLastSecond / timeElapsed : 0;
                    
                    Console.ForegroundColor = ConsoleColor.Green;
                    
                    // Guardar posición actual del cursor
                    int currentTop = Console.CursorTop;
                    int currentLeft = Console.CursorLeft;
                    
                    if (_statsLine >= 0)
                    {
                        Console.SetCursorPosition(0, _statsLine);
                        Console.Write($"Responses per second: {responsesPerSecond:F1}        "); // Espacios para limpiar caracteres anteriores                        
                        Console.SetCursorPosition(0, _statsLine + 1);
                        Console.Write($"Total request read: {currentRequestsCount}        ");
                        Console.SetCursorPosition(0, _statsLine + 2);
                        Console.Write($"Total responses sent: {currentResponsesCount}        ");
                        Console.SetCursorPosition(currentLeft, currentTop);
                    }
                    
                    Console.ResetColor();
                    lastCount = currentResponsesCount;
                    lastTime = currentTime;
                }
                catch (OperationCanceledException) { break; }
                catch (Exception) { }
            }
        }

 
    }
}
