using System;

namespace StampedeLoadTester.Models
{
    /// <summary>
    /// Estructura que contiene información de un mensaje enviado a la cola MQ
    /// </summary>
    internal struct MensajeEnviado
    {
        /// <summary>
        /// MessageId del mensaje (24 bytes)
        /// </summary>
        public byte[] MessageId;

        /// <summary>
        /// Fecha y hora en que el mensaje fue colocado en la cola de salida
        /// </summary>
        public DateTime RequestPutDateTime;

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
            RequestPutDateTime = requestPutDateTime;
            ResponsePutDateTime = default(DateTime); // Vacío por ahora
        }
    }
}

