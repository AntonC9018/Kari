module common.util;

public import std.getopt : getopt, defaultGetoptPrinter;
public import acd.versions;
public import std.algorithm;
public import std.range;
public import std.stdio : writeln;
public import std.conv : to;
public import std.string : toLower;

auto getOptions(T)(string[] args, out T op)
{
    import std.getopt;
    return mixin((){
        auto ret = "getopt(args";
        static foreach (field; T.tupleof)
        {
            import std.format;
            ret ~= `, "%s", "%s", &op.%1$s`.format(__traits(identifier, field), __traits(getAttributes, field)[0]);
        }
        ret ~= ")";
        return ret;
    }());
}


import std.file : DirEntry, dirEntries, SpanMode;

DirEntry getEntryWithLatestChange(string path)
{
    return dirEntries(path, SpanMode.shallow)
        .array
        .sort!((DirEntry a, DirEntry b) => a.timeLastModified > b.timeLastModified)
        .release.front;
}
