# Integración con WPF

## Estado actual

`FRONT/` todavía no contiene un proyecto C# o WPF; su README señala el destino previsto. El backend tampoco expone HTTP, sockets, tuberías con mensajes estructurados ni un comando `predict`.

WPF puede iniciar hoy la CLI de Python para operaciones por lote, pero sus mensajes de consola son texto para personas, no un contrato estable para una interfaz. Durante `capture`, la salida del dispositivo se mezcla con mensajes de estado. Los modelos `.pkl` se cargan desde Python; WPF no los consume directamente.

| Operación | Disponible en Python | Interfaz preparada para WPF |
| --- | --- | --- |
| Capturar a un archivo | Comando `capture` con la ruta de la carpeta externa del programa | No hay eventos estructurados de progreso o control remoto de parada. |
| Crear CSV | Comando `build-dataset` | Solo argumentos y resultado de texto. |
| Entrenar | Comando `train` con `--algorithm` | Solo argumentos y exactitud impresa. |
| Predecir ventanas | Clase `RealtimeClassifier` | No hay comando ni conexión con la adquisición. |

## Contrato por definir

Antes de construir la interfaz, hay que acordar cómo se iniciará y distribuirá Python junto a WPF. Una opción adecuada para este proyecto de escritorio es un proceso Python local controlado por WPF con mensajes JSON por línea: solicitudes de captura, preparación, entrenamiento, predicción y parada; respuestas con identificador de operación, estado, resultados y errores. Los registros y la salida cruda del dispositivo deben quedar separados del canal de mensajes. **Este protocolo es una propuesta; no está implementado.**

También faltan decisiones sobre versión del contrato, cancelación, ubicación de capturas y modelos, esquema de etiquetas, metadatos de sesión y compatibilidad entre el modelo guardado y el extractor usado para inferencia. [Arquitectura](arquitectura.md) y [Datos y modelos](datos-y-modelos.md) describen los componentes Python que servirían de base.
