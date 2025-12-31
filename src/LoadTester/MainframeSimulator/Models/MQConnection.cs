using System.Net;

namespace MainframeSimulator.Models;

public class MQConnection
{
    public string Ip { get; set; } = string.Empty;
    public int Port { get; set; } = 0;
    public string Channel { get; set; } = string.Empty;
    public string ManagerName { get; set; } = string.Empty;


    public MQConnection() { }

    /// <summary>
    /// Parsea el string mqConnection con formato IP:Puerto:Canal:NombreManager
    /// </summary>
    /// <param name="mqConnectionString">Cadena con formato IP:Puerto:Canal:NombreManager</param>
    /// <param name="ip">IP del servidor MQ</param>
    /// <param name="port">Puerto del servidor MQ</param>
    /// <param name="channel">Nombre del canal MQ</param>
    /// <param name="managerName">Nombre del Queue Manager</param>
    /// <exception cref="ArgumentException">Se lanza si el formato es incorrecto o algún parámetro es inválido</exception>
    public void Load(string mqConnectionString)
    {
        if (string.IsNullOrWhiteSpace(mqConnectionString))
            throw new ArgumentException("El parámetro mqConnection no puede estar vacío.");

        string[] partes = mqConnectionString.Split(';');

        if (partes.Length != 4)
        {
            throw new ArgumentException(
                $"El formato de mqConnection es incorrecto. Se espera 'IP:Puerto:Canal:NombreManager' pero se recibió '{mqConnectionString}'. " +
                $"Número de partes encontradas: {partes.Length} (se esperaban 4).");
        }

        string ip = partes[0].Trim();
        if (string.IsNullOrWhiteSpace(ip))
            throw new ArgumentException("La IP del servidor MQ no puede estar vacía.");

        if (!IPAddress.TryParse(ip, out _))
            throw new ArgumentException($"La IP '{ip}' no es una dirección IP válida (IPv4 o IPv6).");

        string portStr = partes[1].Trim();
        if (string.IsNullOrWhiteSpace(portStr))
            throw new ArgumentException("El puerto no puede estar vacío.");

        if (!int.TryParse(portStr, out int port) || port < 1 || port > 65535)
            throw new ArgumentException($"El puerto '{portStr}' no es válido. Debe ser un número entre 1 y 65535.");

        string channel = partes[2].Trim();
        if (string.IsNullOrWhiteSpace(channel))
            throw new ArgumentException("El nombre del canal no puede estar vacío.");

        string managerName = partes[3].Trim();
        if (string.IsNullOrWhiteSpace(managerName))
            throw new ArgumentException("El nombre del Queue Manager no puede estar vacío.");

        this.Ip = ip;
        this.Port = port;
        this.Channel = channel;
        this.ManagerName = managerName;
    }


    /// <summary>
    /// Parsea el string de colas separadas por coma. Acepta hasta 2 colas.
    /// </summary>
    /// <param name="queuesString">Cadena con nombres de colas separadas por coma</param>
    /// <returns>Lista de nombres de colas (1 o 2 colas)</returns>
    /// <exception cref="ArgumentException">Se lanza si el formato es incorrecto o hay más de 2 colas</exception>
    public static List<string> ParseQueues(string queuesString)
    {
        if (string.IsNullOrWhiteSpace(queuesString))
            throw new ArgumentException("El parámetro queues no puede estar vacío.");

        string[] queues = queuesString.Split(';');
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

}