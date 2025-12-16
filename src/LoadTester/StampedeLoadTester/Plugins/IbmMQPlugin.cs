using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using IBM.WMQ;

namespace LoadTester.Plugins
{
    public class IbmMQPlugin
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

                Stopwatch swAccessQueue = new Stopwatch();
                swAccessQueue.Start();

                queueOut = qmgr.AccessQueue(outputQueueName, openOutOptions);
                queueIn = qmgr.AccessQueue(inputQueueName, openInOptions);
                swAccessQueue.Stop();
                Console.WriteLine($"AccesQueue {swAccessQueue.ElapsedMilliseconds}");

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
                double cicloMs = (t1 - t0) * 1000.0 / Stopwatch.Frequency;
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


        public static (double tiempoMs, byte[] messageId) EnviarMensaje(MQQueueManager qmgr, string queueName, string texto)
        {
            MQQueue? cola = null;
            Stopwatch sw = new Stopwatch();
            try
            {
                // Abrir la cola con opción de salida (PUT)
                int openOptions = MQC.MQOO_OUTPUT;
                cola = qmgr.AccessQueue(queueName, openOptions);
                
                (double tiempoMs, byte[] messageId) resultado = EnviarMensaje(cola, texto);
                return resultado;
            }
            catch (MQException mqe)
            {
                Console.WriteLine("Error MQ al hacer PUT:");
                Console.WriteLine($"  Reason  : {mqe.ReasonCode}");
                Console.WriteLine($"  Message : {mqe.Message}");
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error general al hacer PUT:");
                Console.WriteLine($"  {ex.Message}");
                throw;
            }
            finally
            {
                // Cierre ordenado de la cola
                cola?.Close();
            }
        }

        public static (double tiempoMs, byte[] messageId) EnviarMensaje(MQQueue queue, string texto)
        {
            if (queue is null) throw new ArgumentNullException(nameof(queue));

            // Crear el mensaje MQ
            MQMessage mensaje = new MQMessage
            {
                Format = MQC.MQFMT_STRING,
                MessageId = MQC.MQMI_NONE,
                CorrelationId = MQC.MQCI_NONE
            };

            mensaje.WriteString(texto);

            // Opciones de envío: no persistente, sin espera, que asigne nuevo MessageID
            MQPutMessageOptions pmo = new MQPutMessageOptions
            {
                Options = MQC.MQPMO_NO_SYNCPOINT | MQC.MQPMO_NEW_MSG_ID
            };

           // long t0 = Stopwatch.GetTimestamp();
            queue.Put(mensaje, pmo);
           // long t1 = Stopwatch.GetTimestamp();

            // Obtener el MessageID asignado por MQ
           // byte[] messageId = null; //new byte[24];
            //Array.Copy(mensaje.MessageId, messageId, 24);

            //return ((t1 - t0) * 1000.0 / Stopwatch.Frequency, messageId);
            return (0, null!);
        }


        public static double RecibirMensaje(MQQueueManager qmgr, string queueName, byte[]? correlationId = null)
        {
            MQQueue? cola = null;
            try
            {
                int openOptions = MQC.MQOO_INPUT_AS_Q_DEF;
                cola = qmgr.AccessQueue(queueName, openOptions);
                double ms = RecibirMensaje(cola, correlationId);
                return ms;
            }
            catch (MQException mqe) when (mqe.ReasonCode == MQC.MQRC_NO_MSG_AVAILABLE)
            {
                Console.WriteLine("⚠️  No hay mensajes disponibles.");
                throw;
            }
            catch (MQException mqe)
            {
                Console.WriteLine($"Error MQ GET: Reason={mqe.ReasonCode} Msg={mqe.Message}");
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error general GET: {ex.Message}");
                throw;
            }
            finally
            {
                cola?.Close();
            }
        }

        public static double RecibirMensaje(MQQueue queue, byte[]? correlationId = null)
        {
            if (queue is null) throw new ArgumentNullException(nameof(queue));

            var msg = new MQMessage
            {
                CharacterSet = 1208,           // <— pedir UTF-8
                Format = MQC.MQFMT_STRING,
                MessageId = MQC.MQMI_NONE
            };

            // Si se proporciona CorrelationID, usarlo para hacer match
            if (correlationId != null && correlationId.Length == 24)
            {
                msg.CorrelationId = correlationId;
            }
            else
            {
                msg.CorrelationId = MQC.MQCI_NONE;
            }

            var gmo = new MQGetMessageOptions
            {
                Options = MQC.MQGMO_WAIT | MQC.MQGMO_CONVERT,
                WaitInterval = 5000,
                MatchOptions = correlationId != null ? MQC.MQMO_MATCH_CORREL_ID : MQC.MQMO_NONE
            };

            long t0 = Stopwatch.GetTimestamp();
            queue.Get(msg, gmo);
            long t1 = Stopwatch.GetTimestamp();

            string contenido = msg.ReadString(msg.MessageLength);
            //Console.WriteLine($"✅ GET: {contenido}");
            return (t1 - t0) * 1000.0 / Stopwatch.Frequency;
        }


