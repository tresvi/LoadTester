using IBM.WMQ;
using System.Text;
using System.Collections;
using Tresvi.CommandParser;
using MainframeSimulator.Options;
using Tresvi.CommandParser.Exceptions;
using System.Globalization;

namespace MainframeSimulator
{
    /// <summary>
    /// Ejemplo de ejecución:
    /// dotnet run -- -s "192.168.0.31" -m "MQGD" -p 1414 -c "CHANNEL1" -i "BNA.XX1.PEDIDO" -o "BNA.XX1.RESPUESTA"
    /// agregar "-q" para modo silencioso
    /// </summary>
    internal class Program
    {

        static async Task Main(string[] args)
        {
            try
            {
                Parameters options = CommandLine.Parse<Parameters>(args);

                if (options.InputQueue?.ToUpper() == options.OutputQueue?.ToUpper())
                {
                    Console.WriteLine("Error: Input queue and output queue must be different to avoid infinite loops");
                    return;
                }

                Console.WriteLine($"Connecting to MQ Server: {options.Server}:{options.Port}");
                Console.WriteLine($"Manager: {options.Manager}, Channel: {options.Channel}");
                Console.WriteLine($"Input Queue: {options.InputQueue}, Output Queue: {options.OutputQueue}");
                Console.WriteLine($"Delay: {options.Delay}ms");
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

                // Número de hilos para lectura (aumentado para mejor throughput en I/O bound operations)
                // Para operaciones I/O como MQ, más threads que CPUs suele dar mejor rendimiento
                int numberOfThreads = Environment.ProcessorCount;
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
                        threadId,
                        cts.Token)));
                }

                Console.WriteLine($"Started {numberOfThreads} worker threads. Press Ctrl+C to stop.");

                // Esperar todas las tareas o cancelación
                await Task.WhenAll(tasks).ConfigureAwait(false);

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
        /// Se conecta a la cola de entrada procesa el mensaje recibido y lo coloca en la cola de salida
        /// </summary>
        /// <param name="queueManager"></param>
        /// <param name="inputQueueName"></param>
        /// <param name="outputQueueName"></param>
        /// <param name="delayMs"></param>
        /// <param name="quiet"></param>
        /// <param name="threadId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        static async Task ProcessMessagesAsync(
            MQQueueManager queueManager,
            string inputQueueName,
            string outputQueueName,
            int delayMs,
            bool quiet,
            int threadId,
            CancellationToken cancellationToken)
        {
            // Abrir colas para este hilo
            MQQueue? inputQueue, outputQueue;
            
            try
            {
                inputQueue = queueManager.AccessQueue(inputQueueName, MQC.MQOO_INPUT_AS_Q_DEF + MQC.MQOO_FAIL_IF_QUIESCING);
                outputQueue = queueManager.AccessQueue(outputQueueName, MQC.MQOO_OUTPUT + MQC.MQOO_FAIL_IF_QUIESCING);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Thread {threadId}] Error opening queues: {ex.Message}");
                return;
            }

            // Reutilizar objetos para reducir allocations
            var getMessageOptions = new MQGetMessageOptions
            {
                Options = MQC.MQGMO_WAIT | MQC.MQGMO_FAIL_IF_QUIESCING,
                WaitInterval = 1000 // Reducido de 5s a 1s para mejor responsividad
            };
            var putMessageOptions = new MQPutMessageOptions();
            
            // Pre-allocar StringBuilder para construir respuestas
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

                        if (!quiet)
                        {
                            string preview = messageText.Length > 50 ? messageText.Substring(0, 50) + "..." : messageText;
                            Console.WriteLine($"[Thread {threadId}] Received message: {preview} {message.PutDateTime:HH:mm:ss.ff}");
                        }

                        // Aplicar delay de forma síncrona si es muy pequeño para evitar overhead de async
                        if (delayMs > 0)
                        {
                            if (delayMs < 50)
                                Thread.Sleep(delayMs); // Para delays pequeños, usar Sleep síncrono
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

                        outputQueue.Put(responseMessage, putMessageOptions);
 
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

 
    }
}
