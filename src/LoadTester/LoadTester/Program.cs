using IBM.WMQ;
using NBomber.CSharp;
using System.Collections;
using System.Numerics;
using LoadTester.Plugins;

namespace LoadTester
{
    class Program
    {
        
        static void Main(string[] args)
        {
            const string MENSAJE = "Carga NBomber MQ";
            const string OUTPUT_QUEUE = "BNA.XX1.PEDIDO";
            const string INPUT_QUEUE = "BNA.XX1.PEDIDO";


            var properties = new Hashtable
            {
                { MQC.HOST_NAME_PROPERTY, "192.168.0.15" },
                //{ MQC.HOST_NAME_PROPERTY, "10.6.248.10" },
                { MQC.PORT_PROPERTY, 1414 },
                { MQC.CHANNEL_PROPERTY, "CHANNEL1" },/*
                { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_CLIENT } // conexión remota tipo cliente
               */
            };

            MQQueueManager qmgr = new MQQueueManager("MQGD", properties);

            var scenario = Scenarios.Scenario1(qmgr, OUTPUT_QUEUE, INPUT_QUEUE, MENSAJE);

            NBomberRunner
                .RegisterScenarios(scenario)
                .Run();
        }
    }
}
