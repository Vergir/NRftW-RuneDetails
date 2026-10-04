import re,sys
sys.path.insert(0,r'C:\Users\vergir\Downloads\nrftw\tools')
from aspect_scan import parse_sections, rva_to_off
from capstone import Cs, CS_ARCH_X86, CS_MODE_64
data=open(r'C:\Users\vergir\Downloads\nrftw\reference\GameAssembly.dll','rb').read()
base,secs=parse_sections(data)
md=Cs(CS_ARCH_X86,CS_MODE_64)
want=set(int(x) for x in sys.argv[1:]) if len(sys.argv)>1 else None
for l in open(r'C:\Users\vergir\AppData\Local\Temp\claude\C--Users-vergir-Downloads-nrftw\646afbc7-9767-4923-9694-4ac4ae0b02c4\scratchpad\tim\secx.txt'):
    m=re.match(r'call @0x([0-9A-F]+) -> 0x([0-9A-F]+)\s+in (.*)',l)
    if not m: continue
    site=int(m.group(1),16); tgt=int(m.group(2),16)
    off,_=rva_to_off(secs,site-0x50)
    regs={}
    for ins in md.disasm(data[off:off+0x55],site-0x50):
        if ins.address>=site: break
        if ins.mnemonic=='mov' and ins.op_str.split(',')[0] in ('edx','r8d','r9d'):
            regs[ins.op_str.split(',')[0]]=ins.op_str.split(',')[1].strip()
    # id arg: TimelineActionData.IsActive(id..) -> edx ; ActionData.IsSectionActive(f, entity, id) -> r9d
    idv = regs.get('edx') if tgt in (0x5BA4000,0x5BA4030,0x5BA41A0,0x5BA42F0,0x5BA4620) else regs.get('r9d')
    print(hex(site), hex(tgt), 'id=',idv, m.group(3)[:90])
