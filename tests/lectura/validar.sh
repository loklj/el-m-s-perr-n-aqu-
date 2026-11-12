#!/bin/sh
set -eu
cd "$(dirname "$0")/../.."
: "${ANDROID_JAR:?Indica android.jar de API 34}"
JAVAC=${JAVAC:-javac}
JAVA=${JAVA:-java}
classes=$(mktemp -d)
trap 'rm -rf "$classes"' EXIT HUP INT TERM
"$JAVAC" --release 8 -Xlint:-options -cp "$ANDROID_JAR" -d "$classes" hito1/Assets/Plugins/Android/Folio*.java tests/lectura/ReadingTest.java
"$JAVA" -Xmx96m -cp "$classes" com.loklj.folioxr.ReadingTest "${1:-/tmp/folio-reading-layouts}"
