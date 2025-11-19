using IBM.WMQ;
using LoadTester.Plugins;
using System.Collections;

namespace MQQueueMonitor
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var properties = new Hashtable
            {
                { MQC.HOST_NAME_PROPERTY, "192.168.0.15" },
                //{ MQC.HOST_NAME_PROPERTY, "10.6.248.10" },
                { MQC.PORT_PROPERTY, 1414 },
                { MQC.CHANNEL_PROPERTY, "CHANNEL1" },/*
                { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_CLIENT } // conexión remota tipo cliente
               */
            };

            MQQueueManager queueMgr = new MQQueueManager("MQGD", properties);
            const string mensaje = "Carga NBomber MQ";
            const string outputQueue = "BNA.XX1.PEDIDO";
            const string inputQueue = "BNA.XX1.PEDIDO";
            int contador = 0;

            while (true)
            {
                int depth = IbmMQPlugin.GetDepth(queueMgr, outputQueue);
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} \t- Profundidad de cola {outputQueue}: {depth}");
                Thread.Sleep(1000);
            }

        }
    }
}
