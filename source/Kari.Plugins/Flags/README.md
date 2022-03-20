# Flags

This is a Kari plugin for generating helper methods for flag enums.

## Features

After running this plugin, all enums marked with `[NiceFlags]` will be generated useful extension methods for:

- `Has`, `DoesNotHave`, which checks if all of a certain set of flags are set, or not set.
- `HasEither`, which checks for intersection between the given flag sets.
- `DoesNotHaveEither`, which is the negation of `HasEitherFlag`.
- `Set` and `Unset`, which sets, either conditionally or directly, on or off a spefic set of flags, mutating the argument.
- `WithSet` and `WithUnset`, which act like `Set` and `Unset`, but don't mutate the argument.
