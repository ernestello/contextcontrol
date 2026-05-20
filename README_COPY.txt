Copy these files into D:\Projects\vulkanas\contextcontrol, preserving folders.

Main recovery set:
  .ccReplace.settings.json
  lib\Cc.Replace.Settings.ps1
  lib\replace\Cc.Replace.Settings.ps1
  lib\Cc.Settings.ps1
  lib\shared\Cc.Settings.ps1

The included .ccReplace.settings.json uses ProjectRoot = "." so ccReplace edits Context Control itself while you are fixing the tool.
After the tool opens again, use SS > 11 and enter "auto" to return to the normal Vulkan project root behavior.

Optional normal-engine settings:
  .ccReplace.settings.auto.json

To use the optional normal-engine settings, rename it to .ccReplace.settings.json or set ProjectRoot to "auto" in SS.
