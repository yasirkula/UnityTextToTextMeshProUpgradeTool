= Text to TextMesh Pro Upgrade Tool =

Online documentation available at: https://github.com/yasirkula/UnityTextToTextMeshProUpgradeTool
E-mail: yasirkula@gmail.com

1. ABOUT
This asset helps you upgrade the Text, InputField, Dropdown and TextMesh objects in your projects to their TextMesh Pro variants. It also upgrades the scripts so that e.g. Text variables in those scripts become TMP_Text variables. Then, it reconnects the references to the upgraded components (e.g. if a public variable was referencing an upgraded Text component, it will now reference the corresponding TextMeshProUGUI component).

2. HOW TO
Before proceeding, you are strongly recommended to backup your project; just in case.

- Open the "Window-Upgrade Text to TMP" window
- Add the prefabs, Scenes, scripts and ScriptableObjects to upgrade to the "Assets & Scenes To Upgrade" list (if you add a folder there, its whole contents will be upgraded). If an Object wasn't added to that list but it had references to the upgraded components, those references will be lost
- To determine which Unity Fonts will be upgraded to which TextMesh Pro FontAssets, use the "Font Upgrades" list
- Hit START and then follow the presented instructions

3. EXAMPLES
Please see: https://github.com/yasirkula/UnityTextToTextMeshProUpgradeTool#examples