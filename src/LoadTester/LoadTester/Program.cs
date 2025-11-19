using IBM.WMQ;
using NBomber.CSharp;
using System.Collections;
using LoadTester.Plugins;

namespace LoadTester
{
    class Program
    {
        
        static void Main(string[] args)
        {
            args = new string[] { "10.6.248.10", "1414", "CHANNEL1", "MQGD", @"C:\Users\d67784\Documents\GitHub\BNA\Cliente_HCS\ClienteHCS_2\bin\Debug\EjemploTransaccionesHCS\CU1_TRX500.hcs"};

            if (args.Length < 5)
            {
                throw new ArgumentException("Debe proporcionar cinco parámetros: IP, Port, CHANNEL, ManagerName y nombre de archivo.");
            }

            string ip = args[0];
            if (string.IsNullOrWhiteSpace(ip))
            {
                throw new ArgumentException("El parámetro IP no puede estar vacío.");
            }

            if (!int.TryParse(args[1], out int port) || port <= 0)
            {
                throw new ArgumentException($"El parámetro Port debe ser un número entero positivo válido. Valor recibido: {args[1]}");
            }

            string channel = args[2];
            if (string.IsNullOrWhiteSpace(channel))
            {
                throw new ArgumentException("El parámetro CHANNEL no puede estar vacío.");
            }

            string managerName = args[3];
            if (string.IsNullOrWhiteSpace(managerName))
            {
                throw new ArgumentException("El parámetro ManagerName no puede estar vacío.");
            }

            string nombreArchivo = args[4];
            if (!File.Exists(nombreArchivo)) throw new FileNotFoundException($"El archivo '{nombreArchivo}' no existe.", nombreArchivo);
            string contenidoArchivo = File.ReadAllText(nombreArchivo);
            string[] columnas = contenidoArchivo.Split("|@|", StringSplitOptions.None);

            if (columnas.Length < 2) throw new InvalidDataException("El archivo debe contener al menos dos columnas separadas por '|@|'.");
            string cola = columnas[0];
            string mensaje = columnas[1];

            string outputQueue = $"BNA.{cola}.PEDIDO";
            string inputQueue = $"BNA.{cola}.RESPUESTA";

            Console.WriteLine($"ip: {ip}, port: {port}, channel: {channel}, managername: {managerName}, file: {nombreArchivo}");

            var properties = new Hashtable
            {
                { MQC.HOST_NAME_PROPERTY, ip },
                { MQC.PORT_PROPERTY, port },
                { MQC.CHANNEL_PROPERTY, channel },/*
                { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_CLIENT } // conexión remota tipo cliente
               */
            };

            MQQueueManager qmgr = new MQQueueManager(managerName, properties);

            //100 VUs/seg fijos durante 30 segundos. Es decir, se garantiza que haya 100 request en cada segundo.
            var scenario1 = Scenarios.ScenarioEnviarRecibir(qmgr, outputQueue, inputQueue, mensaje)
                     .WithWarmUpDuration(TimeSpan.FromSeconds(5))
                     .WithLoadSimulations(
                                         Simulation.Inject(rate: 1000,
                                         interval: TimeSpan.FromSeconds(1),
                                         during: TimeSpan.FromSeconds(10))
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
