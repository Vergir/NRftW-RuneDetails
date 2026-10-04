import re,sys,pickle,bisect
sys.path.insert(0,r'C:\Users\vergir\Downloads\nrftw\tools')
from aspect_scan import parse_sections, rva_to_off
from capstone import Cs, CS_ARCH_X86, CS_MODE_64
import disasm_rva as D
meths=D.methods()
rvas=[r for r,_ in meths]
def name(r):
    i=bisect.bisect_right(rvas,r)-1
    return meths[i][1] if i>=0 else '?'
data=open(r'C:\Users\vergir\Downloads\nrftw\reference\GameAssembly.dll','rb').read()
base,secs=parse_sections(data)
text=[s for s in secs if s['name'].startswith('.text') or s['name'].startswith('il2cpp')]
md=Cs(CS_ARCH_X86,CS_MODE_64)
vals=[int(x,0) for x in sys.argv[1:]]
pats=[]
for v in vals:
    b=v.to_bytes(4,'little')
    pats += [(b'\x41\xb9'+b,'r9d',v),(b'\x41\xb8'+b,'r8d',v),(b'\xba'+b,'edx',v)]
for s in secs:
    if not (s['name'].startswith('.text') or s['name'].startswith('il2cpp')): continue
    lo=s['rawptr']; hi=lo+s['rawsize']
    for p,reg,v in pats:
        i=data.find(p,lo,hi)
        while i!=-1:
            rva=s['vaddr']+i-lo
            # disasm forward up to 48 bytes, find first call
            for ins in md.disasm(data[i:i+64],rva):
                if ins.mnemonic=='call':
                    try: t=int(ins.op_str,16)
                    except: t=None
                    tn=name(t) if t else ins.op_str
                    if 'Stat' in tn:
                        print(hex(rva),reg,hex(v),name(rva),'->',tn)
                    break
            i=data.find(p,i+1,hi)
