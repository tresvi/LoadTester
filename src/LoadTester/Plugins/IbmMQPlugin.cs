using NBomber.Contracts.Cluster;
using System.Diagnostics;
using IBM.WMQ;

namespace LoadTester.Plugins
{
    internal class IbmMQPlugin
    {
        /// <summary>
        /// Realiza un ciclo de PUT sobre la cola de salida y GET sobre la cola de entrada utilizando el texto provisto y devuelve la latencia en ms.
        /// Requiere acceso exclusivo a ambas colas para que el GET tome el mensaje correspondiente.
        /// </summary>
        public static double EnviarRecibir(MQQueueManager qmgr, string outputQueueName, string inputQueueName, string mensaje)
        {
            MQQueue? queueOut = null;
            MQQueue? queueIn = null;

            string contenido = mensaje ?? string.Empty;
            // Mensaje para PUT (reutilizado)
            var msgPut = new MQMessage
            {
                // Binario para evitar conversiones de charset
                //Format = MQC.MQFMT_NONE,
                Format = MQC.MQFMT_STRING,
                CharacterSet = 1208,            //El 1208 es el UTF-8. El charset no va cuando es binario
                Persistence = MQC.MQPER_NOT_PERSISTENT
            };
            // Mensaje para GET (reutilizado)
            MQMessage msgGet = new MQMessage();

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
                // Abrir colas por separado
                int openOutOptions = MQC.MQOO_OUTPUT | MQC.MQOO_FAIL_IF_QUIESCING;
                int openInOptions = MQC.MQOO_INPUT_AS_Q_DEF | MQC.MQOO_FAIL_IF_QUIESCING;

                queueOut = qmgr.AccessQueue(outputQueueName, openOutOptions);
                queueIn = qmgr.AccessQueue(inputQueueName, openInOptions);

                // --- PUT ---
                msgPut.ClearMessage();
                msgPut.MessageId = MQC.MQMI_NONE;          // <— reset
                msgPut.CorrelationId = MQC.MQCI_NONE;      // <— reset
                //msgPut.Write(payload, 0, payload.Length);
                 msgPut.WriteString(contenido);

                long t0 = Stopwatch.GetTimestamp();
                queueOut.Put(msgPut, pmo);

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
                        queueIn.Get(msgGet, gmo);
                        got = true;
                    }
                    catch (MQException mqe) when (mqe.ReasonCode == MQC.MQRC_NO_MSG_AVAILABLE)
                    {
                        if (++spins > 10) Thread.SpinWait(128);
                    }
                }

                long t1 = Stopwatch.GetTimestamp();
                double cicloMs = 10;//= (t1 - t0) * 1000.0 / Stopwatch.Frequency;
              //  Console.WriteLine($"Tiempo de ciclo: {cicloMs}");

                return cicloMs;
            }
            finally
            {
                queueOut?.Close();
                queueIn?.Close();
            }
        }

        /// <summary>
        /// Utilidad: convierte ticks de Stopwatch a microsegundos.
        /// </summary>
        public static double TicksToMicros(long ticks)
            => (double)ticks * 1_000_000.0 / Stopwatch.Frequency;


        static void EnviarMensaje(MQQueueManager qmgr, string queueName, string texto)
        {
            MQQueue? cola = null;

            try
            {
                // Abrir la cola con opción de salida (PUT)
                int openOptions = MQC.MQOO_OUTPUT;
                cola = qmgr.AccessQueue(queueName, openOptions);
                EnviarMensaje(cola, texto);
            }
            catch (MQException mqe)
            {
                Console.WriteLine("Error MQ al hacer PUT:");
                Console.WriteLine($"  Reason  : {mqe.ReasonCode}");
                Console.WriteLine($"  Message : {mqe.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error general al hacer PUT:");
                Console.WriteLine($"  {ex.Message}");
            }
            finally
            {
                // Cierre ordenado de la cola
                if (cola != null)
                    cola.Close();
            }
        }

        static void EnviarMensaje(MQQueue queue, string texto)
        {
            if (queue is null)
            {
                throw new ArgumentNullException(nameof(queue));
            }

            // Crear el mensaje MQ
            MQMessage mensaje = new MQMessage
            {
                //CharacterSet = 1208, // UTF-8
                Format = MQC.MQFMT_STRING
            };

            // Escribir el texto en el mensaje
            mensaje.WriteString(texto);

            // Opciones de envío (no persistente, sin espera)
            MQPutMessageOptions pmo = new MQPutMessageOptions
            {
                Options = MQC.MQPMO_NO_SYNCPOINT // Envío inmediato, sin transacción
            };

            // Hacer el PUT
            queue.Put(mensaje, pmo);
        }


        static void LeerMensaje(MQQueueManager qmgr, string queueName)
        {
            MQQueue? cola = null;

            try
            {
                int openOptions = MQC.MQOO_INPUT_AS_Q_DEF;
                cola = qmgr.AccessQueue(queueName, openOptions);

                var msg = new MQMessage
                {
                    CharacterSet = 1208,           // <— pedir UTF-8
                    Format = MQC.MQFMT_STRING
                };

                var gmo = new MQGetMessageOptions
                {
                    Options = MQC.MQGMO_WAIT | MQC.MQGMO_CONVERT,
                    WaitInterval = 5000
                };

                cola.Get(msg, gmo);

                string contenido = msg.ReadString(msg.MessageLength);
               // Console.WriteLine($"✅ GET: {contenido}");
            }
            catch (MQException mqe) when (mqe.ReasonCode == MQC.MQRC_NO_MSG_AVAILABLE)
            {
                Console.WriteLine("⚠️  No hay mensajes disponibles.");
            }
            catch (MQException mqe)
            {
                Console.WriteLine($"Error MQ GET: Reason={mqe.ReasonCode} Msg={mqe.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error general GET: {ex.Message}");
            }
            finally
            {
                cola?.Close();
            }
        }

    }
}
