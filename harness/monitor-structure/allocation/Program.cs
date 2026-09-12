using Microsoft.Diagnostics.Tracing.Etlx;
using System.Text.Json;
var converted=TraceLog.CreateFromEventPipeDataFile(args[0]);
using var trace=new TraceLog(converted);
var types=new Dictionary<string,long>();var paths=new Dictionary<string,long>();
double first=double.MaxValue,last=0;int count=0,withStack=0;
foreach(var ev in trace.Events){
 if(ev.EventName!="GC/AllocationTick")continue;
 long size=Convert.ToInt64(ev.PayloadByName("AllocationAmount64"));
 string type=ev.PayloadByName("TypeName")?.ToString()??"unknown";
 types[type]=types.GetValueOrDefault(type)+size;count++;
 first=Math.Min(first,ev.TimeStampRelativeMSec);last=Math.Max(last,ev.TimeStampRelativeMSec);
 var frames=new List<string>();for(var stack=ev.CallStack();stack!=null;stack=stack.Caller)frames.Add(stack.CodeAddress.FullMethodName);
 if(frames.Count>0)withStack++;
 // Attribute a tick once to its complete path, not every inclusive ancestor.
 string path=string.Join(" <- ",frames.Take(24));paths[path]=paths.GetValueOrDefault(path)+size;
}
double seconds=(last-first)/1000;
if(seconds<25||count==0)throw new InvalidOperationException("Insufficient allocation capture");
Console.WriteLine(JsonSerializer.Serialize(new{seconds,count,withStack,eventsLost=trace.EventsLost,estimatedMiBps=types.Values.Sum()/1048576.0/seconds,
 types=types.OrderByDescending(x=>x.Value).Take(30).Select(x=>new{name=x.Key,bytes=x.Value,MiBps=x.Value/1048576.0/seconds}),
 paths=paths.OrderByDescending(x=>x.Value).Take(35).Select(x=>new{path=x.Key,bytes=x.Value,MiBps=x.Value/1048576.0/seconds})},new JsonSerializerOptions{WriteIndented=true}));
