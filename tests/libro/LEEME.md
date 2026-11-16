# Pruebas de la forma del libro

`validar.py` compila `Reading/BookGeometry.cs` (la misma forma que usa el visor) con una matemática mínima de
Unity (`UnityShim.cs`) y corre `GeometryTest.cs`: hojas y sombra mirando a quien lee, arte de la tapa hacia
afuera, portada y contraportada sin espejo con el libro cerrado (normal y manga), lomo del lado correcto,
grosor según la cantidad de páginas y lo leído, tapa dura o blanda.

También compila `Reading/PageTurnShape.cs` (la hoja que sigue a la mano): la hoja sale y llega con la forma
exacta de las páginas, no se estira ni las atraviesa, el punto tomado avanza siempre hacia el otro lado, de la
posición de la mano se recupera el avance, a mitad de camino queda parada sobre el lomo, y la tapa en la mano
sigue el ángulo que pide la mano.

```
DOTNET_ROOT=/usr/lib/dotnet python3 tests/libro/validar.py
```

No reemplaza la prueba en el visor (materiales, luz y gestos).
