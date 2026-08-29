import subprocess as sp
import threading
import pickle as pkl
from typing import Any, List

import numpy as np
import pandas as pd

nombremodelo="model.pkl"
datosA = [Any] * 125
datosB = [Any] * 125
datosC = [Any] * 125
datosD = [Any] * 125
n=125
procesar = 0
Hz = np.fft.fftfreq(n, 1 / 125)
with open(nombremodelo, "rb") as f:
     rf = pkl.load(f)

def predict(datosA,datosB,datosC,datosD):
    dfA = datosA[0:125]
    dfB = datosB[0:125]
    dfC = datosC[0:125]
    dfD = datosD[0:125]
    dfA = np.abs(np.fft.fft(dfA)) ** 2
    dfB = np.abs(np.fft.fft(dfB)) ** 2
    dfC = np.abs(np.fft.fft(dfC)) ** 2
    dfD = np.abs(np.fft.fft(dfD)) ** 2
    ThetaA=np.sum(dfA[(Hz >= 4) & (Hz < 8)])
    ThetaB=np.sum(dfB[(Hz >= 4) & (Hz < 8)])
    ThetaC=np.sum(dfC[(Hz >= 4) & (Hz < 8)])
    ThetaD=np.sum(dfD[(Hz >= 4) & (Hz < 8)])
    AlphaA=np.sum(dfA[(Hz >= 8) & (Hz < 13)])
    AlphaB=np.sum(dfB[(Hz >= 8) & (Hz < 13)])
    AlphaC=np.sum(dfC[(Hz >= 8) & (Hz < 13)])
    AlphaD=np.sum(dfD[(Hz >= 8) & (Hz < 13)])
    BetaA=np.sum(dfA[(Hz >= 13) & (Hz < 30)])
    BetaB=np.sum(dfB[(Hz >= 13) & (Hz < 30)])
    BetaC=np.sum(dfC[(Hz >= 13) & (Hz < 30)])
    BetaD=np.sum(dfD[(Hz >= 13) & (Hz < 30)])
    GammaA=np.sum(dfA[(Hz >= 30)])
    GammaB=np.sum(dfB[(Hz >= 30)])
    GammaC=np.sum(dfC[(Hz >= 30)])
    GammaD=np.sum(dfD[(Hz >= 30)])
    predictData = dict(zip(['ThetaA', 'ThetaB', 'ThetaC', 'ThetaD',
                          'AlphaA', 'AlphaB', 'AlphaC', 'AlphaD',
                          'BetaA', 'BetaB', 'BetaC', 'BetaD',
                          'GammaA', 'GammaB', 'GammaC', 'GammaD'],
                         [ThetaA, ThetaB, ThetaC, ThetaD,
                          AlphaA, AlphaB, AlphaC, AlphaD,
                          BetaA, BetaB, BetaC, BetaD,
                          GammaA, GammaB, GammaC, GammaD]))
    df=pd.DataFrame(predictData)
    return rf.predict(df)

while True:
    process = sp.Popen(["ConDumpAuto.exe"], stdout=sp.PIPE, text=True, shell=False)
    for line in iter(process.stdout.readline, ""):
        print(line)
        if line.startswith("-") or line[0].isdigit():
            line = line.replace('\r', '').replace('\n', '')
            current = line.split(",")
            datosA[procesar]=current[0]
            datosB[procesar]=current[1]
            datosC[procesar]=current[2]
            datosD[procesar]=current[3]
            procesar += 1
        if procesar == 64:
            print(predict(datosA,datosB,datosC,datosD))
        elif procesar == 126:
            print(predict(datosA,datosB,datosC,datosD))
            procesar = 0