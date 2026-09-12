from pathlib import Path
import json,collections,sys
w=Path(sys.argv[1]); rows=[json.loads(x) for x in (w/'app.jsonl').read_text(encoding='utf-8-sig').splitlines() if x.startswith('{')]
app=next(x for x in rows if x['type']=='start');pid=app['pid'];ui=app['uiNativeThread']
counts=collections.Counter(); frames=collections.Counter(); starts={}; intervals=collections.defaultdict(list); off={}; waits=collections.defaultdict(list);cpus=collections.defaultdict(set);render=set()
last=0;first=None
for line in (w/'native-stacks.jsonl').open(encoding='utf-8-sig'):
 r=json.loads(line);t=r['ms'];last=max(last,t);first=t if first is None else min(first,t);f=r['fields']
 if r['name']=='PerfInfo/Sample' and r['pid']==pid:
  counts[r['tid']]+=1;cpus[r['tid']].add(int(f['ProcessorNumber']))
  for name in set(r['frames']):frames[name]+=1
  if any('CPartitionThread::ThreadMain' in x for x in r['frames']):render.add(r['tid'])
 if r['name']!='Thread/CSwitch':continue
 old,new=int(f['OldThreadID']),int(f['NewThreadID'])
 if int(f['OldProcessID'])==pid:
  if old in starts:intervals[old].append((starts.pop(old),t))
  off[old]=(t,f['OldThreadState'],f['OldThreadWaitReason'])
 if int(f['NewProcessID'])==pid:
  starts[new]=t
  if new in off:
   at,state,reason=off.pop(new);waits[new].append((t-at,state,reason))
span=last-first
def stat(v):
 v=sorted(v);return {'count':len(v),'sumMs':sum(v),'maxMs':max(v,default=0),'p95Ms':v[int((len(v)-1)*.95)] if v else None}
out={'pid':pid,'uiNativeThread':ui,'renderThreadsByStack':sorted(render),'spanMs':span,'totalSamples':sum(counts.values()),'threads':{},'inclusive':frames.most_common(35)}
for tid,n in counts.items():
 run=intervals[tid]
 out['threads'][tid]={'samples':n,'role':'UI (logged native ID)' if tid==ui else 'WPF render (identified by stack)' if tid in render else 'other',
 'running':stat([b-a for a,b in run]),'logicalProcessorsObserved':sorted(cpus[tid]),'readyPreemptedOffCpu':stat([t for t,s,r in waits[tid] if s=='Ready']),
 'waitOffCpuIncludesUnknownReadyDelay':stat([t for t,s,r in waits[tid] if s!='Ready'])}
# Actual simultaneous execution of identified UI and render threads, from CSwitch intervals.
events=[]
for tid in {ui}|render:
 for a,b in intervals[tid]:events.extend([(a,1),(b,-1)])
events.sort();n=0;prior=None;concurrent=0
for t,delta in events:
 if prior is not None and n>=2:concurrent+=t-prior
 n+=delta;prior=t
out['uiRenderConcurrentMs']=concurrent
(w/'schedule-summary.json').write_text(json.dumps(out,indent=2),encoding='utf8')
print(json.dumps(out,indent=2)[:10000])
