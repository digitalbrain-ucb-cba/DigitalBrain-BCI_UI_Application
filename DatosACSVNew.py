import os
import glob
import pandas as pd
import numpy as np


def rms(l):
    t = 0
    for n in l:
        t = t + pow(n, 2)
    t = np.sqrt(t / len(l))
    return t


def avr(l):
    t = 0
    for n in l:
        t = t + abs(n)
    t = t / len(l)
    return t


def leer_datos_a_csv(folder_path, nombre_csv, label="lol,lmao", tamanowindow=125, avancewindow=63):
    """Lee todos los .txt de una carpeta y genera un CSV con features por ventana."""
    folder_path = os.path.abspath(folder_path)
    file_list = sorted(glob.glob(os.path.join(folder_path, "*.txt")))

    if not file_list:
        raise FileNotFoundError(f"No se encontraron archivos .txt en: {folder_path}")

    labels = []
    ThetaA, ThetaB, ThetaC, ThetaD = [], [], [], []
    AlphaA, AlphaB, AlphaC, AlphaD = [], [], [], []
    BetaA, BetaB, BetaC, BetaD = [], [], [], []
    GammaA, GammaB, GammaC, GammaD = [], [], [], []

    for currentfile in file_list:
        with open(currentfile, encoding="utf-8") as file:
            datosAp, datosBp, datosCp, datosDp = [], [], [], []
            for line in file:
                if line.strip() and (line.startswith("-") or line[0].isdigit()):
                    line = line.replace('\r', '').replace('\n', '')
                    current = line.split(",")
                    guardar = 1
                    for num in current:
                        if num == "-800" or num == "800":
                            guardar = 0
                    if guardar == 1 and len(current) >= 4:
                        datosAp.append(current[0])
                        datosBp.append(current[1])
                        datosCp.append(current[2])
                        datosDp.append(current[3])

        if len(datosAp) == 0:
            continue

        tamano = len(datosAp)
        sl = 0
        sr = tamanowindow
        step = avancewindow
        datosAp = list(map(float, datosAp))
        datosBp = list(map(float, datosBp))
        datosCp = list(map(float, datosCp))
        datosDp = list(map(float, datosDp))
        Hz = np.fft.fftfreq(tamanowindow, 1 / 125)

        while sr < tamano:
            dfA = datosAp[sl:sr]
            dfB = datosBp[sl:sr]
            dfC = datosCp[sl:sr]
            dfD = datosDp[sl:sr]

            dfA = np.abs(np.fft.fft(dfA)) ** 2
            dfB = np.abs(np.fft.fft(dfB)) ** 2
            dfC = np.abs(np.fft.fft(dfC)) ** 2
            dfD = np.abs(np.fft.fft(dfD)) ** 2

            ThetaA.append(np.sum(dfA[(Hz >= 4) & (Hz < 8)]))
            ThetaB.append(np.sum(dfB[(Hz >= 4) & (Hz < 8)]))
            ThetaC.append(np.sum(dfC[(Hz >= 4) & (Hz < 8)]))
            ThetaD.append(np.sum(dfD[(Hz >= 4) & (Hz < 8)]))

            AlphaA.append(np.sum(dfA[(Hz >= 8) & (Hz < 13)]))
            AlphaB.append(np.sum(dfB[(Hz >= 8) & (Hz < 13)]))
            AlphaC.append(np.sum(dfC[(Hz >= 8) & (Hz < 13)]))
            AlphaD.append(np.sum(dfD[(Hz >= 8) & (Hz < 13)]))

            BetaA.append(np.sum(dfA[(Hz >= 13) & (Hz < 30)]))
            BetaB.append(np.sum(dfB[(Hz >= 13) & (Hz < 30)]))
            BetaC.append(np.sum(dfC[(Hz >= 13) & (Hz < 30)]))
            BetaD.append(np.sum(dfD[(Hz >= 13) & (Hz < 30)]))

            GammaA.append(np.sum(dfA[(Hz >= 30)]))
            GammaB.append(np.sum(dfB[(Hz >= 30)]))
            GammaC.append(np.sum(dfC[(Hz >= 30)]))
            GammaD.append(np.sum(dfD[(Hz >= 30)]))

            labels.append(label)
            sl += step
            sr += step

    pandaData = dict(zip(
        ['ThetaA', 'ThetaB', 'ThetaC', 'ThetaD',
         'AlphaA', 'AlphaB', 'AlphaC', 'AlphaD',
         'BetaA', 'BetaB', 'BetaC', 'BetaD',
         'GammaA', 'GammaB', 'GammaC', 'GammaD',
         'label'],
        [ThetaA, ThetaB, ThetaC, ThetaD,
         AlphaA, AlphaB, AlphaC, AlphaD,
         BetaA, BetaB, BetaC, BetaD,
         GammaA, GammaB, GammaC, GammaD,
         labels]
    ))

    df = pd.DataFrame(pandaData)
    df.to_csv(nombre_csv, index=False)
    return df


if __name__ == "__main__":
    # Ejemplo de uso: lee la carpeta left y luego right
    leer_datos_a_csv(os.path.join("datos", "left"), "left.csv")
    leer_datos_a_csv(os.path.join("datos", "right"), "right.csv")