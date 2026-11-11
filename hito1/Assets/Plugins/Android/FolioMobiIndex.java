package com.loklj.folioxr;
import java.io.*;
import java.util.*;
import static com.loklj.folioxr.FolioMobiCodec.*;

/** Lector propio INDX/TAGX; usado por las tablas de esqueletos y fragmentos KF8. */
final class FolioMobiIndex {
    interface Records { byte[] get(int index) throws IOException; }
    static final class Entry {
        String label;
        final Map<Integer,long[]> values=new HashMap<>();
        long value(int tag,int index) throws IOException {
            long[] v=values.get(tag); if(v==null || index>=v.length) throw new IOException("Tag KF8 ausente"); return v[index];
        }
    }
    private static long variable(byte[] data,int[] cursor,int end) throws IOException {
        long value=0;
        for(int i=0;i<5;i++) { if(cursor[0]>=end) throw new IOException("VLI truncado"); int b=data[cursor[0]++]&255; value=(value<<7)|(b&127); if((b&128)!=0)return value; }
        throw new IOException("VLI demasiado grande");
    }
    static List<Entry> read(Records records,int index) throws IOException {
        byte[] meta=records.get(index); magic(meta,0,"INDX"); int tag=(int)u32(meta,4); magic(meta,tag,"TAGX");
        int tagLength=(int)u32(meta,tag+4), controls=(int)u32(meta,tag+8), sections=(int)u32(meta,24);
        range(meta,tag,tagLength);
        if(controls<1 || controls>32 || sections<1 || sections>1024 || tagLength<12 || (tagLength-12)%4!=0) throw new IOException("TAGX invalido");
        List<Entry> result=new ArrayList<>();
        for(int section=1;section<=sections;section++) {
            byte[] data=records.get(index+section); magic(data,0,"INDX"); int idxt=(int)u32(data,20), count=(int)u32(data,24); magic(data,idxt,"IDXT");
            if(count<0 || count>65535 || result.size()+count>100000) throw new IOException("INDX demasiado grande");
            range(data,idxt+4,count*2);
            for(int e=0;e<count;e++) {
                int begin=u16(data,idxt+4+2*e), end=e+1<count?u16(data,idxt+6+2*e):idxt;
                if(begin>=end)throw new IOException("Entrada INDX vacia"); range(data,begin,end-begin);
                int length=data[begin]&255; range(data,begin+1,length+controls);
                Entry entry=new Entry(); entry.label=new String(data,begin+1,length,java.nio.charset.StandardCharsets.UTF_8);
                int control=begin+1+length; int[] cursor={control+controls}; int c=0;
                List<int[]> descriptors=new ArrayList<>();
                for(int t=tag+12;t<tag+tagLength;t+=4) {
                    int id=meta[t]&255, per=meta[t+1]&255, mask=meta[t+2]&255;
                    if((meta[t+3]&255)==1) { c++; continue; }
                    if(mask==0 || c>=controls || per==0) throw new IOException("Descriptor TAGX invalido");
                    int present=(data[control+c]&255)&mask;
                    if(present==0)continue;
                    if(present==mask && Integer.bitCount(mask)>1) descriptors.add(new int[]{id,per,-1,(int)variable(data,cursor,end)});
                    else descriptors.add(new int[]{id,per,present>>>Integer.numberOfTrailingZeros(mask),0});
                }
                for(int[] desc:descriptors) {
                    List<Long> values=new ArrayList<>(); int stop=desc[2]<0?cursor[0]+desc[3]:-1;
                    if(stop>end) throw new IOException("Valores TAGX truncados");
                    if(stop>=0) while(cursor[0]<stop)values.add(variable(data,cursor,stop));
                    else for(int n=0;n<desc[1]*desc[2];n++) values.add(variable(data,cursor,end));
                    long[] v=new long[values.size()]; for(int n=0;n<v.length;n++)v[n]=values.get(n); entry.values.put(desc[0],v);
                }
                result.add(entry);
            }
        }
        return result;
    }
}
