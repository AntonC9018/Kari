module common.path_config;

import std.path;

immutable buildFolder = "build_folder";
immutable nupkgFolder = buildFolder.buildPath(".nupkg");
immutable tempFolder  = buildFolder.buildPath("tool_cache");

immutable defaultKariConfigFileName = "kari.json";
immutable defaultKariPluginsDirectoryName = "kari_plugins";
