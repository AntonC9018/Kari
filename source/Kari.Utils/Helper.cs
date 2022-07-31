using System.Collections.Generic;

namespace Kari.Utils;
public static class Helper
{
    public static T? MaybeFirst<T>(this IEnumerable<T> e, System.Predicate<T> func) where T : struct
    {
        foreach (T el in e)
        {
            if (func(el))
                return el;
        }
        return null;
    } 
}