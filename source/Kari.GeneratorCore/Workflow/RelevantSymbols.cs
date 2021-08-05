namespace Kari.GeneratorCore.Workflow
{
	using Microsoft.CodeAnalysis;

	public static class Symbols
	{
		public static ITypeSymbol Short { get; private set; }
		public static ITypeSymbol Int { get; private set; }
		public static ITypeSymbol Long { get; private set; }
		public static ITypeSymbol Ushort { get; private set; }
		public static ITypeSymbol Uint { get; private set; }
		public static ITypeSymbol Ulong { get; private set; }
		public static ITypeSymbol Float { get; private set; }
		public static ITypeSymbol Double { get; private set; }
		public static ITypeSymbol Bool { get; private set; }
		public static ITypeSymbol Byte { get; private set; }
		public static ITypeSymbol Sbyte { get; private set; }
		public static ITypeSymbol Decimal { get; private set; }
		public static ITypeSymbol Char { get; private set; }
		public static ITypeSymbol String { get; private set; }
		public static ITypeSymbol Object { get; private set; }
		public static ITypeSymbol Void { get; private set; }
		
		public static void Initialize(Compilation compilation)
		{
			Short 	= compilation.GetSpecialType(SpecialType.System_Int16);
			Int 	= compilation.GetSpecialType(SpecialType.System_Int32);
			Long 	= compilation.GetSpecialType(SpecialType.System_Int64);
			Ushort 	= compilation.GetSpecialType(SpecialType.System_UInt16);
			Uint 	= compilation.GetSpecialType(SpecialType.System_UInt32);
			Ulong 	= compilation.GetSpecialType(SpecialType.System_UInt64);
			Float 	= compilation.GetSpecialType(SpecialType.System_Single);
			Double	= compilation.GetSpecialType(SpecialType.System_Double);
			Bool 	= compilation.GetSpecialType(SpecialType.System_Boolean);
			Byte	= compilation.GetSpecialType(SpecialType.System_Byte);
			Sbyte 	= compilation.GetSpecialType(SpecialType.System_SByte);
			Decimal = compilation.GetSpecialType(SpecialType.System_Decimal);
			Char 	= compilation.GetSpecialType(SpecialType.System_Char);
			String 	= compilation.GetSpecialType(SpecialType.System_String);
			Object 	= compilation.GetSpecialType(SpecialType.System_Object);
			Void 	= compilation.GetSpecialType(SpecialType.System_Void);
		}
	}
}