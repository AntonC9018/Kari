using Xunit;
using Kari.Annotator;
using System.IO;

namespace Kari.Annotator.Tests
{
    public class AnnotatorTest
    {
        [Fact]
        public void IntegrationTestWithDefaults()
        {
            var tempFolderName = "Kari_" + System.Guid.NewGuid().ToString();
            var tempFolderFullPath = Path.Combine(Path.GetTempPath(), tempFolderName);
            Assert.False(Directory.Exists(tempFolderFullPath));
            Directory.CreateDirectory(tempFolderFullPath);

            var annotationFileName = "TestAnnotations.cs";
            var annotationFileFullPath = Path.Combine(tempFolderFullPath, annotationFileName);
            const string content = @"namespace Hello
{
    public class AAttribute : System.Attribute
    {
    }
}";
            File.WriteAllText(annotationFileFullPath, content);

            int code = Kari.Annotator.Annotator.Main(new string[] { "-targetedFolder", tempFolderFullPath });
            Assert.Equal(0, code);

            var expectedFileName = "TestAnnotations.Generated.cs";
            var expectedFileFullPath = Path.Combine(tempFolderFullPath, expectedFileName);
            Assert.True(File.Exists(expectedFileFullPath));
            string actualContent = File.ReadAllText(expectedFileFullPath);
            const string expectedContent = @"namespace Hello
{
    using Kari.GeneratorCore.Workflow;
    using Kari.Utils;
    internal static class DummyTestAnnotations
    {
        internal const string Text = @""" + content + @""";
    }
    internal static partial class TestSymbols
    {
        internal static AttributeSymbolWrapper<AAttribute> AAttribute { get; private set; }

        internal static void Initialize(Logger logger)
        {
            var compilation = MasterEnvironment.Instance.Compilation;
            AAttribute = new AttributeSymbolWrapper<AAttribute>(compilation, logger);
        }
    }
}
";
            Assert.Equal(expectedContent, actualContent);
        }
    }
}