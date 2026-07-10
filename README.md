<div align="center">
  <img src="snappy.png" alt="Snappy Logo" width="150" />
  <h1>Snappy</h1>
  <p><strong>(Formerly XIVSnapper)</strong></p>
  <p>A forked and maintained mod originally based on work by <a href="https://github.com/eqbot">@eqbot</a>, <a href="https://github.com/ViviAshe">@ViviAshe</a> and <a href="https://github.com/astrodoobs">@astrodoobs</a>.</p>

  <a href="https://github.com/BeSlightly/Snappy/releases"><img src="https://img.shields.io/github/v/release/BeSlightly/Snappy?style=for-the-badge&color=blue" alt="Latest Release"></a>   <a href="https://github.com/BeSlightly/Snappy/releases"><img src="https://img.shields.io/github/v/release/BeSlightly/Snappy?include_prereleases&label=Testing&style=for-the-badge&color=orange" alt="Latest Testing (Pre-release)"></a>
  </div>

## What it does

Snappy is a Dalamud plugin for saving and loading a character's appearance, including the mod files used by that appearance. Each snapshot is stored as a single-character mod collection and can be exported for sharing without requiring Mare.

## Supported Mare forks

- [Lightless Sync](https://git.lightless-sync.org/Lightless-Sync/LightlessClient)
- [Snowcloak](https://github.com/Eauldane/SnowcloakClient)
- [Player Sync](https://github.com/universalconquistador/MareSynchronosClient)

For other Mare forks, enable `Use Penumbra/Customize+/Glamourer (fallback)` in Settings to capture live Penumbra, Glamourer, and Customize+ data.

## Installation

Add this custom repo to Dalamud:

```
https://github.com/BeSlightly/Snappy/raw/refs/heads/master/repo.json
```



---

## Usage

1. Set a working directory in the bottom-left corner of the main window.
2. To save, select an actor and press the save button. If a snapshot is selected, saving appends a new state to it.
3. To load, enter GPose, select an actor, and choose a snapshot with the Load button.

Loading is tested primarily with actors spawned by [Brio](https://github.com/AsgardXIV/Brio). Other actor types may behave differently.

## Responsible use

The following is a message from the previous developer, which remains relevant to this fork:

> **I am acutely aware of the controversial nature of this plugin.**
>
> I've decided to maintain this plugin as it's served incredible purpose to me for GPosing myself and my friends. This tool is being maintained explicitly for **character customization and mod sharing**. Nothing more, nothing less. It’s here to make it easier to preserve, export, and share the way a character looks, especially among friends or within a private modding circle.
>
> This plugin retains its original ability to capture appearances from other players: what this means is that if they’re using mods, you’ll obtain them too. That makes consent **absolutely a non-negotiable**.
>
> **Always ask. Always get permission.**
>
> Please use this tool responsibly. Do not use it to pirate paid mods, and do not use it to exploit characters that aren’t your own. Support the creators who pour countless hours into their work.
>
> If the existence of this mod upsets you, understand this: it is being maintained with honest, transparent, and constructive intentions. Its purpose will always serve primarily as utility and never exploitation. It’s about preserving creativity, not crossing lines.

