using System.Collections;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using IBM.WMQ;
using LoadTester.Plugins;

namespace StampedeLoadTester;


internal sealed class TestManager : IDisposable
{

    
    internal struct MensajeEnviado
    {
        /// <summary>
        /// Contador estático y thread-safe para asignar el orden secuencial
        /// </summary>
        private static int _orderingCounter = 0;

        public byte[] MessageId;

        /// <summary>
        /// Orden secuencial en que fue enviado el mensaje (asignado automáticamente)
        /// </summary>
        public int Ordering { get; private set; }

        private DateTime _requestPutDateTime;

        /// <summary>
        /// Fecha y hora en que el mensaje fue colocado en la cola de salida.
        /// Al asignarse, automáticamente se asigna un valor secuencial a Ordering.
        /// </summary>
        public DateTime RequestPutDateTime
        {
            get => _requestPutDateTime;
            set
            {
                _requestPutDateTime = value;
                // Asignar orden secuencial thread-safe solo si aún no se ha asignado
                if (Ordering == 0)
                {
                    Ordering = Interlocked.Increment(ref _orderingCounter);
                }
            }
        }

        public DateTime ResponsePutDateTime;
    
        public MensajeEnviado(byte[] messageId, DateTime requestPutDateTime)
        {
            MessageId = messageId;
            RequestPutDateTime = requestPutDateTime;
            ResponsePutDateTime = default;
        }
        
    }

    public List<MensajeEnviado>[]? MensajesEnviados {get; private set;}

    private readonly string _queueManagerName;
    private readonly string _outputQueueName;
    private readonly string _mensaje;
    private readonly List<Hashtable> _connectionProperties;
    private readonly MQQueueManager?[] _queueManagers = new MQQueueManager?[4];
    private readonly MQQueue?[] _outputQueues = new MQQueue?[4];
    
    private static int _contadorSegmento = 0;
    private const int MAX_SEGMENTOS = 164;

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
                //DelayMicroseconds(5000);
                
                // Incrementar contador de forma thread-safe y obtener valor entre 1 y 164
                int valorSegmento = (Interlocked.Increment(ref _contadorSegmento) - 1) % MAX_SEGMENTOS + 1;
                string segmentoReemplazo = $"D{valorSegmento:D5}  "; // 8 caracteres: "D" + 5 dígitos + 2 espacios
                string mensajeConSegmento = _mensaje.Replace("%XXXXXX%", segmentoReemplazo);
                //System.Console.WriteLine(mensajeConSegmento);    //!!!!
                (DateTime putDateTime, byte[] messageId) = IbmMQPlugin.EnviarMensaje(queueActual, mensajeConSegmento);
                
                MensajeEnviado mensajeEnviado = new(messageId, putDateTime);
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
        int conexionesCerradasOK = 0;

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
                Console.WriteLine($"Error al cerrar conexión {i}: {ex.Message}");
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
    //TODO: Deberia conectarme a una cola especifica pasada por nombre, o asumir la de TestManager como hago ahora?
    public async Task<Dictionary<int, int>> MonitorearProfundidadColaAsync(string queueName, CancellationToken cancellationToken)
    {
        Dictionary<int, int> mediciones = [];
        const int intervaloMs = 100;
        MQQueue? inquireQueue = null;

        try
        {
            if (_queueManagers[0] is null)
                throw new InvalidOperationException("Las conexiones no están inicializadas");

            inquireQueue = this.AbrirQueueInquire();

            long horaUltimaMedicion = 0;
            Stopwatch swEnsayo = new();
            Stopwatch swMedicion = new();
            swEnsayo.Start();

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    horaUltimaMedicion = swEnsayo.ElapsedMilliseconds;

                    swMedicion.Restart();
                    int profundidad = inquireQueue.CurrentDepth;
                    long tiempoEspera = swMedicion.ElapsedMilliseconds;

                    mediciones[(int)swEnsayo.ElapsedMilliseconds] = profundidad;

                     await Task.Run(() =>
                     {
                         long tiempoRestante = intervaloMs - tiempoEspera;
                         tiempoRestante = tiempoRestante < 0 ? 0 : tiempoRestante;

                         while (!cancellationToken.IsCancellationRequested)
                         {
                             if (swEnsayo.ElapsedMilliseconds >= horaUltimaMedicion + tiempoRestante) break;
                             Thread.Sleep((int)Math.Min(tiempoRestante, 5));
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
        ArgumentNullException.ThrowIfNull(mensajesEnviados, nameof(mensajesEnviados));
        if (_queueManagers[0] is null) throw new InvalidOperationException("Las conexiones no están inicializadas");

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
                        listaMensajes[i] = mensajeEnviado;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error al recibir mensaje para Hilo {hiloIndex}, mensaje {i}: {ex.Message}");
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
    public bool WaitForQueueEmptied(string queueName, out List<(DateTime, int)> measurements, int? timeoutMs = null, int pollingIntervalMs = 100)
    {
        if (_queueManagers[0] is null)
            throw new InvalidOperationException("Las conexiones no están inicializadas");

        if (pollingIntervalMs <= 0)
            throw new ArgumentException("El intervalo de polling debe ser mayor a 0", nameof(pollingIntervalMs));

        Stopwatch sw = Stopwatch.StartNew();
        measurements = new();

        while (true)
        {
            try
            {
                // Verificar que el queue manager aún esté conectado
                var queueMgr = _queueManagers[0];
                if (queueMgr == null || !queueMgr.IsConnected)
                {
                    throw new InvalidOperationException("El queue manager no está conectado");
                }

                int depth = IbmMQPlugin.GetDepth(queueMgr, queueName);
                if (depth == 0) return true;

                // Verificar timeout si está configurado
                if (timeoutMs.HasValue && sw.ElapsedMilliseconds >= timeoutMs.Value)
                    return false;

                measurements.Add((DateTime.Now, depth));

                //TODO: aca puedo obtener el maximo sostenible. Si tengo mas de 2 mediciones (debo descartar las puntas), puedo calcular la velocidad de througput máximo
                //Promedio y saco desvio estandar.
                Thread.Sleep(pollingIntervalMs);
            }
            catch (MQException mqe) when (mqe.ReasonCode == 2017) // MQRC_HANDLE_NOT_AVAILABLE
            {
                Console.WriteLine($"\nError: El handle del queue manager ya no está disponible (Reason: {mqe.ReasonCode}). La conexión puede haberse perdido.");
                throw new InvalidOperationException($"No se puede consultar la profundidad de la cola {queueName}: el queue manager se desconectó", mqe);
            }
            catch (Exception ex)
            {
                // Si el error contiene información sobre handle inválido, relanzar con más contexto
                if (ex.Message.Contains("HANDLE_NOT_AVAILABLE") || ex.Message.Contains("HOBJ") || ex.Message.Contains("2017"))
                {
                    Console.WriteLine($"\nError: El handle del queue manager ya no está disponible. La conexión puede haberse perdido.");
                    throw new InvalidOperationException($"No se puede consultar la profundidad de la cola {queueName}: el queue manager se desconectó", ex);
                }
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

