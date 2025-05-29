# Sunshine Game Launcher

Original Work by [@klattimer](https://github.com/klattimer/SunshineGameLauncher)

Sunshine game launcher is a simple VS project to wrap the launching of Steam, 
Epic Game Launcher or Ubisoft Launcher games for example. The intention is simply to execute the game without fuss,
hiding the desktop and exiting appropriately when the game exits.

## Usage 

```
SunshineLauncher.exe [GameEXE] <LaunchCommand> <TimeoutSeconds>
```

In [Sunshine Server](https://docs.lizardbyte.dev/projects/sunshine/en/latest/about/overview.html) you should now execute the binary for sunshine game launcher like so:

```
c:\games\SunshineLauncher.exe RDR2 "com.epicgames.launcher://apps/b30b6d1b4dfd4dcc93b5490be5e094e5%3A22a7b503221442daa2fb16ad37b6ccbf%3AHeather?action=launch&silent=true"
```

### Command-line options

There are various game level hacks necessary to stop blockages on streaming for instance:

 - Tombraider - add the ```-nolauncher``` command line option
 - Control - add the ```-dx12``` or ```-dx11``` command line option. 

#### Steam:

  1. Click the gear icon for the game
  1. Select properties
  1. Edit the launch options
  
#### Epic: 

  1. Click your account icon
  1. Select settings 
  1. Scroll down to the game 
  1. Click the "Additional command line arguments checkbox"
  1. Edit the text in the unlabeled box

## Changelog

- Asynchronous operation
- Better tolerance for Game Launch (ex: UPlay WATCH_DOGS process stops and then starts again)
- LaunchCommand is optional (you can use DetachedCommand from Sunshine)
- Optional Launch Timeout
- Set Game to Foreground
- Kill Game before Exit if Closing does not work
- Close handler is now async (fixes never able to gracefully close game)