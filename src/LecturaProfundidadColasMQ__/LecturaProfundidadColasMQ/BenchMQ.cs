using IBM.WMQ;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LecturaProfundidadColasMQ
{
    public static class BenchMQ
    {
        /// <summary>
        /// Realiza N ciclos de PUT+GET sobre la misma cola y devuelve los tiempos (en ticks) de cada ciclo.
        /// Requiere acceso exclusivo a la cola para que el GET tome el mensaje recién puesto.
        /// </summary>
        public static long[] EnviarRecibir(MQQueueManager qmgr, string queueName, int iterations = 1000, int payloadBytes = 1)
        {
            MQQueue queue = null;
            var lat = new long[iterations];

            // Payload fijo y reutilizable
            var payload = new byte[payloadBytes]; // si querés, llenalo con datos no cero
                                                  // Mensaje para PUT (reutilizado)
            var msgPut = new MQMessage
            {
                // Binario para evitar conversiones de charset
                Format = MQC.MQFMT_NONE,
                Persistence = MQC.MQPER_NOT_PERSISTENT
            };
            // Mensaje para GET (reutilizado)
            var msgGet = new MQMessage();

            // Opciones rápidas de PUT y GET
            var pmo = new MQPutMessageOptions
            {
                Options =
                    MQC.MQPMO_NO_SYNCPOINT |   // no transacción
                    MQC.MQPMO_NEW_MSG_ID |   // que asigne ID el QM
                    MQC.MQPMO_NO_CONTEXT    // evita costos de contexto
            };

            var gmo = new MQGetMessageOptions
            {
                Options =
                    MQC.MQGMO_NO_WAIT |  // latencia mínima (spin tuyo)
                    MQC.MQGMO_NO_PROPERTIES |  // no traigas propiedades
                    MQC.MQGMO_FAIL_IF_QUIESCING,
                            MatchOptions = MQC.MQMO_NONE     // importante: sin match
            };

            try
            {
                // Abrir una sola vez con INPUT + OUTPUT
                int openOptions = MQC.MQOO_OUTPUT | MQC.MQOO_INPUT_AS_Q_DEF | MQC.MQOO_FAIL_IF_QUIESCING;
                queue = qmgr.AccessQueue(queueName, openOptions);


                // --- WARM-UP (calienta JIT y buffers MQ) ---
                for (int w = 0; w < 5; w++)
                {
                    msgPut.ClearMessage();
                    msgPut.MessageId = MQC.MQMI_NONE;
                    msgPut.CorrelationId = MQC.MQCI_NONE;
                    msgPut.Write(payload, 0, payload.Length);
                    queue.Put(msgPut, pmo);

                    msgGet.ClearMessage();
                    msgGet.MessageId = MQC.MQMI_NONE;
                    msgGet.CorrelationId = MQC.MQCI_NONE;

                    bool got = false;
                    int spins = 0;
                    while (!got)
                    {
                        try
                        {
                            queue.Get(msgGet, gmo);
                            got = true;
                        }
                        catch (MQException mqe) when (mqe.ReasonCode == MQC.MQRC_NO_MSG_AVAILABLE)
                        {
                            if (++spins > 10)
                                Thread.SpinWait(128); // microespera para dar tiempo al QM
                        }
                    }
                }



                // MEDICIÓN
                for (int i = 0; i < iterations; i++)
                {
                    // --- PUT ---
                    msgPut.ClearMessage();
                    msgPut.MessageId = MQC.MQMI_NONE;          // <— reset
                    msgPut.CorrelationId = MQC.MQCI_NONE;      // <— reset
                    msgPut.Write(payload, 0, payload.Length);

                    long t0 = Stopwatch.GetTimestamp();
                    queue.Put(msgPut, pmo);

                    // --- GET ---
                    bool got = false;
                    int spins = 0;

                    // limpiar SIEMPRE el mensaje de lectura y los IDs
                    msgGet.ClearMessage();
                    msgGet.MessageId = MQC.MQMI_NONE;          // <— reset
                    msgGet.CorrelationId = MQC.MQCI_NONE;      // <— reset

                    while (!got)
                    {
                        try
                        {
                            queue.Get(msgGet, gmo);
                            got = true;
                        }
                        catch (MQException mqe) when (mqe.ReasonCode == MQC.MQRC_NO_MSG_AVAILABLE)
                        {
                            if (++spins > 10) Thread.SpinWait(128);
                        }
                    }

                    long t1 = Stopwatch.GetTimestamp();
                    lat[i] = t1 - t0;
                }

                return lat;
            }
            finally
            {
                queue?.Close();
            }
        }

        /// <summary>
        /// Utilidad: convierte ticks de Stopwatch a microsegundos.
        /// </summary>
        public static double TicksToMicros(long ticks)
            => (double)ticks * 1_000_000.0 / Stopwatch.Frequency;
    }
}
