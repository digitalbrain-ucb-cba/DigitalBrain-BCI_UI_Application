from sklearn.model_selection import train_test_split
from sklearn.ensemble import RandomForestClassifier
import pandas as pd
import pickle as pkl

csv1="PiernaIzquierda.csv"
csv2="BrazoDer.csv"
nombremodelo="ModelMotores.pkl"

df0 = pd.read_csv(csv1, encoding="latin-1")
df1 = pd.read_csv(csv2, encoding="latin-1")
df = pd.concat([df0, df1])
df.drop(columns=["Unnamed: 0"], inplace=True)
X = df.iloc[:,0:16]
y = df.iloc[:,16]

X_train, X_test, y_train, y_test = train_test_split(X, y, test_size = 0.2, random_state = 7)

#Esto guarda el modelo
rf = RandomForestClassifier()
rf.fit(X_train, y_train)
print(rf.score(X_test, y_test))
with open(nombremodelo, "wb") as f:
    pkl.dump(rf, f)
#Esto carga el modelo
# with open(nombremodelo, "rb") as f:
#      rf = pkl.load(f)