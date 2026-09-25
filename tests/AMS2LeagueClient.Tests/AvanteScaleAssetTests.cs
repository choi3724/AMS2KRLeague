using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AMS2LeagueClient.Presentation;
using AMS2LeagueClient.Core.Presentation;

namespace AMS2LeagueClient.Tests
{
    internal static partial class Program
    {
        private static void AvantePackagedScales()
        {
            foreach (bool expanded in new[] { false, true })
            {
                var view = new AvanteClusterView(expanded) { Width = expanded ? 1024 : 454, Height = 375 };
                void Layout()
                { view.Measure(new Size(view.Width,view.Height));view.Arrange(new Rect(0,0,view.Width,view.Height));view.UpdateLayout(); }
                for (int maximum = 4000; maximum <= 20000; maximum += 1000)
                {
                    double red=maximum*.9137, yellow=maximum*.8173;
                    ConfigureReferenceN(view,maximum,yellow,red); Layout();
                    AssertEqual(maximum,view.PreRenderedMaximum);
                    AssertEqual(maximum/1000+1,((DrawingVisual)VisualTreeHelper.GetChild(view,5)).Children.Count);
                    AssertEqual(red,view.RpmScale.RedStart);AssertEqual(yellow,view.RpmScale.YellowStart);
                    int builds=view.StaticFaceBuilds;
                    foreach(double rpm in new[]{yellow-.01,yellow,yellow+.01,red-.01,red,red+.01})
                    {
                        view.SetSample(new DrivingTelemetrySample(DateTimeOffset.UtcNow,1,0,0,0,0,0,50,3,rpm:rpm,maxRpm:maximum));
                        Layout();AssertEqual(rpm>=red?2:rpm>=yellow?1:0,view.RpmBand);
                        AssertEqual(builds,view.StaticFaceBuilds);
                        AssertEqual(view.RpmScale.Angle(rpm),view.NeedleAngle);
                    }
                    // Parent/style invalidation must not rasterize an unchanged face again.
                    view.InvalidateVisual();Layout();AssertEqual(builds,view.StaticFaceBuilds);
                    AssertEqual(5,view.RpmScale.LitPairs(red));
                    if(maximum==4000 || maximum==8000 || maximum==12000 || maximum==19000 || maximum==20000)
                        CaptureLayout(view,$"packaged-scale-{maximum}-{expanded}",1);
                }
                ConfigureReferenceN(view,22500,17000.25,21155.5);Layout();
                AssertEqual(0,view.PreRenderedMaximum);AssertEqual(22500d,view.RpmScale.Maximum);
                int old=view.StaticFaceBuilds;
                view.Width*=1.25;view.Height*=1.25;Layout();AssertEqual(old+1,view.StaticFaceBuilds);
                view.InvalidateVisual();Layout();AssertEqual(old+1,view.StaticFaceBuilds);
                view.SetSample(null);
            }
            Console.WriteLine("PROOF 17 packaged scales x normal/expanded; exact non-rounded boundaries, numeral counts, full light threshold, unchanged invalidation cache, resize and custom-range fallback preserved");
        }
        // Offline asset generation only. Calls the original font-outline routine so the
        // packaged images retain its exact font, skew, rim and shadow; no gameplay capture.
        private static void ExportAvanteScales(string directory)
        {
            Directory.CreateDirectory(directory);
            var view = new AvanteClusterView();
            var type = typeof(AvanteClusterView);
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            var text = type.GetMethod("Text", flags)!;
            var sector = type.GetMethod("Sector", flags)!;
            var major = (Pen)type.GetField("MajorTickPen", flags)!.GetValue(null)!;
            var minor = (Pen)type.GetField("MinorTickPen", flags)!.GetValue(null)!;
            void Text(DrawingContext dc,string value,double x,double y,double size,bool styled=false)
                => text.Invoke(view,new object?[]{dc,value,x,y,size,styled,false,null,false,false});
            Point Point(double radius,double angle)
                => new Point(1024+radius*Math.Cos(angle*Math.PI/180),397+radius*Math.Sin(angle*Math.PI/180));
            for(int maximum=4000;maximum<=20000;maximum+=1000)
            {
                var visual = new DrawingVisual();
                using(var dc=visual.RenderOpen())
                {
                    dc.PushClip(new RectangleGeometry(new Rect(0,0,908,623)));
                    dc.PushTransform(new TranslateTransform(-570,0));
                    dc.DrawGeometry(Brushes.AliceBlue,null,(Geometry)sector.Invoke(null,new object[]{348d,350d,150d,390d})!);
                    for(int tick=0;tick<=maximum;tick+=200)
                    {
                        double angle=150+tick*240.0/maximum;
                        bool isMajor=tick%1000==0;
                        dc.DrawLine(isMajor?major:minor,Point(isMajor?331:340,angle),Point(349,angle));
                    }
                    Text(dc,"km/h",1168,589,17);Text(dc,"x1000",805,591,23);Text(dc,"rpm",805,613,20);
                    dc.Pop();dc.Pop();
                    double count=maximum/1000.0;
                    double size=Math.Min(60,276*240/count*Math.PI/180/(count>=10?1.5:.8));
                    for(int i=0;i<=count;i++)
                        Text(dc,i.ToString(System.Globalization.CultureInfo.InvariantCulture),(i%8)*128+64,623+(i/8)*128+64,size,true);
                }
                // 2 physical pixels per original design unit, including numeral pulse headroom.
                var bitmap=new RenderTargetBitmap(2048,2014,192,192,PixelFormats.Pbgra32);
                bitmap.Render(visual);bitmap.Freeze();
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output=File.Create(Path.Combine(directory,$"avante-scale-{maximum}.png"));
                encoder.Save(output);
                Console.WriteLine($"SCALE_ASSET maximum={maximum} pixels=2048x2014 glyphs={maximum/1000+1}");
            }
        }
    }
}
