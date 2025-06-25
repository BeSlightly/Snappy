# Snappy 0.3

### A forked plugin originally based on work by @eqbot, @ViviAshe and @astrodoobs.

A plugin for saving/loading the current appearance of a character and all applied mods

### Repository URL

0.3.0 is a major change and currently only available from this new repo. </br>
Please do not update if you don't know what you are doing and wait for stuff to be tested properly. </br>
Before updating, make sure to backup your snapshots. </br>
`https://github.com/BeSlightly/Snappy/raw/main/repo.json`

### How to use it:
1. **Set a working directory** in the bottom left of the plugin window </br>(if none is set, snapshots will automatically save to the pluginConfigs/Snappy folder).
2. **To save snapshots**: Select an actor from the actor selection, then press 'Save Snapshot' / 'Update Snapshot' button.
3. **To load snapshots**:
   - Enter GPose.
   - Select an actor.
   - Choose one of your saved snapshots from the dropdown,
   then select one of the Glamourer/Customize+ presets to load onto your character
   - Best performance comes from using [Brio](https://github.com/AsgardXIV/Brio)-spawned actors. I test against those, and if you load onto anything else, you're on your own.

### To do:
- Button to unlock selected actor
- Async snapshot saving
- Find a way to handle multiple mods existing for the same item
- Bug fixes

### Outside of API bumps (API 13 + Mare/Penumbra changes) I will not maintain this actively. </br>When astrodoobs finds time to maintain his fork again this fork will  stop receiving updates.