import sys,os,collections
os.chdir(os.path.dirname(os.path.abspath(__file__))+'/..')
sys.argv=['dumpq.py']
exec(open('dumpq.py',encoding='utf-8').read().split("if __name__")[0])
cnt=collections.Counter(); ex={}
def walk(o,path,g):
    if isinstance(o,dict):
        if 'AdditionalPercentage' in o:
            cnt[(tn.get(A[g][0]),path[-4:] if len(path)>=4 else path)]+=1
            ex.setdefault(tn.get(A[g][0]),[]).append((nm(g),o.get('Type'),o.get('AdditionalPercentage')))
        for k,v in o.items(): walk(v,path+(str(k),),g)
    elif isinstance(o,(list,tuple)):
        for v in o: walk(v,path+('[]',),g)
for g,el in A.items(): walk(el[1],(),g)
for k,v in cnt.most_common(20): print(v,k)
for t,l in ex.items(): print(t,len(l),l[:12])
