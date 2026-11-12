#!/bin/sh
# Hornea en serie los temas indicados (o todos). Un tema que falle no detiene a los demás.
cd "$(dirname "$0")"
mkdir -p /tmp/bl/logs
temas=${*:-"cabana-nevada casa-del-bosque otono-en-el-valle isla-tropical casa-del-desierto loft-nocturno noche-estrellada casa-del-lago"}
for t in $temas; do
  inicio=$(date +%s)
  if sh hornear_tema.sh "$t" --vista terraza,sofa,cama,jardin; then estado=ok; else estado=FALLO; fi
  echo "$t $estado $(( ($(date +%s) - inicio) / 60 )) min" >> /tmp/bl/logs/lote.txt
done
echo FIN >> /tmp/bl/logs/lote.txt
