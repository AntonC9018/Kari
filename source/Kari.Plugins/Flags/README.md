# Flags

This is a Kari plugin for generating helper methods for flag enums.

## Features

After running this plugin, all enums marked with `[NiceFlags]` will be generated useful extension methods for:

- `Has`, `DoesNotHave`, which checks if all of a certain set of flags are set, or not set.
- `HasEither`, which checks for intersection between the given flag sets.
- `DoesNotHaveEither`, which is the negation of `HasEitherFlag`.
- `Set` and `Unset`, which sets, either conditionally or directly, on or off a spefic set of flags, mutating the argument.
- `WithSet` and `WithUnset`, which act like `Set` and `Unset`, but don't mutate the argument.


## Example

```csharp
using System.Diagnostics;
using Kari.Plugins.Flags;
using Root.Generated;

namespace FlagsExample
{
    [NiceFlags]
    public enum Flags
    {
        Shy = 1 << 0,
        Brave = 1 << 1,
        Strong = 1 << 2,
        Beautiful = 1 << 3,
    }

    public static class Program
    {
        public static void Example()
        {
            Flags flags = Flags.Shy | Flags.Brave;

            // Check if it has the Shy flag
            Debug.Assert(flags.Has(Flags.Shy));

            // Check if it has both the Shy and the Brave flags
            Debug.Assert(flags.Has(Flags.Shy | Flags.Brave));

            // Check it's neither Strong nor Beautiful
            Debug.Assert(flags.DoesNotHaveEither(Flags.Strong | Flags.Beautiful));

            // Clear the Shy flag
            flags.Unset(Flags.Shy);

            // Conditionally set/unset the Beautiful flag
            flags.Set(Flags.Beautiful, true);
            Debug.Assert(flags.Has(Flags.Beautiful));
        }
    }
}
```