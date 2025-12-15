using System.Collections;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using IBM.WMQ;
using LoadTester.Plugins;

namespace StampedeLoadTester;




internal sealed class TestManager : IDisposable
{
    internal struct MensajeEnviado
    {
        public byte[] MessageId;
        public DateTime RequestPutDateTime;
        public DateTime ResponsePutDateTime;
    /*
        public MensajeEnviado(byte[] messageId, DateTime requestPutDateTime)
        {
            MessageId = new byte[24];
            if (messageId != null && messageId.Length >= 24)
            {
                Array.Copy(messageId, MessageId, 24);
            }
            RequestPutDateTime = requestPutDateTime;
            ResponsePutDateTime = default(DateTime); // Vacío por ahora
        }
        */
    }

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
                //Console.WriteLine($"Conexión {i + 1} establecida");
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

    /// <summary>
    /// Vacía la cola especificada realizando GETs de la manera más rápida posible
    /// </summary>
    /// <param name="queueName">Nombre de la cola a vaciar</param>
    /// <returns>Cantidad de mensajes eliminados por segundo</returns>
    public float VaciarCola(string queueName)
    {
        if (_queueManagers[0] is null)
        {
            throw new InvalidOperationException("Las conexiones no están inicializadas");
        }

        return IbmMQPlugin.VaciarCola(_queueManagers[0]!, queueName);
    }

    public int EjecutarWriteQueueLoadTest(TimeSpan duracionEnsayo, int numHilos)
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
            if (queue is null) continue;

            for (int i = 0; i < mensajesPorConexion; i++)
            {
                IbmMQPlugin.EnviarMensaje(queue, _mensaje);
            }
        }
    }

    public void CerrarConexiones()
    {
        int conexionesCerradasOK = 0, conexionesCerradasFail = 0;

        for (int i = 0; i < _queueManagers.Length; i++)
        {
            try
            {
                _outputQueues[i]?.Close();
                _queueManagers[i]?.Close();
                conexionesCerradasOK++;
            }
            catch (Exception ex)
            {
                conexionesCerradasFail++;
                Console.WriteLine($"Error al cerrar conexión {i + 1}: {ex.Message}");
            }
        }
        Console.WriteLine($"Conexiones cerradas OK: {conexionesCerradasOK} de {_queueManagers.Length}");
    }

    /// <summary>
    /// Monitorea la profundidad de una cola de forma asíncrona, realizando lecturas cada 20ms
    /// </summary>
    /// <param name="queueName">Nombre de la cola a monitorear</param>
    /// <param name="cancellationToken">Token para cancelar el monitoreo</param>
    /// <returns>Diccionario donde la clave es el tiempo transcurrido en milisegundos desde el inicio y el valor es la profundidad de la cola</returns>
    public async Task<Dictionary<int, int>> MonitorearProfundidadColaAsync(string queueName, CancellationToken cancellationToken)
    {
        Dictionary<int, int> mediciones = new Dictionary<int, int>();
        const int intervaloMs = 25;
        MQQueue? inquireQueue = null;

        try
        {
            if (_queueManagers[0] is null)
            {
                throw new InvalidOperationException("Las conexiones no están inicializadas");
            }
            inquireQueue = this.AbrirQueueInquire();

            long tiempoInicio = Stopwatch.GetTimestamp();
            long frecuencia = Stopwatch.Frequency;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    int profundidad = inquireQueue.CurrentDepth;
                    
                    // Calcular milisegundos transcurridos desde el inicio
                    long tiempoActual = Stopwatch.GetTimestamp();
                    long ticksTranscurridos = tiempoActual - tiempoInicio;
                    // Calcular con double primero para mayor precisión, luego convertir a int
                    double milisegundosTranscurridosD = (ticksTranscurridos * 1000.0) / frecuencia;
                    int milisegundosTranscurridos = (int)milisegundosTranscurridosD;
                    
                    mediciones[milisegundosTranscurridos] = profundidad;

                    // Para delays pequeños (< 15ms), Thread.Sleep es más preciso que Task.Delay en Windows
                    // Usamos Task.Run para mantener la asincronía mientras usamos Thread.Sleep
                    await Task.Run(() =>
                    {
                        // Verificar cancelación periódicamente durante el sleep
                        int tiempoRestante = intervaloMs;
                        while (tiempoRestante > 0 && !cancellationToken.IsCancellationRequested)
                        {
                            int sleepTime = Math.Min(tiempoRestante, 5); // Sleep en chunks de 5ms para poder cancelar
                            Thread.Sleep(sleepTime);
                            tiempoRestante -= sleepTime;
                        }
                    }, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al leer profundidad: {ex.Message}");
                    // Si la cola fue cerrada, salir del loop
                    if (ex.Message.Contains("HOBJ") || ex.Message.Contains("MQRC_HOBJ"))
                    {
                        Console.WriteLine("La cola fue cerrada. Finalizando monitoreo.");
                        break;
                    }
                    // Esperar un poco antes de reintentar
                    await Task.Run(() => Thread.Sleep(10), cancellationToken);
                }
            }
            return mediciones;
        }
        catch (OperationCanceledException)
        {
            return mediciones;
        }
        catch (Exception)
        {
            throw;
        }
        finally
        {
            inquireQueue?.Close();
        }
    }


    public void Dispose()
    {
        CerrarConexiones();
        GC.SuppressFinalize(this);
    }
}

