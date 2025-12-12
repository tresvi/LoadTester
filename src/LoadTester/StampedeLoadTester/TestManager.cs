using System.Collections;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using IBM.WMQ;
using LoadTester.Plugins;

namespace StampedeLoadTester;

internal sealed class TestManager : IDisposable
{
    private readonly string _queueManagerName;
    private readonly string _outputQueueName;
    private readonly string _mensaje;
    private readonly List<Hashtable> _connectionProperties;
    private readonly MQQueueManager?[] _queueManagers = new MQQueueManager?[4];
    private readonly MQQueue?[] _outputQueues = new MQQueue?[4];

    public TestManager(string queueManagerName, string outputQueueName, string mensaje, List<Hashtable> connectionProperties)
    {
        _queueManagerName = queueManagerName;
        _outputQueueName = outputQueueName;
        _mensaje = mensaje;
        _connectionProperties = connectionProperties;
    }

    public void InicializarConexiones()
    {
        try
        {
            for (int i = 0; i < _queueManagers.Length; i++)
            {
                Hashtable props = i switch
                {
                    0 => _connectionProperties[0], 
                    1 => _connectionProperties[1],  
                    2 => _connectionProperties[2],  
                    _ => _connectionProperties[0],  
                };
                _queueManagers[i] = new MQQueueManager(_queueManagerName, props);
                _outputQueues[i] = IbmMQPlugin.OpenOutputQueue(_queueManagers[i]!, _outputQueueName, false);
                Console.WriteLine($"Conexión {i + 1} establecida");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: al conectarse al manager {ex.Message}");
            CerrarConexiones();
            throw;
        }
    }

    public MQQueue AbrirQueueInquire()
    {
        if (_queueManagers[0] is null)
        {
            throw new InvalidOperationException("Las conexiones no están inicializadas");
        }

        return IbmMQPlugin.OpenOutputQueue(_queueManagers[0]!, _outputQueueName, true);
    }

    public int EjecutarHilosCarga(TimeSpan duracionEnsayo, int numHilos)
    {
        int messageCounter = 0;
        long tiempoLimiteTicks = (long)(duracionEnsayo.TotalSeconds * Stopwatch.Frequency);

        Parallel.For(0, numHilos, hiloIndex =>
        {
            MQQueue queueActual = _outputQueues[hiloIndex % _outputQueues.Length]!;
            long horaInicio = Stopwatch.GetTimestamp();
            long horaFin = horaInicio + tiempoLimiteTicks;

            while (Stopwatch.GetTimestamp() < horaFin)
            {
                IbmMQPlugin.EnviarMensaje(queueActual, _mensaje);
                Interlocked.Increment(ref messageCounter);
            }

            double elapsedMs = (Stopwatch.GetTimestamp() - horaInicio) * 1000.0 / Stopwatch.Frequency;
            Console.WriteLine($"Hilo {hiloIndex} tardó {elapsedMs:F2} ms");
        });

        return messageCounter;
    }


    public int EjecutarHilosInquire(TimeSpan duracionEnsayo, int numHilos)
    {
        int messageCounter = 0;
        long tiempoLimiteTicks = (long)(duracionEnsayo.TotalSeconds * Stopwatch.Frequency);

        Parallel.For(0, numHilos, hiloIndex =>
        {
            MQQueue queueActual = _outputQueues[hiloIndex % _outputQueues.Length]!;
            long horaInicio = Stopwatch.GetTimestamp();
            long horaFin = horaInicio + tiempoLimiteTicks;

            while (Stopwatch.GetTimestamp() < horaFin)
            {
                IbmMQPlugin.EnviarMensaje(queueActual, _mensaje);
                Interlocked.Increment(ref messageCounter);
            }

            double elapsedMs = (Stopwatch.GetTimestamp() - horaInicio) * 1000.0 / Stopwatch.Frequency;
            Console.WriteLine($"Hilo {hiloIndex} tardó {elapsedMs:F2} ms");
        });

        return messageCounter;
    }


    public void EnviarMensajesPrueba(int mensajesPorConexion = 1)
    {
        foreach (MQQueue? queue in _outputQueues)
        {
            if (queue is null)
            {
                continue;
            }

            for (int i = 0; i < mensajesPorConexion; i++)
            {
                IbmMQPlugin.EnviarMensaje(queue, _mensaje);
            }
        }
    }

    public void CerrarConexiones()
    {
        for (int i = 0; i < _queueManagers.Length; i++)
        {
            try
            {
                _outputQueues[i]?.Close();
                _queueManagers[i]?.Close();
                Console.WriteLine($"Conexión {i + 1} cerrada");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al cerrar conexión {i + 1}: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        CerrarConexiones();
        GC.SuppressFinalize(this);
    }
}

