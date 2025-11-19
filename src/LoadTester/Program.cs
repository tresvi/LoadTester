using IBM.WMQ;
using NBomber.CSharp;
using System.Collections;

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
                //{ MQC.HOST_NAME_PROPERTY, "192.168.0.15" },
                { MQC.HOST_NAME_PROPERTY, "10.6.248.10" },
                { MQC.PORT_PROPERTY, 1414 },
                { MQC.CHANNEL_PROPERTY, "CHANNEL1" },/*
                { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_CLIENT } // conexión remota tipo cliente
               */
            };

            MQQueueManager qmgr = new MQQueueManager("MQGD", properties);

            var operacionService = new OperacionService();

            var scenario = Scenario.Create("ibmmq_scenario", async context =>
            {
                try
                {
                    /*var resultado = await operacionService.Ejecutar();
                    return resultado == 1 ? Response.Ok() : Response.Fail();*/

                    //double ms = Plugins.IbmMQPlugin.EnviarRecibir(qmgr, OUTPUT_QUEUE, INPUT_QUEUE, MENSAJE);
                    double ms = Plugins.IbmMQPlugin.EnviarMensaje(qmgr, OUTPUT_QUEUE, MENSAJE);
                    double ms = Plugins.IbmMQPlugin.RecibirMensaje(qmgr, INPUT_QUEUE, MENSAJE);
                    return Response.Ok(statusCode: "200", sizeBytes: MENSAJE.Length, customLatencyMs: ms);
                }
                catch
                {
                    return Response.Fail();
                }
            })
            .WithoutWarmUp()
            .WithLoadSimulations(
                Simulation.Inject(rate: 100, 
                                  interval: TimeSpan.FromSeconds(1), 
                                  during: TimeSpan.FromSeconds(30))
            );

            NBomberRunner
                .RegisterScenarios(scenario)
                .Run();
        }
    }
}
