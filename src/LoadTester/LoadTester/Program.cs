using IBM.WMQ;
using NBomber.CSharp;
using System.Collections;
using LoadTester.Plugins;

///Ejemplo invocacion
///DESA:  LoadTester.exe 10.6.248.10 1414 CHANNEL1 MQGD "C:\Users\d67784\Documents\GitHub\BNA\Cliente_HCS\ClienteHCS_2\bin\Debug\EjemploTransaccionesHCS\CU1_TRX500.hcs"
///TEST:  LoadTester.exe 10.6.248.10 1514 CHANNEL1 MQGQ ".\EjemplosTransacciones\CU1_TRX500.hcs"

namespace LoadTester
{
    class Program
    {
        
        static void Main(string[] args)
        {
            //args = new string[] { "10.6.248.10", "1414", "CHANNEL1", "MQGD", @"C:\Users\d67784\Documents\GitHub\BNA\Cliente_HCS\ClienteHCS_2\bin\Debug\EjemploTransaccionesHCS\CU1_TRX500.hcs"};

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

            //100 VUs/seg fijos durante 30 segundos. Es decir, se garantiza que haya 100 request en cada segundo.
            var scenario1 = Scenarios.ScenarioEnviarRecibir(qmgr, outputQueue, inputQueue, mensaje)
                     .WithWarmUpDuration(TimeSpan.FromSeconds(5))
                     .WithLoadSimulations(
                                         Simulation.Inject(rate: 100,
                                         interval: TimeSpan.FromSeconds(1),
                                         during: TimeSpan.FromSeconds(30))
                 );

            var scenario2 = Scenarios.ScenarioSoloEnviar(qmgr, outputQueue, mensaje)
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
