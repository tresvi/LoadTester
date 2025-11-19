using IBM.WMQ;
using LoadTester.Plugins;
using System.Collections;


namespace MQQueueMonitor
{
    internal class Program
    {
        static void Main(string[] args)
        {
            
            if (args.Length != 5)
            {
                Console.WriteLine("Error: Se requieren 5 parámetros obligatorios:");
                Console.WriteLine("Uso: MQQueueMonitor.exe <IP> <Puerto> <NombreCola> <Channel> <ManagerName>");
                Console.WriteLine("Ejemplo: MQQueueMonitor.exe 10.6.248.10 1414 BNA.XX1.PEDIDO CHANNEL1 MQGD");
                Environment.Exit(1);
            }

            string ip = args[0];
            string queueName = args[2];
            string channel = args[3];
            string managerName = args[4];

            if (!int.TryParse(args[1], out int port))
            {
                Console.WriteLine($"Error: El puerto '{args[1]}' no es un número entero válido.");
                Environment.Exit(1);
            }

            if (port < 1 || port > 65535)
            {
                Console.WriteLine($"Error: El puerto debe estar entre 1 y 65535. Valor recibido: {port}");
                Environment.Exit(1);
            }

            var properties = new Hashtable
            {
                { MQC.HOST_NAME_PROPERTY, ip },
                { MQC.PORT_PROPERTY, port },
                { MQC.CHANNEL_PROPERTY, channel },/*
                { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_CLIENT } // conexión remota tipo cliente
               */
            };

            try
            {
                MQQueueManager queueMgr = new MQQueueManager(managerName, properties);
                string outputQueue = queueName;

                while (true)
                {
                    int depth = IbmMQPlugin.GetDepth(queueMgr, outputQueue);
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss} \t- Profundidad de cola {outputQueue}: {depth}");
                    Thread.Sleep(1000);
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

        }
    }
}
