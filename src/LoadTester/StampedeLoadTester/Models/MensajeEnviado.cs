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

        /// <summary>
        /// MessageId del mensaje (24 bytes)
        /// </summary>
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

        /// <summary>
        /// Fecha y hora en que el mensaje fue recibido en la cola de entrada (por ahora vacío)
        /// </summary>
        public DateTime ResponsePutDateTime;

        public MensajeEnviado(byte[] messageId, DateTime requestPutDateTime)
        {
            MessageId = new byte[24];
            if (messageId != null && messageId.Length >= 24)
            {
                Array.Copy(messageId, MessageId, 24);
            }
            RequestPutDateTime = default;
            ResponsePutDateTime = default;
        }
    }
}

