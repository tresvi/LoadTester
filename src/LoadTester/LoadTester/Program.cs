using IBM.WMQ;
using NBomber.CSharp;
using System.Collections;
using System.Numerics;
using LoadTester.Plugins;
using static System.Runtime.InteropServices.JavaScript.JSType;

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

            //100 VUs/seg fijos durante 30 segundos. Es decir, se garantiza que haya 100 request en cada segundo.
            var scenario1 = Scenarios.ScenarioEnviarRecibir(qmgr, OUTPUT_QUEUE, INPUT_QUEUE, MENSAJE)
                     .WithWarmUpDuration(TimeSpan.FromSeconds(5))
                     .WithLoadSimulations(
                                         Simulation.Inject(rate: 100,
                                         interval: TimeSpan.FromSeconds(1),
                                         during: TimeSpan.FromSeconds(30))
                 );

            var scenario2 = Scenarios.ScenarioSoloEnviar(qmgr, OUTPUT_QUEUE, MENSAJE)
              .WithWarmUpDuration(TimeSpan.FromSeconds(5))
              .WithLoadSimulations(
                                  Simulation.Inject(rate: 100,
                                  interval: TimeSpan.FromSeconds(1),
                                  during: TimeSpan.FromSeconds(30))
          );

            NBomberRunner
                .RegisterScenarios(scenario1)
                .Run();
        }
    }
}
