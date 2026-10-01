using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace AMS2LeagueClient.Core.Presentation
{
    // Content signature of a presentation view model: every public readable value, including
    // nested presentation rows. Equal signatures mean a view bound to the model would show the
    // same content, so the overlay can skip rebinding (which re-creates converted brushes and
    // repaints) when a rebuilt model carries nothing new.
    public static class DisplaySignature
    {
        private const int MaxDepth = 4;
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Properties = new ConcurrentDictionary<Type, PropertyInfo[]>();

        public static string Of(object? model)
        {
            var builder = new StringBuilder(4096);
            Append(builder, model, 0);
            return builder.ToString();
        }

        private static void Append(StringBuilder builder, object? value, int depth)
        {
            switch (value)
            {
                case null: builder.Append('\u0000'); break;
                case string text: builder.Append(text.Length).Append(':').Append(text); break;
                case IFormattable formattable when value.GetType().IsPrimitive || value is decimal || value is Enum
                    || value is DateTime || value is DateTimeOffset || value is TimeSpan:
                    builder.Append(formattable.ToString(null, CultureInfo.InvariantCulture)); break;
                case bool flag: builder.Append(flag ? '1' : '0'); break;
                case IEnumerable items:
                    builder.Append('[');
                    foreach (object? item in items) { Append(builder, item, depth + 1); builder.Append('\u001e'); }
                    builder.Append(']');
                    break;
                default:
                    Type type = value.GetType();
                    // Only presentation models are expanded; anything else is identified by its text.
                    if (depth >= MaxDepth || type.Namespace != typeof(DisplaySignature).Namespace)
                    {
                        builder.Append(value);
                        break;
                    }
                    builder.Append('{');
                    foreach (PropertyInfo property in Properties.GetOrAdd(type, Readable))
                    {
                        Append(builder, property.GetValue(value), depth + 1);
                        builder.Append('\u001f');
                    }
                    builder.Append('}');
                    break;
            }
        }

        private static PropertyInfo[] Readable(Type type) => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.MetadataToken)
            .ToArray();
    }
}
