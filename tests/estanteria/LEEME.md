# Pruebas de la estantería

`validar.py` compila `Books/ShelfLayout.cs` (las cuentas que deciden dónde queda cada libro en una fila) y corre
`LayoutTest.cs`: los libros no se enciman ni se salen de la fila, el que se suelta queda donde lo dejaron y
aparta a los vecinos, una fila llena no acepta otro libro y los libros nuevos van al primer hueco libre.
Incluye 4 000 filas al azar.

```
DOTNET_ROOT=/usr/lib/dotnet python3 tests/estanteria/validar.py
```

No reemplaza la prueba en el visor (tomar, soltar y ver los libros en las estanterías de la casa).
