using IBM.WMQ;
using NBomber.CSharp;
using System.Collections;
using LoadTester.Plugins;
using System.Text;
using System.Diagnostics;
using System.Reflection;

///Ejemplo invocacion
///DESA:  LoadTester.exe 10.6.248.10 1414 CHANNEL1 MQGD "C:\Users\d67784\Documents\GitHub\BNA\Cliente_HCS\ClienteHCS_2\bin\Debug\EjemploTransaccionesHCS\CU1_TRX500.hcs"
///TEST:  LoadTester.exe 10.6.248.10 1514 CHANNEL1 MQGQ ".\EjemplosTransacciones\CU1_TRX500.hcs"

namespace LoadTester
{
    class Program
    {
        
        static void Main(string[] args)
        {
            args = new string[] { "10.6.248.10", "1414", "CHANNEL1", "MQGD", @"C:\Users\d67784\Documents\GitHub\BNA\Cliente_HCS\ClienteHCS_2\bin\Debug\EjemploTransaccionesHCS\CU1_TRX500.hcs"};
            //args = new string[] { "10.6.248.10", "1414", "CHANNEL1", "MQGD", @"C:\Users\d67784\Documents\GitHub\BNA\Cliente_HCS\ClienteHCS_2\bin\Debug\EjemploTransaccionesHCS\CU2_TRX559.hcs" };

            if (args.Length < 5)
            {
                Console.Error.WriteLine("ERROR: Debe proporcionar cinco parámetros: IP, Port, CHANNEL, ManagerName y nombre de archivo.");
                Environment.Exit(1);
            }

            string ip = args[0];
            if (string.IsNullOrWhiteSpace(ip))
            {
                Console.Error.WriteLine("ERROR: El parámetro IP no puede estar vacío.");
                Environment.Exit(1);
            }

            if (!int.TryParse(args[1], out int port) || port <= 0)
            {
                Console.Error.WriteLine($"ERROR: El parámetro Port debe ser un número entero positivo válido. Valor recibido: {args[1]}");
                Environment.Exit(1);
            }

            string channel = args[2];
            if (string.IsNullOrWhiteSpace(channel))
            {
                Console.Error.WriteLine("ERROR: El parámetro CHANNEL no puede estar vacío.");
                Environment.Exit(1);
            }

            string managerName = args[3];
            if (string.IsNullOrWhiteSpace(managerName))
            {
                Console.Error.WriteLine("ERROR: El parámetro ManagerName no puede estar vacío.");
                Environment.Exit(1);
            }

            string nombreArchivo = args[4];
            if (!File.Exists(nombreArchivo))
            {
                Console.Error.WriteLine($"ERROR: El archivo '{nombreArchivo}' no existe.", nombreArchivo);
                Environment.Exit(1);
            }
            string contenidoArchivo = File.ReadAllText(nombreArchivo);
            string[] columnas = contenidoArchivo.Split("|@|", StringSplitOptions.None);

            if (columnas.Length < 2)
            {
                Console.Error.WriteLine("ERROR: El archivo debe contener al menos dos columnas separadas por '|@|'.");
                Environment.Exit(1);
            }
            string cola = columnas[0];
            string mensaje = columnas[1];

            string outputQueue = $"BNA.{cola}.PEDIDO";
            string inputQueue = $"BNA.{cola}.RESPUESTA";

            Console.WriteLine($"PARAMETROS. ip: {ip}, port: {port}, channel: {channel}, managername: {managerName}, file: {nombreArchivo}");

            var properties = new Hashtable
            {
                { MQC.HOST_NAME_PROPERTY, ip },
                { MQC.PORT_PROPERTY, port },
                { MQC.CHANNEL_PROPERTY, channel },/*
                { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_CLIENT } // conexión remota tipo cliente
               */
            };

            MQQueueManager qmgr = null;
            try
            {
                qmgr = new MQQueueManager(managerName, properties);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR: al conectarse al manager {ex.Message}");
                Environment.Exit(1);
            }

            MQQueue inquireQueue = IbmMQPlugin.OpenOutputQueue(qmgr, outputQueue, true);
            int profundidad = inquireQueue.CurrentDepth;

            /*
            IbmMQPlugin.EnviarMensaje(qmgr, outputQueue, mensaje);
            IbmMQPlugin.EnviarMensaje(qmgr, outputQueue, mensaje);
            Stopwatch swEnviarMensaje = Stopwatch.StartNew();
            IbmMQPlugin.EnviarMensaje(qmgr, outputQueue, mensaje);
            swEnviarMensaje.Stop();
            Console.WriteLine($"Demora {swEnviarMensaje.ElapsedMilliseconds} ms de EnviarMensaje, profundidad inicial: {profundidad}");
            profundidad = 0;

            Thread.Sleep(4000);

            Stopwatch sw = new Stopwatch();
            sw.Start();
            int i;
            for (i=0 ; i < 4000; i++)
            {
                if (sw.ElapsedMilliseconds < (1000 - swEnviarMensaje.ElapsedMilliseconds))
                {
                    IbmMQPlugin.EnviarMensaje(qmgr, outputQueue, mensaje);
                    // Console.WriteLine($"{i} - {tiempoEnvio} - {Encoding.UTF8.GetString(messageId)}");
                }
                else
                {
                    sw.Stop();
                    profundidad = inquireQueue.CurrentDepth;
                    break;
                }
            }
            
            sw.Stop();
            Console.WriteLine($"FIN: Tardó {sw.ElapsedMilliseconds} - Profundidad: {profundidad} - Msjes colocados: {i}");
            qmgr.Close();
            Environment.Exit(0);
            */
            //31 de Octubre: 345MB en colas MQ, 
            /*
             *En la CU2 respuesta hubo un estimado de 337.200 PUTs , 385MB en total y el mensaje mas grande fue de 1,45MB
             * 1145 Bytes es el tamaño maximo del mensaje 337 200 mensajes estimados
             * 20M transacciones 4M de login en 2HS en la 1ra semana de octubre.
            5,9 pedido + 2.3 de espera en salida
            El Soap UI de Guille, llega a 125 en la 559 y a 360 en 500. 280 cuando usa los 4 en una situacion fuera de lo comun
            */
            // Escenario con ramp up, período constante y ramp down usando RampingConstant
            var scenario1 = Scenarios.ScenarioEnviarRecibir(qmgr, outputQueue, inputQueue, mensaje)
                     .WithWarmUpDuration(TimeSpan.FromSeconds(5))
                     .WithLoadSimulations(/*
                         // Ramp up: aumentar de 0 a 1200 copias en 30 segundos
                         Simulation.RampingConstant(
                            copies: 1000,
                            during: TimeSpan.FromSeconds(30)
                         ),*/
                         // Período constante: mantener 1200 copias durante 60 segundos
                         Simulation.KeepConstant(
                            copies: 5,
                            during: TimeSpan.FromSeconds(100)
                         )/*,
                         // Ramp down: disminuir de 1200 a 0 copias en 30 segundos
                         Simulation.RampingConstant(
                            copies: 0,
                            during: TimeSpan.FromSeconds(30)
                         )*/
                 );

            var scenario2 = Scenarios.ScenarioSoloEnviar(qmgr, outputQueue, mensaje)
              .WithWarmUpDuration(TimeSpan.FromSeconds(5))
              .WithLoadSimulations(
                                  Simulation.Inject(rate: 2000,
                                  interval: TimeSpan.FromSeconds(1),
                                  during: TimeSpan.FromSeconds(30))
          );

            NBomberRunner
                .RegisterScenarios(scenario1)
                .Run();
        }
    }
}
