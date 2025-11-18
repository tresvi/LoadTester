using System;
using System.Threading.Tasks;

namespace LoadTester
{
    public class OperacionService
    {
        private readonly Random _random;

        public OperacionService()
        {
            _random = new Random();
        }

        public async Task<int> Ejecutar()
        {
            // Simular duración aleatoria entre 1 y 5 ms
            int duracionMs = 1; //_random.Next(1, 6); // Next(1, 6) genera valores de 1 a 5
            await Task.Delay(duracionMs);

            // 20% de probabilidad de lanzar excepción
            if (_random.Next(1, 101) <= 20) // Genera un número entre 1 y 100
            {
                throw new Exception("Error simulado en la ejecución");
            }

            // Devolver 1 si no hay error
            return 1;
        }
    }
}

