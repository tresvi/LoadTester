using System;
using System.Threading;

namespace StampedeLoadTester.Models
{
    /// <summary>
    /// Estructura que contiene información de un mensaje enviado a la cola MQ
    /// </summary>
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
}

