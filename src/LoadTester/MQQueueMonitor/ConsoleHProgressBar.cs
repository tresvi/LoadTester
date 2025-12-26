namespace MQQueueMonitor;

/// <summary>
/// Clase para mostrar una barra de progreso horizontal en la consola con zonas de colores
/// </summary>
internal class ConsoleHProgressBar
{
    /// <summary>
    /// Longitud de la barra en caracteres (dentro de los corchetes)
    /// </summary>
    public int BarLength { get; set; }

    /// <summary>
    /// Porcentaje a partir del cual se mostrará la zona amarilla (0-100)
    /// </summary>
    public double YellowThreshold { get; set; }

    /// <summary>
    /// Porcentaje a partir del cual se mostrará la zona roja (0-100)
    /// </summary>
    public double RedThreshold { get; set; }

    /// <summary>
    /// Indica si se debe mostrar el porcentaje al costado de la barra
    /// </summary>
    public bool ShowPercentage { get; set; } = false;

    /// <summary>
    /// Formato para mostrar el porcentaje (por defecto "F1" para 1 decimal)
    /// </summary>
    public string PercentageFormat { get; set; } = "F1";

    /// <summary>
    /// Constructor de la barra de progreso
    /// </summary>
    /// <param name="barLength">Longitud de la barra en caracteres (dentro de los corchetes)</param>
    /// <param name="yellowThreshold">Porcentaje a partir del cual se mostrará la zona amarilla (0-100)</param>
    /// <param name="redThreshold">Porcentaje a partir del cual se mostrará la zona roja (0-100)</param>
    public ConsoleHProgressBar(int barLength, double yellowThreshold, double redThreshold, bool showPercentage)
    {
        if (barLength <= 0)
            throw new ArgumentException("La longitud de la barra debe ser mayor que 0.", nameof(barLength));
        
        if (yellowThreshold < 0 || yellowThreshold > 100)
            throw new ArgumentException("El umbral amarillo debe estar entre 0 y 100.", nameof(yellowThreshold));
        
        if (redThreshold < 0 || redThreshold > 100)
            throw new ArgumentException("El umbral rojo debe estar entre 0 y 100.", nameof(redThreshold));
        
        if (yellowThreshold >= redThreshold)
            throw new ArgumentException("El umbral amarillo debe ser menor que el umbral rojo.");

        BarLength = barLength;
        YellowThreshold = yellowThreshold;
        RedThreshold = redThreshold;
        ShowPercentage = showPercentage;
    }

    public void Update(int line, int currentValue, int maxValue)
    {
        int currentTop = Console.CursorTop;
        int currentLeft = Console.CursorLeft;
        ConsoleColor originalColor = Console.ForegroundColor;

        // Calcular el porcentaje una sola vez
        double percentage = maxValue > 0 ? Math.Min(100.0, (double)currentValue / maxValue * 100.0) : 0.0;

        // Mantener el cursor oculto (ya está oculto desde el inicio)
        Console.SetCursorPosition(0, line);

        // Corchete de apertura en blanco
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("[");

        if (maxValue <= 0)
        {
            // Si no hay valor máximo, mostrar barra vacía
            Console.Write(new string(' ', BarLength));
        }
        else
        {
            // Calcular el número de caracteres a llenar
            int filledChars = (int)Math.Round(percentage / 100.0 * BarLength);
            filledChars = Math.Min(filledChars, BarLength);

            // Calcular los límites de las zonas en caracteres
            int yellowStartChar = (int)Math.Round(YellowThreshold / 100.0 * BarLength);
            int redStartChar = (int)Math.Round(RedThreshold / 100.0 * BarLength);

            // Imprimir pipes con colores según la zona
            for (int i = 0; i < filledChars; i++)
            {
                if (i < yellowStartChar)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                }
                else if (i < redStartChar)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                }
                Console.Write("|");
            }

            // Espacios restantes en blanco
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(new string(' ', BarLength - filledChars));
        }

        // Corchete de cierre en blanco
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("]");

        // Mostrar porcentaje si está habilitado
        if (ShowPercentage)
        {
            Console.Write($" {percentage.ToString(PercentageFormat)}%");
        }

        // Limpiar el resto de la línea
        int totalLength = BarLength + 2; // Corchetes
        if (ShowPercentage)
        {
            // Calcular el espacio necesario para el porcentaje (asumiendo formato razonable)
            double samplePercentage = 100.0;
            string sampleText = samplePercentage.ToString(PercentageFormat) + "%";
            totalLength += sampleText.Length + 1; // +1 para el espacio antes del porcentaje
        }
        Console.Write(new string(' ', Math.Max(0, Console.WindowWidth - totalLength)));

        Console.ForegroundColor = originalColor;
        Console.SetCursorPosition(currentLeft, currentTop);
    }
}
