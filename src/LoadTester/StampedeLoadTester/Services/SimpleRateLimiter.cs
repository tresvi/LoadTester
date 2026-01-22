using System;
using System.Diagnostics;
using System.Threading;

namespace StampedeLoadTester.Services;

public sealed class SimpleRateLimiter
{
    private readonly int _limitPerSecond;
    private readonly object _gate = new();
    private long _windowStartTicks; // Stopwatch ticks
    private int _countInWindow;

    private static readonly double TickFrequency = Stopwatch.Frequency; // ticks por segundo

    public SimpleRateLimiter(int limitPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limitPerSecond);

        _limitPerSecond = limitPerSecond;
        _windowStartTicks = Stopwatch.GetTimestamp();
        _countInWindow = 0;
    }

    public void Wait()
    {
        while (true)
        {
            int sleepMs;

            lock (_gate)
            {
                var now = Stopwatch.GetTimestamp();
                var elapsedTicks = now - _windowStartTicks;

                // Reset "perezoso" de la ventana al cruzar 1 segundo
                if (elapsedTicks >= (long)TickFrequency)
                {
                    _windowStartTicks = now;
                    _countInWindow = 0;
                    elapsedTicks = 0;
                }

                // ¿Hay cupo?
                if (_countInWindow < _limitPerSecond)
                {
                    _countInWindow++;
                    return; // permitido
                }

                // No hay cupo entonces calcular cuánto falta para que termine la ventana
                var remainingTicks = (long)TickFrequency - elapsedTicks;
                var remainingSec = remainingTicks / TickFrequency;

                sleepMs = (int)Math.Ceiling(remainingSec * 1000.0);
                if (sleepMs < 1) sleepMs = 1;
            }

            // Dormir FUERA del lock
            Thread.Sleep(sleepMs);
        }
    }
}
