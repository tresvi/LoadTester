# LoadTester

LoadTester es una herramienta de línea de comandos para realizar pruebas de carga y medir el rendimiento de endpoints HTTP. Permite enviar muchas peticiones concurrentes, personalizar métodos, cabeceras y cuerpos, y recoger métricas clave como latencias, tasa de errores y throughput.

Características
- Envío de peticiones HTTP/HTTPS (GET, POST, PUT, DELETE, etc.).
- Configuración de concurrencia (número de trabajadores) y número total de peticiones.
- Soporte para cabeceras personalizadas y cuerpos de petición.
- Métricas: tiempos p50/p95/p99, latencia media, tasa de éxito/errores, RPS (requests per second).
- Exportación de resultados en formato JSON/CSV.
- Salida en consola con resumen y detalles por petición (opcional).

Requisitos
- Go >= 1.18 (si el proyecto está escrito en Go) o Python >= 3.8 (ajusta según el lenguaje real del repositorio).
- Conexión de red hacia los endpoints a testear.

Instalación
1. Clona el repositorio:

   git clone https://github.com/tresvi/LoadTester.git

2. Compila o instala según el lenguaje del proyecto. Ejemplo para Go:

   cd LoadTester
   go build -o loadtester ./...

Uso
Ejemplo básico (envía 1000 peticiones con 50 concurrentes):

   ./loadtester --url https://example.com/api --requests 1000 --concurrency 50

Opciones comunes
- --url: URL objetivo.
- --requests: número total de peticiones a enviar.
- --concurrency: número de peticiones en paralelo.
- --method: método HTTP (GET, POST, ...).
- --header: cabeceras adicionales (puede repetirse).
- --body: cuerpo de la petición (archivo o literal).
- --output: ruta de salida para exportar resultados (JSON/CSV).

Salida y métricas
Al finalizar, LoadTester muestra un resumen con:
- Total de peticiones, exitosas y con error.
- Latencia media y percentiles (p50, p95, p99).
- Requests per second (RPS).
- Distribución de códigos de estado HTTP.

Ejemplo de exportación a JSON:

   ./loadtester --url https://example.com/api --requests 1000 --concurrency 50 --output resultados.json

Contribuir
Si quieres contribuir:
- Abre un issue describiendo la mejora o bug.
- Crea un branch para tu cambio y abre un pull request.
- Asegúrate de añadir tests cuando sea posible.

Licencia
Añade aquí la licencia del proyecto (por ejemplo, MIT). Si no hay licencia, incluye una nota indicando que debes añadirla.

Notas
Ajusta las instrucciones de instalación y requisitos al lenguaje y herramientas reales usadas en este repositorio. Si quieres, puedo adaptar el README al funcionamiento exacto del proyecto si me indicas el lenguaje principal y cómo se ejecuta (por ejemplo, el comando para correr el binario o script).