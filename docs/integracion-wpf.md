# Integración con WPF

## Estado actual

`FRONT/` ya contiene la arquitectura base de escritorio en C# con WPF sobre .NET 10 (con patrón MVVM, inyección de dependencias, búfer circular y servicios desacoplados). Para detalles de la arquitectura implementada, ver [documentacion.md](../documentacion.md).

Actualmente, WPF se integra con la CLI de Python (`PythonCliBridge`) para operaciones por lote (`build-dataset` y `train`), mientras que la captura y osciloscopio son administrados de forma desacoplada por `ConDumpCaptureService`. Durante `capture`, la salida del dispositivo se procesa de forma asíncrona por líneas. Los modelos `.pkl` se gestionan desde Python.

| Operación | Disponible en Python | Interfaz preparada para WPF |
| --- | --- | --- |
| Capturar a un archivo | Comando `capture` con la ruta de la carpeta externa del programa | No hay eventos estructurados de progreso o control remoto de parada. |
| Crear CSV | Comando `build-dataset` | Solo argumentos y resultado de texto. |
| Entrenar | Comando `train` con `--algorithm` | Solo argumentos y exactitud impresa. |
| Predecir ventanas | Clase `RealtimeClassifier` | No hay comando ni conexión con la adquisición. |

## Contrato por definir

Antes de construir la interfaz, hay que acordar cómo se iniciará y distribuirá Python junto a WPF. Una opción adecuada para este proyecto de escritorio es un proceso Python local controlado por WPF con mensajes JSON por línea: solicitudes de captura, preparación, entrenamiento, predicción y parada; respuestas con identificador de operación, estado, resultados y errores. Los registros y la salida cruda del dispositivo deben quedar separados del canal de mensajes. **Este protocolo es una propuesta; no está implementado.**

También faltan decisiones sobre versión del contrato, cancelación, ubicación de capturas y modelos, esquema de etiquetas, metadatos de sesión y compatibilidad entre el modelo guardado y el extractor usado para inferencia. [Arquitectura](arquitectura.md) y [Datos y modelos](datos-y-modelos.md) describen los componentes Python que servirían de base.
