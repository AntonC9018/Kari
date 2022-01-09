using Kari.Plugins.DataObject;
using Xunit;

namespace DataObject.Tests;

[DataObject]
public partial class ADataObject 
{ 
    // Just plain datatype
    public int integer;

    // Overloaded equals operator
    public string str;

    public class MyClass {}
    // Checks the references
    public MyClass customReference;

    #pragma warning disable CS0660
    #pragma warning disable CS0661
    public class MyClass2 
    { 
        public int field;
        public static bool operator==(MyClass2 a, MyClass2 b) => a?.field == b?.field;
        public static bool operator!=(MyClass2 a, MyClass2 b) => a?.field != b?.field;
    }
    #pragma warning restore CS0660
    #pragma warning restore CS0661

    // Uses the overloaded operator
    public MyClass2 customOperator; 
}

public class EssentialTests
{
    [Fact]
    public void Essentials()
    {
        var a0 = new ADataObject();
        var a1 = new ADataObject();
        Assert.Equal(a0, a1);

        {
            a0.integer = 0;
            a1.integer = 1;
            Assert.NotEqual(a0, a1);

            a0.Sync(a1);
            Assert.Equal(a0, a1);
        }

        {
            var str0 = new string("123");
            var str1 = new string("123");
            Assert.False(ReferenceEquals(str0, str1));
            Assert.Equal(str0, str1);

            a0.str = str0; 
            a1.str = str1;
            Assert.Equal(a0, a1);
        }

        {
            var reference0 = new ADataObject.MyClass();
            var reference1 = new ADataObject.MyClass();
            Assert.False(ReferenceEquals(reference0, reference1));

            a0.customReference = reference0; 
            a1.customReference = reference1;
            Assert.NotEqual(a0, a1);

            a0.Sync(a1);
            Assert.Equal(a0, a1);
        }

        {
            var operator0 = new ADataObject.MyClass2();
            var operator1 = new ADataObject.MyClass2();
            
            operator0.field = 0;
            operator1.field = 1;
            Assert.True(operator0 != operator1);

            a0.customOperator = operator0;
            a1.customOperator = operator1;
            Assert.NotEqual(a0, a1);

            operator0.field = 1;        
            Assert.Equal(a0, a1);
        }
    }
}