        public static int GetDepth(MQQueueManager queueMgr, string queueName)
        {
            MQQueue queue = OpenOutputQueue(queueMgr, queueName, true);
            return queue.CurrentDepth;
        }


        public static int GetMaxDepth(MQQueueManager queueMgr, string queueName)
        {
            MQQueue queue = OpenOutputQueue(queueMgr, queueName, true);
            return queue.MaximumDepth;
        }


        public static MQQueue OpenOutputQueue(MQQueueManager qManager, string QueueName, bool Inquire)
        {
            MQQueue q;

            //Abrir una cola de salida (para hacer put)
            if (Inquire)
                q = qManager.AccessQueue(QueueName, MQC.MQOO_OUTPUT + MQC.MQOO_FAIL_IF_QUIESCING + MQC.MQOO_INQUIRE);
            else
                q = qManager.AccessQueue(QueueName, MQC.MQOO_OUTPUT + MQC.MQOO_FAIL_IF_QUIESCING);

            return q;
        }


        public static MQQueue OpenInputQueue(MQQueueManager qManager, string QueueName)
        {
            MQQueue q;
            //Abrir la cola de entrada (para hacer get).
            q = qManager.AccessQueue(QueueName, MQC.MQOO_INPUT_SHARED + MQC.MQOO_FAIL_IF_QUIESCING);
            return q;
        }

        /// <summary>
        /// Vacía una cola realizando GETs de la manera más rápida posible usando múltiples hilos.
        /// Retorna la cantidad de mensajes eliminados por segundo.
        /// </summary>
        /// <param name="qmgr">Queue Manager de IBM MQ</param>
        /// <param name="queueName">Nombre de la cola a vaciar</param>
        /// <param name="numHilos">Número de hilos a usar para paralelizar. Si es null, usa Environment.ProcessorCount</param>
        /// <returns>Cantidad de mensajes eliminados por segundo (float)</returns>
        public static float VaciarCola(MQQueueManager qmgr, string queueName, int? numHilos = null)
        {
            int hilos = numHilos ?? Environment.ProcessorCount;
            int mensajesEliminados = 0;
            long inicioTicks = Stopwatch.GetTimestamp();

            // Opciones de GET optimizadas para velocidad máxima
            var gmo = new MQGetMessageOptions
            {
                Options =
                    MQC.MQGMO_NO_WAIT |          // Sin espera, falla inmediatamente si no hay mensaje
                    MQC.MQGMO_NO_PROPERTIES |    // No traer propiedades del mensaje (más rápido)
                    MQC.MQGMO_FAIL_IF_QUIESCING,
                MatchOptions = MQC.MQMO_NONE      // Sin match, tomar el siguiente mensaje disponible
            };

            try
            {
                // Paralelizar los GETs usando múltiples hilos
                Parallel.For(0, hilos, hiloIndex =>
                {
                    MQQueue? queue = null;
                    try
                    {
                        // Cada hilo abre su propia conexión a la cola
                        int openOptions = MQC.MQOO_INPUT_SHARED | MQC.MQOO_FAIL_IF_QUIESCING;
                        queue = qmgr.AccessQueue(queueName, openOptions);

                        // Reutilizar el objeto mensaje para evitar allocaciones
                        MQMessage msg = new MQMessage();

                        int mensajesHilo = 0;

                        // Loop hasta que la cola esté vacía
                        while (true)
                        {
                            try
                            {
                                // Limpiar el mensaje antes de cada GET
                                msg.ClearMessage();
                                msg.MessageId = MQC.MQMI_NONE;
                                msg.CorrelationId = MQC.MQCI_NONE;

                                // Intentar hacer GET
                                queue.Get(msg, gmo);
                                mensajesHilo++;
                            }
                            catch (MQException mqe) when (mqe.ReasonCode == MQC.MQRC_NO_MSG_AVAILABLE)
                            {
                                // No hay más mensajes, este hilo terminó
                                break;
                            }
                        }

                        // Sumar al contador total de forma thread-safe
                        Interlocked.Add(ref mensajesEliminados, mensajesHilo);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error en hilo {hiloIndex} al vaciar la cola {queueName}: {ex.Message}");
                    }
                    finally
                    {
                        queue?.Close();
                    }
                });

                long finTicks = Stopwatch.GetTimestamp();
                double tiempoSegundos = (finTicks - inicioTicks) / (double)Stopwatch.Frequency;
                if (tiempoSegundos == 0) tiempoSegundos = 0.000001;

                return (float)(mensajesEliminados / tiempoSegundos);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al vaciar la cola {queueName}: {ex.Message}");
                throw;
            }
        }

    }
}
