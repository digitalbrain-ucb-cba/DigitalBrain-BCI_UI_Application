# Ejecutables externos

Guarda aquí el `.exe` de adquisición que quieras ejecutar. Por ejemplo:

```text
assets/executables/ConDumpAuto.exe
```

Desde la raíz del proyecto, indica su nombre para que el comando lo busque en esta carpeta:

```powershell
python -m digital_brain.cli capture ConDumpAuto.exe
```

También puedes pasar una ruta completa si el ejecutable está en otra ubicación:

```powershell
python -m digital_brain.cli capture "C:\ruta\a\mi-capturador.exe" datos_capturados --prefix "sesion" --seconds 30 --capture-from 6
```

La carpeta de salida predeterminada es `datos_capturados/`. Usa `--prefix` para elegir el prefijo del archivo y `--seconds` / `--capture-from` para definir la duración total y desde qué segundo se guarda la salida.

El binario es una dependencia de infraestructura: no pertenece al código de dominio ni se inicia al importar ningún módulo. Está excluido de Git.
