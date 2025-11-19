using NBomber.CSharp;
using NBomber.Contracts;
using IBM.WMQ;

namespace LoadTester.Plugins
{
    public static class Scenarios
    {
        /// <summary>
        /// Escenario 1: Envío y recepción de mensajes MQ
        /// Mide el tiempo total de envío + recepción
        /// </summary>
        public static ScenarioProps ScenarioEnviarRecibir(MQQueueManager qmgr, string outputQueue, string inputQueue, string mensaje)
        {
            return Scenario.Create("ibmmq_send_receive", async context =>
            {
                try
                {
                    double tiempoEnvio = IbmMQPlugin.EnviarMensaje(qmgr, outputQueue, mensaje);
                    double tiempoRecepcion = IbmMQPlugin.RecibirMensaje(qmgr, inputQueue);
                    double tiempoTotal = tiempoRecepcion;       //Solo el tiempo E2E (descarta tiempo de PUT)

                    return Response.Ok(statusCode: "200", sizeBytes: mensaje.Length, customLatencyMs: tiempoTotal);
                }
                catch (Exception ex)
                {
                    return Response.Fail(ex);
                }
            });
        }

        /// <summary>
        /// Escenario 2: Solo envío de mensajes (alta carga)
        /// Prueba el throughput de escritura en la cola
        /// </summary>
        public static ScenarioProps ScenarioSoloEnviar(MQQueueManager qmgr, string outputQueue, string mensaje)
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
            });
        }

        /// <summary>
        /// Escenario 3: Solo recepción de mensajes (baja carga)
        /// Prueba el throughput de lectura con espera
        /// </summary>
        public static ScenarioProps Scenario3(MQQueueManager qmgr, string inputQueue
            , int rate, int intervalSec, int duringSec)
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
            });
        }

    }
}
