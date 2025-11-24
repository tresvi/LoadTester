# LoadTester

LoadTester es una colección de herramientas y utilidades escritas en C# (.NET 8) para pruebas de carga y monitorización de colas IBM MQ. El proyecto usa NBomber para generar carga y capturar métricas, y el cliente `IBMMQDotnetClient` para integrarse con IBM MQ.

## Características principales
- Generación de carga y métricas con NBomber.
- Integración con IBM MQ para pruebas sobre colas.
- Proyectos incluidos:
  - src/LoadTester: generador de carga (aplicación principal).
  - src/MonitorColasMQ: monitor de colas IBM MQ.
  - src/LecturaProfundidadColasMQ: utilidades/solución auxiliar.
- Reportes y resultados en la carpeta `reports` dentro del proyecto LoadTester.

## Estado y dependencias
- TargetFramework: net8.0 (.NET 8)
- Paquetes relevantes (en src/LoadTester/LoadTester.csproj):
  - IBMMQDotnetClient (cliente IBM MQ)
  - NBomber (framework de carga/benchmarking)

## Requisitos
- .NET SDK 8.0 (https://dotnet.microsoft.com/)
- Acceso a un IBM MQ al que conectar para pruebas reales.
- (Opcional) Certificados/credenciales si IBM MQ está protegido con TLS/SSL.

## Instalación y compilación
1. Clona el repositorio:

   git clone https://github.com/tresvi/LoadTester.git
   cd LoadTester

2. Compila los proyectos relevantes:

   dotnet build src/LoadTester
   dotnet build src/MonitorColasMQ

Si existe una solución (.sln) en `src/LecturaProfundidadColasMQ`, puedes compilar la solución completa con:

   dotnet build src/LecturaProfundidadColasMQ/LecturaProfundidadColasMQ.sln

## Ejecución
- Ejecutar LoadTester (ejecución desde fuente):

   dotnet run --project src/LoadTester -- [opciones]

- Ejecutar MonitorColasMQ:

   dotnet run --project src/MonitorColasMQ -- [opciones]

## Argumentos comunes (adaptar según implementación en Program.cs)
- --mq-host / --mq-connection-string: datos de conexión a IBM MQ
- --mq-queue: nombre de la cola a usar
- --concurrency: número de hilos/consumidores
- --requests: número total de mensajes/peticiones a enviar
- --output: ruta de salida para resultados (JSON/CSV)

## Ejemplo hipotético:

   dotnet run --project src/LoadTester -- --mq-host my-mq-host:1414 --mq-queue TEST.QUEUE --concurrency 50 --requests 10000 --output ./src/LoadTester/reports/result.json

## Reportes y métricas
- NBomber genera reportes con métricas como latencias (p50/p95/p99), RPS, tasa de errores y distribucción de pasos.
- Revisa `src/LoadTester/reports` para archivos HTML/JSON/CSV generados por las pruebas.

## Configuración
- Parametriza las conexiones a IBM MQ por variables de entorno o añadiendo un archivo `appsettings.json` si lo prefieres.
- Para TLS/SSL en IBM MQ, configura los certificados en el host donde se ejecute la prueba.

## Buenas prácticas
- No ejecutes pruebas de carga sin avisar en entornos compartidos o producción.
- Monitoriza recursos del servidor (CPU, memoria, conexiones) durante las pruebas.
- Aumenta la concurrencia progresivamente y repite pruebas para obtener resultados consistentes.

## Desarrollo y contribuciones
- Fork -> branch -> PR. Describe cambios y añade tests si procede.
- Mantén actualizadas las versiones en los .csproj y añade documentación si introduces nuevos parámetros CLI.

## Posibles mejoras
- Documentar las opciones reales de la CLI extraídas de Program.cs (puedo hacerlo si quieres).
- Añadir un workflow de CI que compile y valide el proyecto en GitHub Actions.
- Incluir ejemplos concretos de configuración de IBM MQ (canal, usuario, TLS).

## Licencia
- Añade la licencia del proyecto (ej. MIT) o indica el permiso de uso.

## Contacto
- Mantenedor: tresvi (GitHub)

---

Hecho por: GitHub Copilot (asistente)