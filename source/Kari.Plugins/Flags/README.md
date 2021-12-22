# Flags

This is a Kari plugin for generating helper methods for flag enums.

## Features

After running this plugin, all enums marked with `[NiceFlags]` will be generated useful extension methods for:

- `HasFlag` is not generated, since it is already available transitively via `[System.Flags]`.
- `HasEitherFlag`, which checks for intersection between the given flag sets.
- `HasNeitherFlag`, which is the negation of `HasEitherFlag`.
- `Set` and `Unset`, which sets, either conditionally or directly, on or off a spefic set of flags, mutating the argument.
- `WithSet` and `WithUnset`, which act like `Set` and `Unset`, but don't mutate the argument.
- `GetBitCombinations` and `GetSetBits` which are perhaps too specific and will probably be removed later.

## Example

Here's example code that I used in Unity.
The usage is not good per say, perhaps setting a `dirty` flag would have been more efficient, but it's an example nonetheless.

Here I only use the `!=` operator and the `Sync` method, but it also automatically gets a `==` operator, `GetHashCode`, `Copy` and `Equals` methods.

```C#
using Kari.Plugins.DataObject;

[Serializable]
[DataObject]
public partial class PhysicalBoardProperties
{
    public float Spacing = 0.05f;
    public ushort Radius = 3;
    public ushort PanelNeckWidth = 1;
    public GameObject HexPrefab;
    public GameObject PanelHexPrefab;

    public float HalfWidth => HexVectorTransformations.sqrt3 * HalfHeight; 
    public float Width => HexVectorTransformations.sqrt3 * Height; 
    public float HalfHeight => 0.5f + Spacing;
    public float Height => 1.0f + Spacing * 2;
}

public class BoardManager : MonoBehaviour
{
    [SerializeField] private PhysicalBoardProperties _props;
    private PhysicalBoardProperties _previousParams;

    // ...

    private void Update()
    {
        if (_previousParams != _props)
        {
            _board.Reset();
            _previousParams.Sync(_props);
        }
    }
}
```

