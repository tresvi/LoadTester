namespace MQQueueMonitor;


internal class QueueStatistics
{
    public string QueueName { get; private set; }
    public int MaxDepth { get; private set; }
    
    // Propiedades de solo lectura que se calculan internamente
    public int CurrentDepth { get; private set; }
    public int MinDepth { get; private set; } = int.MaxValue;
    public DateTime MinDepthTimestamp { get; private set; }
    public int MaxDepthRecorded { get; private set; } = int.MinValue;
    public DateTime MaxDepthTimestamp { get; private set; }
    public int SaturationCount { get; private set; }
    
    // Campo privado para detectar transiciones a saturación
    private bool _wasAtMaxDepth = false;

    public QueueStatistics(string queueName, int maxDepth)
    {
        QueueName = queueName;
        MaxDepth = maxDepth;
    }

    /// <summary>
    /// Actualiza las estadísticas con un nuevo valor de profundidad actual
    /// </summary>
    /// <param name="currentDepth">Profundidad actual de la cola</param>
    public void Update(int currentDepth)
    {
        DateTime currentTime = DateTime.Now;
        CurrentDepth = currentDepth;

        if (currentDepth < MinDepth)
        {
            MinDepth = currentDepth;
            MinDepthTimestamp = currentTime;
        }

        if (currentDepth > MaxDepthRecorded)
        {
            MaxDepthRecorded = currentDepth;
            MaxDepthTimestamp = currentTime;
        }

        // Detectar saturación: pasar de un valor menor a la profundidad máxima a la profundidad máxima
        if (currentDepth >= MaxDepth && !_wasAtMaxDepth)
        {
            SaturationCount++;
            _wasAtMaxDepth = true;
        }
        else if (currentDepth < MaxDepth)
        {
            _wasAtMaxDepth = false;
        }
    }

    /// <summary>
    /// Indica si hay un valor mínimo registrado válido
    /// </summary>
    public bool HasMinDepth => MinDepth != int.MaxValue;

    /// <summary>
    /// Indica si hay un valor máximo registrado válido
    /// </summary>
    public bool HasMaxDepthRecorded => MaxDepthRecorded != int.MinValue;
}
