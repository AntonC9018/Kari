namespace Bitfield.Tests
{
    using Bitfield.Tests.Generated;
    using Xunit;
    
    [MyPluginAttribute]
    public class Thing {}

    public class TestsStuff
    {
        [Fact]
        void Works()
        {
            Assert.Equal(MarkedTypes.Thing == "Thing");
        }
    }
}