using Tresvi.CommandParser.Attributtes.Keywords;


namespace StampedeLoadTester.Models.CommandLineOptions;

[Verb("slave", "Queda a la espera de la señal del amo para ejecutar el ensayo de carga que este le solicite", false)]
internal class SlaveVerb
{
    private const string SLAVE_PORT_DEFAULT = "8888";

    [Option("port", 'p', false, "Puerto para donde el esclavo se quedará escuchando al maestro. Default: + " + SLAVE_PORT_DEFAULT)]
    public int Port { get; set; } = int.Parse(SLAVE_PORT_DEFAULT);


    [Option("threadNumber", 't', false, "Número de hilos para el test de carga. Si se omite, se usará el nro de Threads de la CPU")]
    public int? ThreadNumber { get; set; } = Environment.ProcessorCount;

}

