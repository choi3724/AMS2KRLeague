using Microsoft.Diagnostics.Tracing;
using System.Text.Json;
using var source=new ETWTraceEventSource(args[0]);
using var output=new StreamWriter(args[1]);
Console.WriteLine($"START={source.SessionStartTime:O}");
source.Dynamic.All += ev => {
 if(args.Length>3 && args[3]=="frames" && ev.EventName!="ETWGUID_DWMUPDATEWINDOW" && ev.EventName!="Present/Start") return;
 if (ev.ProcessID!=int.Parse(args[2]) && !((ev.EventName.StartsWith("QueuePacket")||ev.EventName.StartsWith("DmaPacket"))&& ev.ProviderName.Contains("DxgKrnl")) && !ev.ProviderName.Contains("Dwm") && !(ev.ProviderName.Contains("DXGI")&&ev.EventName.StartsWith("Present/"))) return;
 output.WriteLine(JsonSerializer.Serialize(new{ms=ev.TimeStampRelativeMSec,pid=ev.ProcessID,tid=ev.ThreadID,provider=ev.ProviderName,name=ev.EventName,fields=ev.PayloadNames.ToDictionary(n=>n,n=>ev.PayloadByName(n)?.ToString())}));
};
source.Process();
Console.WriteLine($"LOST={source.EventsLost}");

