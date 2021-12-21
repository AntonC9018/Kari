dotnet pack --configuration Debug source\Kari.Generator
dotnet tool uninstall --global Kari.Generator
dotnet tool install --global Kari.Generator --add-source build_folder\.nupkg\Kari.Generator\Debug --version=0.0.0-g4a24a1eae4