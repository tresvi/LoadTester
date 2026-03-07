# Stampede Load Tester

## Descripción

Stampede Load Tester es una herramienta de prueba de carga diseñada para evaluar el rendimiento de sistemas que utilizan IBM MQ (Message Queue). El programa permite ejecutar pruebas de carga distribuidas, enviando mensajes a colas MQ y midiendo métricas de rendimiento como latencia, throughput y estadísticas de procesamiento.

El programa puede operar en dos modos:
- **Master**: Coordina y ejecuta pruebas de carga, puede trabajar solo o coordinar múltiples instancias esclavas
- **Slave**: Instancia que espera comandos del master para ejecutar pruebas de carga de forma distribuida

## Características

- ✅ Pruebas de carga distribuidas (master/slave)
- ✅ Control de tasa de envío (rate limiting)
- ✅ Análisis estadístico de latencia (promedio, percentiles, desviación estándar)
- ✅ Monitoreo de profundidad de colas en tiempo real
- ✅ Cálculo de throughput (mensajes por segundo)
- ✅ Configuración mediante archivos JSON
- ✅ Compatible con AOT (Ahead-Of-Time compilation)
- ✅ Warmup automático antes de las pruebas
- ✅ Manejo de expiración de mensajes

## Formato del Archivo de Transacciones

El programa utiliza archivos JSON para definir las transacciones a enviar. El formato es el siguiente:

```json
{
  "version": 1,
  "inputQueue": "COLA_ENTRADA",
  "outputQueue": "COLA_SALIDA",
  "transacciones": [
    "TRANSACCION_1",
    "TRANSACCION_2",
    "TRANSACCION_3"
  ]
}
```

- `version`: Versión del formato (actualmente 1)
- `inputQueue`: Nombre de la cola de entrada (donde se reciben las respuestas)
- `outputQueue`: Nombre de la cola de salida (donde se envían los mensajes)
- `transacciones`: Array de strings, cada uno representa una transacción a enviar

## Opciones de Línea de Comandos

### Modo Master

El modo master se activa con el verbo `master` y permite ejecutar pruebas de carga de forma local o distribuidas.

#### Opciones Requeridas

- `-f, --file <ruta>`: Archivo JSON con las transacciones a enviar
- `-m, --mqConnection <cadena>`: Parámetros de conexión MQ en formato: `IP;Puerto;Canal;ManagerName`
  - Ejemplo: `192.168.0.31;1414;CHANNEL1;MQGD`
- `-d, --duration <segundos>`: Duración de la prueba en segundos

#### Opciones Opcionales

- `-s, --slaves <IPs>`: Lista de IPs de los esclavos separadas por punto y coma
  - Ejemplo: `192.168.0.31;192.168.0.24;192.168.0.25`
- `-p, --slavePort <puerto>`: Puerto donde escuchan los esclavos (default: 8888)
  - Requiere que se especifique `-s`
- `-T, --slaveTimeout <segundos>`: Timeout de espera para respuestas de esclavos (default: 5)
- `-t, --threadNumber <número>`: Número de hilos para el test (default: número de CPUs)
- `-r, --rateLimit <mensajes/segundo>`: Límite de mensajes por segundo a enviar
  - Ejemplo: `-r 100` para limitar a 100 mensajes/segundo
  - Default: 0 (sin límite)
- `-v, --ResponsePreview`: Flag para mostrar previsualización de cada respuesta recibida
- `-e, --messageExpiration <segundos>`: Tiempo de expiración de mensajes (mínimo: 240 segundos)
  - Default: 0 (sin expiración)

### Modo Slave

El modo slave se activa con el verbo `slave` y queda a la espera de comandos del master.

#### Opciones Requeridas

- `-m, --mqConnection <cadena>`: Parámetros de conexión MQ (mismo formato que master)
- `-i, --InputQueue <nombre>`: Nombre de la cola de entrada
- `-o, --OutputQueue <nombre>`: Nombre de la cola de salida

#### Opciones Opcionales

