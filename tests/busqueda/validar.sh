#!/bin/sh
set -eu
cd "$(dirname "$0")/../.."
: "${ANDROID_JAR:?Indica android.jar de API 34}"
JAVAC=${JAVAC:-javac}
JAVA=${JAVA:-java}
NODE=${NODE:-node}
classes=$(mktemp -d)
trap 'rm -rf "$classes"' EXIT HUP INT TERM
"$JAVAC" --release 8 -Xlint:-options -cp "$ANDROID_JAR" -d "$classes" hito1/Assets/Plugins/Android/Folio*.java tests/busqueda/BookSearchTest.java
"$JAVA" -Xmx96m -cp "$classes" com.loklj.folioxr.BookSearchTest "$classes/nucleo.js"
if command -v "$NODE" >/dev/null 2>&1; then "$NODE" tests/busqueda/nucleo.test.js "$classes/nucleo.js"
else echo "node no instalado: se omite la prueba del nucleo JS"; fi
