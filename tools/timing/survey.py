import sys,os,csv,collections
os.chdir(os.path.dirname(os.path.abspath(__file__))+'/..')
sys.argv=['dumpq.py']
exec(open('dumpq.py',encoding='utf-8').read().split("if __name__")[0])
rows=list(csv.DictReader(open(r'C:\Users\vergir\Downloads\nrftw\analysis\rune_inventory.csv',encoding='utf-8')))
obt=set()
try:
    for r in csv.DictReader(open(r'C:\Users\vergir\Downloads\nrftw\analysis\rune_obtainability.csv',encoding='utf-8')):
        if r.get('obtainable','').lower() in ('true','yes','1'): obt.add(r.get('stem'))
except Exception as e: print('obt',e)
stem2g={}
for k,v in names.items():
    if v.get('stem'): stem2g.setdefault(v['stem'],[]).append(int(k))
c=collections.Counter()
for r in rows:
    gs=[g for g in stem2g.get(r['action'],[]) if g in A and 'TimelineData' in A[g][1]]
    if not gs: c['noaction']+=1; continue
    d=A[gs[0]][1]; tl=d['TimelineData']
    segs=d.get('Segments') or []
    ssum=sum(s['FrameCount'] for s in segs)/60
    dur=conv(tl['Duration'])
    isatk=(d['ActionType']&0x20e)!=0
    proc=bool(d.get('UseProceduralInterrupts')) and isatk
    rf=any(s.get('RetimingFrames') for s in segs)
    states=len(tl.get('StateInfos') or [])
    key=('seg' if segs else 'noseg', 'proc' if proc else 'sect', 'durOK' if abs(dur-ssum)<0.02 else 'durStale', 'retime' if rf else '', 'multistate' if states>1 else '')
    c[key]+=1
    if r['stem'] in obt or not obt:
        if not segs or states>1 or rf: print(r['stem'],r['action'],key,round(ssum,3),round(dur,3),'AT',d['ActionType'],'states',states, type(d).__name__ if False else tn.get(A[gs[0]][0]))
for k,v in c.most_common(): print(v,k)