- `-p, --port <puerto>`: Puerto donde escuchará el esclavo (default: 8888)
- `-t, --threadNumber <número>`: Número de hilos para el test (default: número de CPUs)
- `-r, --rateLimitDelay <microsegundos>`: Delay intencional entre mensajes (default: 0)
- `-e, --messageExpiration <segundos>`: Tiempo de expiración de mensajes (mínimo: 240 segundos)

## Ejemplos de Invocación

### Ejemplo 1: Prueba Local Simple

Ejecutar una prueba de carga local durante 60 segundos:

```bash
StampedeLoadTester master -f ".\TestFiles\Desa\trx_0559_164users.json" -m "10.6.248.10;1414;CHANNEL1;MQGD" -d 60
```

### Ejemplo 2: Prueba con Rate Limiting

Ejecutar una prueba limitando el envío a 100 mensajes por segundo:

```bash
StampedeLoadTester master -f ".\TestFiles\Desa\trx_0559_164users.json" -m "10.6.248.10;1414;CHANNEL1;MQGD" -d 120 -r 100
```

### Ejemplo 3: Prueba Distribuida con Esclavos

Ejecutar una prueba distribuyendo la carga entre el master y dos esclavos:

```bash
# En el master:
StampedeLoadTester master -f ".\TestFiles\Desa\trx_0559_164users.json" -m "10.6.248.10;1414;CHANNEL1;MQGD" -d 300 -s "192.168.0.31;192.168.0.24" -p 8888

# En cada esclavo (ejecutar en máquinas separadas):
StampedeLoadTester slave -m "10.6.248.10;1414;CHANNEL1;MQGD" -i "COLA_ENTRADA" -o "COLA_SALIDA" -p 8888
```

### Ejemplo 4: Prueba con Preview de Respuestas

Ejecutar una prueba mostrando el contenido de cada respuesta:

```bash
StampedeLoadTester master -f ".\TestFiles\Desa\trx_0559_164users.json" -m "10.6.248.10;1414;CHANNEL1;MQGD" -d 30 -v
```

### Ejemplo 5: Prueba con Expiración de Mensajes

Ejecutar una prueba con mensajes que expiran después de 5 minutos (300 segundos):

```bash
StampedeLoadTester master -f ".\TestFiles\Desa\trx_0559_164users.json" -m "10.6.248.10;1414;CHANNEL1;MQGD" -d 180 -e 300
```

### Ejemplo 6: Prueba con Configuración Personalizada de Hilos

Ejecutar una prueba usando 4 hilos específicamente:

```bash
StampedeLoadTester master -f ".\TestFiles\Desa\trx_0559_164users.json" -m "10.6.248.10;1414;CHANNEL1;MQGD" -d 60 -t 4
```

## Métricas Reportadas

El programa genera un reporte detallado con las siguientes métricas:

### Estadísticas de Latencia
- Promedio, desviación estándar, mínimo y máximo
- Percentiles: P25, P50 (mediana), P75, P95, P99
- Coeficiente de variación
- Tasa de éxito (porcentaje de mensajes con respuesta)

### Throughput
- Mensajes enviados por segundo
- Respuestas recibidas por segundo
- Tiempo total de envío

### Estadísticas de Procesamiento de Cola
- Velocidad de procesamiento (mensajes/segundo)
- Estadísticas de profundidad de cola durante el procesamiento

### Resumen de Mensajes
- Total de mensajes enviados
- Mensajes con respuesta
- Mensajes sin respuesta
- Mensajes colocados por master y esclavos

## Notas Importantes

- Las colas de entrada y salida se toman del archivo JSON, no de los parámetros de línea de comandos
- El programa realiza automáticamente un warmup antes de iniciar la prueba
- Las colas se vacían automáticamente antes y después del warmup
- El programa espera a que la cola de salida se vacíe antes de comenzar a recibir respuestas
- Los mensajes se correlacionan usando el MessageId como CorrelationId

## Requisitos

- .NET 8.0 o superior
- IBM MQ Client Libraries
- Acceso a las colas MQ configuradas

