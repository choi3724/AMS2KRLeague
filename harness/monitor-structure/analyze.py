from pathlib import Path
from datetime import datetime
import json, sys, statistics, ctypes

def read(path):
    raw=path.read_bytes()
    return raw.decode('utf-16' if raw.startswith((b'\xff\xfe',b'\xfe\xff')) else 'utf-8-sig')
def dt(value): return datetime.fromisoformat(value)
def stats(times):
    unique=[]
    for t in sorted(set(times)):
        if not unique or t-unique[-1]>.1:unique.append(t)
    times=unique;gaps=sorted(b-a for a,b in zip(times,times[1:]))
    return dict(count=len(times),hz=1000*len(gaps)/sum(gaps),p95=gaps[int((len(gaps)-1)*.95)],p99=gaps[int((len(gaps)-1)*.99)],max=max(gaps),stalls33=sum(g>33 for g in gaps)) if gaps else None
def main(folder):
    runs={}
    for file in folder.glob('*-?.jsonl'):
        rows=[json.loads(line) for line in read(file).splitlines() if line.startswith('{')]
        bytype={r['type']:r for r in rows}
        if not {'start','measure','result'}<=bytype.keys():continue
        start,measurement,result=(bytype[k] for k in ('start','measure','result'))
        runs[file.stem]=dict(start=start,result=result,begin=dt(measurement['utc']),end=dt(result['utc']),windows={str(w['hwnd']):dict(name=w['name'],times=[]) for w in start['windows']},dxgi=[r for r in rows if r['type']=='dxgi-statistics'])
    meta=read(folder/'frames-meta.txt');origin=dt(meta.split('START=')[1].splitlines()[0]);lost=int(meta.split('LOST=')[1].splitlines()[0]);assert lost==0,meta
    with (folder/'frames.jsonl').open(encoding='utf-8-sig') as stream:
        for line in stream:
            if 'ETWGUID_DWMUPDATEWINDOW' not in line:continue
            row=json.loads(line)
            try:hwnd=str(int(row['fields'].get('hWnd',''),0))
            except ValueError:continue
            t=origin.timestamp()*1000+row['ms']
            for run in runs.values():
                if run['begin'].timestamp()*1000<=t<=run['end'].timestamp()*1000 and hwnd in run['windows']:run['windows'][hwnd]['times'].append(t)
    output={}
    freq=ctypes.c_longlong();ctypes.windll.kernel32.QueryPerformanceFrequency(ctypes.byref(freq))
    for name,run in runs.items():
        result=dict(process=run['result'],dwm={w['name']:stats(w['times']) for w in run['windows'].values()},dxgi_reported={},physical_fps='NOT MEASURED')
        for dx in run['dxgi']:
            samples=[s for s in dx['samples'] if run['begin']<=dt(s['utc'])<=run['end']]
            periods=[(b['SyncQPCTime']-a['SyncQPCTime'])/freq.value*1000/(b['SyncRefreshCount']-a['SyncRefreshCount']) for a,b in zip(samples,samples[1:]) if b['SyncRefreshCount']>a['SyncRefreshCount']]
            period=statistics.median(periods) if periods else 0
            times=[s['SyncQPCTime']/freq.value*1000+(s['PresentRefreshCount']-s['SyncRefreshCount'])*period for s in samples]
            result['dxgi_reported'][dx['name']]=dict(metrics=stats(times),refreshPeriodMs=period,uniquePolls=len(samples),presentCountDelta=(samples[-1]['PresentCount']-samples[0]['PresentCount']) if samples else 0,
                caveat='DXGI statistics; not identical to DWM UpdateWindow; multi-monitor reliability not independently validated')
        gpu_file=folder/(name+'-gpu.json')
        gpu=json.loads(read(gpu_file)) if gpu_file.exists() else []
        values=[sum(r['UtilizationPercentage'] for r in s['gpu']) for s in gpu if s['gpu'] and run['begin']<=dt(s['utc'])<=run['end']]
        result['gpuEngineSumPercent']=statistics.mean(values) if values else None
        result['gpuCounterSamples']=len(values);output[name]=result
    (folder/'summary.json').write_text(json.dumps(output,indent=2),encoding='utf8')
    for name,r in output.items(): print(name,json.dumps(dict(cpu=r['process']['cpuMachinePercent'],ram=r['process']['workingSetMB'],allocation=r['process']['allocationMBps'],dwm=r['dwm'],dxgi=r['dxgi_reported'],gpu=r['gpuEngineSumPercent'])))
if __name__=='__main__':
    if len(sys.argv)==1:
        assert stats([0,10,20,60])['stalls33']==1
        assert stats([0,10,10,20])['hz']==100
        assert stats([]) is None
        print('analysis self-check PASS')
    else:main(Path(sys.argv[1]))
