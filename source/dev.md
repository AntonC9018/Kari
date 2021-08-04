## Reference issues

### Flags enum example

So, currently, Kari outputs all the generated code into a single file/directory.
With this, come reference issues. Let me explain.

Assume code from module `A` (say asmdef or csproj, essentially compiling into a separate dll) used the code generator to create helper extension methods for flags. This is the simplest usage scenario.

Assume for illustration purposes that all the generated files compile into a single dll as well. 

So, the folder structure is like this:

- A
  - FlagsEnum.cs
  - A.csproj
- Generated
  - FlagsEnumHelpers.cs
  - Generated.csproj

If `A` were to use the generated helper functions, it would have to reference `Generated.csproj`, so `A.dll` is dependent on `Generated.dll`. However, the extension methods sitting in `FlagsEnumHelpers.cs` must also be aware of the flags enum from `A`.
We have a circualr reference, which is no good.

A solution would be generate `FlagsEnumHelpers.cs` within `A`, like this:

- A
  - FlagsEnum.cs
  - Generated
    - FlagsEnumHelpers.cs

Without a global generated folder in this case.

### How to solve? - Requirements

We're working with code at semantic level. There are a couple of ideas of how to figure out which folder to place the output `FlagsEnumHelpers.cs`:

1. Based on the previously found `asmdef` or `csproj`, in a `Generated` folder within the folder, so `A/Generated`. 
This would have the issue of placing both editor and runtime scripts within the same `Generated` folder, which can be salvaged programatically, by checking the path for any `Editor` parts.

2. Base the folder structure on namespaces. So, the enum `A/FlagsEnum.cs` will have to be in the namespace `RootNamespace.A`, while a file `A/Editor/Stuff/class.cs` will have to be `RootNamespace.A.Editor.Stuff`. They will be generated in the folders `A/Generated` and `A/Editor/Stuff/Generated` respectively. This is good, but a combined approach might be better and would reduce the number of generated folders, so they would generate into `A/Generated/FlagsEnumHelpers.cs` and `A/Generated/Editor/Stuff_class.cs` respectively.

Both are tough to accomplish.

Currently, all the output goes into a single 

- Run 