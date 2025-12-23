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
    
        public MensajeEnviado(byte[] messageId, DateTime requestPutDateTime)
        {
            MessageId = messageId;
            /*
            MessageId = new byte[24];
            if (messageId != null && messageId.Length >= 24)
            {
                Array.Copy(messageId, MessageId, 24);
            }
            */
            RequestPutDateTime = requestPutDateTime;
            ResponsePutDateTime = default(DateTime); // Vacío por ahora
        }
        
    }

    public List<MensajeEnviado>[]? MensajesEnviados {get; private set;}

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
        MensajesEnviados = null; // Se inicializará cuando se ejecute EjecutarWriteQueueLoadTest
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
        MensajesEnviados = new List<MensajeEnviado>[numHilos];

        Parallel.For(0, numHilos, hiloIndex =>
        {
            MQQueue queueActual = _outputQueues[hiloIndex % _outputQueues.Length]!;
            long horaInicio = Stopwatch.GetTimestamp();
            long horaFin = horaInicio + tiempoLimiteTicks;
            MensajesEnviados[hiloIndex] = new List<MensajeEnviado>();

            while (Stopwatch.GetTimestamp() < horaFin)
            {
                //Thread.Sleep(14);
                DelayMicroseconds(10500);
                (DateTime putDateTime, byte[] messageId) = IbmMQPlugin.EnviarMensaje(queueActual, _mensaje);
                
                MensajeEnviado mensajeEnviado = new MensajeEnviado(messageId, putDateTime);
                MensajesEnviados[hiloIndex].Add(mensajeEnviado);
                
                Interlocked.Increment(ref messageCounter);
            }

            double elapsedMs = (Stopwatch.GetTimestamp() - horaInicio) * 1000.0 / Stopwatch.Frequency;
            Console.WriteLine($"Hilo {hiloIndex} tardó {elapsedMs:F2} ms");
        });

        return messageCounter;
    }


    static void DelayMicroseconds(int microseconds)
    {
        long ticksObjetivo =
            microseconds * (Stopwatch.Frequency / 1_000_000);

        long start = Stopwatch.GetTimestamp();

        while (Stopwatch.GetTimestamp() - start < ticksObjetivo)
        {
            // busy wait
        }
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


    /// <summary>
    /// Recibe mensajes de la cola de entrada usando los MessageId como CorrelationId y actualiza el campo ResponsePutDateTime
    /// </summary>
    /// <param name="mensajesEnviados">Array de listas de mensajes enviados, donde cada índice corresponde a un hilo</param>
    /// <param name="inputQueueName">Nombre de la cola de entrada de donde se recibirán los mensajes</param>
    public void RecibirRespuestasYActualizarPutDateTime(List<MensajeEnviado>[] mensajesEnviados, string inputQueueName)
    {
        if (mensajesEnviados == null)
        {
            throw new ArgumentNullException(nameof(mensajesEnviados));
        }

        if (_queueManagers[0] is null)
        {
            throw new InvalidOperationException("Las conexiones no están inicializadas");
        }

        // Abrir cola de entrada una vez para todos los hilos
        MQQueue? inputQueue = null;
        try
        {
            inputQueue = IbmMQPlugin.OpenInputQueue(_queueManagers[0]!, inputQueueName);

            // Recorrer índice por índice (cada índice = un hilo)
            for (int hiloIndex = 0; hiloIndex < mensajesEnviados.Length; hiloIndex++)
            {
                var listaMensajes = mensajesEnviados[hiloIndex];
                
                if (listaMensajes == null || listaMensajes.Count == 0) continue;

                // Recorrer elemento por elemento de la lista
                for (int i = 0; i < listaMensajes.Count; i++)
                {
                    var mensajeEnviado = listaMensajes[i];
                    
                    // Si el MessageId es null o no tiene 24 bytes, saltar este mensaje
                    if (mensajeEnviado.MessageId == null || mensajeEnviado.MessageId.Length != 24)
                    {
                        Console.WriteLine($"Advertencia: Hilo {hiloIndex}, mensaje {i} tiene MessageId inválido. Saltando...");
                        continue;
                    }

                    try
                    {
                        // Hacer GET usando el MessageId como CorrelationId
                        DateTime putDateTime = IbmMQPlugin.RecibirMensajeYObtenerPutDateTime(inputQueue, mensajeEnviado.MessageId);
                        mensajeEnviado.ResponsePutDateTime = putDateTime;
                        
                        // Actualizar el elemento en la lista (necesario porque es una struct)
                        listaMensajes[i] = mensajeEnviado;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error al recibir mensaje para Hilo {hiloIndex}, mensaje {i}: {ex.Message}");
                        // Continuar con el siguiente mensaje
                    }
                }
            }
        }
        finally
        {
            inputQueue?.Close();
        }
    }
    

    /// <summary>
    /// Espera a que la cola especificada esté vacía consultando periódicamente su profundidad
    /// </summary>
    /// <param name="queueName">Nombre de la cola a verificar</param>
    /// <param name="timeoutMs">Timeout en milisegundos. Si es null, esperará indefinidamente. Valor por defecto: null</param>
    /// <param name="pollingIntervalMs">Intervalo en milisegundos entre consultas de profundidad. Valor por defecto: 100ms</param>
    /// <returns>true si la cola se vació, false si se alcanzó el timeout</returns>
    public bool WaitForQueueEmptied(string queueName, int? timeoutMs = null, int pollingIntervalMs = 100)
    {
        if (_queueManagers[0] is null)
            throw new InvalidOperationException("Las conexiones no están inicializadas");

        if (pollingIntervalMs <= 0)
            throw new ArgumentException("El intervalo de polling debe ser mayor a 0", nameof(pollingIntervalMs));

        Stopwatch sw = Stopwatch.StartNew();
        
        while (true)
        {
            try
            {
                int depth = IbmMQPlugin.GetDepth(_queueManagers[0]!, queueName);
                
                if (depth == 0)
                {
                    return true;
                }

                // Verificar timeout si está configurado
                if (timeoutMs.HasValue && sw.ElapsedMilliseconds >= timeoutMs.Value)
                {
                    return false;
                }

                Thread.Sleep(pollingIntervalMs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al consultar profundidad de la cola {queueName}: {ex.Message}");
                throw;
            }
        }
    }

    public void Dispose()
    {
        CerrarConexiones();
        GC.SuppressFinalize(this);
    }
}

