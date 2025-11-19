using NBomber.CSharp;
using IBM.WMQ;

namespace LoadTester.Plugins
{
    public static class Scenarios
    {
        /// <summary>
        /// Escenario 1: Envío y recepción de mensajes MQ
        /// Mide el tiempo total de envío + recepción
        /// </summary>
        public static Scenario Scenario1(MQQueueManager qmgr, string outputQueue, string inputQueue, string mensaje)
        {
            return Scenario.Create("ibmmq_scenario", async context =>
            {
                try
                {
                    double tiempoEnvio = IbmMQPlugin.EnviarMensaje(qmgr, outputQueue, mensaje);
                    double tiempoRecepcion = IbmMQPlugin.RecibirMensaje(qmgr, inputQueue);
                    double tiempoTotal = tiempoEnvio + tiempoRecepcion;

                    return Response.Ok(statusCode: "200", sizeBytes: mensaje.Length, customLatencyMs: tiempoTotal);
                }
                catch (Exception ex)
                {
                    return Response.Fail(ex);
                }
            })
            .WithWarmUpDuration(TimeSpan.FromSeconds(5))
            .WithLoadSimulations(
                Simulation.Inject(rate: 100,
                                  interval: TimeSpan.FromSeconds(1),
                                  during: TimeSpan.FromSeconds(30))
            );
        }

        /// <summary>
        /// Escenario 2: Solo envío de mensajes (alta carga)
        /// Prueba el throughput de escritura en la cola
        /// </summary>
        public static Scenario Scenario2(MQQueueManager qmgr, string outputQueue, string mensaje)
        {
            return Scenario.Create("ibmmq_send_only", async context =>
            {
                try
                {
                    double tiempoEnvio = IbmMQPlugin.EnviarMensaje(qmgr, outputQueue, mensaje);

                    return Response.Ok(statusCode: "200", sizeBytes: mensaje.Length, customLatencyMs: tiempoEnvio);
                }
                catch (Exception ex)
                {
                    return Response.Fail(ex);
                }
            })
            .WithWarmUpDuration(TimeSpan.FromSeconds(3))
            .WithLoadSimulations(
                Simulation.Inject(rate: 200,
                                  interval: TimeSpan.FromSeconds(1),
                                  during: TimeSpan.FromSeconds(60))
            );
        }

        /// <summary>
        /// Escenario 3: Solo recepción de mensajes (baja carga)
        /// Prueba el throughput de lectura con espera
        /// </summary>
        public static Scenario Scenario3(MQQueueManager qmgr, string inputQueue)
        {
            return Scenario.Create("ibmmq_receive_only", async context =>
            {
                try
                {
                    double tiempoRecepcion = IbmMQPlugin.RecibirMensaje(qmgr, inputQueue);

                    return Response.Ok(statusCode: "200", customLatencyMs: tiempoRecepcion);
                }
                catch (Exception ex)
                {
                    return Response.Fail(ex);
                }
            })
            .WithWarmUpDuration(TimeSpan.FromSeconds(2))
            .WithLoadSimulations(
                Simulation.Inject(rate: 50,
                                  interval: TimeSpan.FromSeconds(1),
                                  during: TimeSpan.FromSeconds(45))
            );
        }
    }
}
