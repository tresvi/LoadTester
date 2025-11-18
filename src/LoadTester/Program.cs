using IBM.WMQ;
using NBomber.CSharp;
using System.Collections;

namespace LoadTester
{
    class Program
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

            var operacionService = new OperacionService();

            MQQueueManager qmgr = new MQQueueManager("MQGD", properties);
            const string mensaje = "Carga NBomber MQ";
            const string outputQueue = "BNA.XX1.PEDIDO";
            const string inputQueue = "BNA.XX1.PEDIDO";

            var scenario = Scenario.Create("http_scenario", async context =>
            {
                try
                {
                    /*var resultado = await operacionService.Ejecutar();
                    return resultado == 1 ? Response.Ok() : Response.Fail();*/

                    double ms = Plugins.IbmMQPlugin.EnviarRecibir(qmgr, outputQueue, inputQueue, mensaje);
                    return Response.Ok(statusCode: "200", sizeBytes: mensaje.Length, customLatencyMs: ms);
                }
                catch
                {
                    return Response.Fail();
                }
            })
            .WithoutWarmUp()
            .WithLoadSimulations(
                Simulation.Inject(rate: 10, 
                                  interval: TimeSpan.FromSeconds(1), 
                                  during: TimeSpan.FromSeconds(30))
            );

            NBomberRunner
                .RegisterScenarios(scenario)
                .Run();
        }
    }
}
