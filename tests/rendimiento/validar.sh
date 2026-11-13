#!/bin/sh
set -eu
cd "$(dirname "$0")/../.."
: "${ANDROID_JAR:?Indica android.jar de API 34}"
JAVAC=${JAVAC:-javac}
JAVA=${JAVA:-java}
classes=$(mktemp -d)
trap 'rm -rf "$classes"' EXIT HUP INT TERM
"$JAVAC" --release 8 -cp "$ANDROID_JAR" -d "$classes" hito1/Assets/Plugins/Android/Folio*.java tests/rendimiento/LargeFilesTest.java
"$JAVA" -Xmx64m -cp "$classes:$ANDROID_JAR" com.loklj.folioxr.LargeFilesTest

"$JAVAC" --release 8 -Xlint:-options -cp "$classes:$ANDROID_JAR" -d "$classes" $(find tests/rendimiento/android-stubs -name '*.java') tests/rendimiento/RendererTest.java tests/rendimiento/MetricsTest.java
"$JAVA" -Xmx96m -cp "$classes:$ANDROID_JAR" com.loklj.folioxr.RendererTest
"$JAVA" -Xmx64m -cp "$classes:$ANDROID_JAR" com.loklj.folioxr.MetricsTest
