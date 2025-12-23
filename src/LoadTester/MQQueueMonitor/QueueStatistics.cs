namespace MQQueueMonitor;

/// <summary>
/// Clase para mantener las estadísticas de una cola MQ
/// </summary>
internal class QueueStatistics
{
    public string QueueName { get; set; } = "";
    public int MaxDepth { get; set; }
    public int CurrentDepth { get; set; }
    public int MinDepth { get; set; } = int.MaxValue;
    public DateTime MinDepthTimestamp { get; set; }
    public int MaxDepthRecorded { get; set; } = int.MinValue;
    public DateTime MaxDepthTimestamp { get; set; }
    public int SaturationCount { get; set; }
    public bool WasAtMaxDepth { get; set; } = false; // Para detectar transiciones a saturación
}

