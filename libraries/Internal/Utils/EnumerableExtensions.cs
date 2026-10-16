using System;
using System.Collections.Generic;

namespace Integrations.Utils;

public static class EnumerableExtensions
{
    extension<T>(IEnumerable<T> source)
    {
        public IReadOnlyCollection<T> AsReadOnlyCollection()
        {
            ArgumentNullException.ThrowIfNull(source);
            return source as IReadOnlyCollection<T> ?? [.. source];
        }
    }
}
