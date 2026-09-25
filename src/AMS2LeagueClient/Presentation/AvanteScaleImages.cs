using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Imaging;

namespace AMS2LeagueClient.Presentation
{
    // Seventeen offline-rendered atlases. Decode only the selected scale; normal and
    // expanded views share frozen images. Weak entries do not retain abandoned scales.
    internal sealed class AvanteScaleImages
    {
        private static readonly Dictionary<int, WeakReference<AvanteScaleImages>> Shared = new Dictionary<int, WeakReference<AvanteScaleImages>>();
        public int Maximum { get; }
        public BitmapSource Ticks { get; }
        public BitmapSource[] Numbers { get; }
        private AvanteScaleImages(int maximum)
        {
            Maximum = maximum;
            using var stream = Application.GetResourceStream(new Uri($"pack://application:,,,/AMS2LeagueClient;component/Assets/Hud/avante-scale-{maximum}.png")).Stream;
            var atlas = new BitmapImage();
            atlas.BeginInit(); atlas.CacheOption = BitmapCacheOption.OnLoad; atlas.StreamSource = stream; atlas.EndInit(); atlas.Freeze();
            Ticks = Crop(atlas, 0, 0, 908, 623);
            Numbers = new BitmapSource[maximum / 1000 + 1];
            for (int i = 0; i < Numbers.Length; i++) Numbers[i] = Crop(atlas, (i % 8) * 128, 623 + (i / 8) * 128, 128, 128);
        }
        private static BitmapSource Crop(BitmapSource image, int x, int y, int width, int height)
        {
            var crop = new CroppedBitmap(image, new Int32Rect(x * 2, y * 2, width * 2, height * 2));
            crop.Freeze(); return crop;
        }
        public static AvanteScaleImages? ForMaximum(double maximum)
        {
            // Runtime diagnostic/recovery switch; WPF renderer and warning policy stay identical.
            if (AppContext.TryGetSwitch("AMS2KRLeague.Avante.UseGeneratedScales", out bool generated) && generated) return null;
            if (!double.IsFinite(maximum) || maximum < 4000 || maximum > 20000 || maximum % 1000 != 0) return null;
            int key = (int)maximum;
            lock (Shared)
            {
                if (Shared.TryGetValue(key, out var weak) && weak.TryGetTarget(out var found)) return found;
                var images = new AvanteScaleImages(key);
                Shared[key] = new WeakReference<AvanteScaleImages>(images);
                return images;
            }
        }
    }
}
