using System;
using System.Collections;
using System.Diagnostics;
using System.Threading;
using IBM.WMQ;


namespace LecturaProfundidadColasMQ
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // Configuración de conexión al Queue Manager remoto

            var properties = new Hashtable
            {
                { MQC.HOST_NAME_PROPERTY, "192.168.0.15" },
                //{ MQC.HOST_NAME_PROPERTY, "10.6.248.10" },
                { MQC.PORT_PROPERTY, 1414 },
                { MQC.CHANNEL_PROPERTY, "CHANNEL1" },/*
                { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_CLIENT } // conexión remota tipo cliente
               */
            };

            MQQueueManager qmgr = null;
            MQQueue queue = null;

            try
            {
                // Conexión al Queue Manager
                qmgr = new MQQueueManager("MQGD", properties);
                Console.WriteLine("Conectado al Queue Manager: MQGD\n");


                Thread.CurrentThread.Priority = ThreadPriority.Highest;
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
                /*
                // Enviar un mensaje de prueba
                EnviarMensaje(qmgr, "BNA.XX1.PEDIDO", "Mensaje de prueba desde C#");
                LeerMensaje(qmgr, "BNA.XX1.PEDIDO");

                Stopwatch sw = Stopwatch.StartNew();
                EnviarMensaje(qmgr, "BNA.XX1.PEDIDO", "Mensaje de prueba desde C#");
                LeerMensaje(qmgr, "BNA.XX1.PEDIDO");
                sw.Stop();
                Console.WriteLine($"Tardó {sw.ElapsedMilliseconds}");
                */

                // Conectado el qmgr…
                var times = BenchMQ.EnviarRecibir(qmgr, "BNA.XX1.PEDIDO", iterations: 1000, payloadBytes: 32);

                // Stats simples (promedio y p95) en microsegundos
                Array.Sort(times);
                double avg = 0;
                for (int i = 0; i < times.Length; i++) avg += BenchMQ.TicksToMicros(times[i]);
                avg /= times.Length;
                double p95 = BenchMQ.TicksToMicros(times[(int)(times.Length * 0.95)]);

                Console.WriteLine($"PUT+GET avg: {avg:F2} µs | p95: {p95:F2} µs | n={times.Length}");



                // Abrir la cola con permiso de INQUIRE
                int openOptions = MQC.MQOO_INQUIRE;
                queue = qmgr.AccessQueue("BNA.XX1.PEDIDO", openOptions);

                while (true)
                {
                    // Leer los valores
                    int currentDepth = queue.CurrentDepth;
                    int maximumDepth = queue.MaximumDepth;

                    Console.WriteLine($"Cola.............: {queue.Name}");
                    Console.WriteLine($"Mensajes actuales: {currentDepth}");
                    Console.WriteLine($"Máximo permitido : {maximumDepth}");
                    Thread.Sleep(1000);
                }
            }
            catch (MQException mqe)
            {
                Console.WriteLine("Error MQ:");
                Console.WriteLine($"  Reason  : {mqe.ReasonCode}");
                Console.WriteLine($"  Message : {mqe.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error general:");
                Console.WriteLine($"  {ex.Message}");
            }
            finally
            {
                // Cierre ordenado
                if (queue != null) queue.Close();
                if (qmgr != null && qmgr.IsConnected) qmgr.Disconnect();
            }

            Console.WriteLine("\nPresione una tecla para salir...");
            Console.ReadKey();
        }


        static void EnviarMensaje(MQQueueManager qmgr, string queueName, string texto)
        {
            MQQueue cola = null;

            try
            {
                // Abrir la cola con opción de salida (PUT)
                int openOptions = MQC.MQOO_OUTPUT;
                cola = qmgr.AccessQueue(queueName, openOptions);

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
                cola.Put(mensaje, pmo);
                Console.WriteLine($"✅ Mensaje enviado correctamente a la cola '{queueName}'");
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


        static void LeerMensaje(MQQueueManager qmgr, string queueName)
        {
            MQQueue cola = null;

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
                Console.WriteLine($"✅ GET: {contenido}");
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

        /*
        public static (TimeSpan putElapsed, TimeSpan getElapsed, TimeSpan roundTrip, int bytes)
    EnviarRecibir(MQQueueManager qmgr, string queueName, int payloadBytes = 32)
        {
            MQQueue q = null;
            try
            {
                // Abrir una única vez con INPUT + OUTPUT (lo ideal es reusar 'q' fuera si medís en loop)
                int openOptions = MQC.MQOO_OUTPUT | MQC.MQOO_INPUT_AS_Q_DEF;
                q = qmgr.AccessQueue(queueName, openOptions);

                // Payload minimal sin costos de codificación
                var data = new byte[payloadBytes]; // queda en cero; si querés, podés llenar con random

                // --- PUT ---
                var msg = new MQMessage
                {
                    Format = MQC.MQFMT_NONE,                  // sin conversión
                    Persistence = MQC.MQPER_NOT_PERSISTENT    // más rápido
                };
                msg.WriteBytes(data);

                var pmo = new MQPutMessageOptions
                {
                    Options = MQC.MQPMO_NO_SYNCPOINT | MQC.MQPMO_NEW_MSG_ID | MQC.MQPMO_NEW_CORREL_ID
                };

                var sw = Stopwatch.StartNew();
                q.Put(msg, pmo);
                var putElapsed = sw.Elapsed;

                // Guardamos el MessageId que asignó MQ al mensaje
                var mid = msg.MessageId;

                // --- GET (por MessageId, sin esperar) ---
                var getMsg = new MQMessage
                {
                    Format = MQC.MQFMT_NONE
                };
                // Le decimos a MQ que queremos exactamente ese mensaje
                Buffer.BlockCopy(mid, 0, getMsg.MessageId, 0, mid.Length);

                var gmo = new MQGetMessageOptions
                {
                    Options = MQC.MQGMO_NO_WAIT,              // no bloquear
                    MatchOptions = MQC.MQMO_MATCH_MSG_ID      // match por MessageId
                };

                sw.Restart();
                q.Get(getMsg, gmo);
                var getElapsed = sw.Elapsed;

                // Leer el cuerpo en bytes (sin encoding)
                var len = getMsg.MessageLength;
                var sink = new byte[len];
                getMsg.ReadFully(ref sink);

                return (putElapsed, getElapsed, putElapsed + getElapsed, len);
            }
            catch (MQException mqe)
            {
                // Si aparece MQRC_NO_MSG_AVAILABLE en el GET, quizás otro consumidor lo tomó.
                // En entornos concurridos, asegurate de usar una cola dedicada para la prueba.
                Console.WriteLine($"MQ error: Reason={mqe.ReasonCode} '{mqe.Message}'");
                throw;
            }
            finally
            {
                q?.Close();
            }
        }
        */


    }
}
