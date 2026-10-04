import sys,os,math,csv
os.chdir(os.path.dirname(os.path.abspath(__file__))+'/..')
args=sys.argv[1:]
sys.argv=['dumpq.py']
exec(open('dumpq.py',encoding='utf-8').read().split("if __name__")[0])
def f(x):
    v=conv(x); return float(v) if v is not None else None
AS=float(os.environ.get('AS','1'))
def model(d):
    segs=d.get('Segments') or []
    tl=d['TimelineData']
    S=[];N=[];FC=[];s=0
    for g in segs:
        fc=g['FrameCount']; rf=g.get('RetimingFrames',0)
        n=math.ceil(max(1,fc+rf)/AS)
        S.append(s);FC.append(fc);N.append(n);s+=fc
    end=s/60
    def real(t):
        x=t*60
        acc=0
        for i in range(len(segs)):
            if x < S[i]+FC[i] or i==len(segs)-1:
                return (acc + (x-S[i])*N[i]/FC[i])/60
            acc+=N[i]
        return acc/60
    return segs,end,real,sum(N)/60
def events(tl):
    st=[];en=[];first=[]
    for c in tl.get('WeaponColliders') or []:
        st.append(f(c['StartTime']));en.append(f(c['EndTime']));first.append(('hit',f(c['StartTime'])))
    for p in tl.get('ProjectileEvents') or []:
        st.append(f(p['SpawnTime']));en.append(f(p['ReleaseTime']));first.append(('proj',f(p['ReleaseTime'])))
    for e in tl.get('SpawnEntityEvents') or []:
        a=f(e['spawnTime']);b=f(e['unspawnTime'])
        st.append(a); en.append(a if (e.get('fireAndForget') or b==-1) else b); first.append(('spawn:'+nm(e['entityToSpawn']['Id']['Value']),a))
    for e in tl.get('SpawnMinionEvents') or []:
        a=f(e['StartTime']);st.append(a);en.append(a)
    for e in tl.get('SpecialEffectEvents') or []:
        a=f(e['StartTime']);st.append(a);en.append(a);first.append(('fx',a))
    return st,en,first
def secs(tl,i):
    return [(f(s['Start']),f(s['End']),s.get('Mask')) for s in tl.get('Sections') or [] if s['Id']==i]
def run(g):
    d=A[g][1]; tl=d['TimelineData']
    segs,end,real,realend=model(d)
    st,en,first=events(tl)
    isatk = (d['ActionType'] & 0x20e)!=0
    proc = d.get('UseProceduralInterrupts') and isatk
    out={'action':nm(g),'AT':d['ActionType'],'proc':proc,'end_auth':round(end,3),'end_real':round(realend,3),'Duration_field':round(f(tl['Duration']),3)}
    fe=sorted(first,key=lambda x:x[1])
    out['first']= [(k,round(t,3),round(real(t),3)) for k,t in fe[:3]]
    if proc:
        rs=max(en)+f(d['RecoveryTimeOffset']) if en else None
        if rs is None or rs<0: out['lockout']='never (no events) -> end'
        else:
            for name,thr in (('dodge',0.18),('attack',0.05),('comboEnd',0.5)):
                t=rs+thr*(end-rs)
                out['lock_'+name]=(round(t,3),round(real(t),3))
            out['recStart']=(round(rs,3),round(real(rs),3))
    else:
        cand=[s for s in secs(tl,2)+secs(tl,4) if (s[2] & 16)]
        out['lock_dodge_sections']=[(round(a,3),round(real(a),3)) for a,b,m in cand]
    for i in (7,45,46,47):
        w=secs(tl,i)
        if w: out['sec%d'%i]=[(round(a,3),round(b,3),round(real(a),3),round(real(min(b,end)),3)) for a,b,m in w]
    if d.get('ActionFlags',0)&4: out['MakeInvincible']=True
    print(out)
for a in args:
    for k,v in names.items():
        if v.get('stem')==a:
            el=A.get(int(k))
            if el and 'TimelineData' in el[1]: run(int(k))
