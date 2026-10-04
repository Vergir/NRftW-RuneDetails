import re,sys
sys.path.insert(0,r'C:\Users\vergir\Downloads\nrftw\tools')
from aspect_scan import parse_sections, rva_to_off
from capstone import Cs, CS_ARCH_X86, CS_MODE_64
data=open(r'C:\Users\vergir\Downloads\nrftw\reference\GameAssembly.dll','rb').read()
base,secs=parse_sections(data)
md=Cs(CS_ARCH_X86,CS_MODE_64)
lines=open(r'C:\Users\vergir\AppData\Local\Temp\claude\C--Users-vergir-Downloads-nrftw\646afbc7-9767-4923-9694-4ac4ae0b02c4\scratchpad\getstat_callers.txt').read().splitlines()
for l in lines:
    m=re.match(r'call @0x([0-9A-F]+) -> 0x([0-9A-F]+)\s+in (.*)',l)
    if not m: continue
    site=int(m.group(1),16)
    off,_=rva_to_off(secs,site-0x60)
    code=data[off:off+0x60+5]
    r9=None
    for ins in md.disasm(code,site-0x60):
        if ins.address>=site: break
        if ins.mnemonic=='mov' and ins.op_str.startswith('r9d,'):
            r9=ins.op_str.split(',')[1].strip()
    if r9 in ('0x49','0x4a','0x48','0x47'):
        print(hex(site),r9,m.group(3))
