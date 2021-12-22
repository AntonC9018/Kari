# DataObject

This is a Kari plugin.

A *data object* just means simple struct or class with just data. 
Checking such an object for equality is just comparing the values of all the fields, serializing it means just serializing all fields, etc.

It effectively has the same semantics as C# 9's records (C# 10's record structs). [More info](https://docs.microsoft.com/en-us/dotnet/csharp/whats-new/tutorials/records).

How is this useful, you may ask, if record types already exist? 
Well, they don't for older C# versions; they don't exist in Unity either.


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

## Features

- Works for both structs and classes.
- Works with generic structs and classes.
- Allows any visibility for the type (public / internal / private).

The generated methods:
- `==`, `!=`, `Equals`, `GetHashCode` - self explanatory. 
  These are generated for both structs and classes. 
  The fields are checked using the `==` operator, if available, otherwise `Equals` method is used.
- `Copy` just returns the object for value types, and does a memberwise copy for reference types.
- `Sync` is only generated if the type is not readonly. It currently generates incorrectly if there are readonly fields. 

> Note: 
> Mark your type as partial, otherwise you'll get errors. 
> The operators and the methods have to be members of the type.
