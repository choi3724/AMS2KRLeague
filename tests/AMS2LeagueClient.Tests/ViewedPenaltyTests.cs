using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AMS2LeagueClient.Core.Presentation;
using AMS2LeagueClient.Core.Telemetry;
using AMS2LeagueClient.Presentation;
using AMS2LeagueClient.Runtime;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void ViewedPenaltyReachesTower()
        {
            var fixture=new RawFixtureBuilder(3).SetSession(SessionState.Race).SetViewedIndex(1);
            OverlayViewModel Build()
            {
                var s=Parse(fixture);
                return OverlayViewModel.Build(s,ResolveLocal(s),Classify(s),30,20,false,"FIXTURE");
            }
            foreach(bool racing in new[]{false,true})
            {
                var tower=new OverlayHudView{VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Left};tower.SetRacingDesign(racing);
                var host=new Window{Content=tower,Width=680,Height=250,Left=-5000,Top=-5000,ShowActivated=false};
                try
                {
                host.Show();PumpDispatcher();
                foreach(var item in new[]{(PitSchedule.DriveThrough,"드라이브스루"),(PitSchedule.StopGo,"스톱 앤 고"),(PitSchedule.None,"—"),(PitSchedule.Mandatory,"—")})
                {
                    fixture.SetRootControl(FlagColour.None,pitSchedule:item.Item1);
                    var model=Build();
                    AssertEqual(item.Item2,model.AllRankingRows.Single(r=>r.ParticipantIndex==1).PenaltyText);
                    AssertTrue(model.AllRankingRows.Where(r=>r.ParticipantIndex!=1).All(r=>r.PenaltyText=="—"));
                    tower.SetViewModel(model);host.UpdateLayout();
                    var frame=new DispatcherFrame();
                    var settle=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(550)};
                    settle.Tick+=(_,__)=>{settle.Stop();frame.Continue=false;};
                    settle.Start();Dispatcher.PushFrame(frame);
                    var cells=Descendants<TextBlock>(tower).Where(t=>t.Name=="PenaltyText").ToArray();
                    AssertEqual(3,cells.Length);
                    AssertTrue(cells.All(t=>t.IsVisible && t.ActualWidth>0 && t.ActualHeight>0));
                    AssertEqual(item.Item2=="—"?3:1,cells.Count(t=>t.Text==item.Item2));
                    CaptureLayout(tower,$"viewed-penalty-{racing}-{item.Item1}");
                }
                }
                finally{host.Content=null;host.Close();}
            }
            fixture.SetRootControl(FlagColour.None,pitSchedule:PitSchedule.StopGo).SetViewedIndex(-1);
            var unknown=Parse(fixture);
            AssertTrue(unknown.Participants.All(p=>StateText.Penalty(p,unknown)=="—"));
            fixture.SetViewedIndex(2).SetParticipantControl(0,PitSchedule.DriveThrough);
            var switched=Build();
            AssertEqual("드라이브스루",switched.AllRankingRows.Single(r=>r.ParticipantIndex==0).PenaltyText);
            AssertEqual("—",switched.AllRankingRows.Single(r=>r.ParticipantIndex==1).PenaltyText);
            AssertEqual("스톱 앤 고",switched.AllRankingRows.Single(r=>r.ParticipantIndex==2).PenaltyText);
            fixture.SetParticipantControl(2,PitSchedule.None,FlagColour.Black);
            AssertEqual("실격",Build().AllRankingRows.Single(r=>r.ParticipantIndex==2).PenaltyText);
            Console.WriteLine("PROOF viewed root DT/SG reaches both tower designs; clear, view switch, unknown view, other participants and DSQ preserved; time penalties unavailable in v14 header");
        }

        private static void NumericPenaltyColumnRendersSeparately()
        {
            var preview = DemoSnapshotFactory.CreateViewModel(false);
            AssertTrue(preview.AllRankingRows.Any(row => row.PenaltyText == "드라이브스루"));
            AssertTrue(preview.AllRankingRows.Any(row => row.TimeOrGridPenaltyText == "+33초"));
            AssertTrue(preview.AllRankingRows.Any(row => row.TimeOrGridPenaltyText == "순위 +1"));
            var fixture = new RawFixtureBuilder(3).SetSession(SessionState.Race).SetViewedIndex(1)
                .SetParticipantControl(0, PitSchedule.DriveThrough);
            TelemetrySnapshot snapshot = Parse(fixture);
            var model = OverlayViewModel.Build(snapshot, ResolveLocal(snapshot), Classify(snapshot),
                30, 20, false, "FIXTURE", timeOrGridPenalties: new System.Collections.Generic.Dictionary<int, string>
                {
                    [0] = "+33초",
                    [1] = "순위 +1"
                });
            AssertEqual("드라이브스루", model.AllRankingRows.Single(row => row.ParticipantIndex == 0).PenaltyText);
            AssertEqual("+33초", model.AllRankingRows.Single(row => row.ParticipantIndex == 0).TimeOrGridPenaltyText);
            AssertEqual("순위 +1", model.AllRankingRows.Single(row => row.ParticipantIndex == 1).TimeOrGridPenaltyText);
            AssertEqual("—", model.AllRankingRows.Single(row => row.ParticipantIndex == 2).TimeOrGridPenaltyText);
            foreach (bool racing in new[] { false, true })
            {
                var tower = new OverlayHudView();
                tower.SetRacingDesign(racing);
                var host = new Window { Content = tower, Width = 648, Height = 210,
                    Left = -5000, Top = -5000, ShowActivated = false };
                try
                {
                    tower.SetViewModel(model);
                    host.Show(); PumpDispatcher(); tower.UpdateLayout();
                    var numeric = Descendants<TextBlock>(tower)
                        .Where(text => text.Name == "TimeOrGridPenaltyText").ToArray();
                    AssertEqual(3, numeric.Length);
                    AssertTrue(numeric.All(text => text.IsVisible && text.ActualWidth > 0 && text.ActualHeight > 0));
                    AssertTrue(numeric.Any(text => text.Text == "+33초"));
                    AssertTrue(numeric.Any(text => text.Text == "순위 +1"));
                    AssertTrue(Descendants<TextBlock>(tower).Any(text => text.Text == "시간·순위"));
                    AssertTrue(Descendants<TextBlock>(tower).Any(text => text.Name == "PenaltyText"
                        && text.Text == "드라이브스루"));
                    CaptureLayout(tower, racing ? "numeric-penalty-racing" : "numeric-penalty-legacy");
                }
                finally { host.Content = null; host.Close(); }
            }
        }
    }
}
