# Signalsmith Stretch wrapper

Pitch shift uses [Signalsmith Stretch](https://signalsmith-audio.co.uk/code/stretch/) (MIT) and [Signalsmith Linear](https://github.com/Signalsmith-Audio/linear) (MIT).

Rebuild the Windows x64 DLL:

```
native\SignalsmithStretch\build-win-x64.cmd
```

Requires Visual Studio Build Tools with the C++ x64 toolset. The C# project copies `win-x64\SignalsmithStretch.dll` next to the app.
