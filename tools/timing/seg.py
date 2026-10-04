import sys,os
os.chdir(os.path.dirname(os.path.abspath(__file__))+'/..')
args=sys.argv[1:]
sys.argv=['dumpq.py']
exec(open('dumpq.py',encoding='utf-8').read().split("if __name__")[0])
def r(x): 
    v=conv(x); return round(v,3) if isinstance(v,(int,float)) else v
def show_action(g):
    el=A.get(g); d=el[1]
    tl=d.get('TimelineData') or {}
    print('== %s %s type=%s AT=%s flags=%s DefInt=%s procInt=%s recOff=%s procAS=%s PowerArmour=%s'%(g,nm(g),tn.get(el[0]),d.get('ActionType'),d.get('ActionFlags'),d.get('DefaultInterruptingType'),d.get('UseProceduralInterrupts'),r(d.get('RecoveryTimeOffset')),d.get('UseProceduralAttackSections'),r(d.get('PowerArmour'))))
    segs=d.get('Segments') or []
    print('  segs',[(s.get('Name'),s.get('FrameCount'),s.get('RetimingFrames'),s.get('State')) for s in segs],'sum',sum(s.get('FrameCount',0) for s in segs)/60)
    print('  anims',[(a.get('Name'),a.get('FrameCount'),a.get('SegmentIndex'),a.get('StartFrame')) for a in d.get('Animations') or []])
    print('  Duration',r(tl.get('Duration')),'states',[(r(s.get('End')),s.get('SegmentIndex'),[(t.get('Condition'),t.get('StateIndex')) for t in s.get('Transitions') or []]) for s in tl.get('StateInfos') or []],'start',tl.get('StartingStateIndex'))
    print('  AttackSections',conv(tl.get('AttackSections')))
    print('  sections',[(s.get('Id'),r(s.get('Start')),r(s.get('End')),s.get('Mask')) for s in tl.get('Sections') or []])
    print('  immunity',conv(tl.get('ImmunitySections')))
    print('  TSR',conv(tl.get('TimeScaleRegions')))
    print('  colliders',[(r(c['StartTime']),r(c['EndTime']),c.get('SubDamageId')) for c in tl.get('WeaponColliders') or []])
    print('  proj',[(r(c['SpawnTime']),r(c['ReleaseTime'])) for c in tl.get('ProjectileEvents') or []])
    print('  spawn',[(r(c.get('spawnTime')),r(c.get('unspawnTime')),c.get('fireAndForget'),nm(c.get('entityToSpawn',{}).get('Id',{}).get('Value'))) for c in tl.get('SpawnEntityEvents') or []])
    print('  minion',[r(c.get('StartTime')) for c in tl.get('SpawnMinionEvents') or []],'sfx',[r(c.get('StartTime')) for c in tl.get('SpecialEffectEvents') or []])
    print('  States(field)',str(d.get('States'))[:300])
    other={k:(str(conv(v))[:150]) for k,v in d.items() if k not in ('TimelineData','Segments','Animations','Actors','Baked','DamageConfig','Cost','AdditionalCost','Identifier','Guid','Path','AiTargetSwitchSettings','TargetingData','Conditions','States')}
    print('  other',other)
for a in args:
    for k,v in names.items():
        if v.get('stem')==a:
            el=A.get(int(k))
            if el and 'TimelineData' in el[1]: show_action(int(k))
