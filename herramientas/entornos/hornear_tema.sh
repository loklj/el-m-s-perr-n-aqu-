#!/bin/sh
# Hornea un tema en tres procesos (casa, terreno cercano, panorama y exportación) para no agotar la memoria.
# Uso: sh hornear_tema.sh moderna-calida [opciones de generar.py]
set -e
cd "$(dirname "$0")"
tema=$1; shift
mkdir -p /tmp/bl/logs
python3 generar.py "$tema" --solo casa "$@" > "/tmp/bl/logs/$tema.casa.log" 2>&1
python3 generar.py "$tema" --solo cerca "$@" > "/tmp/bl/logs/$tema.cerca.log" 2>&1
python3 generar.py "$tema" --todo "$@" > "/tmp/bl/logs/$tema.log" 2>&1
