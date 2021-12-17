dotnet pack --configuration Debug source\Kari.Generator
dotnet tool uninstall --global kari.generator
dotnet tool install --global kari.generator --add-source build_folder\.nupkg\Kari.Generator\Debug --version=0.0.0-g7c3e476b2c