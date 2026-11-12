#!/bin/sh
set -eu
cd "$(dirname "$0")/../.."
: "${ANDROID_JAR:?Indica el android.jar de API 34}"
JAVAC=${JAVAC:-javac}
JAVA=${JAVA:-java}
classes=$(mktemp -d)
trap 'rm -rf "$classes"' EXIT HUP INT TERM
"$JAVAC" --release 8 -Xlint:-options -cp "$ANDROID_JAR" -d "$classes" hito1/Assets/Plugins/Android/Folio*.java tests/documentos/*.java
"$JAVA" -Xmx64m -cp "$classes" com.loklj.folioxr.DocumentsTest
"$JAVA" -cp "$classes:$ANDROID_JAR" com.loklj.folioxr.DocumentDetectionTest
