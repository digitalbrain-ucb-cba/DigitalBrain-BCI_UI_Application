import os
import subprocess as sp
from datetime import datetime


def siguiente_nombre(carpeta, prefijo, extension=".txt"):
    """Busca el siguiente numero libre 'prefijo_N.txt' dentro de carpeta."""
    os.makedirs(carpeta, exist_ok=True)
    n = 1
    while True:
        nombre = os.path.join(carpeta, f"{prefijo}_{n}{extension}")
        if not os.path.exists(nombre):
            return nombre
        n += 1


def ejecutar_y_guardar(ruta_exe, carpeta_salida="datos_capturados", prefijo="captura"):
    """Ejecuta ruta_exe, imprime su salida en tiempo real y la guarda en un .txt nuevo.

    Cada llamada genera un archivo distinto (captura_1.txt, captura_2.txt, ...)
    para no sobrescribir ejecuciones anteriores.
    """
    nombre_archivo = siguiente_nombre(carpeta_salida, prefijo)

    process = sp.Popen([ruta_exe], stdout=sp.PIPE, stderr=sp.STDOUT, text=True, shell=False)

    with open(nombre_archivo, "w", encoding="utf-8") as f:
        for line in iter(process.stdout.readline, ""):
            print(line, end="")
            f.write(line)

    process.wait()
    print(f"\nDatos guardados en: {nombre_archivo}")
    return nombre_archivo


if __name__ == "__main__":
    ejecutar_y_guardar("ConDumpAuto.exe", carpeta_salida="datos_capturados", prefijo="captura")
