using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace StampedeLoadTester.Services;

/// <summary>
/// Clase para coordinar múltiples instancias del programa en diferentes equipos
/// mediante comunicación UDP simple (ping/pong)
/// </summary>
internal sealed class RemoteControllerService
{
    private const string PING_MESSAGE = "ping";
    private const string PONG_MESSAGE = "pong";
    private const string START_MESSAGE = "start-test";

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

            if (response == PONG_MESSAGE)
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
    /// Escucha en el puerto especificado y responde con "pong" cuando recibe "ping"
    /// Este método se queda bloqueado esperando mensajes hasta que se cancele
    /// </summary>
    /// <param name="port">Puerto en el que escuchar</param>
    /// <param name="cancellationToken">Token para cancelar la escucha</param>
    public void Listen(int port, CancellationToken cancellationToken)
    {
        UdpClient? listener = null;
        try
        {
            listener = new UdpClient(port);
            // Timeout de 1 segundo para poder verificar el CancellationToken periódicamente
            // pero el comportamiento es bloqueante esperando mensajes
            listener.Client.ReceiveTimeout = 5000;
            Console.WriteLine($"Escuchando en puerto {port}...");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    IPEndPoint? remoteEndPoint = null;
                    // Receive() se bloquea aquí esperando un mensaje (o timeout de 1 segundo)
                    byte[] receivedBytes = listener.Receive(ref remoteEndPoint);

                    string receivedMessage = Encoding.UTF8.GetString(receivedBytes);

                    if (receivedMessage == PING_MESSAGE)
                    {
                        byte[] pongBytes = Encoding.UTF8.GetBytes(PONG_MESSAGE);
                        listener.Send(pongBytes, pongBytes.Length, remoteEndPoint);
                        Console.WriteLine($"Ping recibido de {remoteEndPoint}, enviado pong");
                        // Salir del loop después de responder para continuar con el programa
                        break;
                    }
                    else
                    {
                        Console.WriteLine($"Mensaje inesperado de {remoteEndPoint}: {receivedMessage}");
                    }
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                {
                    // Timeout de 1 segundo - verificar si se canceló y continuar esperando
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    // Si no se canceló, continuar el loop y volver a esperar
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted)
                {
                    // Interrupción normal cuando se cancela
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
}

