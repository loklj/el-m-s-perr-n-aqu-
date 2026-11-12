package com.loklj.folioxr;

import java.util.Locale;

/** Motivos sin prometer lectores futuros ni deducir DRM a partir de una extension. */
public final class FolioDocumentDiagnostics {
    private FolioDocumentDiagnostics(){}
    public static String unsupported(String format){
        String value=format.toLowerCase(Locale.ROOT);int dot=value.lastIndexOf('.');if(dot>=0)value=value.substring(dot+1);
        String reason;
        switch(value){
            case "doc":reason="DOC binario requiere un conversor no incluido. Exporta a DOCX o PDF desde el PC.";break;
            case "ole":reason="Contenedor OLE/CFB: puede ser DOC binario u Office cifrado. No hay lector incluido; no se ha confirmado DRM. Usa una copia DOCX/PDF sin proteccion.";break;
            case "kfx":reason="No hay lector KFX incluido. Usa una edicion EPUB/PDF sin DRM; la extension no demuestra proteccion.";break;
            default:reason="No hay lector para este formato o no se reconoce su estructura. Usa PDF o EPUB sin DRM.";break;
        }
        return "Formato a\u00fan no compatible: "+reason;
    }
}
