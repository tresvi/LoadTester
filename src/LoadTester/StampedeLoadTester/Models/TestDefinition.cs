using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StampedeLoadTester.Models
{
    internal class TestDefinition
    {
        public string QueueManager { get; set; } = "MQGD";
        public string OutputQueue { get; set; } = "BNA.XX1.PEDIDO";
        public string InputQueue { get; set; } = "BNA.XX1.PEDIDO";
        
        public string Mensaje { get; set; } = "    00000008500000020251118115559N0001   000000PC  01100500000000000000                        00307384";
        
        public int TiempoCargaMs { get; set; } = 2000;
        
        public string IpMqServer { get; set; } = "10.6.248.10";
        
        public string Channel { get; set; } = "CHANNEL1";
        
        public List<MQProperties> MQProperties { get; set; } = new List<MQProperties>();
    }
}
