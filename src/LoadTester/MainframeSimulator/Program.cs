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

                // Número de hilos para lectura (configurable, usando Environment.ProcessorCount por defecto para mejor rendimiento)
                int numberOfThreads = 1; //Environment.ProcessorCount;
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
                await Task.WhenAll(tasks);

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

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        // Intentar obtener un mensaje con timeout
                        var message = new MQMessage();
                        var getMessageOptions = new MQGetMessageOptions
                        {
                            Options = MQC.MQGMO_WAIT | MQC.MQGMO_FAIL_IF_QUIESCING,
                            WaitInterval = 5000 // Timeout de 5 segundos
                        };

                        inputQueue.Get(message, getMessageOptions);
                        byte[] messageId = message.MessageId;
                        message.Seek(0);
                        string messageText = message.ReadString(message.MessageLength);
                        
                        /*
                        // Obtener timestamp del servidor cuando el mensaje fue puesto en la cola
                        DateTime? messagePutTime = GetMessagePutDateTime(message);
                        TimeSpan? timeSincePut = null;
                        if (messagePutTime.HasValue)
                        {
                            // Obtener tiempo actual del servidor (aproximado usando tiempo local)
                            // Nota: Para mayor precisión, se podría consultar el tiempo del servidor MQ
                            timeSincePut = DateTime.Now - messagePutTime.Value;
                            Console.WriteLine($"HAYYY TIEMPOOO");
                        }
                        else
                        {
                            Console.WriteLine("NO HAY TIEMPOOO");
                        }*/

                        if (!quiet)
                        {
                            string preview = messageText.Length > 50 ? messageText.Substring(0, 50) + "..." : messageText;
                            /*string timeInfo = timeSincePut.HasValue 
                                ? $" (puesto hace {FormatTimeSpan(timeSincePut.Value)})" 
                                : "";
                            Console.WriteLine($"[Thread {threadId}] Received message: {preview}{timeInfo}");
                            */
                            Console.WriteLine($"[Thread {threadId}] Received message: {preview}{message.PutDateTime.ToString("hh:mm:ss.ffff")}");
                        }

                        if (delayMs > 0) await Task.Delay(delayMs, cancellationToken);

                        string responseText = "eco " + messageText;
                        var responseMessage = new MQMessage
                        {
                            MessageId = MQC.MQMI_NONE,
                            CorrelationId = messageId // Usar el messageId entrante como CorrelationId
                        };
                        responseMessage.WriteString(responseText);

                        var putMessageOptions = new MQPutMessageOptions();
                        outputQueue.Put(responseMessage, putMessageOptions);
                        //Console.WriteLine($"Respuesta colocada a las {responseMessage.PutDateTime.ToString("HH:mm:ss.ff")}");
                        
                        // Obtener timestamp del servidor cuando el mensaje fue puesto
                       /* DateTime? putTime = GetMessagePutDateTime(responseMessage);
                        string putTimeInfo = putTime.HasValue 
                            ? $" (puesto a las {putTime.Value:yyyy-MM-dd HH:mm:ss})" 
                            : "";
                       */
                        if (!quiet)
                        {
                            var responsePreview = responseText.Length > 50 ? responseText.Substring(0, 50) + "..." : responseText;
                            // Console.WriteLine($"[Thread {threadId}] Sent response: {responsePreview}{putTimeInfo}");
                            Console.WriteLine($"[Thread {threadId}] Sent response: {responsePreview} {responseMessage.PutDateTime.ToString("HH:mm:ss.ff")}");
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
                            await Task.Delay(1000, cancellationToken);    // Continuar procesando otros mensajes
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;    // Cancelación solicitada, sale ordenadamente
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Thread {threadId}] Error processing message: {ex.Message}");
                            await Task.Delay(1000, cancellationToken);
                    }
                }
            }
            finally
            {
                // Cerrar colas para este hilo
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
        /// Obtiene la fecha y hora cuando el mensaje fue puesto en la cola desde las propiedades del mensaje MQ
        /// </summary>
        static DateTime? GetMessagePutDateTime(MQMessage message)
        {
            try
            {
            // En IBM MQ .NET, las propiedades PutDate y PutTime están disponibles directamente
            // después de hacer Get o Put. Usamos reflexión o propiedades directas si están disponibles.

            // Intentar acceder a PutDate y PutTime usando reflexión por si no están expuestas directamente

            //https://www.ibm.com/docs/es/ibm-mq/9.3.x?topic=descriptor-puttime-mqchar8-mqmd
                
                Console.WriteLine($"Hora {Encoding.UTF8.GetString(message.MQMD.PutTime)}");
                Console.WriteLine($"Hora {message.PutDateTime.ToString("hh:mm:ss.ffff")}"); 
                var messageType = message.GetType();
                var putDateProp = messageType.GetProperty("PutDate");
                var putTimeProp = messageType.GetProperty("PutTime");

                if (putDateProp == null || putTimeProp == null)
                {
                    // Si no están disponibles como propiedades, intentar con campos
                    putDateProp = messageType.GetProperty("PutDate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    putTimeProp = messageType.GetProperty("PutTime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                }

                if (putDateProp != null && putTimeProp != null)
                {
                    object? putDateObj = putDateProp.GetValue(message);
                    object? putTimeObj = putTimeProp.GetValue(message);

                    if (putDateObj != null && putTimeObj != null)
                    {
                        int putDate = Convert.ToInt32(putDateObj);
                        int putTime = Convert.ToInt32(putTimeObj);

                        if (putDate == 0 || putTime == 0)
                            return null;

                        // Convertir formato MQ a DateTime
                        string dateStr = putDate.ToString("00000000");
                        string timeStr = putTime.ToString("00000000");

                        // Parsear fecha: YYYYMMDD
                        if (DateTime.TryParseExact(dateStr, "yyyyMMdd", CultureInfo.InvariantCulture, 
                            DateTimeStyles.None, out DateTime date))
                        {
                            // Parsear hora: HHMMSSTH donde T es décimas de segundo
                            if (timeStr.Length >= 6)
                            {
                                int hours = int.Parse(timeStr.Substring(0, 2));
                                int minutes = int.Parse(timeStr.Substring(2, 2));
                                int seconds = int.Parse(timeStr.Substring(4, 2));
                                int tenths = timeStr.Length >= 8 ? int.Parse(timeStr.Substring(6, 1)) : 0;

                                DateTime putDateTime = date.AddHours(hours).AddMinutes(minutes)
                                    .AddSeconds(seconds).AddMilliseconds(tenths * 100);

                                return putDateTime;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Si hay error al parsear, retornar null
            }

            return null;
        }

        /// <summary>
        /// Formatea un TimeSpan en formato legible (ej: "2h 15m 30s" o "45s")
        /// </summary>
        static string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.TotalDays >= 1)
            {
                return $"{(int)timeSpan.TotalDays}d {timeSpan.Hours}h {timeSpan.Minutes}m";
            }
            else if (timeSpan.TotalHours >= 1)
            {
                return $"{timeSpan.Hours}h {timeSpan.Minutes}m {timeSpan.Seconds}s";
            }
            else if (timeSpan.TotalMinutes >= 1)
            {
                return $"{timeSpan.Minutes}m {timeSpan.Seconds}s";
            }
            else
            {
                return $"{timeSpan.Seconds}s";
            }
        }
    }
}
