using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IBM.WMQ;
using StampedeLoadTester.Models;

namespace StampedeLoadTester.Services;

    /// <summary>
    /// Clase para coordinar múltiples instancias del programa en diferentes equipos
    /// mediante comunicación UDP simple (ping/pong)
    /// </summary>
    internal sealed class RemoteControllerService
    {
        private const string PING_MESSAGE = "ping";
        private const string PONG_MESSAGE = "pong";
        private const string START_COMMAND = "START";
        private const string CLOSE_COMMAND = "CLOSE";
        private const string INIT_CON_COMMAND = "INIT_CON";
        private const string GET_RESULT_COMMAND = "GET_RESULT";
        private const string ACK_MESSAGE = "ACK";
        private const string ERROR_MESSAGE = "ERROR";
        private const string RESULT_PREFIX = "RESULT:";

        private TestDefinition? _testDefinition;
        private TestManager? _testManager;
        private int? _lastMessageCounter = null;

        /// <summary>
        /// Establece la definición de prueba que se usará para inicializar las conexiones
        /// </summary>
        public void SetTestDefinition(TestDefinition testDefinition)
        {
            _testDefinition = testDefinition;
        }

    /// <summary>
    /// Envía un mensaje "ping" a la IP y puerto especificados y espera bloqueado una respuesta "pong"
    /// Este método se queda bloqueado esperando la respuesta hasta que llegue o se agote el timeout
    /// </summary>
    /// <param name="ip">Dirección IP del servidor remoto</param>
    /// <param name="port">Puerto del servidor remoto</param>
    /// <param name="timeout">Tiempo máximo de espera para la respuesta</param>
    /// <returns>El tiempo de respuesta en milisegundos, o null si se agotó el timeout</returns>
    public TimeSpan Ping(IPAddress ip, int port, TimeSpan timeout)
    {
        UdpClient? client = null;
        try
        {
            client = new UdpClient();
            client.Client.ReceiveTimeout = (int)timeout.TotalMilliseconds;

            IPEndPoint remoteEndPoint = new IPEndPoint(ip, port);
            byte[] pingBytes = Encoding.UTF8.GetBytes(PING_MESSAGE);

            long startTime = Stopwatch.GetTimestamp();

            // Enviar ping
            client.Send(pingBytes, pingBytes.Length, remoteEndPoint);


            // Receive() se bloquea aquí esperando la respuesta "pong" del servidor
            IPEndPoint? senderEndPoint = null;
            byte[] responseBytes = client.Receive(ref senderEndPoint);

            long endTime = Stopwatch.GetTimestamp();
            double elapsedMs = (endTime - startTime) * 1000.0 / Stopwatch.Frequency;

            string response = Encoding.UTF8.GetString(responseBytes);

            if (response == ACK_MESSAGE)
            {
                return TimeSpan.FromMilliseconds(elapsedMs);
            }

            throw new Exception($"Respuesta inesperada: {response}");
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
        {
            throw new Exception($"Timeout esperando respuesta de {ip}:{port}");
        }
        finally
        {
            client?.Close();
            client?.Dispose();
        }
    }

    /// <summary>
    /// Escucha en el puerto especificado y procesa comandos desde el master
    /// Comandos disponibles: PING (responde pong), START (ejecuta ensayo), CLOSE (cierra aplicación)
    /// </summary>
    /// <param name="port">Puerto en el que escuchar</param>
    /// <param name="cancellationToken">Token para cancelar la escucha</param>
    public void Listen(int port, CancellationToken cancellationToken,string queueManagerName, string outputQueueName, string mensaje, List<Hashtable> connectionProperties)
    {
        UdpClient? listener = null;
        try
        {
            listener = new UdpClient(port);
            listener.Client.ReceiveTimeout = 5000;
            Console.WriteLine($"Escuchando comandos en puerto {port}...");
            using TestManager manager = new(queueManagerName, outputQueueName, mensaje, connectionProperties);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    IPEndPoint? remoteEndPoint = null;
                    byte[] receivedBytes = listener.Receive(ref remoteEndPoint);

                    string receivedMessage = Encoding.UTF8.GetString(receivedBytes).Trim();

                    if (receivedMessage == PING_MESSAGE)
                    {
                        Console.WriteLine($"Ping recibido de {remoteEndPoint}, enviado ack");
                        byte[] pongBytes = Encoding.UTF8.GetBytes(ACK_MESSAGE);
                        listener.Send(pongBytes, pongBytes.Length, remoteEndPoint);
                    }
                    else if (receivedMessage == INIT_CON_COMMAND)
                    {
                        Console.WriteLine($"Comando INIT_CON recibido de {remoteEndPoint}");
                        bool success = false;
                        string errorMessage = "";
                        try
                        {
                            manager.InicializarConexiones();
                            success = true;
                        }
                        catch (Exception ex)
                        { 
                            errorMessage = ex.Message;
                            success = false; 
                        }
                        
                        if (success)
                        {
                            byte[] ackBytes = Encoding.UTF8.GetBytes(ACK_MESSAGE);
                            listener.Send(ackBytes, ackBytes.Length, remoteEndPoint);
                        }
                        else
                        {
                            Console.WriteLine($"ERROR al inicializar conexiones: {errorMessage}");
                            byte[] errorBytes = Encoding.UTF8.GetBytes(ERROR_MESSAGE);
                            listener.Send(errorBytes, errorBytes.Length, remoteEndPoint);
                        }
                    }
                    else if (receivedMessage == START_COMMAND)
                    {
                        Console.WriteLine($"Comando START recibido de {remoteEndPoint}");
                        
                        // Responder ACK inmediatamente
                        byte[] ackBytes = Encoding.UTF8.GetBytes(ACK_MESSAGE);
                        listener.Send(ackBytes, ackBytes.Length, remoteEndPoint);
                        
                        /*************************************************/
                        manager.EnviarMensajesPrueba();

                        TimeSpan duracionEnsayo = TimeSpan.FromMilliseconds(2000);
                        int messageCounter = manager.EjecutarHilosCarga(duracionEnsayo, 6);
                        Console.WriteLine($"FIN: Msjes colocados: {messageCounter}");
                        
                        // Guardar el messageCounter para consultas posteriores
                        _lastMessageCounter = messageCounter;
                        /*************************************************/
                    }
                    else if (receivedMessage == GET_RESULT_COMMAND)
                    {
                        Console.WriteLine($"Comando GET_RESULT recibido de {remoteEndPoint}");
                        
                        string response;
                        if (_lastMessageCounter.HasValue)
                        {
                            response = $"{RESULT_PREFIX}{_lastMessageCounter.Value}";
                        }
                        else
                        {
                            response = ERROR_MESSAGE;
                        }
                        
                        byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                        listener.Send(responseBytes, responseBytes.Length, remoteEndPoint);
                        Console.WriteLine($"Respuesta enviada a {remoteEndPoint}: {response}");
                    }
                    else if (receivedMessage == CLOSE_COMMAND)
                    {
                        Console.WriteLine($"Comando CLOSE recibido de {remoteEndPoint}. Cerrando aplicación...");
                        manager.CerrarConexiones();
                        byte[] ackBytes = Encoding.UTF8.GetBytes(ACK_MESSAGE);
                        listener.Send(ackBytes, ackBytes.Length, remoteEndPoint);
                        break;
                    }
                    else
                    {
                        Console.WriteLine($"Comando desconocido recibido de {remoteEndPoint}: {receivedMessage}");
                        // Continuar escuchando
                    }
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        Console.WriteLine($"Error al recibir mensaje: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al iniciar listener en puerto {port}: {ex.Message}");
            throw;
        }
        finally
        {
            listener?.Close();
            listener?.Dispose();
            Console.WriteLine("Listener detenido");
        }
    }


    /// <summary>
    /// Crea un Hashtable con las propiedades MQ a partir de MQProperties
    /// </summary>
    private Hashtable CreateMQPropertiesHashtable(MQProperties mqProps)
    {
        return new Hashtable
        {
            { MQC.HOST_NAME_PROPERTY, mqProps.HostName },
            { MQC.PORT_PROPERTY, mqProps.Port },
            { MQC.CHANNEL_PROPERTY, mqProps.Channel }
        };
    }

    /// <summary>
    /// Ejecuta un ensayo de prueba (simulado)
    /// </summary>
    private void ExecuteTest()
    {
        Console.WriteLine("Ensayo en progreso");
        Thread.Sleep(2000); // Esperar 2 segundos
        Console.WriteLine("Ensayo finalizado");
    }

    /// <summary>
    /// Envía un comando INIT_CON a un esclavo
    /// </summary>
    /// <param name="ip">Dirección IP del esclavo</param>
    /// <param name="port">Puerto del esclavo</param>
    /// <param name="timeout">Timeout para la respuesta</param>
    /// <returns>True si el comando fue aceptado (ACK), False si falló (ERROR o timeout)</returns>
    public bool SendInitConCommand(IPAddress ip, int port, TimeSpan timeout)
    {
        return SendCommand(ip, port, INIT_CON_COMMAND, timeout);
    }

    /// <summary>
    /// Envía un comando START a un esclavo
    /// </summary>
    /// <param name="ip">Dirección IP del esclavo</param>
    /// <param name="port">Puerto del esclavo</param>
    /// <param name="timeout">Timeout para la respuesta</param>
    /// <returns>True si el comando fue aceptado, False en caso contrario</returns>
    public bool SendStartCommand(IPAddress ip, int port, TimeSpan timeout)
    {
        return SendCommand(ip, port, START_COMMAND, timeout);
    }

    /// <summary>
    /// Envía un comando START a un esclavo de forma asíncrona
    /// </summary>
    /// <param name="ip">Dirección IP del esclavo</param>
    /// <param name="port">Puerto del esclavo</param>
    /// <param name="timeout">Timeout para la respuesta</param>
    /// <returns>True si el comando fue aceptado, False en caso contrario</returns>
    public async Task<bool> SendStartCommandAsync(IPAddress ip, int port, TimeSpan timeout)
    {
        return await SendCommandAsync(ip, port, START_COMMAND, timeout);
    }

    /// <summary>
    /// Consulta el resultado del último ensayo (messageCounter) a un esclavo
    /// </summary>
    /// <param name="ip">Dirección IP del esclavo</param>
    /// <param name="port">Puerto del esclavo</param>
    /// <param name="timeout">Timeout para la respuesta</param>
    /// <returns>El número de mensajes colocados, o null si no hay resultado disponible o hubo error</returns>
    public async Task<int?> GetLastResultAsync(IPAddress ip, int port, TimeSpan timeout)
    {
        UdpClient? client = null;
        try
        {
            client = new UdpClient();
            client.Client.ReceiveTimeout = (int)timeout.TotalMilliseconds;

            IPEndPoint remoteEndPoint = new IPEndPoint(ip, port);
            byte[] commandBytes = Encoding.UTF8.GetBytes(GET_RESULT_COMMAND);

            // Enviar comando
            await client.SendAsync(commandBytes, commandBytes.Length, remoteEndPoint);

            // Esperar respuesta con timeout
            var receiveTask = client.ReceiveAsync();
            var timeoutTask = Task.Delay(timeout);
            
            var completedTask = await Task.WhenAny(receiveTask, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                Console.WriteLine($"Timeout consultando resultado de {ip}:{port}");
                return null;
            }
            
            var result = await receiveTask;
            string response = Encoding.UTF8.GetString(result.Buffer).Trim();

            if (response.StartsWith(RESULT_PREFIX))
            {
                string countStr = response.Substring(RESULT_PREFIX.Length);
                if (int.TryParse(countStr, out int messageCount))
                {
                    return messageCount;
                }
                else
                {
                    Console.WriteLine($"Formato de resultado inválido de {ip}:{port}: {response}");
                    return null;
                }
            }
            else if (response == ERROR_MESSAGE)
            {
                Console.WriteLine($"El esclavo {ip}:{port} respondió con ERROR - no hay resultado disponible");
                return null;
            }
            else
            {
                Console.WriteLine($"Respuesta inesperada del esclavo {ip}:{port}: {response}");
                return null;
            }
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
        {
            Console.WriteLine($"Timeout consultando resultado de {ip}:{port}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error consultando resultado de {ip}:{port}: {ex.Message}");
            return null;
        }
        finally
        {
            client?.Close();
            client?.Dispose();
        }
    }


    /// <summary>
    /// Envía un comando genérico a un esclavo
    /// </summary>
    /// <param name="ip">Dirección IP del esclavo</param>
    /// <param name="port">Puerto del esclavo</param>
    /// <param name="command">Comando a enviar</param>
    /// <param name="timeout">Timeout para la respuesta</param>
    /// <returns>True si el comando fue aceptado (se recibió ACK), False en caso contrario</returns>
    private bool SendCommand(IPAddress ip, int port, string command, TimeSpan timeout)
    {
        UdpClient? client = null;
        try
        {
            client = new UdpClient();
            client.Client.ReceiveTimeout = (int)timeout.TotalMilliseconds;

            IPEndPoint remoteEndPoint = new IPEndPoint(ip, port);
            byte[] commandBytes = Encoding.UTF8.GetBytes(command);

            // Enviar comando
            client.Send(commandBytes, commandBytes.Length, remoteEndPoint);

            // Esperar respuesta ACK
            IPEndPoint? senderEndPoint = null;
            byte[] responseBytes = client.Receive(ref senderEndPoint);

            string response = Encoding.UTF8.GetString(responseBytes).Trim();

            if (response == ACK_MESSAGE)
            {
                return true;
            }
            else if (response == ERROR_MESSAGE)
            {
                Console.WriteLine($"El esclavo {ip}:{port} respondió con ERROR para el comando {command}");
                return false;
            }

            Console.WriteLine($"Respuesta inesperada del esclavo {ip}:{port}: {response}");
            return false;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
        {
            Console.WriteLine($"Timeout enviando comando {command} a {ip}:{port}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error enviando comando {command} a {ip}:{port}: {ex.Message}");
            return false;
        }
        finally
        {
            client?.Close();
            client?.Dispose();
        }
    }

    /// <summary>
    /// Envía un comando genérico a un esclavo de forma asíncrona
    /// </summary>
    /// <param name="ip">Dirección IP del esclavo</param>
    /// <param name="port">Puerto del esclavo</param>
    /// <param name="command">Comando a enviar</param>
    /// <param name="timeout">Timeout para la respuesta</param>
    /// <returns>True si el comando fue aceptado (se recibió ACK), False en caso contrario</returns>
    private async Task<bool> SendCommandAsync(IPAddress ip, int port, string command, TimeSpan timeout)
    {
        UdpClient? client = null;
        try
        {
            client = new UdpClient();
            client.Client.ReceiveTimeout = (int)timeout.TotalMilliseconds;

            IPEndPoint remoteEndPoint = new IPEndPoint(ip, port);
            byte[] commandBytes = Encoding.UTF8.GetBytes(command);

            // Enviar comando de forma asíncrona
            await client.SendAsync(commandBytes, commandBytes.Length, remoteEndPoint);

            // Esperar respuesta ACK de forma asíncrona con timeout
            using (var cts = new CancellationTokenSource(timeout))
            {
                try
                {
                    var receiveTask = client.ReceiveAsync();
                    var timeoutTask = Task.Delay(timeout, cts.Token);
                    
                    var completedTask = await Task.WhenAny(receiveTask, timeoutTask);
                    
                    if (completedTask == timeoutTask)
                    {
                        Console.WriteLine($"Timeout enviando comando {command} a {ip}:{port}");
                        return false;
                    }
                    
                    var result = await receiveTask;
                    string response = Encoding.UTF8.GetString(result.Buffer).Trim();

                    if (response == ACK_MESSAGE)
                    {
                        return true;
                    }
                    else if (response == ERROR_MESSAGE)
                    {
                        Console.WriteLine($"El esclavo {ip}:{port} respondió con ERROR para el comando {command}");
                        return false;
                    }

                    Console.WriteLine($"Respuesta inesperada del esclavo {ip}:{port}: {response}");
                    return false;
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine($"Timeout enviando comando {command} a {ip}:{port}");
                    return false;
                }
            }
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
        {
            Console.WriteLine($"Timeout enviando comando {command} a {ip}:{port}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error enviando comando {command} a {ip}:{port}: {ex.Message}");
            return false;
        }
        finally
        {
            client?.Close();
            client?.Dispose();
        }
    }
}

