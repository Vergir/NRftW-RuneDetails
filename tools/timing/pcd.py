import sys,os
os.chdir(os.path.dirname(os.path.abspath(__file__))+'/..')
sys.argv=['dumpq.py']
exec(open('dumpq.py',encoding='utf-8').read().split("if __name__")[0])
for g,el in A.items():
    d=el[1]
    if isinstance(d,dict) and ('AttackInterruptByActionRecoveryPercentage' in d or 'DesiredRTTMWindowLength' in d):
        print(g,nm(g),tn.get(el[0]))
        for k,v in d.items():
            if k in ('Plugins',): continue
            print('  ',k,conv(v))
