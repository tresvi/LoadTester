using System;
using System.Net;

namespace StampedeLoadTester.Models
{
    public class MqConnectionParams
    {
        public string MqServerIp { get; set; } = "";
        public int MqServerPort { get; set; }
        public string MqServerChannel { get; set; } = "";
        public string MqManagerName { get; set; } = "";


        /// <summary>
        /// Valida y carga la clase. El string debe tener el formato: IP:Puerto:Canal:NombreManager
        /// </summary>
        /// <returns>Una tupla con (IP, Puerto, Canal, NombreManager) si la validación es exitosa</returns>
        /// <exception cref="ArgumentException">Se lanza si el formato es incorrecto o algún parámetro es inválido</exception>
        internal void LoadMqConnectionParams(string mqConnectionString)
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

            string ip = partes[0].Trim();
            if (string.IsNullOrWhiteSpace(ip))
                throw new ArgumentException("La IP del servidor MQ no puede estar vacía.");
            
            if (!IPAddress.TryParse(ip, out IPAddress? ipAddress))
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

            this.MqServerIp = ip;
            this.MqServerPort = port;
            this.MqServerChannel = channel;
            this.MqManagerName = managerName;
        }


        public override string ToString()
        {
            return $"MQServerIp: {MqServerIp}\nMQServerPort: {MqServerPort}\nMQChannel: {MqServerChannel}\nMQManager: {MqManagerName}";
        }
    }
}

