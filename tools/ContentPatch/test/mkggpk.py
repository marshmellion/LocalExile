import struct, hashlib, sys
def murmur2_32(data, seed=0):
    m=0x5bd1e995; r=24; h=(seed ^ len(data)) & 0xffffffff; i=0
    while len(data)-i>=4:
        k=struct.unpack_from('<I',data,i)[0]; k=(k*m)&0xffffffff; k^=k>>r; k=(k*m)&0xffffffff
        h=(h*m)&0xffffffff; h^=k; i+=4
    rem=len(data)-i
    if rem==3: h^=data[i+2]<<16
    if rem>=2: h^=data[i+1]<<8
    if rem>=1: h^=data[i]; h=(h*m)&0xffffffff
    h^=h>>13; h=(h*m)&0xffffffff; h^=h>>15; return h
def namehash(n): return murmur2_32(n.lower().encode('utf-16-le'))
def name_bytes(n): return (n+'\0').encode('utf-16-le')
class W:
    def __init__(s): s.b=bytearray()
def file_rec(name,data):
    nb=name_bytes(name); h=hashlib.sha256(data).digest()
    body=struct.pack('<I',len(nb)//2)+h+nb+data
    return struct.pack('<I',8+len(body))+b'FILE'+body, h
def dir_rec(name,entries):  # entries: list of (name, offset, hash)
    nb=name_bytes(name); ents=sorted(entries,key=lambda e: namehash(e[0]))
    h=hashlib.sha256(b''.join(e[2] for e in ents)).digest()
    body=struct.pack('<II',len(nb)//2,len(ents))+h+nb+b''.join(struct.pack('<Iq',namehash(e[0]),e[1]) for e in ents)
    return struct.pack('<I',8+len(body))+b'PDIR'+body, h
def build(files_by_dir, out):
    # layout: GGPK header (28) | files | Bundles2 dir | root dir
    out_b=bytearray(b'\0'*28); entries={}
    for d,files in files_by_dir.items():
        for name,data in files:
            rec,h=file_rec(name,data); entries.setdefault(d,[]).append((name,len(out_b),h)); out_b+=rec
    sub=[]
    for d,ents in entries.items():
        rec,h=dir_rec(d,ents); sub.append((d,len(out_b),h)); out_b+=rec
    root,_=dir_rec('',sub); root_off=len(out_b); out_b+=root
    struct.pack_into('<I4sIqq',out_b,0,28,b'GGPK',3,root_off,0)
    open(out,'wb').write(out_b)
if __name__=='__main__':
    import json; spec=json.load(open(sys.argv[1]))
    build({d:[(n,open(p,'rb').read()) for n,p in f] for d,f in spec.items()}, sys.argv[2])
