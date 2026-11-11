#!/bin/sh
set -eu
cd "$(dirname "$0")/../.."
: "${ANDROID_JAR:?Indica el android.jar de API 34}"
JAVAC=${JAVAC:-javac}
JAVA=${JAVA:-java}
classes=$(mktemp -d)
trap 'rm -rf "$classes"' EXIT HUP INT TERM
"$JAVAC" --release 8 -Xlint:-options -cp "$ANDROID_JAR" -d "$classes" hito1/Assets/Plugins/Android/Folio*.java tests/formatos/*.java tests/epub/ContainerTest.java
"$JAVA" -Xmx64m -cp "$classes:hito1/Assets/Plugins/Android/FolioRar/*" com.loklj.folioxr.FormatsTest "${1:-}"
"$JAVA" -cp "$classes" com.loklj.folioxr.CodecTest
"$JAVA" -cp "$classes:$ANDROID_JAR" com.loklj.folioxr.LibraryFormatTest
"$JAVA" -cp "$classes" com.loklj.folioxr.ContainerTest tests/fixtures/libro-prueba.epub
