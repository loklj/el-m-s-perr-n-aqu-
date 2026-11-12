package com.loklj.folioxr;

/** Geometria del paginador: el paso entre columnas siempre mide 596 px. */
public final class FolioReadingSettings {
    public final int font, margin;
    public final float line;
    public FolioReadingSettings(int font,float line,int margin){
        this.font=font==1?1:0;
        this.line=line==1.2f||line==1.8f?line:1.4f;
        this.margin=margin==24||margin==72?margin:48;
    }
    public String family(){return font==0?"serif":"sans";}
    public boolean equals(Object o){if(!(o instanceof FolioReadingSettings))return false;FolioReadingSettings s=(FolioReadingSettings)o;return font==s.font&&line==s.line&&margin==s.margin;}
    public int hashCode(){return font*10000+Float.floatToIntBits(line)+margin;}
    public static boolean fontAsset(String path){return path.matches("folio-fonts/(serif|sans)-(Regular|Italic|Bold|BoldItalic)\\.ttf");}
    public String css(float scale){
        StringBuilder b=new StringBuilder("<style>");
        String[] variants={"Regular","Italic","Bold","BoldItalic"};
        for(int i=0;i<4;i++)b.append("@font-face{font-family:FolioText;src:url('https://folio.invalid/folio-fonts/").append(family()).append('-').append(variants[i]).append(".ttf') format('truetype');font-style:").append(i%2==1?"italic":"normal").append(";font-weight:").append(i>=2?"bold":"normal").append(";}");
        b.append("html{width:596px!important;height:842px!important;overflow:hidden!important;}")
         .append("body{box-sizing:content-box!important;margin:").append(margin).append("px!important;padding:0!important;width:").append(596-2*margin)
         .append("px!important;height:").append(842-2*margin).append("px!important;column-width:").append(596-2*margin).append("px!important;column-gap:").append(2*margin)
         .append("px!important;column-fill:auto!important;font-size:").append(16*scale).append("px!important;color:#111!important;background:white!important;}")
         .append("*{font-family:FolioText,serif!important;line-height:").append(line).append("!important;max-width:100%!important;}img,svg{max-height:").append(842-2*margin)
         .append("px!important;object-fit:contain;}script,iframe,object,embed,form{display:none!important;}</style>");
        return b.toString();
    }
    public static String label(String value){
        if(value==null)return "";
        value=value.replaceAll("[\\p{Cntrl}\\s]+"," ").trim();
        int end=value.offsetByCodePoints(0,Math.min(120,value.codePointCount(0,value.length())));
        return value.substring(0,end);
    }
}
