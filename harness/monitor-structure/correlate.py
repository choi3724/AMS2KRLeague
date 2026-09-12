from pathlib import Path
import json,sys,collections
w=Path(sys.argv[1]); app=json.loads((w/'app.jsonl').read_text(encoding='utf-8-sig').splitlines()[0]);summary=json.loads((w/'schedule-summary.json').read_text());pid=app['pid'];ui=app['uiNativeThread'];render=set(summary['renderThreadsByStack']);selected={ui}|render
starts={};runs=collections.defaultdict(list);wait=collections.defaultdict(collections.Counter)
for line in (w/'native-stacks.jsonl').open(encoding='utf-8-sig'):
 r=json.loads(line)
 if r['name']!='Thread/CSwitch':continue
 f=r['fields'];old,new=int(f['OldThreadID']),int(f['NewThreadID']);t=r['ms']
 if int(f['OldProcessID'])==pid and old in selected:
  wait[old][f['OldThreadState']+'/'+f['OldThreadWaitReason']]+=1
  if old in starts:runs[old].append((starts.pop(old),t))
 if int(f['NewProcessID'])==pid and new in selected:starts[new]=t
hwnds={str(x['hwnd']):x['name'] for x in app['windows']};times=collections.defaultdict(list)
for line in (w/'frames.jsonl').open(encoding='utf-8-sig'):
 r=json.loads(line)
 if r['name']!='ETWGUID_DWMUPDATEWINDOW':continue
 try:h=str(int(r['fields'].get('hWnd',''),0))
 except ValueError:continue
 if h in hwnds:times[hwnds[h]].append(r['ms'])
out=[]
for surface,points in times.items():
 if surface=='timing':continue
 points=sorted(set(points));points=[t for i,t in enumerate(points) if i==0 or t-points[i-1]>.1]
 for a,b in zip(points,points[1:]):
  if b-a<=16.667:continue
  cpu={str(tid):sum(max(0,min(y,b)-max(x,a)) for x,y in runs[tid] if x<b and y>a) for tid in selected}
  out.append(dict(surface=surface,startMs=a,endMs=b,gapMs=b-a,cpuMs=cpu))
result=dict(ui=ui,render=list(render),overBudget=len(out),over33=sum(x['gapMs']>33 for x in out),waitReasonCounts=wait,intervals=out,
 limitations='Profiled run. Wait intervals combine blocked and unknown ready delay; ReadyThread events not captured. DWM delivery is not physical FPS. Running sums may exceed gap because threads execute concurrently.')
(w/'frame-correlation.json').write_text(json.dumps(result,indent=2),encoding='utf8')
print('correlated',len(out),'over-budget DWM intervals;',result['over33'],'over33')
